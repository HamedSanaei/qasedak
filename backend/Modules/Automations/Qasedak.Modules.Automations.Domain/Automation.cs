using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Modules.Automations.Domain;

/// <summary>Lifecycle of an automation. Terminal state is Disabled; Draft allows edits.</summary>
public enum AutomationStatus
{
    /// <summary>Being authored; definition may change freely.</summary>
    Draft = 1,

    /// <summary>Eligible for execution; current version is frozen and immutable.</summary>
    Active = 2,

    /// <summary>Retired by the workspace; never executes again, keeps history.</summary>
    Disabled = 3,
}

/// <summary>An immutable snapshot of a definition with its version number.</summary>
public sealed record AutomationVersion(int Number, AutomationDefinition Definition, DateTimeOffset CreatedAtUtc);

/// <summary>
/// Workspace-owned automation aggregate.
///
/// Invariants:
/// - identity/name/workspace are fixed at creation;
/// - the channel-account binding is fixed at creation and immutable afterwards: an
///   automation bound to one connected account can never execute for another, and a
///   version replayed from history keeps the same account scope (M13-002, ADR-011);
///   a missing binding marks a legacy pre-M13-002 automation, which never matches
///   exact-account events — rebinding means creating a new automation;
/// - definitions live as numbered versions. While a version has never been activated it
///   may still be edited in place (a draft was never executed); activation freezes the
///   current version permanently so executions stay reproducible; editing afterwards
///   requires unpublishing (Active → Draft) which starts a new version number — frozen
///   history is never rewritten;
/// - only an Active automation executes; disabling is final;
/// - timestamps are always passed in (no clock inside the Domain).
/// </summary>
public sealed class Automation
{
    public const int MaxNameLength = 200;

    private readonly List<AutomationVersion> _versions = [];

    private bool _currentVersionFrozen;

    private Automation(Guid id, Guid workspaceId, string name, AutomationDefinition definition, DateTimeOffset createdAtUtc, ChannelAccountId? channelAccountId)
    {
        Id = id;
        WorkspaceId = workspaceId;
        Name = name;
        ChannelAccountId = channelAccountId;
        Status = AutomationStatus.Draft;
        CreatedAtUtc = createdAtUtc;
        ActivatedAtUtc = null;
        DisabledAtUtc = null;
        _versions.Add(new AutomationVersion(1, definition, createdAtUtc));
    }

    public Guid Id { get; }

    public Guid WorkspaceId { get; }

    public string Name { get; }

    /// <summary>
    /// Exact connected account this automation serves (opaque, provider-neutral).
    /// Null marks a legacy pre-M13-002 automation: preserved, but never eligible for
    /// exact-account event execution.
    /// </summary>
    public ChannelAccountId? ChannelAccountId { get; }

    public AutomationStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset? ActivatedAtUtc { get; private set; }

    public DateTimeOffset? DisabledAtUtc { get; private set; }

    public IReadOnlyList<AutomationVersion> Versions => _versions.AsReadOnly();

    /// <summary>Version number executions must use while the automation is active.</summary>
    public int CurrentVersionNumber => _versions[^1].Number;

    public AutomationDefinition CurrentDefinition => _versions[^1].Definition;

    /// <summary>Whether the current version has been frozen by activation (reproducibility marker).</summary>
    public bool CurrentVersionFrozen => _currentVersionFrozen;

    public static Automation Create(Guid id, Guid workspaceId, string name, AutomationDefinition definition, DateTimeOffset createdAtUtc, ChannelAccountId? channelAccountId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AutomationsDomainException("automation.nameRequired", "Automation name is required.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
        {
            throw new AutomationsDomainException("automation.nameTooLong", $"Automation name exceeds {MaxNameLength} characters.");
        }

        if (channelAccountId is { IsResolved: false })
        {
            throw new AutomationsDomainException("automation.accountInvalid", "A channel account binding must name a real account.");
        }

        ArgumentNullException.ThrowIfNull(definition);
        return new Automation(id, workspaceId, trimmed, definition, createdAtUtc, channelAccountId);
    }

    /// <summary>Draft-only edit. An unfrozen draft version is replaced in place; a previously
    /// frozen lineage continues with a fresh version number. Frozen snapshots never change.</summary>
    public void ReviseDraftDefinition(AutomationDefinition definition, DateTimeOffset revisedAtUtc)
    {
        EnsureEditable();
        ArgumentNullException.ThrowIfNull(definition);

        if (_currentVersionFrozen)
        {
            _versions.Add(new AutomationVersion(_versions[^1].Number + 1, definition, revisedAtUtc));
            _currentVersionFrozen = false;
        }
        else
        {
            _versions[^1] = _versions[^1] with { Definition = definition };
        }
    }

    /// <summary>Activates the automation, freezing the current version permanently.</summary>
    public void Activate(DateTimeOffset activatedAtUtc)
    {
        if (Status == AutomationStatus.Active)
        {
            throw new AutomationsDomainException("automation.alreadyActive", "Automation is already active.");
        }

        if (Status == AutomationStatus.Disabled)
        {
            throw new AutomationsDomainException("automation.disabled", "A disabled automation cannot be reactivated.");
        }

        Status = AutomationStatus.Active;
        ActivatedAtUtc = activatedAtUtc;
        _currentVersionFrozen = true;
    }

    /// <summary>Takes an active automation back into Draft for editing. Execution refuses
    /// while paused; the previously frozen version stays in history untouched.</summary>
    public void Unpublish(DateTimeOffset unpublishedAtUtc)
    {
        if (Status != AutomationStatus.Active)
        {
            throw new AutomationsDomainException("automation.notActive", "Only active automations can be unpublished.");
        }

        Status = AutomationStatus.Draft;
        _ = unpublishedAtUtc; // Recorded by persistence layers; Domain keeps lifecycle facts minimal.
    }

    /// <summary>Terminal retirement. Execution refuses afterwards; history stays readable.</summary>
    public void Disable(DateTimeOffset disabledAtUtc)
    {
        if (Status == AutomationStatus.Disabled)
        {
            throw new AutomationsDomainException("automation.alreadyDisabled", "Automation is already disabled.");
        }

        Status = AutomationStatus.Disabled;
        DisabledAtUtc = disabledAtUtc;
        _currentVersionFrozen = false;
    }

    /// <summary>The exact version executions must load: the frozen active version.</summary>
    public AutomationVersion FrozenActiveVersion()
    {
        if (Status != AutomationStatus.Active)
        {
            throw new AutomationsDomainException("automation.notActive", "Only active automations expose an executable version.");
        }

        return _versions[^1];
    }

    /// <summary>
    /// Rehydrates a persisted aggregate. Persistence owns the storage format; the Domain
    /// only restores state that satisfied every invariant when it was saved.
    /// </summary>
    public static Automation FromState(
        Guid id,
        Guid workspaceId,
        string name,
        ChannelAccountId? channelAccountId,
        AutomationStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? activatedAtUtc,
        DateTimeOffset? disabledAtUtc,
        IReadOnlyList<AutomationVersion> versions,
        bool currentVersionFrozen)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (versions.Count == 0)
        {
            throw new AutomationsDomainException("automation.versionRequired", "A persisted automation requires at least one version.");
        }

        var automation = new Automation(id, workspaceId, name, versions[0].Definition, createdAtUtc, channelAccountId);
        automation._versions.Clear();
        automation._versions.AddRange(versions);
        automation.Status = status;
        automation.ActivatedAtUtc = activatedAtUtc;
        automation.DisabledAtUtc = disabledAtUtc;
        automation._currentVersionFrozen = currentVersionFrozen;
        return automation;
    }

    private void EnsureEditable()
    {
        if (Status == AutomationStatus.Active)
        {
            throw new AutomationsDomainException("automation.versionFrozen", "The active definition is frozen; unpublish first to draft a revision.");
        }

        if (Status == AutomationStatus.Disabled)
        {
            throw new AutomationsDomainException("automation.disabled", "A disabled automation can no longer be edited.");
        }
    }
}

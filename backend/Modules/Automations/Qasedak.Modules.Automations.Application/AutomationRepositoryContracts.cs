using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Modules.Automations.Application;

/// <summary>Persistence contract for automation aggregates (including version history).</summary>
public interface IAutomationRepository
{
    /// <summary>Tracked load with the full version history; null when absent.</summary>
    Task<Automation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists a workspace's automations ordered by creation, newest first.</summary>
    Task<IReadOnlyList<Automation>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a workspace's automations bound to one exact channel account, newest
    /// first. Legacy unbound automations are never returned here.
    /// </summary>
    Task<IReadOnlyList<Automation>> ListByAccountAsync(Guid workspaceId, ChannelAccountId channelAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Channel-neutral comment-reconciliation scope (M13-013 §13): for the exact
    /// account, whether any ACTIVE comment-trigger automation has AnySource scope and
    /// the deduplicated union of SpecificSource media ids. Returns null when the
    /// account has no active comment-trigger automation — callers must then perform
    /// zero provider traffic. Never exposes message text/actions outside Automations.
    /// </summary>
    Task<AutomationReconciliationScope?> GetCommentReconciliationScopeAsync(
        Guid workspaceId, ChannelAccountId channelAccountId, CancellationToken cancellationToken = default);

    /// <summary>Persists the current aggregate state (insert or full-row upsert).</summary>
    Task SaveChangesAsync(Automation automation, CancellationToken cancellationToken = default);
}

/// <summary>Stable failure codes surfaced by automation application services.</summary>
public static class AutomationFailures
{
    public const string NotFound = "automation.notFound";
}

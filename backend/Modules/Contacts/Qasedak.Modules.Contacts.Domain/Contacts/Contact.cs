namespace Qasedak.Modules.Contacts.Domain;

/// <summary>
/// A workspace-owned contact: the CRM anchor that accumulates social identities and
/// interaction recency. Ownership rules:
/// - a contact belongs to exactly one workspace; identities are unique per workspace,
///   channel and provider identity (enforced here within the aggregate and by a
///   workspace-wide unique index in persistence);
/// - merging absorbs a secondary contact's identities into the primary — the secondary
///   becomes terminally Merged with provenance, never deleted, so history survives;
/// - timestamps are always parameters; the Domain owns no clock.
/// </summary>
public sealed class Contact
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxChannelLength = 32;
    public const int MaxProviderIdentityLength = 128;
    public const int MaxIdentitiesPerContact = 10;
    public const int MaxTagLength = 32;
    public const int MaxTagsPerContact = 12;
    public const int MaxNoteLength = 2000;

    private readonly List<SocialIdentity> _identities = [];
    private readonly List<string> _tags = [];
    private readonly List<ContactNote> _notes = [];

    private Contact()
    {
    }

    public Guid Id { get; private init; }

    public Guid WorkspaceId { get; private init; }

    public string DisplayName { get; private set; } = string.Empty;

    public ContactStatus Status { get; private set; } = ContactStatus.Active;

    public DateTimeOffset CreatedAtUtc { get; private init; }

    /// <summary>Earliest observed activity across the contact and any absorbed contacts.</summary>
    public DateTimeOffset FirstSeenAtUtc { get; private set; }

    /// <summary>Most recent observed activity.</summary>
    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public long InteractionCount { get; private set; }

    /// <summary>The absorbing contact when Status is Merged.</summary>
    public Guid? MergedIntoId { get; private set; }

    public IReadOnlyList<SocialIdentity> Identities => _identities.AsReadOnly();

    public IReadOnlyList<string> Tags => _tags.AsReadOnly();

    public IReadOnlyList<ContactNote> Notes => _notes.AsReadOnly();

    public static Contact Create(
        Guid id,
        Guid workspaceId,
        string displayName,
        string channel,
        string providerIdentity,
        DateTimeOffset firstSeenAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ContactsDomainException("contact.invalidId", "Contact id must be provided.");
        }

        if (workspaceId == Guid.Empty)
        {
            throw new ContactsDomainException("contact.workspaceRequired", "A contact requires a workspace.");
        }

        var name = ValidateDisplayName(displayName);
        var normalizedChannel = NormalizeChannel(channel);
        var identity = ValidateProviderIdentity(providerIdentity);

        var contact = new Contact
        {
            Id = id,
            WorkspaceId = workspaceId,
            DisplayName = name,
            CreatedAtUtc = firstSeenAtUtc,
            FirstSeenAtUtc = firstSeenAtUtc,
            LastSeenAtUtc = firstSeenAtUtc,
        };
        contact._identities.Add(new SocialIdentity(id, workspaceId, normalizedChannel, identity, firstSeenAtUtc));
        return contact;
    }

    /// <summary>
    /// Binds another provider identity to this contact. Linking an identical identity is an
    /// idempotent no-op returning false; a genuinely new identity returns true.
    /// </summary>
    public bool LinkIdentity(string channel, string providerIdentity, DateTimeOffset linkedAtUtc)
    {
        EnsureMutable("link");
        var normalizedChannel = NormalizeChannel(channel);
        var identity = ValidateProviderIdentity(providerIdentity);

        if (_identities.Any(i => i.SameAs(normalizedChannel, identity)))
        {
            return false;
        }

        if (_identities.Count >= MaxIdentitiesPerContact)
        {
            throw new ContactsDomainException(
                "contact.tooManyIdentities",
                $"A contact can hold at most {MaxIdentitiesPerContact} identities.");
        }

        _identities.Add(new SocialIdentity(Id, WorkspaceId, normalizedChannel, identity, linkedAtUtc));
        return true;
    }

    /// <summary>
    /// Adds a normalized (trimmed, lowercase) tag. Adding an existing tag is an idempotent
    /// no-op returning false; a genuinely new tag returns true.
    /// </summary>
    public bool AddTag(string tag)
    {
        EnsureMutable("tag");
        var normalized = NormalizeTag(tag);
        if (_tags.Contains(normalized))
        {
            return false;
        }

        if (_tags.Count >= MaxTagsPerContact)
        {
            throw new ContactsDomainException(
                "contact.tooManyTags",
                $"A contact can hold at most {MaxTagsPerContact} tags.");
        }

        _tags.Add(normalized);
        return true;
    }

    /// <summary>Removes a tag; removing an absent tag is an idempotent no-op.</summary>
    public bool RemoveTag(string tag) => _tags.Remove(NormalizeTag(tag));

    /// <summary>Appends an immutable note to the contact's history.</summary>
    public ContactNote AddNote(string body, DateTimeOffset createdAtUtc)
    {
        EnsureMutable("note");
        var text = body?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new ContactsDomainException("contact.noteRequired", "A note requires content.");
        }

        if (text.Length > MaxNoteLength)
        {
            throw new ContactsDomainException(
                "contact.noteTooLong",
                $"Notes are limited to {MaxNoteLength} characters.");
        }

        var note = new ContactNote(Guid.CreateVersion7(), Id, WorkspaceId, text, createdAtUtc);
        _notes.Add(note);
        return note;
    }

    /// <summary>Records one observed interaction, keeping recency monotonic (never regresses).</summary>
    public void RecordInteraction(DateTimeOffset occurredAtUtc)
    {
        EnsureMutable("record an interaction on");
        InteractionCount++;
        if (occurredAtUtc > LastSeenAtUtc)
        {
            LastSeenAtUtc = occurredAtUtc;
        }
    }

    public void Rename(string displayName)
    {
        EnsureMutable("rename");
        DisplayName = ValidateDisplayName(displayName);
    }

    public void Archive(DateTimeOffset archivedAtUtc)
    {
        if (Status != ContactStatus.Active)
        {
            throw new ContactsDomainException("contact.notArchivable", "Only active contacts can be archived.");
        }

        Status = ContactStatus.Archived;
        LastSeenAtUtc = archivedAtUtc > LastSeenAtUtc ? archivedAtUtc : LastSeenAtUtc;
    }

    /// <summary>
    /// Absorbs an active secondary contact: recency bounds and interaction totals combine
    /// into this contact and the secondary becomes terminally Merged with provenance
    /// pointing here. Identities are NOT copied — they stay attached to the secondary row,
    /// so identity lookups must resolve <see cref="MergedIntoId"/> chains. This keeps the
    /// workspace-wide identity uniqueness invariant unbreakable even if the two sides are
    /// persisted independently.
    /// </summary>
    public void Absorb(Contact secondary, DateTimeOffset mergedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(secondary);
        EnsureMutable("merge");
        if (secondary.Status != ContactStatus.Active)
        {
            throw new ContactsDomainException("contact.notMergeable", "Only active contacts can be merged away.");
        }

        if (secondary.WorkspaceId != WorkspaceId)
        {
            throw new ContactsDomainException("contact.crossWorkspaceMerge", "Contacts from different workspaces cannot merge.");
        }

        if (ReferenceEquals(this, secondary) || Id == secondary.Id)
        {
            throw new ContactsDomainException("contact.selfMerge", "A contact cannot merge into itself.");
        }

        FirstSeenAtUtc = secondary.FirstSeenAtUtc < FirstSeenAtUtc ? secondary.FirstSeenAtUtc : FirstSeenAtUtc;
        LastSeenAtUtc = secondary.LastSeenAtUtc > LastSeenAtUtc ? secondary.LastSeenAtUtc : LastSeenAtUtc;
        InteractionCount += secondary.InteractionCount;

        secondary.MarkMerged(Id, mergedAtUtc);
    }

    private void MarkMerged(Guid primaryId, DateTimeOffset mergedAtUtc)
    {
        Status = ContactStatus.Merged;
        MergedIntoId = primaryId;
        if (mergedAtUtc > LastSeenAtUtc)
        {
            LastSeenAtUtc = mergedAtUtc;
        }
    }

    private void EnsureMutable(string operation)
    {
        if (Status != ContactStatus.Active)
        {
            throw new ContactsDomainException(
                "contact.notActive",
                $"Cannot {operation} a contact that is not active.");
        }
    }

    private static string ValidateDisplayName(string? displayName)
    {
        var name = displayName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new ContactsDomainException("contact.nameRequired", "A contact requires a display name.");
        }

        if (name.Length > MaxDisplayNameLength)
        {
            throw new ContactsDomainException("contact.nameTooLong", $"Display names are limited to {MaxDisplayNameLength} characters.");
        }

        return name;
    }

    private static string NormalizeChannel(string channel)
    {
        var normalized = channel?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ContactsDomainException("contact.channelRequired", "An identity requires a channel.");
        }

        if (normalized.Length > MaxChannelLength)
        {
            throw new ContactsDomainException("contact.channelTooLong", $"Channels are limited to {MaxChannelLength} characters.");
        }

        return normalized;
    }

    private static string ValidateProviderIdentity(string providerIdentity)
    {
        var identity = providerIdentity?.Trim() ?? string.Empty;
        if (identity.Length == 0)
        {
            throw new ContactsDomainException("contact.identityRequired", "An identity requires a provider identity.");
        }

        if (identity.Length > MaxProviderIdentityLength)
        {
            throw new ContactsDomainException("contact.identityTooLong", $"Provider identities are limited to {MaxProviderIdentityLength} characters.");
        }

        return identity;
    }

    private static string NormalizeTag(string tag)
    {
        var normalized = tag?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ContactsDomainException("contact.tagRequired", "A tag cannot be empty.");
        }

        if (normalized.Length > MaxTagLength)
        {
            throw new ContactsDomainException(
                "contact.tagTooLong",
                $"Tags are limited to {MaxTagLength} characters.");
        }

        return normalized;
    }

    /// <summary>Rehydration for persistence; state was valid when saved.</summary>
    public static Contact FromState(
        Guid id,
        Guid workspaceId,
        string displayName,
        ContactStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset firstSeenAtUtc,
        DateTimeOffset lastSeenAtUtc,
        long interactionCount,
        Guid? mergedIntoId,
        IReadOnlyList<SocialIdentity> identities,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<ContactNote>? notes = null)
    {
        ArgumentNullException.ThrowIfNull(identities);
        var contact = new Contact
        {
            Id = id,
            WorkspaceId = workspaceId,
            DisplayName = displayName,
            Status = status,
            CreatedAtUtc = createdAtUtc,
            FirstSeenAtUtc = firstSeenAtUtc,
            LastSeenAtUtc = lastSeenAtUtc,
            InteractionCount = interactionCount,
            MergedIntoId = mergedIntoId,
        };
        contact._identities.AddRange(identities.Select(i => new SocialIdentity(id, i.WorkspaceId, i.Channel, i.ProviderIdentity, i.LinkedAtUtc)));
        if (tags is not null)
        {
            contact._tags.AddRange(tags);
        }

        if (notes is not null)
        {
            contact._notes.AddRange(notes);
        }

        return contact;
    }
}

namespace Qasedak.Modules.Contacts.Domain;

/// <summary>Lifecycle of a workspace-owned contact.</summary>
public enum ContactStatus
{
    /// <summary>Live contact visible to queries.</summary>
    Active = 1,

    /// <summary>Soft-hidden; history retained.</summary>
    Archived = 2,

    /// <summary>Absorbed by another contact; terminal, kept for provenance.</summary>
    Merged = 3,
}

/// <summary>
/// One social identity bound to a contact. Channels are logical names ("instagram");
/// provider identities stay opaque — never a Meta type.
/// </summary>
public sealed class SocialIdentity
{
    private SocialIdentity()
    {
    }

    public SocialIdentity(Guid contactId, Guid workspaceId, string channel, string providerIdentity, DateTimeOffset linkedAtUtc)
    {
        ContactId = contactId;
        WorkspaceId = workspaceId;
        Channel = channel.Trim().ToLowerInvariant();
        ProviderIdentity = providerIdentity.Trim();
        LinkedAtUtc = linkedAtUtc;
    }

    public Guid ContactId { get; private init; }

    /// <summary>Denormalized ownership key so the workspace-wide uniqueness index can live here.</summary>
    public Guid WorkspaceId { get; private init; }

    public string Channel { get; private init; } = string.Empty;

    public string ProviderIdentity { get; private init; } = string.Empty;

    public DateTimeOffset LinkedAtUtc { get; private init; }

    public bool SameAs(string channel, string providerIdentity) =>
        string.Equals(Channel, channel.Trim(), StringComparison.OrdinalIgnoreCase)
        && ProviderIdentity == providerIdentity.Trim();
}

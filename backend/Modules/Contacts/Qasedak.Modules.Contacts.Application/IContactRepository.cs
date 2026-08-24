using Qasedak.Modules.Contacts.Domain;

namespace Qasedak.Modules.Contacts.Application;

/// <summary>Persistence contract for the contact CRM aggregate.</summary>
public interface IContactRepository
{
    /// <summary>Loads a contact with its identities, or null.</summary>
    Task<Contact?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Looks up a contact by one of its social identities (workspace-scoped).</summary>
    Task<Contact?> FindByIdentityAsync(Guid workspaceId, string channel, string providerIdentity, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Contact>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Persists the current aggregate state (insert or in-place update).</summary>
    Task SaveChangesAsync(Contact contact, CancellationToken cancellationToken = default);
}

/// <summary>Stable failure codes for contact flows.</summary>
public static class ContactFailures
{
    public const string NotFound = "contact.notFound";
}

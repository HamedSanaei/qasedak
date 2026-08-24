using Qasedak.Modules.Contacts.Domain;

namespace Qasedak.Modules.Contacts.Application;

/// <summary>One normalized social interaction to reflect into the contact CRM.</summary>
public sealed record ContactInteractionProjection(
    Guid WorkspaceId,
    string Channel,
    string ProviderIdentity,
    string? DisplayNameHint,
    string EventId,
    string Kind,
    DateTimeOffset OccurredAtUtc);

public sealed record ContactInteractionOutcome(bool Duplicate, Guid ContactId, bool NewContact);

/// <summary>
/// Idempotently maintains contacts from social activity:
/// 1. find the contact owning the (channel, provider identity) — create it when unknown;
///    concurrent creators are arbitrated by the workspace-wide unique identity index
///    (the loser reloads the winner);
/// 2. gate on the event ledger so webhook redelivery/replay never double-counts;
/// 3. record the interaction recency on the aggregate.
/// Order matters: contact existence is established BEFORE the ledger so a replayed event
/// can never leave a promised-but-missing contact behind.
/// </summary>
public sealed class ProjectContactInteractionUseCase(
    IContactRepository contacts,
    IContactInteractionLedger ledger)
{
    public async Task<ContactInteractionOutcome> ExecuteAsync(ContactInteractionProjection projection, CancellationToken cancellationToken = default)
    {
        var isNew = false;
        var contact = await contacts.FindByIdentityAsync(projection.WorkspaceId, projection.Channel, projection.ProviderIdentity, cancellationToken);
        if (contact is null)
        {
            contact = Contact.Create(
                Guid.CreateVersion7(),
                projection.WorkspaceId,
                projection.DisplayNameHint ?? projection.ProviderIdentity,
                projection.Channel,
                projection.ProviderIdentity,
                projection.OccurredAtUtc);
            isNew = true;
            try
            {
                await contacts.SaveChangesAsync(contact, cancellationToken);
            }
            catch (Exception exception) when (IsUniqueViolation(exception))
            {
                // A concurrent delivery of the same identity created the contact first.
                var winner = await contacts.FindByIdentityAsync(projection.WorkspaceId, projection.Channel, projection.ProviderIdentity, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Unique violation reported but contact {projection.Channel}:{projection.ProviderIdentity} is missing in workspace {projection.WorkspaceId}.");
                contact = winner;
                isNew = false;
            }
        }

        // Ledger gates all mutation; replays stop here having at most created the contact.
        var ledgered = await ledger.TryRecordAsync(new ContactInteractionEntry(
            projection.WorkspaceId,
            contact.Id,
            projection.EventId,
            projection.Kind,
            projection.OccurredAtUtc), cancellationToken);
        if (!ledgered)
        {
            return new ContactInteractionOutcome(true, contact.Id, false);
        }

        // Every newly-ledgered interaction counts, including the founding one.
        contact.RecordInteraction(projection.OccurredAtUtc);

        // Placeholder display names upgrade once real attribution is known.
        if (!string.IsNullOrWhiteSpace(projection.DisplayNameHint)
            && string.Equals(contact.DisplayName, contact.Identities[0].ProviderIdentity, StringComparison.Ordinal))
        {
            contact.Rename(projection.DisplayNameHint.Trim());
        }

        await contacts.SaveChangesAsync(contact, cancellationToken);
        return new ContactInteractionOutcome(false, contact.Id, isNew);
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("23505", StringComparison.Ordinal)
                || current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

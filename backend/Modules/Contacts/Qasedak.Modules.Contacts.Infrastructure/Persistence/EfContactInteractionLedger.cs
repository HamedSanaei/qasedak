using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Contacts.Application;

namespace Qasedak.Modules.Contacts.Infrastructure.Persistence;

/// <summary>
/// Event-id ledger over real PostgreSQL; the unique index arbitrates concurrent
/// deliveries of the same event.
/// </summary>
public sealed class EfContactInteractionLedger(ContactsDbContext context) : IContactInteractionLedger
{
    public async Task<bool> TryRecordAsync(ContactInteractionEntry entry, CancellationToken cancellationToken = default)
    {
        context.ContactInteractions.Add(new ContactInteractionRow
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = entry.WorkspaceId,
            ContactId = entry.ContactId,
            EventId = entry.EventId,
            Kind = entry.Kind,
            OccurredAtUtc = entry.OccurredAtUtc,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (IsDuplicateKeyViolation(exception))
        {
            context.ChangeTracker.Clear();
            return false;
        }
    }

    private static bool IsDuplicateKeyViolation(Exception exception)
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

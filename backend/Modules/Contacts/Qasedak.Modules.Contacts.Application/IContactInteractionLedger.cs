namespace Qasedak.Modules.Contacts.Application;

/// <summary>One ledgered interaction event.</summary>
public sealed record ContactInteractionEntry(
    Guid WorkspaceId,
    Guid ContactId,
    string EventId,
    string Kind,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Idempotency ledger for projected interactions: an eventId may be recorded exactly once
/// per workspace. Backed by a unique index; races surface as a false return.
/// </summary>
public interface IContactInteractionLedger
{
    /// <summary>true when newly recorded; false when the eventId was already ledgered.</summary>
    Task<bool> TryRecordAsync(ContactInteractionEntry entry, CancellationToken cancellationToken = default);
}

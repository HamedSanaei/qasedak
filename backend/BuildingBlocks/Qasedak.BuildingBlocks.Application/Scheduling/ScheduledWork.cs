namespace Qasedak.BuildingBlocks.Application.Scheduling;

/// <summary>Lifecycle of one durable scheduled-work record.</summary>
public enum ScheduledWorkStatus
{
    /// <summary>Durable and awaiting its due time (or a retry time).</summary>
    Pending = 1,

    /// <summary>Atomically claimed by one worker holding the lease.</summary>
    Claimed = 2,

    /// <summary>Handler reported success; terminal.</summary>
    Succeeded = 3,

    /// <summary>Handler reported permanent failure; terminal, kept for inspection.</summary>
    Failed = 4,

    /// <summary>Retries exhausted or unroutable; terminal, kept for inspection.</summary>
    DeadLettered = 5,

    /// <summary>Operator-cancelled; never claimed again.</summary>
    Cancelled = 6,
}

/// <summary>Qasedak-owned durable work record. Payloads must never contain secrets.</summary>
public sealed record ScheduledWorkItem(
    Guid Id,
    string WorkType,
    string IdempotencyKey,
    string PayloadJson,
    int PayloadVersion,
    Guid? ConnectedAccountId,
    Guid? WorkspaceId,
    DateTimeOffset DueAtUtc,
    ScheduledWorkStatus Status,
    int Attempts,
    int MaxAttempts,
    DateTimeOffset NextAttemptAtUtc,
    string? LastFailureCode,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc);

/// <summary>Handler verdict for one claimed execution.</summary>
public abstract record WorkOutcome
{
    /// <summary>Work completed; record terminal success.</summary>
    public sealed record Succeeded : WorkOutcome
    {
        public static readonly Succeeded Instance = new();

        private Succeeded()
        {
        }
    }

    /// <summary>Transient failure; reschedule with backoff (bounded by MaxAttempts).</summary>
    public sealed record Retryable(string FailureCode) : WorkOutcome;

    /// <summary>Permanent failure; terminal without retry.</summary>
    public sealed record Permanent(string FailureCode) : WorkOutcome;

    /// <summary>Unroutable record (e.g. no registered handler); terminal dead letter.</summary>
    public sealed record DeadLetter(string FailureCode) : WorkOutcome;
}

/// <summary>Module-owned handler for one work type. Never receives secrets; resolves
/// protected tokens at execution time from <see cref="ScheduledWorkItem.ConnectedAccountId"/>.</summary>
public interface IScheduledWorkHandler
{
    /// <summary>Work type this handler serves (matches <see cref="ScheduledWorkItem.WorkType"/>).</summary>
    string WorkType { get; }

    Task<WorkOutcome> HandleAsync(ScheduledWorkItem item, CancellationToken cancellationToken);
}

/// <summary>Durable scheduled-work persistence boundary (atomic claim semantics).</summary>
public interface IScheduledWorkStore
{
    /// <summary>
    /// Persists a new record, or returns the existing record when the idempotency key
    /// is already present (one logical job per key, race-safe).
    /// </summary>
    Task<(ScheduledWorkItem Item, bool Duplicate)> EnqueueAsync(
        ScheduledWorkEnqueue request, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> due records for
    /// <paramref name="leaseOwner"/>: pending past-due records plus claimed records
    /// whose lease expired (crashed workers), oldest due first.
    /// </summary>
    Task<IReadOnlyList<ScheduledWorkItem>> ClaimDueAsync(
        string leaseOwner, int batchSize, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Extends the lease on a claimed record; false when no longer held.</summary>
    Task<bool> RenewLeaseAsync(Guid id, string leaseOwner, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Marks a claimed record terminally succeeded.</summary>
    Task CompleteAsync(Guid id, string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a handler verdict: retryable failures reschedule with the computed
    /// backoff until attempts run out (then dead-lettered); permanent failures and
    /// unknown verdicts terminate immediately.
    /// </summary>
    Task FailAsync(Guid id, string leaseOwner, WorkOutcome outcome, DateTimeOffset nextAttemptAtUtc, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Operator cancellation; claimed records keep their lease until expiry.</summary>
    Task CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Read-only lookup by idempotency key; null when absent.</summary>
    Task<ScheduledWorkItem?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}

/// <summary>New-record request. PayloadJson must be Qasedak-owned JSON without secrets.</summary>
public sealed record ScheduledWorkEnqueue(
    string WorkType,
    string IdempotencyKey,
    string PayloadJson,
    int PayloadVersion,
    Guid? ConnectedAccountId,
    Guid? WorkspaceId,
    DateTimeOffset DueAtUtc,
    int MaxAttempts);

/// <summary>Stable failure codes for the scheduled-work boundary.</summary>
public static class ScheduledWorkFailures
{
    public const string SecretMaterial = "scheduledwork.secretMaterial";

    public const string UnknownWorkType = "scheduledwork.unknownWorkType";

    public const string LeaseLost = "scheduledwork.leaseLost";
}

/// <summary>Rule-code exception for scheduled-work boundary violations.</summary>
public sealed class ScheduledWorkException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

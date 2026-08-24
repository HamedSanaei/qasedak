namespace Qasedak.BuildingBlocks.Application.Auditing;

/// <summary>
/// One immutable audit record for a sensitive action. Entries never carry secrets —
/// callers pass codes and identifiers only; redaction helpers handle anything sensitive.
/// </summary>
public sealed record AuditEntry(
    Guid AuditId,
    Guid? WorkspaceId,
    Guid? ActorUserId,
    string Action,
    string? TargetType,
    string? TargetId,
    DateTimeOffset AtUtc,
    string? DetailsJson)
{
    public static AuditEntry New(
        string action,
        DateTimeOffset atUtc,
        Guid? workspaceId = null,
        Guid? actorUserId = null,
        string? targetType = null,
        string? targetId = null,
        string? detailsJson = null) =>
        new(Guid.CreateVersion7(), workspaceId, actorUserId, action, targetType, targetId, atUtc, detailsJson);
}

/// <summary>
/// Append-only audit sink. There is intentionally no update or delete surface: audit
/// records are immutable once written.
/// </summary>
public interface IAuditTrail
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

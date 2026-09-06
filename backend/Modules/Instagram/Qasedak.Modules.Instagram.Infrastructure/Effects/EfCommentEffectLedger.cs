using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;

namespace Qasedak.Modules.Instagram.Infrastructure.Effects;

/// <summary>
/// PostgreSQL-backed global semantic claim store. The unique index on
/// (ConnectedAccountId, ProviderCommentId, EffectType) makes the claim global across
/// automation ids, webhook redeliveries, process restarts and application instances:
/// exactly one concurrent candidate ever wins the insert race, and every other candidate
/// observes the existing claim. Transaction scope is kept to single short statements —
/// no DB lock is ever held across a provider HTTP call (enforced by the coordinator
/// sequence, which calls this adapter in its own short steps).
/// </summary>
public sealed class EfCommentEffectLedger(InstagramDbContext context) : ICommentEffectLedger
{
    public async Task<CommentEffectClaim?> ReserveAsync(
        CommentEffectClaimKey key,
        string ownerOperationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        // Conditional insert raced against every other candidate: exactly one insert wins
        // per semantic key. Same-owner retries conflict with their own row and resume it.
        var row = new CommentEffectRow
        {
            Id = Guid.CreateVersion7(),
            ConnectedAccountId = key.ConnectedAccountId,
            ProviderCommentId = key.ProviderCommentId,
            EffectType = key.EffectType,
            OwnerOperationId = ownerOperationId,
            Status = InstagramEffectStatus.Reserved,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        context.CommentEffects.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            context.Entry(row).State = EntityState.Detached;
            return await ResumeOwnedAsync(key, ownerOperationId, cancellationToken);
        }

        return ToClaim(row);
    }

    public async Task<CommentEffectClaim?> FindAsync(
        CommentEffectClaimKey key,
        CancellationToken cancellationToken = default)
    {
        var row = await context.CommentEffects.AsNoTracking()
            .SingleOrDefaultAsync(r => r.ConnectedAccountId == key.ConnectedAccountId
                && r.ProviderCommentId == key.ProviderCommentId
                && r.EffectType == key.EffectType, cancellationToken);
        return row is null ? null : ToClaim(row);
    }

    public async Task MarkAttemptingAsync(Guid claimId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        // Irreversible attempt marker: after this durable write, recovery treats the
        // effect as possibly-sent and must never issue a second provider mutation.
        var row = await LoadAsync(claimId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Status = InstagramEffectStatus.Attempting;
        row.AttemptedAtUtc ??= nowUtc;
        row.UpdatedAtUtc = nowUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordSuccessAsync(Guid claimId, string providerRecipientId, string providerMessageId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(claimId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Status = InstagramEffectStatus.Succeeded;
        row.ProviderRecipientId = providerRecipientId;
        row.ProviderMessageId = providerMessageId;
        row.CompletedAtUtc = nowUtc;
        row.UpdatedAtUtc = nowUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordTerminalFailureAsync(Guid claimId, string failureCode, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(claimId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Status = InstagramEffectStatus.TerminalFailed;
        row.FailureCode = failureCode;
        row.CompletedAtUtc = nowUtc;
        row.UpdatedAtUtc = nowUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordUncertainAsync(Guid claimId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(claimId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Status = InstagramEffectStatus.Uncertain;
        row.CompletedAtUtc = nowUtc;
        row.UpdatedAtUtc = nowUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Same-owner resume: the crash window before the attempt marker.</summary>
    private async Task<CommentEffectClaim?> ResumeOwnedAsync(
        CommentEffectClaimKey key, string ownerOperationId, CancellationToken cancellationToken)
    {
        var row = await context.CommentEffects.AsNoTracking()
            .SingleOrDefaultAsync(r => r.ConnectedAccountId == key.ConnectedAccountId
                && r.ProviderCommentId == key.ProviderCommentId
                && r.EffectType == key.EffectType, cancellationToken);

        // Only the same logical owner may resume a Reserved claim; a competitor sees null
        // and makes zero provider calls. Any later state is replayed, never re-attempted.
        return row is not null && row.OwnerOperationId == ownerOperationId
            ? ToClaim(row)
            : null;
    }

    private async Task<CommentEffectRow?> LoadAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var row = await context.CommentEffects.SingleOrDefaultAsync(r => r.Id == claimId, cancellationToken);
        return row;
    }

    /// <summary>Npgsql reports unique-index races as SQLSTATE 23505 ("duplicate key").</summary>
    private static bool IsUniqueViolation(DbUpdateException exception)
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

    private static CommentEffectClaim ToClaim(CommentEffectRow row) => new(
        row.Id,
        row.ConnectedAccountId,
        row.ProviderCommentId,
        row.EffectType,
        row.OwnerOperationId,
        row.Status,
        row.AttemptedAtUtc,
        row.CompletedAtUtc,
        row.ProviderRecipientId,
        row.ProviderMessageId,
        row.FailureCode);
}

using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.Effects;

/// <summary>Stable failure codes for comment Private Reply outcomes (dispatcher contract).</summary>
public static class PrivateReplyOutcomeCodes
{
    /// <summary>Claim is held by another operation — zero Meta call, truthful suppression.</summary>
    public const string AlreadyClaimed = "privateReply.alreadyClaimed";

    /// <summary>Attempt began but the outcome is unknown — never a second Meta call.</summary>
    public const string Uncertain = "privateReply.uncertain";

    /// <summary>Provider explicitly rejected after the attempt marker — never re-attempted.</summary>
    public const string TerminalFailed = "privateReply.terminalFailed";

    /// <summary>Local account/token prerequisite failed before any claim — never re-attempted.</summary>
    public const string AccountUnavailable = "privateReply.accountUnavailable";

    /// <summary>Deterministic local policy rejection before any claim.</summary>
    public const string PolicyRejected = "privateReply.policyRejected";

    /// <summary>
    /// The content kind is not supported on the Private Reply path (M13-010 provider
    /// gating): the current first-party Private Reply guide documents only message text;
    /// interactive variants are not independently verified and never reach Meta.
    /// </summary>
    public const string UnsupportedContent = "privateReply.policyRejected.unsupportedContent";
}

public sealed record CommentPrivateReplyCommand(
    Guid WorkspaceId,
    Guid ConnectedAccountId,
    string? ProviderCommentId,
    bool IsLiveComment,
    InstagramMessageContent MessageContent,
    string OwnerOperationId,
    DateTimeOffset NotificationOccurredAtUtc);

public sealed record CommentPrivateReplyResult(
    bool Attempted,
    bool Delivered,
    string? FailureCode,
    string? ProviderRecipientId,
    string? ProviderMessageId)
{
    public static CommentPrivateReplyResult Ok(string recipientId, string messageId) =>
        new(true, true, null, recipientId, messageId);

    public static CommentPrivateReplyResult Terminal(string failureCode) => new(false, false, failureCode, null, null);
}

/// <summary>
/// One-shot comment Private Reply orchestration (M13-009) enforcing the global semantic
/// claim before any provider mutation:
///
/// local prerequisites (exact account, token, text) → deterministic policy → global claim
/// (PostgreSQL-unique ConnectedAccountId+CommentId+PrivateReply) → durable Attempting
/// marker → Meta call outside any DB transaction → durable outcome.
///
/// Crash safety: a Reserved claim resumes only for the same logical owner; after the
/// Attempting marker NO automatic second Meta call ever occurs — timeouts, 5xx, rate
/// limits and crash windows all land in Uncertain/TerminalFailed and are replayed from
/// the ledger with zero provider traffic. A Succeeded effect is replayed with its stored
/// provider identity so an AutomationRun save failure converges without a second send.
/// </summary>
public sealed class CommentPrivateReplyCoordinator(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    ICommentReferenceReader referenceReader,
    ICommentEffectLedger ledger,
    ICommentPrivateReplyClient client,
    IClock clock,
    ICommentEffectObservability observability)
{
    public async Task<CommentPrivateReplyResult> ExecuteAsync(
        CommentPrivateReplyCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Local deterministic prerequisites — BEFORE the claim so local configuration
        //    failures never consume the single provider effect.
        var account = await accounts.FindByIdAsync(command.ConnectedAccountId, cancellationToken);
        if (account is null || account.WorkspaceId != command.WorkspaceId
            || account.IsDisconnected || account.Path != ConnectionPath.InstagramLogin)
        {
            return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.AccountUnavailable);
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.AccountUnavailable);
        }

        if (string.IsNullOrWhiteSpace(command.ProviderCommentId))
        {
            observability.PolicyRejected(InstagramEffectType.PrivateReply, "missingOrigin");
            return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.PolicyRejected + ".missingOrigin");
        }

        // M13-010 provider gating: the current first-party Private Reply guide documents
        // only message:{text}; button templates on recipient.comment_id are NOT verified.
        // Unsupported content is rejected here deterministically — BEFORE the claim and
        // with ZERO provider calls — and never falls back to a second mutation.
        if (command.MessageContent is not InstagramMessageContent.PlainText { Text: not null and not "" } plainText)
        {
            if (command.MessageContent is null)
            {
                observability.PolicyRejected(InstagramEffectType.PrivateReply, "missingContent");
                return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.PolicyRejected + ".missingContent");
            }

            observability.PolicyRejected(InstagramEffectType.PrivateReply, "unsupportedContent");
            return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.UnsupportedContent);
        }

        var contentValidation = MessageValidationPolicy.Validate(plainText);
        if (!contentValidation.IsValid)
        {
            observability.PolicyRejected(InstagramEffectType.PrivateReply, contentValidation.Code.ToString());
            return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.PolicyRejected + "." + contentValidation.Code);
        }

        // 2. Deterministic policy. The comment-creation-time read is a focused non-mutating
        //    reference read (never inside the webhook HTTP endpoint); a failed read falls
        //    back to the one-sided notification guard with Meta as final authority.
        var reference = await referenceReader.ReadCreatedAtUtcAsync(accessToken, command.ProviderCommentId, cancellationToken);
        if (reference is CommentReferenceReadResult.NotFound)
        {
            observability.PolicyRejected(InstagramEffectType.PrivateReply, "commentNotFound");
            return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.PolicyRejected + ".commentNotFound");
        }

        DateTimeOffset? exactCreatedAt = reference is CommentReferenceReadResult.Found found ? found.CreatedAtUtc : null;
        var verdict = PrivateReplyPolicy.Evaluate(new PrivateReplyPolicyInput(
            command.ProviderCommentId,
            command.IsLiveComment,
            exactCreatedAt,
            command.NotificationOccurredAtUtc,
            clock.UtcNow));

        if (verdict.Outcome != PrivateReplyPolicyOutcome.Eligible)
        {
            observability.PolicyRejected(InstagramEffectType.PrivateReply, verdict.Outcome.ToString());
            return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.PolicyRejected + "." + verdict.Outcome);
        }

        // 3. Global semantic claim: PostgreSQL-unique (account, comment, effect). The loser
        //    of a concurrent race makes ZERO Meta calls; the same owner may resume a
        //    Reserved claim after a crash, a competitor never may.
        var key = new CommentEffectClaimKey(command.ConnectedAccountId, command.ProviderCommentId, InstagramEffectType.PrivateReply);
        var claim = await ledger.ReserveAsync(key, command.OwnerOperationId, clock.UtcNow, cancellationToken);
        if (claim is null)
        {
            observability.ClaimConflict(InstagramEffectType.PrivateReply);
            return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.AlreadyClaimed);
        }

        observability.ClaimAcquired(InstagramEffectType.PrivateReply);

        // 4. Replay path: any state beyond a fresh Reserved claim means a provider attempt
        //    is done or in flight — replay the durable outcome with ZERO Meta traffic.
        //    Unknown/future states fail CLOSED (never a second mutation).
        if (claim.Status != InstagramEffectStatus.Reserved)
        {
            if (claim.Status == InstagramEffectStatus.Succeeded
                && claim.ProviderRecipientId is not null
                && claim.ProviderMessageId is not null)
            {
                return CommentPrivateReplyResult.Ok(claim.ProviderRecipientId, claim.ProviderMessageId);
            }

            if (claim.Status == InstagramEffectStatus.TerminalFailed)
            {
                return CommentPrivateReplyResult.Terminal(claim.FailureCode ?? PrivateReplyOutcomeCodes.TerminalFailed);
            }

            // Attempting / Uncertain / any unrecognized state: never a second call.
            return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.Uncertain);
        }

        // 5. Irreversible durable attempt marker — BEFORE the provider mutation. After this
        //    point no automatic second Private Reply may ever be issued.
        await ledger.MarkAttemptingAsync(claim.Id, clock.UtcNow, cancellationToken);
        observability.Attempted(InstagramEffectType.PrivateReply);

        // 6. Provider call — explicitly outside any database transaction.
        var result = await client.SendPrivateReplyAsync(
            accessToken,
            account.ProviderUserId,
            command.ProviderCommentId,
            plainText.Text,
            cancellationToken);

        if (result.Succeeded)
        {
            if (result.RecipientId is null || result.MessageId is null)
            {
                await ledger.RecordUncertainAsync(claim.Id, clock.UtcNow, cancellationToken);
                observability.Uncertain(InstagramEffectType.PrivateReply);
                return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.Uncertain);
            }

            await ledger.RecordSuccessAsync(claim.Id, result.RecipientId, result.MessageId, clock.UtcNow, cancellationToken);
            observability.Succeeded(InstagramEffectType.PrivateReply);
            return CommentPrivateReplyResult.Ok(result.RecipientId, result.MessageId);
        }

        // 7. Provider answered explicitly (rejection) → durable terminal failure.
        //    Transport-level ambiguity (timeout/unreachable) → Uncertain, never retried.
        if (result.Failure?.Reason == PrivateReplyFailureReason.TransportFailure)
        {
            await ledger.RecordUncertainAsync(claim.Id, clock.UtcNow, cancellationToken);
            observability.Uncertain(InstagramEffectType.PrivateReply);
            return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.Uncertain);
        }

        var failureCategory = result.Failure?.Reason switch
        {
            PrivateReplyFailureReason.RejectedByMeta => "rejectedByMeta",
            PrivateReplyFailureReason.MalformedResponse => "malformedResponse",
            _ => "unknown",
        };
        await ledger.RecordTerminalFailureAsync(claim.Id, PrivateReplyOutcomeCodes.TerminalFailed + "." + failureCategory, clock.UtcNow, cancellationToken);
        observability.Failed(InstagramEffectType.PrivateReply, failureCategory);
        return CommentPrivateReplyResult.Terminal(PrivateReplyOutcomeCodes.TerminalFailed);
    }
}

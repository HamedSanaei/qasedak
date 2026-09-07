using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root adapter turning one comment-originated automation action into the
/// Instagram-owned one-shot Public Comment Reply operation (M13-012 §33-34). It reuses
/// the M13-009 global effect ledger (InstagramEffectType.PublicCommentReply) — the same
/// reserve → durable Attempting → provider call → durable outcome discipline as Private
/// Reply, with an independent effect identity so public and private effects may coexist
/// in one definition. Duplicate public replies are never sent after an ambiguous
/// timeout/crash; a Succeeded effect is replayed with its stored provider identity and
/// zero provider traffic. The deterministic owner identity is
/// {automationId}|{version}|{triggerEventId}|{actionIndex} like the Private Reply bridge.
/// </summary>
public sealed class AutomationPublicReplyBridge(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    ICommentEffectLedger ledger,
    ICommentPublicReplyClient publicClient,
    IClock clock)
{
    public async Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default)
    {
        if (dispatch.ChannelAccountId is not { IsResolved: true }
            || dispatch.TriggerKind is null
            || string.IsNullOrWhiteSpace(dispatch.TriggerEntityId))
        {
            // Public reply addressing is impossible without the origin comment semantics.
            return ActionResult.TerminalFailed("publicReply.missingOriginSemantics");
        }

        var accountId = dispatch.ChannelAccountId.Value.Value;

        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null || account.WorkspaceId != dispatch.WorkspaceId || account.IsDisconnected
            || account.Path != ConnectionPath.InstagramLogin)
        {
            return ActionResult.TerminalFailed(PublicReplyOutcomeCodes.AccountUnavailable);
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            return ActionResult.TerminalFailed(PublicReplyOutcomeCodes.AccountUnavailable);
        }

        var key = new CommentEffectClaimKey(accountId, dispatch.TriggerEntityId, InstagramEffectType.PublicCommentReply);
        var ownerOperationId = $"{dispatch.AutomationId}|{dispatch.AutomationVersionNumber}|{dispatch.TriggerEventId}|{dispatch.ActionIndex}";

        var claim = await ledger.ReserveAsync(key, ownerOperationId, clock.UtcNow, cancellationToken);
        if (claim is null)
        {
            var existing = await ledger.FindAsync(key, cancellationToken);
            return existing is not null && existing.OwnerOperationId == ownerOperationId
                ? Replay(existing)
                : ActionResult.TerminalSuppressed(PublicReplyOutcomeCodes.AlreadyClaimed);
        }

        if (claim.Status != InstagramEffectStatus.Reserved)
        {
            return Replay(claim);
        }

        await ledger.MarkAttemptingAsync(claim.Id, clock.UtcNow, cancellationToken);

        var result = await publicClient.SendPublicReplyAsync(accessToken, dispatch.TriggerEntityId, dispatch.MessageText, cancellationToken);
        if (result.Succeeded && result.ReplyCommentId is not null)
        {
            await ledger.RecordSuccessAsync(claim.Id, dispatch.TriggerEntityId, result.ReplyCommentId, clock.UtcNow, cancellationToken);
            return ActionResult.Delivered();
        }

        return result.Failure?.Reason switch
        {
            PublicReplyFailureReason.TransportFailure => await TerminalAsync(claim, InstagramEffectStatus.Uncertain, PublicReplyOutcomeCodes.Uncertain, cancellationToken),
            PublicReplyFailureReason.MalformedResponse => await TerminalAsync(claim, InstagramEffectStatus.Uncertain, PublicReplyOutcomeCodes.Uncertain, cancellationToken),
            _ => await TerminalAsync(claim, InstagramEffectStatus.TerminalFailed, PublicReplyOutcomeCodes.TerminalFailed, cancellationToken),
        };
    }

    private async Task<ActionResult> TerminalAsync(
        CommentEffectClaim claim,
        InstagramEffectStatus status,
        string failureCode,
        CancellationToken cancellationToken)
    {
        switch (status)
        {
            case InstagramEffectStatus.Uncertain:
                await ledger.RecordUncertainAsync(claim.Id, clock.UtcNow, cancellationToken);
                return ActionResult.TerminalUncertain(failureCode);
            default:
                await ledger.RecordTerminalFailureAsync(claim.Id, failureCode, clock.UtcNow, cancellationToken);
                return ActionResult.TerminalFailed(failureCode);
        }
    }

    /// <summary>Replays the durable ledger state with zero provider traffic.</summary>
    private static ActionResult Replay(CommentEffectClaim claim) => claim.Status switch
    {
        InstagramEffectStatus.Succeeded => ActionResult.Delivered(claim.ProviderRecipientId, claim.ProviderMessageId),
        InstagramEffectStatus.Attempting or InstagramEffectStatus.Uncertain => ActionResult.TerminalUncertain(claim.FailureCode ?? PublicReplyOutcomeCodes.Uncertain),
        InstagramEffectStatus.TerminalFailed => ActionResult.TerminalFailed(claim.FailureCode ?? PublicReplyOutcomeCodes.TerminalFailed),
        _ => ActionResult.TerminalSuppressed(PublicReplyOutcomeCodes.AlreadyClaimed),
    };
}

public static class PublicReplyOutcomeCodes
{
    public const string AlreadyClaimed = "publicReply.alreadyClaimed";

    public const string AccountUnavailable = "publicReply.accountUnavailable";

    public const string Uncertain = "publicReply.uncertain";

    public const string TerminalFailed = "publicReply.terminalFailed";
}

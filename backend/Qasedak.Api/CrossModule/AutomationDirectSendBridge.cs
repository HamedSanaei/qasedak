using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root adapter turning one automation dispatch into the M13-010 normal Direct
/// Message operation: exact-account + token resolution (no first-active fallback), then
/// recipient.id = participant IGSID with PlainText content. Meta stays the final window
/// authority — a MessagingWindowExpired response is preserved as a terminal classification,
/// never auto-retried. Pre-provider local failures (unknown/disconnected account, missing
/// token, missing recipient) report <c>ExternalAttempt = false</c> so the run ledger may
/// retry safely; anything after a provider call is terminal (transport ambiguity →
/// Uncertain, explicit rejection → TerminalFailed).
/// </summary>
public sealed class AutomationDirectSendBridge(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IInstagramMessagingClient messaging)
{
    public async Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default)
    {
        if (dispatch.ChannelAccountId is not { IsResolved: true })
        {
            return ActionResult.RejectedLocal("direct.accountUnresolved");
        }

        var account = await accounts.FindByIdAsync(dispatch.ChannelAccountId.Value.Value, cancellationToken);
        if (account is null || account.WorkspaceId != dispatch.WorkspaceId || account.IsDisconnected
            || account.Path != ConnectionPath.InstagramLogin)
        {
            return ActionResult.RejectedLocal("direct.accountUnavailable");
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            return ActionResult.RejectedLocal("direct.tokenMissing");
        }

        if (string.IsNullOrWhiteSpace(dispatch.ParticipantId))
        {
            // Never fabricate a recipient (no username, no comment id).
            return ActionResult.RejectedLocal("direct.recipientUnavailable");
        }

        var result = await messaging.SendDirectAsync(
            accessToken,
            dispatch.ParticipantId,
            new InstagramMessageContent.PlainText(dispatch.MessageText),
            cancellationToken);

        if (result.Succeeded)
        {
            return ActionResult.Delivered(result.ProviderRecipientId, result.ProviderMessageId);
        }

        return result.Failure?.Reason switch
        {
            MessagingFailureReason.MessagingWindowExpired => ActionResult.TerminalFailed("direct.windowExpired"),
            MessagingFailureReason.TransportFailure => ActionResult.TerminalUncertain("direct.uncertain"),
            MessagingFailureReason.MalformedResponse => ActionResult.TerminalUncertain("direct.malformed"),
            _ => ActionResult.TerminalFailed("direct.rejectedByMeta"),
        };
    }
}

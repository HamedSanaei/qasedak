using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Application.Messaging;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root adapter turning one comment-originated automation action dispatch
/// into the Instagram-owned one-shot Private Reply operation (M13-009). The automation
/// ledger stays channel-neutral: concrete provider codes map onto terminal dispatch
/// semantics here. The deterministic owner identity is
/// {automationId}|{version}|{triggerEventId}|{actionIndex} so a resumed execution of the
/// SAME logical slot may safely continue a Reserved claim while a different automation
/// or slot can never steal it.
/// </summary>
public sealed class AutomationPrivateReplyBridge(CommentPrivateReplyCoordinator coordinator)
{
    public async Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default)
    {
        if (dispatch.ChannelAccountId is not { IsResolved: true }
            || dispatch.TriggerKind is null
            || string.IsNullOrWhiteSpace(dispatch.TriggerEntityId)
            || dispatch.TriggerOccurredAtUtc is null)
        {
            // Comment-ID addressing is impossible without the origin comment semantics.
            return ActionResult.TerminalFailed("privateReply.missingOriginSemantics");
        }

        var ownerOperationId = $"{dispatch.AutomationId}|{dispatch.AutomationVersionNumber}|{dispatch.TriggerEventId}|{dispatch.ActionIndex}";
        var result = await coordinator.ExecuteAsync(new CommentPrivateReplyCommand(
            dispatch.WorkspaceId,
            dispatch.ChannelAccountId.Value.Value,
            dispatch.TriggerEntityId,
            dispatch.IsLiveComment,
            new InstagramMessageContent.PlainText(dispatch.MessageText),
            ownerOperationId,
            dispatch.TriggerOccurredAtUtc.Value), cancellationToken);

        if (result.Delivered)
        {
            return ActionResult.Delivered();
        }

        return result.FailureCode switch
        {
            PrivateReplyOutcomeCodes.AlreadyClaimed => ActionResult.TerminalSuppressed(result.FailureCode),
            PrivateReplyOutcomeCodes.Uncertain => ActionResult.TerminalUncertain(result.FailureCode),
            _ => ActionResult.TerminalFailed(result.FailureCode ?? PrivateReplyOutcomeCodes.TerminalFailed),
        };
    }
}

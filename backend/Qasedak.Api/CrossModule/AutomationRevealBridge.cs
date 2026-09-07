using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Instagram.Application.RevealFlow;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root adapter mapping a StartRevealFlow automation action onto the M13-011
/// reveal coordinator (M13-012 §37-38). Nothing in M13-011 is rebuilt: the rv1 correlation,
/// reveal_flows state machine, relationship client, postback handler, 24h logic and
/// single-reveal CAS all stay owned by the coordinator. The action's durable outcome is
/// <c>ContinuationStarted</c> — the opening Private Reply is sent (or truthfully replayed
/// from the one-shot effect ledger) and the flow remains waiting for user interaction;
/// this dispatch never holds open waiting for the final reveal. A claim owned by a
/// different operation suppresses truthfully.
/// </summary>
public sealed class AutomationRevealBridge(
    RevealFlowCoordinator coordinator,
    IClock clock)
{
    public async Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default)
    {
        if (dispatch.ChannelAccountId is not { IsResolved: true }
            || dispatch.TriggerKind is null
            || string.IsNullOrWhiteSpace(dispatch.TriggerEntityId))
        {
            return ActionResult.TerminalFailed("revealFlow.missingCommentOrigin");
        }

        var reveal = dispatch.Extras?.Reveal;
        if (reveal is null)
        {
            return ActionResult.TerminalFailed("revealFlow.contentMissing");
        }

        var outcome = await coordinator.StartFromCommentAsync(new StartRevealFlowCommand(
            dispatch.WorkspaceId,
            dispatch.ChannelAccountId.Value.Value,
            dispatch.TriggerEntityId,
            dispatch.ParticipantId,
            dispatch.IsLiveComment,
            dispatch.TriggerOccurredAtUtc ?? clock.UtcNow,
            new RevealFlowContent(
                dispatch.MessageText,
                reveal.GatePromptText,
                reveal.PostbackButtonTitle,
                reveal.FollowUrl,
                reveal.FollowButtonTitle,
                reveal.RevealText),
            MapFollowGateMode(reveal.FollowGateMode),
            dispatch.AutomationId,
            dispatch.AutomationVersionNumber,
            dispatch.TriggerEventId,
            dispatch.ActionIndex), cancellationToken);

        return MapOutcome(outcome.Code);
    }

    /// <summary>
    /// Maps the M13-011 outcome code to run-ledger semantics. The identity mapping mirrors
    /// the Instagram FollowGateMode values (Disabled = 0, EnabledWhenSupported = 1).
    /// </summary>
    private static FollowGateMode MapFollowGateMode(RevealFollowGateMode mode) => mode switch
    {
        RevealFollowGateMode.EnabledWhenSupported => FollowGateMode.EnabledWhenSupported,
        _ => FollowGateMode.Disabled,
    };

    private static ActionResult MapOutcome(string code) => code switch
    {
        // The flow durably started (opening sent or already in flight under the same
        // automation) — the continuation outlives this dispatch.
        "reveal.openingDelivered" or "reveal.alreadyStarted" => ActionResult.ContinuationStarted(),
        RevealFlowFailures.OpeningClaimedElsewhere => ActionResult.TerminalSuppressed(code),
        RevealFlowFailures.OpeningUncertain => ActionResult.TerminalUncertain(code),
        RevealFlowFailures.AccountUnavailable => ActionResult.TerminalFailed(code),
        _ => ActionResult.TerminalFailed(code),
    };
}

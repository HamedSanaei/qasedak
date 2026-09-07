using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.RevealFlow;

/// <summary>
/// Durable reveal-flow orchestration (M13-011) implementing the provider-correct
/// sequence verified against current first-party Meta documentation (2026-09-07):
///
///   comment → PlainText Private Reply (M13-009 one-shot effect, global claim)
///   → the user replies (reply_to.mid correlation when present; otherwise the
///     deterministic single-pending candidate, never an arbitrary pick)
///   → consent + 24h Direct window proven by the inbound message
///   → Direct button-template gate prompt (postback correlation token)
///   → user taps the postback → exact-account/sender/token validation
///   → optional is_user_follow_business check (tri-state, fail closed)
///   → atomic single-reveal authority → ONE Direct reveal message.
///
/// Safety invariants (mirroring M13-009): every provider mutation is preceded by a
/// durable Attempting-style marker; after that marker NO automatic second provider call
/// ever occurs — timeouts, crashes, redeliveries and replays all fail closed. A postback
/// whose raw token matches the persisted hash is the ONLY way a flow with an ambiguous
/// gate-prompt attempt may continue (the tap itself proves the prompt was delivered).
/// </summary>
public sealed class RevealFlowCoordinator(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IRevealFlowStore store,
    ICommentEffectLedger ledger,
    CommentPrivateReplyCoordinator privateReplyCoordinator,
    IInstagramMessagingClient messagingClient,
    IInstagramRelationshipClient relationshipClient,
    IClock clock,
    IRevealFlowObservability observability)
{
    private static readonly TimeSpan DirectWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Starts one reveal continuation for an origin comment. The flow identity is
    /// DETERMINISTIC per (ConnectedAccountId, ProviderCommentId): a redelivered or
    /// restarted command resumes the SAME durable row and the SAME Private Reply claim
    /// owner ("reveal|{flowId}") — two automations can never produce two openings, and
    /// a crash between steps never duplicates content selection.
    /// </summary>
    public async Task<RevealFlowOutcome> StartFromCommentAsync(
        StartRevealFlowCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Local deterministic validation — before any durable row or provider call.
        var contentValidation = ValidateContent(command.Content);
        if (!contentValidation.IsValid)
        {
            observability.Suppressed("start", "localValidation");
            return new RevealFlowOutcome(RevealFlowFailures.LocalValidation + "." + contentValidation.Code, false);
        }

        // 2. Exact-account prerequisites — zero provider traffic for misconfigured origins.
        var accessToken = await TryResolveAccountAsync(command.WorkspaceId, command.ConnectedAccountId, cancellationToken);
        if (accessToken is null)
        {
            observability.Suppressed("start", "accountUnavailable");
            return new RevealFlowOutcome(RevealFlowFailures.AccountUnavailable, false);
        }

        // 3. Durable flow row — one per logical origin (PostgreSQL-unique).
        var flowId = RevealFlowId.For(command.ConnectedAccountId, command.ProviderCommentId);
        var flow = await store.CreateOrGetAsync(
            flowId,
            command.WorkspaceId,
            command.ConnectedAccountId,
            command.ProviderCommentId,
            command.ParticipantIGSID,
            command.IsLiveComment,
            command.NotificationOccurredAtUtc,
            command.FollowGateMode,
            command.Content,
            command.AutomationId,
            command.AutomationVersionNumber,
            command.TriggerEventId,
            command.ActionIndex,
            clock.UtcNow,
            cancellationToken);

        // 4. State dispatch for the (possibly pre-existing) flow.
        switch (flow.State)
        {
            case RevealFlowState.Starting:
            case RevealFlowState.OpeningAttempted:
                // We own this row (fresh or crash-resumed); proceed with the opening.
                break;
            case RevealFlowState.AwaitingUserResponse:
            case RevealFlowState.PreparingGatePrompt:
            case RevealFlowState.AwaitingPostback:
            case RevealFlowState.Revealing:
            case RevealFlowState.Revealed:
                // The opening already happened — truthful replay, zero new provider calls.
                observability.Suppressed("start", "alreadyStarted");
                return new RevealFlowOutcome("reveal.alreadyStarted", false);
            default:
                observability.Suppressed("start", flow.State.ToString());
                return new RevealFlowOutcome(RevealFlowFailures.OpeningTerminalFailed, false);
        }

        // 5. Durable opening-attempt marker, then the M13-009 one-shot Private Reply.
        //    The deterministic owner "reveal|{flowId}" lets a crash-resumed start continue
        //    a Reserved claim; a different invocation never steals it.
        if (flow.State == RevealFlowState.Starting
            && !await store.TryMarkOpeningAttemptedAsync(flow.Id, clock.UtcNow, cancellationToken))
        {
            return await ReplayAfterRaceAsync(flow.Id, cancellationToken);
        }

        observability.Attempted("opening");
        var opening = await privateReplyCoordinator.ExecuteAsync(new CommentPrivateReplyCommand(
            command.WorkspaceId,
            command.ConnectedAccountId,
            command.ProviderCommentId,
            command.IsLiveComment,
            new InstagramMessageContent.PlainText(flow.Content.OpeningPrivateReplyText),
            "reveal|" + flowId,
            command.NotificationOccurredAtUtc), cancellationToken);

        if (opening.Delivered)
        {
            return await AdoptOpeningAsync(flow, opening.ProviderRecipientId, opening.ProviderMessageId, command.ParticipantIGSID, cancellationToken);
        }

        // Opening not delivered by us. If the one-shot effect already succeeded elsewhere,
        // ADOPT its stored provider identity (M13-009 replay rule) instead of re-sending.
        if (opening.FailureCode == PrivateReplyOutcomeCodes.AlreadyClaimed)
        {
            var claim = await ledger.FindAsync(
                new CommentEffectClaimKey(command.ConnectedAccountId, command.ProviderCommentId, InstagramEffectType.PrivateReply),
                cancellationToken);
            if (claim is { Status: InstagramEffectStatus.Succeeded }
                && claim.ProviderRecipientId is not null
                && claim.ProviderMessageId is not null)
            {
                return await AdoptOpeningAsync(flow, claim.ProviderRecipientId, claim.ProviderMessageId, command.ParticipantIGSID, cancellationToken);
            }

            var code = claim?.Status switch
            {
                InstagramEffectStatus.TerminalFailed => RevealFlowFailures.OpeningTerminalFailed,
                InstagramEffectStatus.Attempting or InstagramEffectStatus.Uncertain => RevealFlowFailures.OpeningUncertain,
                _ => RevealFlowFailures.OpeningClaimedElsewhere,
            };
            observability.Failed("opening", code);
            _ = await store.TryTerminalAsync(flow.Id, code == RevealFlowFailures.OpeningUncertain ? RevealFlowState.Uncertain : RevealFlowState.TerminalFailed, code, clock.UtcNow, cancellationToken);
            return new RevealFlowOutcome(code, false);
        }

        if (opening.FailureCode == PrivateReplyOutcomeCodes.Uncertain)
        {
            observability.Uncertain("opening");
            _ = await store.TryTerminalAsync(flow.Id, RevealFlowState.Uncertain, RevealFlowFailures.OpeningUncertain, clock.UtcNow, cancellationToken);
            return new RevealFlowOutcome(RevealFlowFailures.OpeningUncertain, false);
        }

        var terminalCode = opening.FailureCode?.StartsWith(PrivateReplyOutcomeCodes.PolicyRejected, StringComparison.Ordinal) == true
            ? RevealFlowFailures.OpeningPolicyRejected + opening.FailureCode[PrivateReplyOutcomeCodes.PolicyRejected.Length..]
            : RevealFlowFailures.OpeningTerminalFailed;
        observability.Failed("opening", terminalCode);
        _ = await store.TryTerminalAsync(flow.Id, RevealFlowState.TerminalFailed, terminalCode, clock.UtcNow, cancellationToken);
        return new RevealFlowOutcome(terminalCode, false);
    }

    /// <summary>
    /// Continuation on an inbound user message. The message is the consent/window hinge:
    /// only a qualifying inbound from the exact participant advances the flow — a comment,
    /// a read receipt or a Private Reply alone never does.
    /// </summary>
    public async Task<RevealFlowOutcome> OnUserResponseAsync(
        InstagramMessageReceived message,
        CancellationToken cancellationToken = default)
    {
        if (message.ConnectedAccountId is not { } accountId)
        {
            return new RevealFlowOutcome("reveal.ignored.unenriched", false);
        }

        // Exact correlation first (reply_to.mid = the opening message); otherwise the
        // deterministic single-pending candidate — never First()/Last()/oldest/newest.
        RevealFlowSnapshot? flow;
        if (!string.IsNullOrEmpty(message.RepliedToProviderMessageId))
        {
            flow = await store.GetByOpeningMessageIdAsync(accountId, message.RepliedToProviderMessageId, message.SenderId, cancellationToken);
            if (flow is null)
            {
                observability.Suppressed("userResponse", "noMatchingFlow");
                return new RevealFlowOutcome("reveal.ignored.noMatchingFlow", false);
            }
        }
        else
        {
            var candidates = await store.GetAwaitingResponseAsync(accountId, message.SenderId, cancellationToken);
            if (candidates.Count == 0)
            {
                // No pending reveal continuation — NOT a generic DM trigger (M13-012 owns that).
                observability.Suppressed("userResponse", "noPendingFlow");
                return new RevealFlowOutcome("reveal.ignored.noPendingFlow", false);
            }

            if (candidates.Count > 1)
            {
                // Fail closed: never guess which continuation an ambiguous response belongs to.
                observability.Suppressed("userResponse", "ambiguous");
                return new RevealFlowOutcome(RevealFlowFailures.ParticipantAmbiguous, false);
            }

            flow = candidates[0];
        }

        switch (flow.State)
        {
            case RevealFlowState.AwaitingUserResponse:
                break;
            case RevealFlowState.PreparingGatePrompt when flow.GatePromptStatus == RevealGatePromptStatus.Attempting:
            case RevealFlowState.AwaitingPostback:
                // A NEW inbound message refreshes the window anchor monotonically, but it
                // is never a second gate prompt and never a second reveal: an in-flight or
                // delivered gate attempt stays one-shot (its ambiguous outcome may only be
                // rescued by a valid postback tap).
                _ = await store.TryAdvanceUserResponseAsync(flow.Id, message.SentAtUtc, cancellationToken);
                observability.Suppressed("userResponse", "windowRefreshed");
                return new RevealFlowOutcome("reveal.windowRefreshed", false);
            case RevealFlowState.Revealed:
            case RevealFlowState.Expired:
            case RevealFlowState.TerminalFailed:
            case RevealFlowState.Uncertain:
                return new RevealFlowOutcome(ReplayCode(flow), false);
            default:
                observability.Suppressed("userResponse", "stateMismatch");
                return new RevealFlowOutcome("reveal.ignored.stateMismatch", false);
        }

        // 6. Monotonic window anchor: the new inbound message time.
        if (!await store.TryAdvanceUserResponseAsync(flow.Id, message.SentAtUtc, cancellationToken))
        {
            return await ReplayAfterRaceAsync(flow.Id, cancellationToken);
        }

        // Local guard: queue backlog must not send a gate prompt on a lapsed window.
        if (clock.UtcNow - message.SentAtUtc > DirectWindow)
        {
            _ = await store.TryTerminalAsync(flow.Id, RevealFlowState.Expired, RevealFlowFailures.GatePromptWindowExpired, clock.UtcNow, cancellationToken);
            observability.Failed("gatePrompt", "windowExpired");
            return new RevealFlowOutcome(RevealFlowFailures.GatePromptWindowExpired, false);
        }

        // 7. Local prerequisite BEFORE the attempt marker: a configuration failure must
        //    not consume the one gate-prompt attempt (mirrors M13-009's order).
        var accessToken = await ResolveTokenOrTerminalAsync(flow, RevealFlowFailures.GatePromptRejected, cancellationToken);
        if (accessToken is null)
        {
            return new RevealFlowOutcome(RevealFlowFailures.AccountUnavailable, false);
        }

        // 8. Durable Attempting marker + correlation-token hash BEFORE the provider call.
        var token = RevealCorrelation.Create();
        if (!await store.TryStartGatePromptAsync(flow.Id, token.Sha256Hash, clock.UtcNow, cancellationToken))
        {
            return await ReplayAfterRaceAsync(flow.Id, cancellationToken);
        }

        observability.Attempted("gatePrompt");

        // 9. ONE Direct button-template prompt (M13-010 typed content; declaration order).
        var buttons = new List<InstagramMessageButton>
        {
            new InstagramMessageButton.Postback(flow.Content.PostbackButtonTitle, token.Raw),
        };
        if (!string.IsNullOrWhiteSpace(flow.Content.FollowUrl))
        {
            buttons.Add(new InstagramMessageButton.WebUrl(flow.Content.FollowButtonTitle ?? flow.Content.PostbackButtonTitle, flow.Content.FollowUrl));
        }

        var result = await messagingClient.SendDirectAsync(
            accessToken,
            flow.ParticipantIGSID!,
            new InstagramMessageContent.ButtonTemplate(flow.Content.GatePromptText, buttons),
            cancellationToken);

        return await RecordGatePromptOutcomeAsync(flow, result, cancellationToken);
    }

    /// <summary>
    /// Continuation on a postback tap. Validation order: bounds → correlation token →
    /// durable flow → exact ConnectedAccount → exact sender → expected state → window →
    /// optional follow check → single-reveal authority. Zero provider calls before the
    /// last step, and at most ONE reveal send for the lifetime of the flow.
    /// </summary>
    public async Task<RevealFlowOutcome> OnPostbackAsync(
        InstagramPostbackReceived postback,
        CancellationToken cancellationToken = default)
    {
        if (postback.ConnectedAccountId is not { } accountId)
        {
            return new RevealFlowOutcome("reveal.ignored.unenriched", false);
        }

        if (!RevealCorrelation.TryParse(postback.Payload, out var rawToken))
        {
            observability.Suppressed("postback", "invalid");
            return new RevealFlowOutcome(RevealFlowFailures.CorrelationInvalid, false);
        }

        var flow = await store.GetByCorrelationTokenHashAsync(RevealCorrelation.Hash(rawToken), cancellationToken);
        if (flow is null)
        {
            observability.Suppressed("postback", "unknownToken");
            return new RevealFlowOutcome(RevealFlowFailures.CorrelationInvalid, false);
        }

        // Tamper/binding protection: the token alone is never authority.
        if (flow.ConnectedAccountId != accountId || flow.ParticipantIGSID != postback.SenderId)
        {
            observability.Suppressed("postback", "bindingMismatch");
            return new RevealFlowOutcome(RevealFlowFailures.CorrelationBindingMismatch, false);
        }

        switch (flow.State)
        {
            case RevealFlowState.AwaitingPostback:
                break;
            case RevealFlowState.PreparingGatePrompt when flow.GatePromptStatus == RevealGatePromptStatus.Attempting:
                // Ambiguous gate-prompt rescue: the tap itself proves delivery — safe to continue.
                break;
            case RevealFlowState.Revealed:
                observability.Suppressed("reveal", "alreadyDelivered");
                return new RevealFlowOutcome("reveal.replay.revealed", false);
            case RevealFlowState.Revealing:
            case RevealFlowState.Uncertain:
                observability.Suppressed("reveal", "uncertain");
                return new RevealFlowOutcome(RevealFlowFailures.RevealUncertain, false);
            case RevealFlowState.Expired:
            case RevealFlowState.TerminalFailed:
                return new RevealFlowOutcome(ReplayCode(flow), false);
            default:
                observability.Suppressed("postback", "stateMismatch");
                return new RevealFlowOutcome("reveal.correlation.stateMismatch", false);
        }

        // Local 24h window guard — anchored to the last user message; a postback NEVER
        // refreshes the anchor (no current first-party statement that it does).
        if (flow.LastUserMessageAtUtc is null || clock.UtcNow - flow.LastUserMessageAtUtc.Value > DirectWindow)
        {
            _ = await store.TryTerminalAsync(flow.Id, RevealFlowState.Expired, RevealFlowFailures.WindowExpired, clock.UtcNow, cancellationToken);
            observability.Failed("reveal", "windowExpired");
            return new RevealFlowOutcome(RevealFlowFailures.WindowExpired, false);
        }

        var accessToken = await ResolveTokenOrTerminalAsync(flow, RevealFlowFailures.RevealRejected, cancellationToken);
        if (accessToken is null)
        {
            return new RevealFlowOutcome(RevealFlowFailures.AccountUnavailable, false);
        }

        // Follow gate: only with a proven consent basis (this flow's prior user message)
        // and only when the capability is configured on. Tri-state; unknown never reveals.
        if (flow.FollowGateMode == FollowGateMode.EnabledWhenSupported)
        {
            var relationship = await relationshipClient.GetFollowStateAsync(accessToken, flow.ParticipantIGSID!, cancellationToken);
            switch (FollowGatePolicy.Evaluate(flow.FollowGateMode, relationship))
            {
                case FollowGatePolicy.Verdict.Proceed:
                    _ = await store.RecordFollowCheckAsync(flow.Id, relationship!.State, relationship.UnavailableReason, clock.UtcNow, cancellationToken);
                    break;
                case FollowGatePolicy.Verdict.Blocked:
                    // Reuse the existing prompt; the same button may be tapped again after following.
                    _ = await store.RecordFollowCheckAsync(flow.Id, FollowState.DoesNotFollow, null, clock.UtcNow, cancellationToken);
                    observability.Suppressed("reveal", "followGateBlocked");
                    return new RevealFlowOutcome(RevealFlowFailures.FollowGateBlocked, false);
                default:
                    _ = await store.RecordFollowCheckAsync(flow.Id, FollowState.UnknownUnavailable, relationship?.UnavailableReason, clock.UtcNow, cancellationToken);
                    observability.Suppressed("reveal", "followGateUnavailable");
                    return new RevealFlowOutcome(RevealFlowFailures.FollowGateUnavailable, false);
            }
        }

        // 9. Atomic single-reveal authority (PostgreSQL compare-and-swap).
        if (!await store.TryMarkRevealingAsync(flow.Id, clock.UtcNow, cancellationToken))
        {
            return await ReplayAfterRaceAsync(flow.Id, cancellationToken);
        }

        observability.Attempted("reveal");

        var reveal = await messagingClient.SendDirectAsync(
            accessToken,
            flow.ParticipantIGSID!,
            new InstagramMessageContent.PlainText(flow.Content.RevealText),
            cancellationToken);

        if (reveal.Succeeded)
        {
            if (reveal.ProviderMessageId is not null)
            {
                _ = await store.RecordRevealSuccessAsync(flow.Id, reveal.ProviderMessageId, clock.UtcNow, cancellationToken);
                observability.Succeeded("reveal");
                return new RevealFlowOutcome("reveal.revealed", true);
            }

            // 2xx without provider identity — ambiguous; never a second reveal.
            _ = await store.RecordRevealUncertainAsync(flow.Id, clock.UtcNow, cancellationToken);
            observability.Uncertain("reveal");
            return new RevealFlowOutcome(RevealFlowFailures.RevealUncertain, true);
        }

        var revealFailureCode = RevealFailureCode(reveal, RevealFlowFailures.RevealRejected);
        if (reveal.Failure?.Reason is MessagingFailureReason.TransportFailure or MessagingFailureReason.MalformedResponse)
        {
            _ = await store.RecordRevealUncertainAsync(flow.Id, clock.UtcNow, cancellationToken);
            observability.Uncertain("reveal");
            return new RevealFlowOutcome(RevealFlowFailures.RevealUncertain, true);
        }

        _ = await store.RecordRevealTerminalAsync(flow.Id, revealFailureCode, clock.UtcNow, cancellationToken);
        observability.Failed("reveal", revealFailureCode);
        return new RevealFlowOutcome(revealFailureCode, true);
    }

    private async Task<RevealFlowOutcome> AdoptOpeningAsync(
        RevealFlowSnapshot flow,
        string? providerRecipientId,
        string? providerMessageId,
        string? commandParticipantIgsId,
        CancellationToken cancellationToken)
    {
        var participant = flow.ParticipantIGSID ?? providerRecipientId ?? commandParticipantIgsId;
        if (participant is null)
        {
            // Never fabricate participant identity (no "unknown", no username, no CommentId).
            observability.Failed("opening", RevealFlowFailures.ParticipantUnknown);
            _ = await store.TryTerminalAsync(flow.Id, RevealFlowState.TerminalFailed, RevealFlowFailures.ParticipantUnknown, clock.UtcNow, cancellationToken);
            return new RevealFlowOutcome(RevealFlowFailures.ParticipantUnknown, true);
        }

        var advanced = await store.TryOpenAsync(
            flow.Id,
            providerMessageId ?? string.Empty,
            providerRecipientId ?? string.Empty,
            participant,
            clock.UtcNow,
            cancellationToken);
        if (!advanced)
        {
            // Lost a race (concurrent adoption) — replay the durable state, zero calls.
            return await ReplayAfterRaceAsync(flow.Id, cancellationToken);
        }

        observability.Succeeded("opening");
        return new RevealFlowOutcome("reveal.openingDelivered", true);
    }

    private async Task<RevealFlowOutcome> RecordGatePromptOutcomeAsync(
        RevealFlowSnapshot flow,
        MessagingSendResult result,
        CancellationToken cancellationToken)
    {
        if (result.Succeeded)
        {
            if (result.ProviderMessageId is not null)
            {
                _ = await store.RecordGatePromptSuccessAsync(flow.Id, result.ProviderMessageId, clock.UtcNow, cancellationToken);
                observability.Succeeded("gatePrompt");
                return new RevealFlowOutcome("reveal.gatePrompt.delivered", true);
            }

            _ = await store.RecordGatePromptUncertainAsync(flow.Id, clock.UtcNow, cancellationToken);
            observability.Uncertain("gatePrompt");
            return new RevealFlowOutcome(RevealFlowFailures.GatePromptUncertain, true);
        }

        var code = RevealFailureCode(result, RevealFlowFailures.GatePromptRejected);
        if (result.Failure?.Reason is MessagingFailureReason.TransportFailure or MessagingFailureReason.MalformedResponse)
        {
            _ = await store.RecordGatePromptUncertainAsync(flow.Id, clock.UtcNow, cancellationToken);
            observability.Uncertain("gatePrompt");
            return new RevealFlowOutcome(RevealFlowFailures.GatePromptUncertain, true);
        }

        _ = await store.RecordGatePromptTerminalAsync(flow.Id, code, clock.UtcNow, cancellationToken);
        observability.Failed("gatePrompt", code);
        return new RevealFlowOutcome(code, true);
    }

    private async Task<string?> ResolveTokenOrTerminalAsync(
        RevealFlowSnapshot flow,
        string failureCode,
        CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(flow.ConnectedAccountId, cancellationToken);
        if (account is null || account.WorkspaceId != flow.WorkspaceId || account.IsDisconnected
            || account.Path != ConnectionPath.InstagramLogin)
        {
            _ = await store.TryTerminalAsync(flow.Id, RevealFlowState.TerminalFailed, RevealFlowFailures.AccountUnavailable, clock.UtcNow, cancellationToken);
            observability.Failed("reveal", "accountUnavailable");
            return null;
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            _ = await store.TryTerminalAsync(flow.Id, RevealFlowState.TerminalFailed, RevealFlowFailures.AccountUnavailable, clock.UtcNow, cancellationToken);
            observability.Failed("reveal", "accountUnavailable");
            return null;
        }

        return accessToken;
    }

    private async Task<string?> TryResolveAccountAsync(
        Guid workspaceId,
        Guid connectedAccountId,
        CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(connectedAccountId, cancellationToken);
        if (account is null || account.WorkspaceId != workspaceId || account.IsDisconnected
            || account.Path != ConnectionPath.InstagramLogin)
        {
            return null;
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        return string.IsNullOrEmpty(accessToken) ? null : accessToken;
    }

    private static string RevealFailureCode(MessagingSendResult result, string fallback) =>
        result.Failure?.Reason switch
        {
            MessagingFailureReason.MessagingWindowExpired => RevealFlowFailures.WindowExpired,
            MessagingFailureReason.RejectedByMeta => fallback,
            _ => fallback,
        };

    private static string ReplayCode(RevealFlowSnapshot flow) => flow.State switch
    {
        RevealFlowState.Revealed => "reveal.replay.revealed",
        RevealFlowState.Revealing => RevealFlowFailures.RevealUncertain,
        // Replay the operation-specific durable outcome (gate prompt vs reveal vs expiry).
        RevealFlowState.Uncertain or RevealFlowState.Expired or RevealFlowState.TerminalFailed =>
            flow.FailureCode ?? RevealFlowFailures.RevealUncertain,
        _ => "reveal.replay.inProgress",
    };

    private async Task<RevealFlowOutcome> ReplayAfterRaceAsync(Guid flowId, CancellationToken cancellationToken)
    {
        var flow = await store.GetByIdAsync(flowId, cancellationToken);
        if (flow is null)
        {
            return new RevealFlowOutcome("reveal.replay.missing", false);
        }

        observability.Suppressed("replay", flow.State.ToString());
        return new RevealFlowOutcome(ReplayCode(flow), false);
    }

    private static MessageContentValidationResult ValidateContent(RevealFlowContent content)
    {
        var opening = MessageValidationPolicy.Validate(new InstagramMessageContent.PlainText(content.OpeningPrivateReplyText));
        if (!opening.IsValid)
        {
            return opening;
        }

        var reveal = MessageValidationPolicy.Validate(new InstagramMessageContent.PlainText(content.RevealText));
        if (!reveal.IsValid)
        {
            return reveal;
        }

        if (!string.IsNullOrWhiteSpace(content.FollowUrl) && string.IsNullOrWhiteSpace(content.FollowButtonTitle))
        {
            return MessageContentValidationResult.Invalid(
                MessageContentValidationCode.UrlInvalid, "a follow web-URL button requires a title");
        }

        var buttons = new List<InstagramMessageButton>
        {
            // The placeholder payload matches the real correlation token's size class and
            // is replaced by the actual rv1 token at send time (always within bounds).
            new InstagramMessageButton.Postback(content.PostbackButtonTitle, RevealCorrelation.TokenPurpose + ".placeholder"),
        };
        if (!string.IsNullOrWhiteSpace(content.FollowUrl))
        {
            buttons.Add(new InstagramMessageButton.WebUrl(content.FollowButtonTitle!, content.FollowUrl));
        }

        return MessageValidationPolicy.Validate(new InstagramMessageContent.ButtonTemplate(content.GatePromptText, buttons));
    }
}

/// <summary>
/// Deterministic flow identity per logical origin: a stable Guid derived from
/// (ConnectedAccountId, ProviderCommentId) so redelivery/restart re-enters the SAME row
/// and the SAME Private Reply claim owner. Provider string ids can collide across
/// accounts; the account key keeps the derivation exact-account scoped.
/// </summary>
public static class RevealFlowId
{
    public static Guid For(Guid connectedAccountId, string providerCommentId)
    {
        var material = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(connectedAccountId.ToString("N") + "|" + providerCommentId));
        return new Guid(material.AsSpan(0, 16));
    }
}

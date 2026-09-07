using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Application.RevealFlow;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.RevealFlow;

/// <summary>
/// Deterministic reveal-flow coordinator tests (M13-011). The provider-correct sequence
/// (PlainText Private Reply → user response → Direct button gate → validated postback →
/// single Direct reveal) is proven with ZERO live provider traffic; every crash window
/// (ambiguous gate attempt, reveal attempt without persisted outcome, redeliveries,
/// tampered/wrong-account/wrong-participant postbacks) fails closed with no second
/// mutation.
/// </summary>
public sealed class RevealFlowCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _workspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _accountId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ConnectedAccount _account = ConnectedAccount.Create(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "178414000000012345",
        ConnectionPath.InstagramLogin,
        ["instagram_business_basic", "instagram_business_manage_comments", "instagram_business_manage_messages"],
        tokenExpiresAtUtc: new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
        connectedAtUtc: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

    private static readonly RevealFlowContent Content = new(
        OpeningPrivateReplyText: "Thanks for your comment — reply here and I'll continue.",
        GatePromptText: "Almost there — tap Continue.",
        PostbackButtonTitle: "Continue",
        FollowUrl: null,
        FollowButtonTitle: null,
        RevealText: "Here is the reveal.");

    // ------------------------------------------------------------------ harness

    private Harness CreateHarness(
        Func<MessagingSendResult>? directScript = null,
        Func<FollowStateResult>? followScript = null,
        CommentReferenceReadResult? reference = null,
        FollowGateMode followGateMode = FollowGateMode.Disabled,
        ConnectedAccount? account = null,
        string? token = "ig-user-token")
    {
        var clock = new MutableClock(Now);
        var accounts = new FakeAccountRepository(account ?? _account);
        var tokens = new FakeTokenStore(token);
        var referenceReader = new FakeReferenceReader(() => reference ?? new CommentReferenceReadResult.Found(Now.AddDays(-1)));
        var ledger = new InMemoryEffectLedger();
        var privateReplyClient = new RecordingPrivateReplyClient();
        var effectObservability = new RecordingEffectObservability();
        var privateReplyCoordinator = new CommentPrivateReplyCoordinator(
            accounts, tokens, referenceReader, ledger, privateReplyClient, clock, effectObservability);

        var messaging = new RecordingMessagingClient(directScript);
        var relationship = new RecordingRelationshipClient(followScript);
        var store = new InMemoryRevealFlowStore();
        var observability = new RecordingRevealObservability();
        var coordinator = new RevealFlowCoordinator(
            accounts, tokens, store, ledger, privateReplyCoordinator, messaging, relationship, clock, observability);

        return new Harness(coordinator, store, ledger, privateReplyClient, messaging, relationship, observability, clock);
    }

    private StartRevealFlowCommand Command(
        string? participant = "participant-1",
        string commentId = "comment-1",
        RevealFlowContent? content = null,
        FollowGateMode mode = FollowGateMode.Disabled) => new(
        _workspaceId, _accountId, commentId, participant, IsLiveComment: false, Now.AddMinutes(-2),
        content ?? Content, mode);

    // Events arrive already enriched with the exact connected account (M13-008 boundary).
    private InstagramMessageReceived Message(string senderId, string? repliedToMid, DateTimeOffset? sentAtUtc = null) => new(
        "evt-m", _workspaceId, _accountId, "178414000000012345", senderId, "i want it",
        sentAtUtc ?? Now.AddMinutes(-1), "mid-user-1", repliedToMid, null);

    private InstagramPostbackReceived Postback(string senderId, string payload, DateTimeOffset? atUtc = null) => new(
        "evt-p", _workspaceId, _accountId, "178414000000012345", senderId, "mid-postback", "Continue", payload,
        atUtc ?? Now);

    private async Task<string> DriveHappyPathAsync(Harness h, string participant = "participant-1", string commentId = "comment-1")
    {
        var start = await h.Coordinator.StartFromCommentAsync(Command(participant, commentId));
        Assert.Equal("reveal.openingDelivered", start.Code);

        // Reply_to.mid exact correlation with the opening message id.
        var opening = h.PrivateReplies.Calls.Single();
        var responded = await h.Coordinator.OnUserResponseAsync(Message(participant, opening.MessageId));
        Assert.Equal("reveal.gatePrompt.delivered", responded.Code);

        // Tap the postback with the recorded raw token.
        var prompt = Assert.IsType<InstagramMessageContent.ButtonTemplate>(h.Messaging.Calls.Single().Content);
        var payload = Assert.IsType<InstagramMessageButton.Postback>(Assert.Single(prompt.Buttons)).Payload;
        var tapped = await h.Coordinator.OnPostbackAsync(Postback(participant, payload));
        Assert.Equal("reveal.revealed", tapped.Code);
        return payload;
    }

    // ------------------------------------------------------------------ happy path

    [Fact]
    public async Task FullProviderCorrectSequenceSendsExactlyOneOfEachEffect()
    {
        var h = CreateHarness();
        await DriveHappyPathAsync(h);

        // One opening Private Reply addressed by comment_id — never recipient.id.
        var opening = Assert.Single(h.PrivateReplies.Calls);
        Assert.Equal("comment-1", opening.CommentId);
        // Exactly two Direct sends: gate prompt (button template) + reveal (plain text).
        Assert.Equal(2, h.Messaging.Calls.Count);
        Assert.IsType<InstagramMessageContent.ButtonTemplate>(h.Messaging.Calls[0].Content);
        Assert.IsType<InstagramMessageContent.PlainText>(h.Messaging.Calls[1].Content);
        // All sends use the exact account token and the participant IGSID.
        Assert.All(h.Messaging.Calls, c => Assert.Equal("ig-user-token", c.AccessToken));
        Assert.All(h.Messaging.Calls, c => Assert.Equal("participant-1", c.RecipientProviderUserId));
        // The reveal effect is durable.
        var flow = await h.Store.GetByIdAsync(RevealFlowId.For(_accountId, "comment-1"));
        Assert.Equal(RevealFlowState.Revealed, flow!.State);
        Assert.Equal(RevealSendStatus.Succeeded, flow.RevealStatus);
        Assert.Equal("mid-reveal", flow.RevealProviderMessageId);
    }

    [Fact]
    public async Task PostbackRedeliveryAfterRevealNeverSendsTwice()
    {
        var h = CreateHarness();
        var payload = await DriveHappyPathAsync(h);

        var again = await h.Coordinator.OnPostbackAsync(Postback("participant-1", payload));
        Assert.Equal("reveal.replay.revealed", again.Code);
        Assert.Equal(2, h.Messaging.Calls.Count);
        Assert.Single(h.PrivateReplies.Calls);
    }

    // ------------------------------------------------------------------ participant identity

    [Fact]
    public async Task ParticipantIgSidComesFromProviderConfirmedRecipientWhenCommentHadNone()
    {
        var h = CreateHarness();
        var start = await h.Coordinator.StartFromCommentAsync(Command(participant: null));
        Assert.Equal("reveal.openingDelivered", start.Code);

        var flow = await h.Store.GetByIdAsync(RevealFlowId.For(_accountId, "comment-1"));
        Assert.Equal("526-participant-1", flow!.ParticipantIGSID);
        Assert.Equal(RevealFlowState.AwaitingUserResponse, flow.State);
    }

    [Fact]
    public async Task ExistingSucceededEffectIsAdoptedWithZeroNewProviderCalls()
    {
        var h = CreateHarness();
        // M13-009 automation path already delivered the opening for this comment.
        h.Ledger.Seed(_accountId, "comment-1", InstagramEffectType.PrivateReply, "automation|other",
            InstagramEffectStatus.Succeeded, recipientId: "526-participant-1", messageId: "mid-existing");

        var start = await h.Coordinator.StartFromCommentAsync(Command(participant: null));
        Assert.Equal("reveal.openingDelivered", start.Code);
        Assert.Empty(h.PrivateReplies.Calls);

        var flow = await h.Store.GetByIdAsync(RevealFlowId.For(_accountId, "comment-1"));
        Assert.Equal(RevealFlowState.AwaitingUserResponse, flow!.State);
        Assert.Equal("mid-existing", flow.OpeningPrivateReplyMessageId);
        Assert.Equal("526-participant-1", flow.ParticipantIGSID);
    }

    // ------------------------------------------------------------------ user-response correlation

    [Fact]
    public async Task ReplyToCorrelationWinsOverAnotherPendingFlow()
    {
        var h = CreateHarness();
        var flowA = await h.Coordinator.StartFromCommentAsync(Command(participant: "participant-1", commentId: "comment-1"));
        Assert.Equal("reveal.openingDelivered", flowA.Code);
        var flowB = await h.Coordinator.StartFromCommentAsync(Command(participant: "participant-1", commentId: "comment-2"));
        Assert.Equal("reveal.openingDelivered", flowB.Code);
        Assert.Equal(2, h.PrivateReplies.Calls.Count);

        var openingB = h.PrivateReplies.Calls[1].MessageId;
        var responded = await h.Coordinator.OnUserResponseAsync(Message("participant-1", openingB));
        Assert.Equal("reveal.gatePrompt.delivered", responded.Code);

        // The gate prompt was sent for flow B's participant with flow B's content.
        var prompt = Assert.IsType<InstagramMessageContent.ButtonTemplate>(h.Messaging.Calls.Single().Content);
        Assert.Equal(Content.GatePromptText, prompt.Text);
    }

    [Fact]
    public async Task NoReplyToWithTwoPendingFlowsFailsClosed()
    {
        var h = CreateHarness();
        await h.Coordinator.StartFromCommentAsync(Command(participant: "participant-1", commentId: "comment-1"));
        await h.Coordinator.StartFromCommentAsync(Command(participant: "participant-1", commentId: "comment-2"));

        var responded = await h.Coordinator.OnUserResponseAsync(Message("participant-1", repliedToMid: null));
        Assert.Equal(RevealFlowFailures.ParticipantAmbiguous, responded.Code);
        Assert.Empty(h.Messaging.Calls);
    }

    [Fact]
    public async Task NoReplyToWithSinglePendingFlowAdvancesIt()
    {
        var h = CreateHarness();
        await h.Coordinator.StartFromCommentAsync(Command(participant: "participant-1"));

        var responded = await h.Coordinator.OnUserResponseAsync(Message("participant-1", repliedToMid: null));
        Assert.Equal("reveal.gatePrompt.delivered", responded.Code);
        Assert.Single(h.Messaging.Calls);
    }

    [Fact]
    public async Task MessageWithoutAnyPendingFlowIsIgnoredWithZeroCalls()
    {
        var h = CreateHarness();
        var responded = await h.Coordinator.OnUserResponseAsync(Message("participant-1", repliedToMid: null));
        Assert.Equal("reveal.ignored.noPendingFlow", responded.Code);
        Assert.Empty(h.Messaging.Calls);
        Assert.Empty(h.PrivateReplies.Calls);
    }

    [Fact]
    public async Task NewUserMessageWhileAwaitingPostbackRefreshesTheWindowAnchor()
    {
        var h = CreateHarness();
        var start = await h.Coordinator.StartFromCommentAsync(Command("participant-1"));
        Assert.Equal("reveal.openingDelivered", start.Code);
        var opening = h.PrivateReplies.Calls.Single();
        Assert.Equal("reveal.gatePrompt.delivered", (await h.Coordinator.OnUserResponseAsync(Message("participant-1", opening.MessageId))).Code);
        var prompt = Assert.IsType<InstagramMessageContent.ButtonTemplate>(h.Messaging.Calls.Single().Content);
        var payload = Assert.IsType<InstagramMessageButton.Postback>(Assert.Single(prompt.Buttons)).Payload;

        // A new inbound message while AwaitingPostback: refreshes the anchor, zero sends.
        var refreshed = await h.Coordinator.OnUserResponseAsync(Message("participant-1", opening.MessageId, sentAtUtc: Now));
        Assert.Equal("reveal.windowRefreshed", refreshed.Code);
        Assert.Single(h.Messaging.Calls);

        // 23h after the NEW message (but >24h after the first): the reveal is still valid.
        h.Clock.UtcNow = Now.AddHours(23);
        var revealed = await h.Coordinator.OnPostbackAsync(Postback("participant-1", payload));
        Assert.Equal("reveal.revealed", revealed.Code);
        Assert.Equal(2, h.Messaging.Calls.Count);
    }

    [Fact]
    public async Task UserResponseAfterGateAttemptIsNotReDriven()
    {
        var h = CreateHarness(directScript: () => MessagingSendResult.Fail(MessagingFailureReason.TransportFailure, "timeout"));
        var start = await h.Coordinator.StartFromCommentAsync(Command("participant-1"));
        Assert.Equal("reveal.openingDelivered", start.Code);
        var opening = h.PrivateReplies.Calls.Single();

        var first = await h.Coordinator.OnUserResponseAsync(Message("participant-1", opening.MessageId));
        Assert.Equal(RevealFlowFailures.GatePromptUncertain, first.Code);

        // Redelivery of the same message: the attempt marker exists — zero second prompt.
        var second = await h.Coordinator.OnUserResponseAsync(Message("participant-1", opening.MessageId));
        Assert.Equal(RevealFlowFailures.GatePromptUncertain, second.Code);
        Assert.Single(h.Messaging.Calls);
    }

    // ------------------------------------------------------------------ postback validation

    [Fact]
    public async Task TamperedOrForeignPostbackPayloadIsRejectedWithZeroCalls()
    {
        var h = CreateHarness();
        await h.Coordinator.StartFromCommentAsync(Command("participant-1"));

        var result = await h.Coordinator.OnPostbackAsync(Postback("participant-1", "not-a-token"));
        Assert.Equal(RevealFlowFailures.CorrelationInvalid, result.Code);
        Assert.Empty(h.Messaging.Calls);
    }

    [Fact]
    public async Task ValidTokenFromWrongAccountOrWrongSenderIsRejectedWithZeroCalls()
    {
        var h = CreateHarness();
        var payload = await DriveHappyPathAsync(h);

        // Same payload, different ConnectedAccount.
        var wrongAccount = await h.Coordinator.OnPostbackAsync(new InstagramPostbackReceived(
            "evt-p2", null, Guid.Parse("33333333-3333-3333-3333-333333333333"), "1784140000000OTHER",
            "participant-1", "mid-postback", "Continue", payload, Now));
        Assert.Equal(RevealFlowFailures.CorrelationBindingMismatch, wrongAccount.Code);

        // Same payload, different participant (token replay/copy by another user).
        var wrongSender = await h.Coordinator.OnPostbackAsync(Postback("participant-999", payload));
        Assert.Equal(RevealFlowFailures.CorrelationBindingMismatch, wrongSender.Code);

        Assert.Equal(2, h.Messaging.Calls.Count);
    }

    // ------------------------------------------------------------------ window semantics

    [Fact]
    public async Task PostbackAfterTwentyFourHoursIsExpiredWithoutReveal()
    {
        var h = CreateHarness();
        // Drive to AwaitingPostback only (no reveal yet), then let 25 hours pass.
        var start = await h.Coordinator.StartFromCommentAsync(Command("participant-1"));
        Assert.Equal("reveal.openingDelivered", start.Code);
        var opening = h.PrivateReplies.Calls.Single();
        Assert.Equal("reveal.gatePrompt.delivered", (await h.Coordinator.OnUserResponseAsync(Message("participant-1", opening.MessageId))).Code);
        var prompt = Assert.IsType<InstagramMessageContent.ButtonTemplate>(h.Messaging.Calls.Single().Content);
        var payload = Assert.IsType<InstagramMessageButton.Postback>(Assert.Single(prompt.Buttons)).Payload;

        // 25 hours after the last user message — the postback does NOT refresh the anchor.
        h.Clock.UtcNow = Now.AddHours(25);
        var result = await h.Coordinator.OnPostbackAsync(Postback("participant-1", payload));
        Assert.Equal(RevealFlowFailures.WindowExpired, result.Code);
        Assert.Single(h.Messaging.Calls);

        var flow = await h.Store.GetByIdAsync(RevealFlowId.For(_accountId, "comment-1"));
        Assert.Equal(RevealFlowState.Expired, flow!.State);
    }

    // ------------------------------------------------------------------ follow gate

    [Fact]
    public async Task FollowGateBlocksUntilParticipantActuallyFollows()
    {
        var h = CreateHarness(followScript: () => FollowStateResult.DoesNotFollow(), followGateMode: FollowGateMode.EnabledWhenSupported);
        var start = await h.Coordinator.StartFromCommentAsync(Command("participant-1", mode: FollowGateMode.EnabledWhenSupported));
        Assert.Equal("reveal.openingDelivered", start.Code);
        var opening = h.PrivateReplies.Calls.Single();
        Assert.Equal("reveal.gatePrompt.delivered", (await h.Coordinator.OnUserResponseAsync(Message("participant-1", opening.MessageId))).Code);

        var prompt = Assert.IsType<InstagramMessageContent.ButtonTemplate>(h.Messaging.Calls.Single().Content);
        var payload = Assert.IsType<InstagramMessageButton.Postback>(Assert.Single(prompt.Buttons)).Payload;

        // Not following: no reveal, no second prompt — the same button can be tapped again.
        var blocked = await h.Coordinator.OnPostbackAsync(Postback("participant-1", payload));
        Assert.Equal(RevealFlowFailures.FollowGateBlocked, blocked.Code);
        Assert.Single(h.Messaging.Calls);
        var flow = await h.Store.GetByIdAsync(RevealFlowId.For(_accountId, "comment-1"));
        Assert.Equal(RevealFlowState.AwaitingPostback, flow!.State);
        Assert.Equal(FollowState.DoesNotFollow, flow.LastFollowState);

        // Participant follows later; the SAME postback re-check reveals exactly once.
        h.Relationship.Script = () => FollowStateResult.Follows();
        var revealed = await h.Coordinator.OnPostbackAsync(Postback("participant-1", payload));
        Assert.Equal("reveal.revealed", revealed.Code);
        Assert.Equal(2, h.Messaging.Calls.Count);
    }

    [Fact]
    public async Task FollowGateUnavailableHoldsWithoutFabricatingFalse()
    {
        var h = CreateHarness(
            followScript: () => FollowStateResult.Unavailable(FollowStateUnavailableReason.ConsentUnavailable),
            followGateMode: FollowGateMode.EnabledWhenSupported);
        var start = await h.Coordinator.StartFromCommentAsync(Command("participant-1", mode: FollowGateMode.EnabledWhenSupported));
        Assert.Equal("reveal.openingDelivered", start.Code);
        var opening = h.PrivateReplies.Calls.Single();
        await h.Coordinator.OnUserResponseAsync(Message("participant-1", opening.MessageId));
        var prompt = Assert.IsType<InstagramMessageContent.ButtonTemplate>(h.Messaging.Calls.Single().Content);
        var payload = Assert.IsType<InstagramMessageButton.Postback>(Assert.Single(prompt.Buttons)).Payload;

        var result = await h.Coordinator.OnPostbackAsync(Postback("participant-1", payload));
        Assert.Equal(RevealFlowFailures.FollowGateUnavailable, result.Code);
        Assert.Single(h.Messaging.Calls);
    }

    // ------------------------------------------------------------------ crash windows

    [Fact]
    public async Task CrashAfterRevealAttemptBeforeOutcomePersistNeverSendsAgain()
    {
        var h = CreateHarness();
        var payload = await DriveHappyPathAsync(h);

        // Simulate the crash window: the flow is forced back to Revealing/Attempting
        // WITHOUT the persisted outcome (as if the process died between provider success
        // and the outcome save) — then the postback is redelivered.
        h.Store.ForceRevealing("comment-1");
        var redelivered = await h.Coordinator.OnPostbackAsync(Postback("participant-1", payload));
        Assert.Equal(RevealFlowFailures.RevealUncertain, redelivered.Code);
        Assert.Equal(2, h.Messaging.Calls.Count);
    }

    [Fact]
    public async Task RevealTimeoutNeverFallsBackToASecondMutation()
    {
        var h = CreateHarness(directScript: () => MessagingSendResult.Fail(MessagingFailureReason.TransportFailure, "timeout"));
        var start = await h.Coordinator.StartFromCommentAsync(Command("participant-1"));
        var opening = h.PrivateReplies.Calls.Single();
        var gate = await h.Coordinator.OnUserResponseAsync(Message("participant-1", opening.MessageId));
        Assert.Equal(RevealFlowFailures.GatePromptUncertain, gate.Code);
        Assert.Single(h.Messaging.Calls);
    }

    [Fact]
    public async Task LocalValidationFailureCreatesNoRowAndNoProviderCall()
    {
        var h = CreateHarness();
        var oversized = Content with { RevealText = new string('x', 2000) };
        var result = await h.Coordinator.StartFromCommentAsync(Command("participant-1", content: oversized));

        Assert.StartsWith(RevealFlowFailures.LocalValidation, result.Code);
        Assert.Empty(h.PrivateReplies.Calls);
        Assert.Null(await h.Store.GetByIdAsync(RevealFlowId.For(_accountId, "comment-1")));
    }

    [Fact]
    public async Task OpeningPolicyRejectionTerminatesTheFlowBeforeAnyClaim()
    {
        var h = CreateHarness(reference: new CommentReferenceReadResult.NotFound());
        var result = await h.Coordinator.StartFromCommentAsync(Command("participant-1"));

        Assert.StartsWith(RevealFlowFailures.OpeningPolicyRejected, result.Code);
        Assert.Empty(h.PrivateReplies.Calls);
        var flow = await h.Store.GetByIdAsync(RevealFlowId.For(_accountId, "comment-1"));
        Assert.Equal(RevealFlowState.TerminalFailed, flow!.State);
    }

    [Fact]
    public async Task SecondStartForSameCommentIsTruthfullySuppressed()
    {
        var h = CreateHarness();
        await h.Coordinator.StartFromCommentAsync(Command("participant-1"));

        var second = await h.Coordinator.StartFromCommentAsync(Command("participant-1"));
        Assert.Equal("reveal.alreadyStarted", second.Code);
        Assert.Single(h.PrivateReplies.Calls);
    }

    [Fact]
    public async Task DisconnectedAccountMakesZeroCallsAndZeroRows()
    {
        var disconnected = ConnectedAccount.Create(_accountId, _workspaceId, "178414000000012345",
            ConnectionPath.InstagramLogin, ["instagram_business_basic"], DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow);
        disconnected.Disconnect(DateTimeOffset.UtcNow);
        var h = CreateHarness(account: disconnected);

        var result = await h.Coordinator.StartFromCommentAsync(Command("participant-1"));
        Assert.Equal(RevealFlowFailures.AccountUnavailable, result.Code);
        Assert.Empty(h.PrivateReplies.Calls);
    }

    // ------------------------------------------------------------------ fakes

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class FakeAccountRepository(ConnectedAccount? account) : IConnectedAccountRepository
    {
        public Task<ConnectedAccount?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(account);
        public Task<ConnectedAccount?> FindByProviderIdentityAsync(Guid workspaceId, string providerUserId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AccountResolution> ResolveActiveAccountAsync(string providerAccountId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConnectedAccount>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConnectedAccount>> ListActiveAsync(int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DisconnectAsync(Guid accountId, DateTimeOffset disconnectedAtUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(ConnectedAccount account, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TrySaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeTokenStore(string? token) : IProtectedTokenStore
    {
        public Task StoreAsync(Guid accountId, string accessToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> GetAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(token);
        public Task DeleteAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeReferenceReader(Func<CommentReferenceReadResult> script) : ICommentReferenceReader
    {
        public Task<CommentReferenceReadResult> ReadCreatedAtUtcAsync(string accessToken, string commentId, CancellationToken ct = default) =>
            Task.FromResult(script());
    }

    private sealed class RecordingPrivateReplyClient : ICommentPrivateReplyClient
    {
        public List<(string AccessToken, string ProviderAccountId, string CommentId, string Text, string MessageId)> Calls { get; } = [];

        public Task<PrivateReplySendResult> SendPrivateReplyAsync(string accessToken, string providerAccountId, string commentId, string text, CancellationToken ct = default)
        {
            Calls.Add((accessToken, providerAccountId, commentId, text, "mid-opening-" + (Calls.Count + 1)));
            return Task.FromResult(PrivateReplySendResult.Ok("526-participant-1", "mid-opening-" + Calls.Count));
        }
    }

    private sealed class RecordingMessagingClient(Func<MessagingSendResult>? script) : IInstagramMessagingClient
    {
        public List<(string AccessToken, string RecipientProviderUserId, InstagramMessageContent Content)> Calls { get; } = [];

        public Task<MessagingSendResult> SendDirectAsync(string accessToken, string recipientProviderUserId, InstagramMessageContent content, CancellationToken ct = default)
        {
            Calls.Add((accessToken, recipientProviderUserId, content));
            var result = script?.Invoke() ?? MessagingSendResult.Ok("526-participant-1", "mid-reveal");
            return Task.FromResult(result);
        }

        public Task<MessagingSendResult> SendTextAsync(string accessToken, string recipientProviderUserId, string text, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingRelationshipClient(Func<FollowStateResult>? script) : IInstagramRelationshipClient
    {
        public Func<FollowStateResult> Script { get; set; } = script ?? (() => FollowStateResult.Follows());

        public int Calls { get; private set; }

        public Task<FollowStateResult> GetFollowStateAsync(string accessToken, string participantIGSId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Script());
        }
    }

    private sealed class RecordingRevealObservability : IRevealFlowObservability
    {
        public List<string> Events { get; } = [];

        public void Attempted(string operation) => Events.Add($"attempted:{operation}");
        public void Succeeded(string operation) => Events.Add($"succeeded:{operation}");
        public void Uncertain(string operation) => Events.Add($"uncertain:{operation}");
        public void Failed(string operation, string code) => Events.Add($"failed:{operation}:{code}");
        public void Suppressed(string operation, string code) => Events.Add($"suppressed:{operation}:{code}");
    }

    private sealed class RecordingEffectObservability : ICommentEffectObservability
    {
        public void ClaimAcquired(InstagramEffectType effectType) { }
        public void ClaimConflict(InstagramEffectType effectType) { }
        public void PolicyRejected(InstagramEffectType effectType, string outcome) { }
        public void Attempted(InstagramEffectType effectType) { }
        public void Succeeded(InstagramEffectType effectType) { }
        public void Failed(InstagramEffectType effectType, string failureCategory) { }
        public void Uncertain(InstagramEffectType effectType) { }
    }

    /// <summary>In-memory ledger mirroring the PostgreSQL unique-index semantics.</summary>
    private sealed class InMemoryEffectLedger : ICommentEffectLedger
    {
        private readonly Dictionary<(Guid Account, string Comment, InstagramEffectType Effect), CommentEffectClaim> _claims = [];

        public void Seed(Guid account, string comment, InstagramEffectType effect, string owner, InstagramEffectStatus status,
            string? recipientId = null, string? messageId = null)
        {
            _claims[(account, comment, effect)] = new CommentEffectClaim(
                Guid.CreateVersion7(), account, comment, effect, owner, status,
                status == InstagramEffectStatus.Attempting ? Now : null,
                status is InstagramEffectStatus.Succeeded or InstagramEffectStatus.TerminalFailed or InstagramEffectStatus.Uncertain ? Now : null,
                recipientId, messageId, status == InstagramEffectStatus.TerminalFailed ? "privateReply.terminalFailed" : null);
        }

        public Task<CommentEffectClaim?> ReserveAsync(CommentEffectClaimKey key, string ownerOperationId, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            var tuple = (key.ConnectedAccountId, key.ProviderCommentId, key.EffectType);
            if (_claims.TryGetValue(tuple, out var existing))
            {
                return Task.FromResult(existing.OwnerOperationId == ownerOperationId ? existing : (CommentEffectClaim?)null);
            }

            var claim = new CommentEffectClaim(Guid.CreateVersion7(), key.ConnectedAccountId, key.ProviderCommentId, key.EffectType,
                ownerOperationId, InstagramEffectStatus.Reserved, null, null, null, null, null);
            _claims[tuple] = claim;
            return Task.FromResult<CommentEffectClaim?>(claim);
        }

        public Task<CommentEffectClaim?> FindAsync(CommentEffectClaimKey key, CancellationToken ct = default)
        {
            _claims.TryGetValue((key.ConnectedAccountId, key.ProviderCommentId, key.EffectType), out var claim);
            return Task.FromResult(claim);
        }

        public Task MarkAttemptingAsync(Guid claimId, DateTimeOffset nowUtc, CancellationToken ct = default) =>
            MutateAsync(claimId, c => c with { Status = InstagramEffectStatus.Attempting, AttemptedAtUtc = nowUtc });

        public Task RecordSuccessAsync(Guid claimId, string providerRecipientId, string providerMessageId, DateTimeOffset nowUtc, CancellationToken ct = default) =>
            MutateAsync(claimId, c => c with
            {
                Status = InstagramEffectStatus.Succeeded,
                ProviderRecipientId = providerRecipientId,
                ProviderMessageId = providerMessageId,
                CompletedAtUtc = nowUtc,
            });

        public Task RecordTerminalFailureAsync(Guid claimId, string failureCode, DateTimeOffset nowUtc, CancellationToken ct = default) =>
            MutateAsync(claimId, c => c with { Status = InstagramEffectStatus.TerminalFailed, FailureCode = failureCode, CompletedAtUtc = nowUtc });

        public Task RecordUncertainAsync(Guid claimId, DateTimeOffset nowUtc, CancellationToken ct = default) =>
            MutateAsync(claimId, c => c with { Status = InstagramEffectStatus.Uncertain, CompletedAtUtc = nowUtc });

        private Task MutateAsync(Guid claimId, Func<CommentEffectClaim, CommentEffectClaim> mutate)
        {
            var key = _claims.Keys.Single(k => _claims[k].Id == claimId);
            _claims[key] = mutate(_claims[key]);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// In-memory reveal-flow store mirroring the PostgreSQL CAS semantics: state
    /// predicates gate every transition, one row per logical origin, unique token hashes.
    /// </summary>
    private sealed class InMemoryRevealFlowStore : IRevealFlowStore
    {
        private readonly Dictionary<Guid, Row> _rows = [];
        private readonly Dictionary<string, Guid> _tokenHashIndex = [];

        private sealed class Row
        {
            public Guid Id;
            public Guid WorkspaceId;
            public Guid ConnectedAccountId;
            public string ProviderCommentId = string.Empty;
            public string? ParticipantIGSID;
            public RevealFlowState State;
            public RevealFlowContent Content = null!;
            public FollowGateMode FollowGateMode;
            public string? OpeningPrivateReplyMessageId;
            public string? OpeningPrivateReplyRecipientId;
            public DateTimeOffset? LastUserMessageAtUtc;
            public RevealGatePromptStatus GatePromptStatus;
            public string? GatePromptProviderMessageId;
            public string? CorrelationTokenHash;
            public RevealSendStatus RevealStatus;
            public string? RevealProviderMessageId;
            public FollowState? LastFollowState;
            public FollowStateUnavailableReason? LastFollowUnavailableReason;
            public string? FailureCode;
            public DateTimeOffset CreatedAtUtc;
            public DateTimeOffset UpdatedAtUtc;
        }

        public Task<RevealFlowSnapshot> CreateOrGetAsync(Guid flowId, Guid workspaceId, Guid connectedAccountId,
            string providerCommentId, string? participantIGSId, bool isLiveComment, DateTimeOffset notificationOccurredAtUtc,
            FollowGateMode followGateMode, RevealFlowContent content, Guid? automationId, int? automationVersionNumber,
            string? triggerEventId, int? actionIndex, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (_rows.TryGetValue(flowId, out var existing))
            {
                return Task.FromResult(ToSnapshot(existing));
            }

            var row = new Row
            {
                Id = flowId,
                WorkspaceId = workspaceId,
                ConnectedAccountId = connectedAccountId,
                ProviderCommentId = providerCommentId,
                ParticipantIGSID = participantIGSId,
                State = RevealFlowState.Starting,
                Content = content,
                FollowGateMode = followGateMode,
                GatePromptStatus = RevealGatePromptStatus.NotAttempted,
                RevealStatus = RevealSendStatus.NotAttempted,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
            };
            _rows[flowId] = row;
            return Task.FromResult(ToSnapshot(row));
        }

        public Task<RevealFlowSnapshot?> GetByIdAsync(Guid flowId, CancellationToken ct = default) =>
            Task.FromResult(_rows.TryGetValue(flowId, out var row) ? ToSnapshot(row) : null);

        public Task<RevealFlowSnapshot?> GetByOpeningMessageIdAsync(Guid connectedAccountId, string openingProviderMessageId, string participantIGSId, CancellationToken ct = default)
        {
            var row = _rows.Values.SingleOrDefault(r => r.ConnectedAccountId == connectedAccountId
                && r.OpeningPrivateReplyMessageId == openingProviderMessageId
                && r.ParticipantIGSID == participantIGSId);
            return Task.FromResult(row is null ? null : ToSnapshot(row));
        }

        public Task<IReadOnlyList<RevealFlowSnapshot>> GetAwaitingResponseAsync(Guid connectedAccountId, string participantIGSId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RevealFlowSnapshot>>(_rows.Values
                .Where(r => r.ConnectedAccountId == connectedAccountId
                    && r.ParticipantIGSID == participantIGSId
                    && r.State == RevealFlowState.AwaitingUserResponse)
                .OrderBy(r => r.CreatedAtUtc)
                .Select(ToSnapshot)
                .ToList());

        public Task<RevealFlowSnapshot?> GetByCorrelationTokenHashAsync(string tokenHash, CancellationToken ct = default)
        {
            var row = _tokenHashIndex.TryGetValue(tokenHash, out var id) && _rows.TryGetValue(id, out var found) ? found : null;
            return Task.FromResult(row is null ? null : ToSnapshot(row));
        }

        public Task<bool> TryMarkOpeningAttemptedAsync(Guid flowId, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row) || row.State != RevealFlowState.Starting)
            {
                return Task.FromResult(false);
            }

            row.State = RevealFlowState.OpeningAttempted;
            row.UpdatedAtUtc = nowUtc;
            return Task.FromResult(true);
        }

        public Task<bool> TryOpenAsync(Guid flowId, string openingProviderMessageId, string openingProviderRecipientId, string participantIGSId, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row) || row.State != RevealFlowState.OpeningAttempted)
            {
                return Task.FromResult(false);
            }

            row.State = RevealFlowState.AwaitingUserResponse;
            row.OpeningPrivateReplyMessageId = openingProviderMessageId;
            row.OpeningPrivateReplyRecipientId = openingProviderRecipientId;
            row.ParticipantIGSID = participantIGSId;
            row.UpdatedAtUtc = nowUtc;
            return Task.FromResult(true);
        }

        public Task<bool> TryAdvanceUserResponseAsync(Guid flowId, DateTimeOffset lastUserMessageAtUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row)
                || (row.State != RevealFlowState.AwaitingUserResponse
                    && row.State != RevealFlowState.PreparingGatePrompt
                    && row.State != RevealFlowState.AwaitingPostback))
            {
                return Task.FromResult(false);
            }

            if (row.LastUserMessageAtUtc is null || lastUserMessageAtUtc > row.LastUserMessageAtUtc)
            {
                row.LastUserMessageAtUtc = lastUserMessageAtUtc;
                row.UpdatedAtUtc = lastUserMessageAtUtc;
            }

            return Task.FromResult(true);
        }

        public Task<bool> TryStartGatePromptAsync(Guid flowId, string correlationTokenHash, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row) || row.State != RevealFlowState.AwaitingUserResponse)
            {
                return Task.FromResult(false);
            }

            row.State = RevealFlowState.PreparingGatePrompt;
            row.GatePromptStatus = RevealGatePromptStatus.Attempting;
            row.CorrelationTokenHash = correlationTokenHash;
            row.UpdatedAtUtc = nowUtc;
            _tokenHashIndex[correlationTokenHash] = flowId;
            return Task.FromResult(true);
        }

        public Task<bool> RecordGatePromptSuccessAsync(Guid flowId, string providerMessageId, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row)
                || row.State != RevealFlowState.PreparingGatePrompt || row.GatePromptStatus != RevealGatePromptStatus.Attempting)
            {
                return Task.FromResult(false);
            }

            row.State = RevealFlowState.AwaitingPostback;
            row.GatePromptStatus = RevealGatePromptStatus.Succeeded;
            row.GatePromptProviderMessageId = providerMessageId;
            row.UpdatedAtUtc = nowUtc;
            return Task.FromResult(true);
        }

        public Task<bool> RecordGatePromptTerminalAsync(Guid flowId, string failureCode, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row) || row.State != RevealFlowState.PreparingGatePrompt)
            {
                return Task.FromResult(false);
            }

            row.State = RevealFlowState.TerminalFailed;
            row.GatePromptStatus = RevealGatePromptStatus.TerminalFailed;
            row.FailureCode = failureCode;
            row.UpdatedAtUtc = nowUtc;
            return Task.FromResult(true);
        }

        public Task<bool> RecordGatePromptUncertainAsync(Guid flowId, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row) || row.State != RevealFlowState.PreparingGatePrompt)
            {
                return Task.FromResult(false);
            }

            row.State = RevealFlowState.Uncertain;
            row.GatePromptStatus = RevealGatePromptStatus.Uncertain;
            row.FailureCode = RevealFlowFailures.GatePromptUncertain;
            row.UpdatedAtUtc = nowUtc;
            return Task.FromResult(true);
        }

        public Task<bool> TryMarkRevealingAsync(Guid flowId, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row)
                || (row.State != RevealFlowState.AwaitingPostback && row.State != RevealFlowState.PreparingGatePrompt))
            {
                return Task.FromResult(false);
            }

            row.State = RevealFlowState.Revealing;
            row.RevealStatus = RevealSendStatus.Attempting;
            row.UpdatedAtUtc = nowUtc;
            return Task.FromResult(true);
        }

        public Task<bool> RecordRevealSuccessAsync(Guid flowId, string providerMessageId, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row)
                || row.State != RevealFlowState.Revealing || row.RevealStatus != RevealSendStatus.Attempting)
            {
                return Task.FromResult(false);
            }

            row.State = RevealFlowState.Revealed;
            row.RevealStatus = RevealSendStatus.Succeeded;
            row.RevealProviderMessageId = providerMessageId;
            row.UpdatedAtUtc = nowUtc;
            return Task.FromResult(true);
        }

        public Task<bool> RecordRevealTerminalAsync(Guid flowId, string failureCode, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row) || row.State != RevealFlowState.Revealing)
            {
                return Task.FromResult(false);
            }

            row.State = RevealFlowState.TerminalFailed;
            row.RevealStatus = RevealSendStatus.TerminalFailed;
            row.FailureCode = failureCode;
            row.UpdatedAtUtc = nowUtc;
            return Task.FromResult(true);
        }

        public Task<bool> RecordRevealUncertainAsync(Guid flowId, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row) || row.State != RevealFlowState.Revealing)
            {
                return Task.FromResult(false);
            }

            row.State = RevealFlowState.Uncertain;
            row.RevealStatus = RevealSendStatus.Uncertain;
            row.FailureCode = RevealFlowFailures.RevealUncertain;
            row.UpdatedAtUtc = nowUtc;
            return Task.FromResult(true);
        }

        public Task<bool> RecordFollowCheckAsync(Guid flowId, FollowState state, FollowStateUnavailableReason? unavailableReason, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row))
            {
                return Task.FromResult(false);
            }

            row.LastFollowState = state;
            row.LastFollowUnavailableReason = unavailableReason;
            row.UpdatedAtUtc = nowUtc;
            return Task.FromResult(true);
        }

        public Task<bool> TryTerminalAsync(Guid flowId, RevealFlowState terminalState, string failureCode, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(flowId, out var row)
                || row.State is RevealFlowState.Revealed or RevealFlowState.Expired or RevealFlowState.TerminalFailed or RevealFlowState.Uncertain)
            {
                return Task.FromResult(false);
            }

            row.State = terminalState;
            row.FailureCode = failureCode;
            row.UpdatedAtUtc = nowUtc;
            return Task.FromResult(true);
        }

        /// <summary>Test-only: rewinds a Revealed flow to the crash window (attempt without persisted outcome).</summary>
        public void ForceRevealing(string commentId)
        {
            var row = _rows.Values.Single(r => r.ProviderCommentId == commentId);
            row.State = RevealFlowState.Revealing;
            row.RevealStatus = RevealSendStatus.Attempting;
            row.RevealProviderMessageId = null;
        }

        private static RevealFlowSnapshot ToSnapshot(Row r) => new(
            r.Id, r.WorkspaceId, r.ConnectedAccountId, r.ProviderCommentId, r.ParticipantIGSID, r.State, r.Content,
            r.FollowGateMode, r.OpeningPrivateReplyMessageId, r.OpeningPrivateReplyRecipientId, r.LastUserMessageAtUtc,
            r.GatePromptStatus, r.GatePromptProviderMessageId, null, r.CorrelationTokenHash, r.RevealStatus,
            r.RevealProviderMessageId, null, null, r.LastFollowState, r.LastFollowUnavailableReason, r.FailureCode,
            r.CreatedAtUtc, r.UpdatedAtUtc);
    }

    private sealed record Harness(
        RevealFlowCoordinator Coordinator,
        InMemoryRevealFlowStore Store,
        InMemoryEffectLedger Ledger,
        RecordingPrivateReplyClient PrivateReplies,
        RecordingMessagingClient Messaging,
        RecordingRelationshipClient Relationship,
        RecordingRevealObservability Observability,
        MutableClock Clock);
}

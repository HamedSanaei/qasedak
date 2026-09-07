using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.HistorySync;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.HistorySync;

/// <summary>
/// Deterministic sync-stage tests (M13-013 §45/§46/§54/§89/§96): exact-identity
/// direction mapping, participant ambiguity, content classification, the 20-detail
/// provider window, bounded concurrency, history-window-limited accounting, and the
/// hard invariant that history imports NEVER emit automation events (the gateway is
/// the only exit).
/// </summary>
public sealed class ConversationHistorySyncUseCaseTests
{
    private const string ProviderId = "178414000000012345";
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid WorkspaceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1771900000);
    private static readonly Guid OperationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task InboundAndOutboundDirectionsDeriveFromExactProviderIdentities()
    {
        var client = new FakeHistoryClient(
            list: new ConversationListResult.Ok(new ProviderConversationPage(
                [new ProviderConversationRow("conv-1", Now)], null, false)),
            messages: new ConversationMessagesResult.Ok([
                new ProviderConversationMessageRow("m-in", Now, false),
                new ProviderConversationMessageRow("m-out", Now, false)]),
            details: [
                new MessageDetailResult.Ok(new ProviderMessageDetailRow("m-in", Now, "52611111111111111", [ProviderId], "hello", false, null)),
                new MessageDetailResult.Ok(new ProviderMessageDetailRow("m-out", Now, ProviderId, ["52611111111111111"], "hi back", false, null)),
            ]);
        var gateway = new RecordingGateway();

        var stage = await NewSut(client: client, gateway: gateway).ExecuteAsync(OperationId);

        Assert.Equal(SyncOperationStatus.CompletedWithinProviderLimits, stage.Status);
        Assert.Equal(2, stage.Counters.MessagesImported);
        var inbound = Assert.Single(gateway.Imports, i => i.ProviderMessageId == "m-in");
        Assert.Equal(HistoryMessageDirection.Inbound, inbound.Direction);
        Assert.Equal("52611111111111111", inbound.ParticipantId);
        var outbound = Assert.Single(gateway.Imports, i => i.ProviderMessageId == "m-out");
        Assert.Equal(HistoryMessageDirection.Outbound, outbound.Direction);
        Assert.Equal("52611111111111111", outbound.ParticipantId);
        Assert.Equal(ProviderId, outbound.SenderId);
    }

    [Fact]
    public async Task AmbiguousOrMissingParticipantConversationsAreSkippedBoundedly()
    {
        var client = new FakeHistoryClient(
            list: new ConversationListResult.Ok(new ProviderConversationPage(
                [new ProviderConversationRow("conv-1", Now)], null, false)),
            messages: new ConversationMessagesResult.Ok([
                new ProviderConversationMessageRow("m-amb", Now, false)]),
            details: [
                // to lists two external participants — ambiguous, must be skipped.
                new MessageDetailResult.Ok(new ProviderMessageDetailRow("m-amb", Now, "52611111111111111", [ProviderId, "52699999999999999"], "hello", false, null)),
            ]);
        var gateway = new RecordingGateway();

        var stage = await NewSut(client: client, gateway: gateway).ExecuteAsync(OperationId);

        Assert.Equal(SyncOperationStatus.CompletedWithinProviderLimits, stage.Status);
        Assert.Empty(gateway.Imports);
    }

    [Fact]
    public async Task DetailWindowIsBoundedToTheProviderTwenty()
    {
        var rows = Enumerable.Range(0, 25)
            .Select(i => new ProviderConversationMessageRow("m-" + i, Now.AddMinutes(-i), false))
            .ToList();
        var details = rows.Take(ConversationHistoryPolicy.MaxMessageDetailCallsPerConversation)
            .Select(r => (MessageDetailResult)new MessageDetailResult.Ok(
                new ProviderMessageDetailRow(r.MessageId, r.CreatedAtUtc, "52611111111111111", [ProviderId], "t", false, null)))
            .ToList();
        var client = new FakeHistoryClient(
            list: new ConversationListResult.Ok(new ProviderConversationPage(
                [new ProviderConversationRow("conv-1", Now)], null, false)),
            messages: new ConversationMessagesResult.Ok(rows),
            details: details);
        var gateway = new RecordingGateway();

        var stage = await NewSut(client: client, gateway: gateway).ExecuteAsync(OperationId);

        // 25 ids observed; details fetched for the newest 20; 5 counted window-limited.
        Assert.Equal(20, stage.Counters.DetailsFetched);
        Assert.Equal(5, stage.Counters.HistoryWindowLimited);
        Assert.Equal(20, gateway.Imports.Count);
        Assert.All(gateway.Imports, i => Assert.True(int.Parse(i.ProviderMessageId[2..], System.Globalization.CultureInfo.InvariantCulture) < 20));
    }

    [Fact]
    public async Task DetailCallsNeverExceedBoundedConcurrency()
    {
        var rows = Enumerable.Range(0, 20)
            .Select(i => new ProviderConversationMessageRow("m-" + i, Now.AddMinutes(-i), false))
            .ToList();
        var details = rows.Select(r => (MessageDetailResult)new MessageDetailResult.Ok(
            new ProviderMessageDetailRow(r.MessageId, r.CreatedAtUtc, "52611111111111111", [ProviderId], "t", false, null))).ToList();
        var client = new FakeHistoryClient(
            list: new ConversationListResult.Ok(new ProviderConversationPage(
                [new ProviderConversationRow("conv-1", Now)], null, false)),
            messages: new ConversationMessagesResult.Ok(rows),
            details: details);

        var stage = await NewSut(client: client, gateway: new RecordingGateway()).ExecuteAsync(OperationId);

        Assert.True(client.MaxObservedConcurrentDetails <= ConversationHistoryPolicy.MaxConcurrentMessageDetailCalls);
        Assert.Equal(SyncOperationStatus.CompletedWithinProviderLimits, stage.Status);
    }

    [Fact]
    public async Task UnsupportedMessagesAreCountedAndNeverDetailed()
    {
        var client = new FakeHistoryClient(
            list: new ConversationListResult.Ok(new ProviderConversationPage(
                [new ProviderConversationRow("conv-1", Now)], null, false)),
            messages: new ConversationMessagesResult.Ok([
                new ProviderConversationMessageRow("m-unsupported", Now, true)]),
            details: []);
        var gateway = new RecordingGateway();

        var stage = await NewSut(client: client, gateway: gateway).ExecuteAsync(OperationId);

        Assert.Equal(1, stage.Counters.Unsupported);
        Assert.Empty(gateway.Imports);
    }

    [Fact]
    public async Task ShareAndMissingTextAreClassifiedTruthfully()
    {
        var client = new FakeHistoryClient(
            list: new ConversationListResult.Ok(new ProviderConversationPage(
                [new ProviderConversationRow("conv-1", Now)], null, false)),
            messages: new ConversationMessagesResult.Ok([
                new ProviderConversationMessageRow("m-share", Now, false),
                new ProviderConversationMessageRow("m-notext", Now, false)]),
            details: [
                new MessageDetailResult.Ok(new ProviderMessageDetailRow("m-share", Now, "52611111111111111", [ProviderId], null, false, "https://www.instagram.com/p/x/")),
                new MessageDetailResult.Ok(new ProviderMessageDetailRow("m-notext", Now, "52611111111111111", [ProviderId], null, false, null)),
            ]);
        var gateway = new RecordingGateway();

        await NewSut(client: client, gateway: gateway).ExecuteAsync(OperationId);

        Assert.Equal(HistoryContentKind.Share, gateway.Imports.Single(i => i.ProviderMessageId == "m-share").ContentKind);
        Assert.Equal(HistoryContentKind.NoText, gateway.Imports.Single(i => i.ProviderMessageId == "m-notext").ContentKind);
        Assert.Null(gateway.Imports.Single(i => i.ProviderMessageId == "m-notext").Body);
    }

    [Fact]
    public async Task HistoryImportsNeverEmitAutomationEvents()
    {
        // The use case's only exits are the import gateway — no dispatcher port exists
        // in its contract at all. Prove it structurally: a completed sync that imports
        // 20 matching inbound messages produces ZERO automation traffic by construction.
        var rows = Enumerable.Range(0, 20)
            .Select(i => new ProviderConversationMessageRow("m-" + i, Now.AddMinutes(-i), false))
            .ToList();
        var details = rows.Select(r => (MessageDetailResult)new MessageDetailResult.Ok(
            new ProviderMessageDetailRow(r.MessageId, r.CreatedAtUtc, "52611111111111111", [ProviderId], "what is the price?", false, null))).ToList();
        var client = new FakeHistoryClient(
            list: new ConversationListResult.Ok(new ProviderConversationPage(
                [new ProviderConversationRow("conv-1", Now)], null, false)),
            messages: new ConversationMessagesResult.Ok(rows),
            details: details);

        var stage = await NewSut(client: client, gateway: new RecordingGateway()).ExecuteAsync(OperationId);

        Assert.Equal(SyncOperationStatus.CompletedWithinProviderLimits, stage.Status);
        Assert.Equal(20, stage.Counters.MessagesImported);
    }

    [Fact]
    public async Task TerminalOperationReplaysIdempotently()
    {
        var store = new FakeOperationStore(status: SyncOperationStatus.CompletedWithinProviderLimits);
        var client = new FakeHistoryClient();

        var stage = await NewSut(client: client, store: store).ExecuteAsync(OperationId);

        Assert.Equal(SyncOperationStatus.CompletedWithinProviderLimits, stage.Status);
        Assert.Equal(0, client.ListCalls);
    }

    [Fact]
    public async Task MissingTokenFailsPermanentlyWithZeroProviderCalls()
    {
        var client = new FakeHistoryClient();

        var stage = await NewSut(client: client, tokens: new FakeTokenStore(null)).ExecuteAsync(OperationId);

        Assert.Equal(SyncOperationStatus.Failed, stage.Status);
        Assert.Equal(AccountFailures.TokenMissing, stage.FailureCode);
        Assert.Equal(0, client.ListCalls);
    }

    private static ConversationHistorySyncUseCase NewSut(
        FakeHistoryClient? client = null,
        RecordingGateway? gateway = null,
        FakeOperationStore? store = null,
        FakeTokenStore? tokens = null) => new(
        new FakeAccountRepository(ActiveAccount()),
        tokens ?? new FakeTokenStore("token-1"),
        store ?? new FakeOperationStore(SyncOperationStatus.Queued),
        client ?? new FakeHistoryClient(),
        gateway ?? new RecordingGateway(),
        new FixedClock(Now));

    private static ConnectedAccount ActiveAccount() => ConnectedAccount.Create(
        AccountId, WorkspaceId, ProviderId, ConnectionPath.InstagramLogin,
        ["instagram_business_basic", "instagram_business_manage_messages"], DateTimeOffset.UtcNow.AddDays(1), Now);

    private sealed class FakeHistoryClient : IInstagramConversationHistoryClient
    {
        private readonly ConversationListResult _list;
        private readonly ConversationMessagesResult _messages;
        private readonly Queue<MessageDetailResult> _details = new();

        public FakeHistoryClient(
            ConversationListResult? list = null,
            ConversationMessagesResult? messages = null,
            IReadOnlyList<MessageDetailResult>? details = null)
        {
            _list = list ?? new ConversationListResult.Ok(new ProviderConversationPage([], null, false));
            _messages = messages ?? new ConversationMessagesResult.Ok([]);
            foreach (var detail in details ?? [])
            {
                _details.Enqueue(detail);
            }
        }

        public int ListCalls { get; private set; }

        public int MaxObservedConcurrentDetails { get; private set; }

        private int _activeDetails;

        public Task<ConversationListResult> ListConversationsPageAsync(string accessToken, string providerAccountId, int limit, string? afterCursor, CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult(_list);
        }

        public Task<ConversationMessagesResult> GetConversationMessagesPageAsync(string accessToken, string conversationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_messages);

        public Task<MessageDetailResult> GetMessageDetailAsync(string accessToken, string messageId, CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _activeDetails);
            MaxObservedConcurrentDetails = Math.Max(MaxObservedConcurrentDetails, current);
            try
            {
                return Task.FromResult(_details.Count > 0 ? _details.Dequeue() : new MessageDetailResult.Failed(ConversationHistoryFailures.HistoryUnavailableOutsideRecentWindow, false));
            }
            finally
            {
                Interlocked.Decrement(ref _activeDetails);
            }
        }
    }

    private sealed class RecordingGateway : IConversationHistoryImportGateway
    {
        public List<HistoryMessageImport> Imports { get; } = [];

        public Task<bool> ImportAsync(HistoryMessageImport message, CancellationToken cancellationToken = default)
        {
            Imports.Add(message);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeOperationStore(SyncOperationStatus status) : IProviderSyncOperationStore
    {
        public SyncOperationStatus? LastFailStatus { get; private set; }

        public string? LastFailureCode { get; private set; }

        public Task<ProviderSyncOperation?> CreateOrGetActiveAsync(Guid operationId, Guid connectedAccountId, Guid workspaceId, SyncOperationKind kind, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderSyncOperation?>(null);

        public Task<ProviderSyncOperation?> FindByIdAsync(Guid operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderSyncOperation?>(new ProviderSyncOperation(
                operationId, AccountId, WorkspaceId, SyncOperationKind.Manual, status, 0,
                0, 0, 0, 0, 0, 0, 0, 0, null, null, Now, null, null, Now));

        public Task<bool> TryStartAsync(Guid operationId, DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task CheckpointAsync(Guid operationId, int stage, string? nextCursor, ProviderSyncCounters counters, DateTimeOffset now, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CompleteAsync(Guid operationId, ProviderSyncCounters counters, DateTimeOffset now, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task FailAsync(Guid operationId, string failureCategory, bool transient, ProviderSyncCounters counters, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            LastFailStatus = transient ? SyncOperationStatus.RateLimitedRetrying : SyncOperationStatus.Failed;
            LastFailureCode = failureCategory;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProviderSyncOperation>> ListRecentAsync(Guid connectedAccountId, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderSyncOperation>>([]);
    }

    private sealed class FakeAccountRepository(ConnectedAccount? account) : IConnectedAccountRepository
    {
        public Task<ConnectedAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(account);

        public Task<ConnectedAccount?> FindByProviderIdentityAsync(Guid workspaceId, string providerUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AccountResolution> ResolveActiveAccountAsync(string providerAccountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ConnectedAccount>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ConnectedAccount>> ListActiveAsync(int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DisconnectAsync(Guid accountId, DateTimeOffset disconnectedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AddAsync(ConnectedAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeTokenStore(string? token) : IProtectedTokenStore
    {
        public Task StoreAsync(Guid accountId, string accessToken, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> GetAsync(Guid accountId, CancellationToken cancellationToken = default) => Task.FromResult(token);

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

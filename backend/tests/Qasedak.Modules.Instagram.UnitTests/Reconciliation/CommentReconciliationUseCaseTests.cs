using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Application.Reconciliation;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Reconciliation;

/// <summary>
/// Deterministic sweep tests (M13-013 §86): zero provider traffic without active
/// comment automation, specific-media targeting, AnySource union/dedupe, self-comment
/// skip, unknown-author fail-closed, recovered comments entering the SAME semantic
/// dispatcher path with provider comment identity, cursor caps/loops, page caps,
/// rate-limit/transient/permanent outcomes and wrong-account isolation.
/// </summary>
public sealed class CommentReconciliationUseCaseTests
{
    private const string ProviderId = "178414000000012345";
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid WorkspaceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1771900000);

    [Fact]
    public async Task NoActiveCommentAutomationCausesZeroProviderCalls()
    {
        var comments = new FakeCommentHistoryClient();
        var media = new FakeMediaCatalogClient();
        var scope = new FakeScopeQuery(new CommentReconciliationScope(
            HasAnySourceCommentAutomation: false, SpecificSourceMediaIds: []));

        var outcome = await NewSut(comments: comments, media: media, scope: scope).ExecuteAsync(AccountId);

        Assert.False(outcome.ProviderTrafficAttempted);
        Assert.Equal(0, comments.Calls);
        Assert.Equal(0, media.Calls);
    }

    [Fact]
    public async Task NoScopeAtAllCausesZeroProviderCalls()
    {
        var comments = new FakeCommentHistoryClient();
        var media = new FakeMediaCatalogClient();

        var outcome = await NewSut(comments: comments, media: media, scope: new FakeScopeQuery(null)).ExecuteAsync(AccountId);

        Assert.False(outcome.ProviderTrafficAttempted);
        Assert.Equal(0, comments.Calls);
        Assert.Equal(0, media.Calls);
    }

    [Fact]
    public async Task DisconnectedAccountCausesZeroProviderCalls()
    {
        var comments = new FakeCommentHistoryClient();
        var disconnected = ConnectedAccount.Create(
            AccountId, WorkspaceId, ProviderId, ConnectionPath.InstagramLogin,
            ["instagram_business_basic"], DateTimeOffset.UtcNow.AddDays(1), Now);
        disconnected.Disconnect(Now);
        var accounts = new FakeAccountRepository(disconnected);

        var outcome = await NewSut(comments: comments, accounts: accounts).ExecuteAsync(AccountId);

        Assert.False(outcome.ProviderTrafficAttempted);
        Assert.Equal(0, comments.Calls);
    }

    [Fact]
    public async Task SpecificMediaIsScannedDirectlyAndMediaCatalogIsNotCalled()
    {
        var comments = new FakeCommentHistoryClient(Page(
            Row("c1", FromId: "52611111111111111", Text: "what is the price?", MediaId: "media-1")));
        var media = new FakeMediaCatalogClient();
        var scope = new FakeScopeQuery(new CommentReconciliationScope(false, ["media-1"]));

        var outcome = await NewSut(comments: comments, media: media, scope: scope).ExecuteAsync(AccountId);

        Assert.Equal(["media-1"], comments.MediaIds);
        Assert.Equal(1, outcome.CommentsDispatched);
        Assert.Equal(0, media.Calls);
    }

    [Fact]
    public async Task AnySourceReusesBoundedRecentMediaTraversal()
    {
        var comments = new FakeCommentHistoryClient(Page());
        var media = new FakeMediaCatalogClient(new MediaCatalogPage(
            [new MediaCatalogItem("media-9", null, MediaKind.Image, null, null, null, null, null, false, false, null, null, null)],
            NextCursor: null, HasMore: false));
        var scope = new FakeScopeQuery(new CommentReconciliationScope(true, []));

        var outcome = await NewSut(comments: comments, media: media, scope: scope).ExecuteAsync(AccountId);

        Assert.Equal(1, media.Calls);
        Assert.Equal(["media-9"], comments.MediaIds);
        Assert.True(outcome.ProviderTrafficAttempted);
    }

    [Fact]
    public async Task SpecificPlusAnySourceUnionIsDeduplicated()
    {
        var comments = new FakeCommentHistoryClient(Page());
        var media = new FakeMediaCatalogClient(new MediaCatalogPage(
            [new MediaCatalogItem("media-1", null, MediaKind.Image, null, null, null, null, null, false, false, null, null, null),
             new MediaCatalogItem("media-2", null, MediaKind.Image, null, null, null, null, null, false, false, null, null, null)],
            NextCursor: null, HasMore: false));
        var scope = new FakeScopeQuery(new CommentReconciliationScope(true, ["media-1"]));

        await NewSut(comments: comments, media: media, scope: scope).ExecuteAsync(AccountId);

        Assert.Equal(["media-1", "media-2"], comments.MediaIds);
    }

    [Fact]
    public async Task SelfCommentIsSkippedWithZeroDispatch()
    {
        var dispatcher = new RecordingDispatcher();
        var comments = new FakeCommentHistoryClient(Page(
            Row("c1", FromId: ProviderId, Text: "our own comment", MediaId: "media-1")));
        var scope = new FakeScopeQuery(new CommentReconciliationScope(false, ["media-1"]));

        var outcome = await NewSut(comments: comments, dispatcher: dispatcher, scope: scope).ExecuteAsync(AccountId);

        Assert.Equal(1, outcome.SelfSkipped);
        Assert.Equal(0, outcome.CommentsDispatched);
        Assert.Empty(dispatcher.Events);
    }

    [Fact]
    public async Task CommentWithoutAuthorIdFailsClosedNeverDispatched()
    {
        var dispatcher = new RecordingDispatcher();
        var comments = new FakeCommentHistoryClient(Page(
            Row("c1", FromId: null, Text: "what is the price?", MediaId: "media-1")));
        var scope = new FakeScopeQuery(new CommentReconciliationScope(false, ["media-1"]));

        var outcome = await NewSut(comments: comments, dispatcher: dispatcher, scope: scope).ExecuteAsync(AccountId);

        Assert.Equal(1, outcome.UnknownAuthorSkipped);
        Assert.Equal(0, outcome.CommentsDispatched);
        Assert.Empty(dispatcher.Events);
    }

    [Fact]
    public async Task RecoveredCommentDispatchesThroughTheSemanticDispatcherWithProviderCommentId()
    {
        var dispatcher = new RecordingDispatcher();
        var comments = new FakeCommentHistoryClient(Page(
            Row("c-42", FromId: "52611111111111111", Text: "what is the price?", MediaId: "media-1")));
        var scope = new FakeScopeQuery(new CommentReconciliationScope(false, ["media-1"]));

        var outcome = await NewSut(comments: comments, dispatcher: dispatcher, scope: scope).ExecuteAsync(AccountId);

        var evt = Assert.Single(dispatcher.Events);
        var comment = Assert.IsType<InstagramCommentCreated>(evt);
        Assert.Equal("c-42", comment.CommentId);
        Assert.Equal(AccountId, comment.ConnectedAccountId);
        Assert.Equal(WorkspaceId, comment.WorkspaceId);
        Assert.Equal("52611111111111111", comment.FromId);
        Assert.Equal("media-1", comment.MediaId);
        Assert.Null(comment.OriginalMediaId);
        Assert.Equal(Now.AddMinutes(-5), comment.CreatedAtUtc);
        Assert.Equal(1, outcome.CommentsDispatched);
    }

    [Fact]
    public async Task OlderThanHorizonCommentsAreNotDispatched()
    {
        var dispatcher = new RecordingDispatcher();
        var comments = new FakeCommentHistoryClient(Page(
            Row("c-old", FromId: "52611111111111111", Text: "what is the price?", MediaId: "media-1", Age: TimeSpan.FromDays(30))));
        var scope = new FakeScopeQuery(new CommentReconciliationScope(false, ["media-1"]));

        var outcome = await NewSut(comments: comments, dispatcher: dispatcher, scope: scope).ExecuteAsync(AccountId);

        Assert.Equal(0, outcome.CommentsDispatched);
        Assert.Empty(dispatcher.Events);
    }

    [Fact]
    public async Task CursorLoopStopsTraversalBoundedly()
    {
        var dispatcher = new RecordingDispatcher();
        var comments = new FakeCommentHistoryClient(
            Page(NextCursor: "same-cursor", HasMore: true),
            Page(NextCursor: "same-cursor", HasMore: true));
        var scope = new FakeScopeQuery(new CommentReconciliationScope(false, ["media-1"]));

        var outcome = await NewSut(comments: comments, dispatcher: dispatcher, scope: scope).ExecuteAsync(AccountId);

        Assert.Equal(CommentHistoryFailures.CursorLoop, outcome.FailureCode);
        Assert.Equal(2, comments.Calls);
    }

    [Fact]
    public async Task PageCapBoundsTraversalPerMedia()
    {
        var dispatcher = new RecordingDispatcher();
        var comments = new FakeCommentHistoryClient(
            Page(NextCursor: "c1", HasMore: true),
            Page(NextCursor: "c2", HasMore: true),
            Page(NextCursor: "c3", HasMore: true),
            Page(NextCursor: "c4", HasMore: true));
        var scope = new FakeScopeQuery(new CommentReconciliationScope(false, ["media-1"]));

        await NewSut(comments: comments, dispatcher: dispatcher, scope: scope).ExecuteAsync(AccountId);

        Assert.Equal(CommentReconciliationPolicy.MaxCommentPagesPerMedia, comments.Calls);
    }

    [Fact]
    public async Task RateLimitedFailureIsReportedTransient()
    {
        var comments = new FakeCommentHistoryClient(new CommentHistoryResult.Failed(CommentHistoryFailures.RateLimited, Transient: true));
        var scope = new FakeScopeQuery(new CommentReconciliationScope(false, ["media-1"]));

        var outcome = await NewSut(comments: comments, scope: scope).ExecuteAsync(AccountId);

        Assert.True(outcome.RateLimited);
        Assert.Equal(CommentHistoryFailures.RateLimited, outcome.FailureCode);
    }

    [Fact]
    public async Task WrongAccountSameCommentIdIsIsolatedByExactAccountScope()
    {
        var dispatcher = new RecordingDispatcher();
        var otherAccountId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var comments = new FakeCommentHistoryClient(Page(
            Row("c-same", FromId: "52611111111111111", Text: "what is the price?", MediaId: "media-1")));
        var scope = new FakeScopeQuery(new CommentReconciliationScope(false, ["media-1"]));
        var accounts = new FakeAccountRepository(ConnectedAccount.Create(
            otherAccountId, WorkspaceId, "178414000000099999", ConnectionPath.InstagramLogin,
            ["instagram_business_basic"], DateTimeOffset.UtcNow.AddDays(1), Now));

        await NewSut(comments: comments, dispatcher: dispatcher, scope: scope, accounts: accounts).ExecuteAsync(otherAccountId);

        var evt = Assert.IsType<InstagramCommentCreated>(Assert.Single(dispatcher.Events));
        Assert.Equal(otherAccountId, evt.ConnectedAccountId);
        Assert.Equal("178414000000099999", evt.ProviderAccountId);
    }

    private static CommentReconciliationUseCase NewSut(
        FakeCommentHistoryClient? comments = null,
        FakeMediaCatalogClient? media = null,
        FakeScopeQuery? scope = null,
        RecordingDispatcher? dispatcher = null,
        FakeAccountRepository? accounts = null,
        FakeTokenStore? tokens = null) => new(
        accounts ?? new FakeAccountRepository(ActiveAccount()),
        tokens ?? new FakeTokenStore("token-1"),
        scope ?? new FakeScopeQuery(new CommentReconciliationScope(true, [])),
        media ?? new FakeMediaCatalogClient(),
        comments ?? new FakeCommentHistoryClient(Page()),
        dispatcher ?? new RecordingDispatcher(),
        new FixedClock(Now));

    private static ConnectedAccount ActiveAccount() => ConnectedAccount.Create(
        AccountId, WorkspaceId, ProviderId, ConnectionPath.InstagramLogin,
        ["instagram_business_basic"], DateTimeOffset.UtcNow.AddDays(1), Now);

    private static ProviderCommentRow Row(
        string id, string? FromId, string Text, string MediaId, TimeSpan? Age = null) => new(
        id, FromId, "user-" + id, Text, Now.AddMinutes(-5) - (Age ?? TimeSpan.Zero), MediaId, null, false);

    private static CommentHistoryPage Page(
        params ProviderCommentRow[] rows) => new(rows, null, false);

    private static CommentHistoryPage Page(string? NextCursor, bool HasMore, params ProviderCommentRow[] rows) =>
        new(rows, NextCursor, HasMore);

    private sealed class FakeScopeQuery(CommentReconciliationScope? scope) : ICommentReconciliationScopeQuery
    {
        public Task<CommentReconciliationScope?> ResolveAsync(Guid workspaceId, Guid connectedAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(scope);
    }

    private sealed class FakeCommentHistoryClient : IInstagramCommentHistoryClient
    {
        private readonly Queue<CommentHistoryResult> _results = new();

        public FakeCommentHistoryClient()
        {
        }

        public FakeCommentHistoryClient(params CommentHistoryResult[] results)
        {
            foreach (var result in results)
            {
                _results.Enqueue(result);
            }
        }

        public FakeCommentHistoryClient(params CommentHistoryPage[] pages)
        {
            foreach (var page in pages)
            {
                _results.Enqueue(new CommentHistoryResult.Ok(page));
            }
        }

        public int Calls { get; private set; }

        public List<string> MediaIds { get; } = [];

        public Task<CommentHistoryResult> ListCommentsPageAsync(
            string accessToken, string providerAccountId, string mediaId, int limit, string? afterCursor, CancellationToken cancellationToken = default)
        {
            Calls++;
            MediaIds.Add(mediaId);
            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : new CommentHistoryResult.Ok(new CommentHistoryPage([], null, false)));
        }
    }

    private sealed class FakeMediaCatalogClient : IMediaCatalogClient
    {
        private readonly MediaCatalogResult _result;

        public FakeMediaCatalogClient(MediaCatalogPage? page = null) =>
            _result = new MediaCatalogResult.Ok(page ?? new MediaCatalogPage([], null, false));

        public int Calls { get; private set; }

        public Task<MediaCatalogResult> GetPageAsync(string accessToken, string providerAccountId, Guid accountId, int limit, string? afterCursor, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }

        public Task<MediaCatalogResult> GetRecentAsync(string accessToken, string providerAccountId, Guid accountId, int maxItems, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingDispatcher : IIntegrationEventDispatcher
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(integrationEvent);
            return Task.CompletedTask;
        }
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

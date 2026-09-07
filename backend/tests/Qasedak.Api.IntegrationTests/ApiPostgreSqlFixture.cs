using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Qasedak.Api.CrossModule;
using Qasedak.BuildingBlocks.Application.Auditing;
using Qasedak.BuildingBlocks.Infrastructure.Auditing;
using Qasedak.BuildingBlocks.Infrastructure.Scheduling;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Qasedak.Modules.Billing.Application.Payments;
using Qasedak.Modules.Billing.Infrastructure.Persistence;
using Qasedak.Modules.Contacts.Infrastructure.Persistence;
using Qasedak.Modules.Conversations.Infrastructure.Persistence;
using Qasedak.Modules.Identity.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Application.Insights;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Application.RevealFlow;
using Qasedak.Modules.Instagram.Application.Subscriptions;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Infrastructure.Media;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// Deterministic recording stand-in for Meta's messaging API: outbound DMs are captured
/// for assertions instead of leaving CI. The 24h-window behavior is simulated by
/// <see cref="RejectRecipientsOutsideWindow"/> toggles, mirroring Graph error code 490.
/// </summary>
public sealed class RecordingInstagramMessagingClient : IInstagramMessagingClient
{
    public List<(string AccessToken, string RecipientId, string Text)> Sends { get; } = [];

    /// <summary>Typed sends (M13-010) with the exact content kind for template assertions.</summary>
    public List<(string AccessToken, string RecipientId, InstagramMessageContent Content)> TypedSends { get; } = [];

    public HashSet<string> RejectRecipientsOutsideWindow { get; } = [];

    public Task<MessagingSendResult> SendTextAsync(
        string accessToken,
        string recipientProviderUserId,
        string text,
        CancellationToken cancellationToken = default)
    {
        Sends.Add((accessToken, recipientProviderUserId, text));
        return SendDirectAsync(accessToken, recipientProviderUserId, new InstagramMessageContent.PlainText(text), cancellationToken);
    }

    public Task<MessagingSendResult> SendDirectAsync(
        string accessToken,
        string recipientProviderUserId,
        InstagramMessageContent content,
        CancellationToken cancellationToken = default)
    {
        TypedSends.Add((accessToken, recipientProviderUserId, content));
        return Task.FromResult(RejectRecipientsOutsideWindow.Contains(recipientProviderUserId)
            ? MessagingSendResult.Fail(MessagingFailureReason.MessagingWindowExpired, "recipient outside the 24h window")
            : MessagingSendResult.Ok(recipientProviderUserId, "mid-" + recipientProviderUserId));
    }
}

/// <summary>
/// Deterministic recording stand-in for Meta's comment Private Reply edge (M13-009):
/// outbound Private Reply calls are captured (token, IG_ID, comment_id, text) instead of
/// leaving CI; the one-reply/7-day/Live rules are enforced by the real policy + ledger
/// pipeline, so this client only confirms the transport call happened.
/// </summary>
public sealed class RecordingCommentPrivateReplyClient : ICommentPrivateReplyClient
{
    public List<(string AccessToken, string ProviderAccountId, string CommentId, string Text)> Sends { get; } = [];

    public Task<PrivateReplySendResult> SendPrivateReplyAsync(
        string accessToken,
        string providerAccountId,
        string commentId,
        string text,
        CancellationToken cancellationToken = default)
    {
        Sends.Add((accessToken, providerAccountId, commentId, text));
        return Task.FromResult(PrivateReplySendResult.Ok("526-recipient-" + commentId, "mid-" + commentId));
    }
}

/// <summary>
/// Deterministic recording stand-in for the official User Profile relationship read
/// (M13-011): is_user_follow_business is scripted per test; every call is recorded so
/// tests can prove the follow check only happens with a proven consent basis. No live
/// Meta call ever leaves CI.
/// </summary>
public sealed class RecordingInstagramRelationshipClient : Qasedak.Modules.Instagram.Application.RevealFlow.IInstagramRelationshipClient
{
    /// <summary>Scripted tri-state result; tests override to simulate blocked/unavailable.</summary>
    public Func<FollowStateResult> Script { get; set; } = () => FollowStateResult.Follows();

    public List<(string AccessToken, string ParticipantIGSId)> Calls { get; } = [];

    public void Clear()
    {
        Calls.Clear();
        Script = () => FollowStateResult.Follows();
    }

    public Task<FollowStateResult> GetFollowStateAsync(
        string accessToken,
        string participantIGSId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((accessToken, participantIGSId));
        return Task.FromResult(Script());
    }
}

/// <summary>
/// Scriptable reveal-flow start provider (M13-011): per-test request mapping from the
/// normalized comment event to invocation-owned content; the default returns null (no
/// flow), so every pre-existing comment test is unaffected.
/// </summary>
public sealed class ScriptableRevealFlowStartProvider : Qasedak.Api.CrossModule.IRevealFlowStartContentProvider
{
    public Func<InstagramCommentCreated, RevealFlowStartRequest?>? Script { get; set; }

    public List<string> CommentsRequested { get; } = [];

    public RevealFlowStartRequest? RequestFor(InstagramCommentCreated comment)
    {
        CommentsRequested.Add(comment.CommentId);
        return Script?.Invoke(comment);
    }

    public void Clear()
    {
        Script = null;
        CommentsRequested.Clear();
    }
}

/// <summary>
/// Deterministic recording stand-in for the official IG Comment reference read: the
/// creation timestamp is scripted per test (default: 1 day ago — within the 7-day
/// window). No live Meta call ever leaves CI.
/// </summary>
public sealed class RecordingCommentReferenceReader : ICommentReferenceReader
{
    /// <summary>Scripted reference result; tests override to simulate expired/not-found reads.</summary>
    public Func<CommentReferenceReadResult> Script { get; set; } =
        () => new CommentReferenceReadResult.Found(DateTimeOffset.UtcNow.AddDays(-1));

    public List<string> CommentIdsRead { get; } = [];

    public Task<CommentReferenceReadResult> ReadCreatedAtUtcAsync(
        string accessToken,
        string commentId,
        CancellationToken cancellationToken = default)
    {
        CommentIdsRead.Add(commentId);
        return Task.FromResult(Script());
    }
}

/// <summary>Recording stand-in for the public comment reply edge (no consumer yet in M13-009).</summary>
public sealed class RecordingCommentPublicReplyClient : ICommentPublicReplyClient
{
    public List<(string AccessToken, string CommentId, string Text)> Sends { get; } = [];

    public Task<PublicReplySendResult> SendPublicReplyAsync(
        string accessToken,
        string commentId,
        string text,
        CancellationToken cancellationToken = default)
    {
        Sends.Add((accessToken, commentId, text));
        return Task.FromResult(PublicReplySendResult.Ok("reply-" + commentId));
    }
}

/// <summary>
/// Records every normalized integration event the composition-root fan-out receives
/// (M13-008): lets signed-webhook E2E tests assert exact-account enrichment and
/// zero-dispatch fail-closed behavior for postbacks/reads that have no business
/// consumer yet, without weakening the real fan-out to the bridges.
/// </summary>
public sealed class RecordingIntegrationEventDispatcher(IIntegrationEventDispatcher inner, List<IIntegrationEvent> sink)
    : IIntegrationEventDispatcher
{
    public Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        sink.Add(integrationEvent);
        return inner.DispatchAsync(integrationEvent, cancellationToken);
    }
}

/// <summary>
/// Deterministic stand-ins for Meta's OAuth/profile/subscription edges: the connection
/// endpoints are exercised end to end without any live Meta call. The profile double
/// echoes the expected provider identity (always proven); tests needing mismatch
/// script <see cref="ScriptedProfileClient.Result"/> explicitly.
/// </summary>
public sealed class ScriptedOAuthClient : IMetaOAuthClient
{
    public Func<CodeExchangeResult> CodeResult { get; set; } = () =>
        CodeExchangeResult.Ok(new("SHORT-E2E", "ig-e2e-" + Guid.NewGuid().ToString("N"),
            ["instagram_business_basic", "instagram_business_manage_messages"]));

    public Func<LongLivedTokenResult> LongLivedResult { get; set; } = () =>
        LongLivedTokenResult.Ok(new("LONG-E2E", 60 * 24 * 3600L));

    public Task<CodeExchangeResult> ExchangeCodeAsync(CodeExchangeRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(CodeResult());

    public Task<LongLivedTokenResult> ExchangeShortLivedForLongLivedAsync(string shortLivedAccessToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(LongLivedResult());

    public Task<LongLivedTokenResult> RefreshLongLivedAsync(string longLivedAccessToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(LongLivedResult());
}

public sealed class ScriptedProfileClient : IAccountProfileClient
{
    public Func<string, AccountProfileOutcome> Result { get; set; } = expected =>
        new AccountProfileOutcome.Ok(new InstagramAccountProfile(
            expected, "shop-" + expected, "E2E Shop", null, "Business"));

    public Task<AccountProfileOutcome> GetProfileAsync(
        string accessToken, string expectedProviderAccountId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result(expectedProviderAccountId));
}

public sealed class ScriptedSubscriptionClient : ISubscriptionClient
{
    public Func<IReadOnlyList<string>, SubscriptionResult> Result { get; set; } =
        fields => SubscriptionResult.Subscribed(fields);

    public Task<SubscriptionResult> SubscribeAsync(
        string accessToken, string professionalAccountId, IReadOnlyList<string> fields, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result(fields));
}

/// <summary>
/// Deterministic stand-in for the Meta media edge (M13-006): pages are scripted per
/// provider cursor so cursor pagination can be exercised end to end; every call is
/// recorded (provider account addressed + token seen) so tests can prove exact-account
/// routing, zero provider calls on foreign/unknown accounts and zero token fallback.
/// </summary>
public sealed class ScriptedMediaCatalogClient : IMediaCatalogClient
{
    private readonly IMediaCursorCodec _codec;

    public ScriptedMediaCatalogClient(IMediaCursorCodec codec) => _codec = codec;

    public int CallCount { get; private set; }

    public List<(string ProviderAccountId, string? AfterCursor)> Calls { get; } = [];

    public List<string> SeenTokens { get; } = [];

    /// <summary>Clears per-test recordings; call at the start of each test.</summary>
    public void Reset()
    {
        CallCount = 0;
        Calls.Clear();
        SeenTokens.Clear();
        ResultOverride = null;
    }

    /// <summary>Scripts the page returned for a provider cursor; null after starts at recent media.</summary>
    public Func<string?, Guid, MediaCatalogPage> PageFor { get; set; } =
        (_, _) => new MediaCatalogPage([], null, false);

    /// <summary>When set, every call returns this failure instead of a scripted page.</summary>
    public MediaCatalogResult? ResultOverride { get; set; }

    public Task<MediaCatalogResult> GetPageAsync(
        string accessToken, string providerAccountId, Guid accountId, int limit, string? afterCursor, CancellationToken cancellationToken = default)
    {
        CallCount++;
        Calls.Add((providerAccountId, afterCursor));
        SeenTokens.Add(accessToken);
        return Task.FromResult<MediaCatalogResult>(ResultOverride ?? new MediaCatalogResult.Ok(PageFor(afterCursor, accountId)));
    }

    public Task<MediaCatalogResult> GetRecentAsync(
        string accessToken, string providerAccountId, Guid accountId, int maxItems, CancellationToken cancellationToken = default)
    {
        CallCount++;
        Calls.Add((providerAccountId, null));
        SeenTokens.Add(accessToken);
        return Task.FromResult<MediaCatalogResult>(ResultOverride ?? new MediaCatalogResult.Ok(PageFor(null, accountId)));
    }

    /// <summary>Convenience: one image item record.</summary>
    public static MediaCatalogItem Item(string id, string kind = "Image") =>
        new(id, null, Enum.Parse<MediaKind>(kind), null, null, null,
            $"https://media.example/{id}.jpg", null, true, false, null, null, null);
}

/// <summary>
/// Deterministic stand-in for the Meta insights edges (M13-007): account insights,
/// per-media insights and the direct follower observation are scripted per test and
/// every call is recorded (provider id addressed + token seen) so tests can prove
/// exact-account routing, the permission-loss fan-out stop and zero provider calls
/// on foreign/unknown/disconnected accounts. No live Meta call in CI.
/// </summary>
public sealed class ScriptedInstagramInsightsClient : IInstagramInsightsClient
{
    public int CallCount { get; private set; }

    public List<(string ProviderAccountId, DateOnly DayUtc)> AccountCalls { get; } = [];

    public List<(string ProviderMediaId, MediaKind Kind)> MediaCalls { get; } = [];

    public List<(string ProviderAccountId, DateOnly? DayUtc)> FollowerCalls { get; } = [];

    public List<string> SeenTokens { get; } = [];

    /// <summary>Clears per-test recordings; call at the start of each test.</summary>
    public void Reset()
    {
        CallCount = 0;
        AccountCalls.Clear();
        MediaCalls.Clear();
        FollowerCalls.Clear();
        SeenTokens.Clear();
        AccountResult = () => new AccountInsightsResult.Ok([]);
        MediaResult = (_, _) => new MediaInsightsResult.Ok([]);
        FollowerResult = () => new FollowerCountResult.Value(12_345);
    }

    public Func<AccountInsightsResult> AccountResult { get; set; } = () => new AccountInsightsResult.Ok([]);

    public Func<string, MediaKind, MediaInsightsResult> MediaResult { get; set; } = (_, _) => new MediaInsightsResult.Ok([]);

    public Func<FollowerCountResult> FollowerResult { get; set; } = () => new FollowerCountResult.Value(12_345);

    public Task<AccountInsightsResult> GetAccountInsightsAsync(
        string accessToken, string providerAccountId, DateOnly dayUtc, CancellationToken cancellationToken = default)
    {
        CallCount++;
        AccountCalls.Add((providerAccountId, dayUtc));
        SeenTokens.Add(accessToken);
        return Task.FromResult(AccountResult());
    }

    public Task<MediaInsightsResult> GetMediaInsightsAsync(
        string accessToken, string providerMediaId, MediaKind kind, CancellationToken cancellationToken = default)
    {
        CallCount++;
        MediaCalls.Add((providerMediaId, kind));
        SeenTokens.Add(accessToken);
        return Task.FromResult(MediaResult(providerMediaId, kind));
    }

    public Task<FollowerCountResult> GetFollowerCountAsync(
        string accessToken, string providerAccountId, CancellationToken cancellationToken = default)
    {
        CallCount++;
        FollowerCalls.Add((providerAccountId, null));
        SeenTokens.Add(accessToken);
        return Task.FromResult(FollowerResult());
    }
}

/// <summary>
/// Shared recorder for protected-token operations. The per-scope decorator
/// (<see cref="RecordingTokenStoreDecorator"/>) records into this singleton while
/// delegating to the request-scoped real store, preserving the EF unit of work.
/// </summary>
public sealed class RecordingTokenStore
{
    public List<Guid> TokenGets { get; } = [];

    public List<Guid> TokenDeletes { get; } = [];
}

/// <summary>
/// Records token reads/deletes then delegates to the SAME scoped real store the
/// request uses, so writes stay in one unit of work with the account repository.
/// </summary>
public sealed class RecordingTokenStoreDecorator(RecordingTokenStore recorder, ProtectedTokenStore inner) : IProtectedTokenStore
{
    public Task<string?> GetAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        recorder.TokenGets.Add(accountId);
        return inner.GetAsync(accountId, cancellationToken);
    }

    public Task StoreAsync(Guid accountId, string accessToken, CancellationToken cancellationToken = default) =>
        inner.StoreAsync(accountId, accessToken, cancellationToken);

    public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        recorder.TokenDeletes.Add(accountId);
        return inner.DeleteAsync(accountId, cancellationToken);
    }
}

/// <summary>
/// Deterministic stand-in for real payment providers: records gateway requests and plays
/// scripted verification outcomes so CI never touches a live payment API. The resolver is
/// billing-scoped; replacing it cannot affect other modules' tests.
/// </summary>
public sealed class RecordingPaymentGateway : IPaymentGateway
{
    public List<CreatePaymentRequest> Requests { get; } = [];

    public List<VerifyPaymentRequest> Verifies { get; } = [];

    /// <summary>Scripted verify results consumed in order; defaults to first-time success.</summary>
    public Queue<PaymentVerificationResult> ScriptedVerifications { get; } = new();

    public bool FailRequests { get; set; }

    public string ProviderId => "zarinpal";

    public Task<PaymentInitialization> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (FailRequests)
        {
            throw new PaymentGatewayUnavailableException("simulated provider outage");
        }

        Requests.Add(request);
        var authority = $"auth-{request.AttemptId:N}";
        return Task.FromResult(new PaymentInitialization(ProviderId, authority, $"https://pay.test.local/pg/StartPay/{authority}"));
    }

    public Task<PaymentVerificationResult> VerifyAsync(VerifyPaymentRequest request, CancellationToken cancellationToken = default)
    {
        Verifies.Add(request);
        var result = ScriptedVerifications.Count > 0
            ? ScriptedVerifications.Dequeue()
            : PaymentVerificationResult.Verified(100, $"ref-{Guid.NewGuid():N}", "6037********1234", "card-hash-test");
        return Task.FromResult(result);
    }
}

public sealed class RecordingPaymentGatewayResolver(RecordingPaymentGateway gateway) : IPaymentGatewayResolver
{
    public IReadOnlyList<string> EnabledProviderIds => ["zarinpal"];

    public IPaymentGateway Resolve(string providerId) =>
        string.Equals(providerId, "zarinpal", StringComparison.OrdinalIgnoreCase)
            ? gateway
            : throw new PaymentProviderUnknownException(providerId);
}

/// <summary>
/// Routes zarinpal to the recording fake and mellat to the REAL transport gateway backed
/// by a scripted SOAP fake — the full Behpardakht orchestration (verify→settle→inquiry)
/// runs in CI without any network access to bpm.shaparak.ir.
/// </summary>
public sealed class CompositePaymentGatewayResolver(
    RecordingPaymentGateway zarinpal,
    IPaymentGateway mellat) : IPaymentGatewayResolver
{
    public IReadOnlyList<string> EnabledProviderIds => ["zarinpal", "mellat"];

    public IPaymentGateway Resolve(string providerId) => providerId switch
    {
        "zarinpal" => zarinpal,
        "mellat" => mellat,
        _ => throw new PaymentProviderUnknownException(providerId),
    };
}

/// <summary>
/// Scriptable Behpardakht SOAP boundary for API-level Mellat tests: records operations,
/// plays queued outcomes, and defaults to the documented happy path (pay 0+RefId,
/// verify 0, settle 0). Internal: the transport types stay behind InternalsVisibleTo.
/// </summary>
internal sealed class FakeBehpardakhtSoapClient : Qasedak.Modules.Billing.Infrastructure.Payments.IBehpardakhtSoapClient
{
    public List<string> Operations { get; } = [];

    public List<Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtPayRequest> PayRequests { get; } = [];

    public List<Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtTransactionRequest> Transactions { get; } = [];

    public Queue<object> ScriptedPay { get; } = new();

    public Queue<object> ScriptedVerify { get; } = new();

    public Queue<object> ScriptedSettle { get; } = new();

    public Queue<object> ScriptedInquiry { get; } = new();

    private static T Next<T>(Queue<object> queue, T fallback) =>
        queue.Count > 0 ? (T)queue.Dequeue() : fallback;

    public Task<Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtPayResult> PayAsync(
        Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtPayRequest request, CancellationToken cancellationToken = default)
    {
        Operations.Add("pay");
        PayRequests.Add(request);
        return Task.FromResult(Next(ScriptedPay, new Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtPayResult(0, $"REF-{request.OrderId}")));
    }

    private Task<Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtCodeResult> Run(
        string operation,
        Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtTransactionRequest request,
        Queue<object> scripted,
        CancellationToken cancellationToken)
    {
        Operations.Add(operation);
        Transactions.Add(request);
        return Task.FromResult(Next(scripted, new Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtCodeResult(0)));
    }

    public Task<Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtCodeResult> VerifyAsync(
        Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default) =>
        Run("verify", request, ScriptedVerify, cancellationToken);

    public Task<Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtCodeResult> SettleAsync(
        Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default) =>
        Run("settle", request, ScriptedSettle, cancellationToken);

    public Task<Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtCodeResult> InquiryAsync(
        Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default) =>
        Run("inquiry", request, ScriptedInquiry, cancellationToken);

    public Task<Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtCodeResult> ReverseAsync(
        Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default) =>
        Run("reverse", request, new Queue<object>(), cancellationToken);
}

/// <summary>
/// Boots the real API host against a real PostgreSQL 18 container: migrations are applied
/// once, then every test exercises HTTP endpoints end to end. No database mocking.
/// </summary>
public sealed class ApiPostgreSqlFixture : IAsyncLifetime
{
    public const string SigningKey = "api-integration-signing-key-0123456789abcdef";

    public const string MetaAppSecret = "api-integration-meta-app-secret-0123456789abcdef";

    public const string MetaVerifyToken = "api-integration-meta-verify-token";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(image: "postgres:18-alpine")
        .WithDatabase("qasedak_api_tests")
        .WithUsername("qasedak")
        .WithPassword("qasedak-tests")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public WebApplicationFactory<Program> Factory => _factory;

    public HttpClient Client { get; private set; } = null!;

    public RecordingInstagramMessagingClient Messaging { get; } = new();

    /// <summary>Recording comment Private Reply edge (M13-009); the real claim ledger + policy stay on.</summary>
    public RecordingCommentPrivateReplyClient PrivateReplies { get; } = new();

    /// <summary>Recording public comment reply edge (M13-009 boundary; no consumer yet).</summary>
    public RecordingCommentPublicReplyClient PublicReplies { get; } = new();

    /// <summary>Recording User Profile relationship edge (M13-011); tri-state scripted per test.</summary>
    public RecordingInstagramRelationshipClient Relationships { get; } = new();

    /// <summary>Scriptable reveal-flow start provider (M13-011); default: no flows start.</summary>
    public ScriptableRevealFlowStartProvider RevealFlowStarts { get; } = new();

    /// <summary>Scripted IG Comment reference reads (creation timestamp for the 7-day policy).</summary>
    public RecordingCommentReferenceReader CommentReferences { get; } = new();

    /// <summary>Scripted Meta connection edges shared with connection endpoint tests.</summary>
    public ScriptedOAuthClient OAuth { get; } = new();

    /// <summary>Scripted professional-profile edge shared with connection endpoint tests.</summary>
    public ScriptedProfileClient Profiles { get; } = new();

    /// <summary>Scripted subscription edge shared with connection endpoint tests.</summary>
    public ScriptedSubscriptionClient Subscriptions { get; } = new();

    /// <summary>Scripted media edge shared with media catalog endpoint tests.</summary>
    public ScriptedMediaCatalogClient Media { get; } = new(new MediaCatalogCursorCodec());

    /// <summary>Scripted insights edges shared with overview endpoint tests (M13-007).</summary>
    public ScriptedInstagramInsightsClient Insights { get; } = new();

    /// <summary>Records protected-token reads so tests can prove zero-token-access isolation.</summary>
    public RecordingTokenStore Tokens { get; } = new();

    /// <summary>Captures every dispatched normalized integration event (M13-008).</summary>
    public List<IIntegrationEvent> Dispatched { get; } = [];

    public RecordingPaymentGateway Payments { get; } = new();

    /// <summary>Scripted Mellat SOAP boundary shared with assertions.</summary>
    internal FakeBehpardakhtSoapClient MellatSoap { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Identity", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Instagram", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Conversations", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Automations", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Contacts", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Billing", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Audit", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Platform", _container.GetConnectionString());
            // Detailed error surfaces keep integration failures diagnosable.
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
            builder.UseSetting("Identity:Auth:TokenSigningKey", SigningKey);
            builder.UseSetting("Identity:Auth:TokenLifetimeHours", "12");
            builder.UseSetting("Instagram:Meta:AppSecret", MetaAppSecret);
            builder.UseSetting("Instagram:Meta:VerifyToken", MetaVerifyToken);
            // Deterministic token-protection key (exactly 32 bytes, base64) so seeded
            // tokens decrypt inside the test host.
            builder.UseSetting("Instagram:Protection:KeyBase64",
                Convert.ToBase64String("api-integration-token-prot-key!!"u8.ToArray()));
            // The shared test client presents one source IP; the whole assembly's
            // register/login traffic would exhaust the production Sensitive fixed window.
            // Generous windows keep the suite deterministic (rate limiting itself has its
            // own dedicated tests).
            builder.UseSetting("Qasedak:RateLimits:Sensitive:Limit", "10000");
            builder.UseSetting("Qasedak:RateLimits:Sensitive:WindowSeconds", "60");
            builder.UseSetting("Qasedak:RateLimits:Authenticated:Limit", "100000");
            builder.UseSetting("Qasedak:RateLimits:Public:Limit", "100000");
            // Payments: provider selection is enabled for the deterministic recording
            // gateway only; CI never reaches a live Zarinpal/Behpardakht endpoint.
            builder.UseSetting("Billing:Payments:CallbackBaseUrl", "https://api.test.local");
            builder.UseSetting("Billing:Payments:FrontendBaseUrl", "https://app.test.local");
            builder.UseSetting("Billing:Payments:Zarinpal:Enabled", "true");
            builder.UseSetting("Billing:Payments:Zarinpal:MerchantId", "0123456789abcdef0123456789abcdefabcd");
            // Mellat runs the REAL gateway transport against the scripted SOAP fake.
            builder.UseSetting("Billing:Payments:Mellat:Enabled", "true");
            builder.UseSetting("Billing:Payments:Mellat:TerminalId", "123456");
            builder.UseSetting("Billing:Payments:Mellat:Username", "test-user");
            builder.UseSetting("Billing:Payments:Mellat:Password", "test-pass");
            builder.UseSetting("Billing:Payments:Mellat:ServiceUrl", "https://bpm.test.local/pgwchannel/services/pgw");
            builder.UseSetting("Billing:Payments:Mellat:PaymentPageUrl", "https://bpm.test.local/pgwchannel/startpay.mellat");
            // Deterministic token-protection key (exactly 32 bytes, base64) so seeded
            // tokens decrypt inside the test host.
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInstagramMessagingClient>();
                services.AddSingleton(Messaging);
                services.AddSingleton<IInstagramMessagingClient>(sp => sp.GetRequiredService<RecordingInstagramMessagingClient>());
                // M13-009: scripted comment-effect edges — the real claim ledger, policy
                // and coordinator stay wired; only provider HTTP is replaced.
                services.RemoveAll<ICommentPrivateReplyClient>();
                services.AddSingleton(PrivateReplies);
                services.AddSingleton<ICommentPrivateReplyClient>(sp => sp.GetRequiredService<RecordingCommentPrivateReplyClient>());
                services.RemoveAll<ICommentPublicReplyClient>();
                services.AddSingleton(PublicReplies);
                services.AddSingleton<ICommentPublicReplyClient>(sp => sp.GetRequiredService<RecordingCommentPublicReplyClient>());
                services.RemoveAll<ICommentReferenceReader>();
                services.AddSingleton(CommentReferences);
                services.AddSingleton<ICommentReferenceReader>(sp => sp.GetRequiredService<RecordingCommentReferenceReader>());
                // M13-011: scripted relationship edge + start-provider seam; the real
                // reveal store, coordinator and continuation bridges stay wired.
                services.RemoveAll<Qasedak.Modules.Instagram.Application.RevealFlow.IInstagramRelationshipClient>();
                services.AddSingleton(Relationships);
                services.AddSingleton<Qasedak.Modules.Instagram.Application.RevealFlow.IInstagramRelationshipClient>(sp => sp.GetRequiredService<RecordingInstagramRelationshipClient>());
                services.RemoveAll<Qasedak.Api.CrossModule.IRevealFlowStartContentProvider>();
                services.AddSingleton(RevealFlowStarts);
                services.AddSingleton<Qasedak.Api.CrossModule.IRevealFlowStartContentProvider>(sp => sp.GetRequiredService<ScriptableRevealFlowStartProvider>());
                // Connection endpoints run against scripted Meta edges: no live
                // calls in CI. Nothing else in the suite resolves these ports.
                services.RemoveAll<IMetaOAuthClient>();
                services.AddSingleton(OAuth);
                services.AddSingleton<IMetaOAuthClient>(sp => sp.GetRequiredService<ScriptedOAuthClient>());
                services.RemoveAll<IAccountProfileClient>();
                services.AddSingleton(Profiles);
                services.AddSingleton<IAccountProfileClient>(sp => sp.GetRequiredService<ScriptedProfileClient>());
                services.RemoveAll<ISubscriptionClient>();
                services.AddSingleton(Subscriptions);
                services.AddSingleton<ISubscriptionClient>(sp => sp.GetRequiredService<ScriptedSubscriptionClient>());
                // Media catalog (M13-006): scripted pages, recording token reads.
                services.RemoveAll<IMediaCatalogClient>();
                services.AddSingleton(Media);
                services.AddSingleton<IMediaCatalogClient>(sp => sp.GetRequiredService<ScriptedMediaCatalogClient>());
                // Insights (M13-007): scripted account/media/follower edges, recording reads.
                services.RemoveAll<IInstagramInsightsClient>();
                services.AddSingleton(Insights);
                services.AddSingleton<IInstagramInsightsClient>(sp => sp.GetRequiredService<ScriptedInstagramInsightsClient>());
                // M13-008: capture every normalized integration event the fan-out receives
                // (postbacks/reads have no business consumer yet; exact-account enrichment
                // is asserted on the recorded events). The real bridges stay in the fan-out.
                services.RemoveAll<Qasedak.Modules.Instagram.Application.Webhooks.IIntegrationEventDispatcher>();
                services.AddScoped<Qasedak.Modules.Instagram.Application.Webhooks.IIntegrationEventDispatcher>(sp =>
                    new RecordingIntegrationEventDispatcher(
                        new Qasedak.Api.CrossModule.FanOutIntegrationEventDispatcher(
                        [
                            sp.GetRequiredService<Qasedak.Api.CrossModule.InstagramConversationBridge>(),
                            sp.GetRequiredService<Qasedak.Api.CrossModule.AutomationCommentBridge>(),
                            sp.GetRequiredService<Qasedak.Api.CrossModule.ContactsInteractionBridge>(),
                            sp.GetRequiredService<Qasedak.Api.CrossModule.RevealFlowContinuationBridge>(),
                            sp.GetRequiredService<Qasedak.Api.CrossModule.RevealFlowStartBridge>(),
                        ]),
                        Dispatched));
                services.RemoveAll<IProtectedTokenStore>();
                services.AddScoped<ProtectedTokenStore>();
                services.AddSingleton(Tokens);
                services.AddScoped<IProtectedTokenStore>(sp =>
                    new RecordingTokenStoreDecorator(sp.GetRequiredService<RecordingTokenStore>(), sp.GetRequiredService<ProtectedTokenStore>()));
                services.RemoveAll<IPaymentGatewayResolver>();
                services.AddSingleton(Payments);
                services.AddSingleton(MellatSoap);
                services.AddSingleton<Qasedak.Modules.Billing.Infrastructure.Payments.IBehpardakhtSoapClient>(sp => sp.GetRequiredService<FakeBehpardakhtSoapClient>());
                // Singleton so tests can resolve the exact gateway instance the resolver uses.
                services.AddSingleton(sp => new Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtMellatPaymentGateway(
                    Microsoft.Extensions.Options.Options.Create(
                        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtOptions>>().Value),
                    sp.GetRequiredService<Qasedak.Modules.Billing.Infrastructure.Payments.IBehpardakhtSoapClient>()));
                services.AddSingleton<IPaymentGatewayResolver>(sp => new CompositePaymentGatewayResolver(
                    Payments,
                    sp.GetRequiredService<Qasedak.Modules.Billing.Infrastructure.Payments.BehpardakhtMellatPaymentGateway>()));
            });
        });

        // Apply module migrations before any request hits persistence.
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<InstagramDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<ConversationsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AutomationsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<ContactsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<ScheduledWorkDbContext>().Database.MigrateAsync();

        Client = _factory.CreateClient();
    }

    /// <summary>Reads the append-only audit log directly (real PostgreSQL).</summary>
    public async Task<List<AuditEntryRow>> ReadAuditEntriesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await context.Entries.OrderBy(e => e.AtUtc).ToListAsync();
    }

    /// <summary>Appends an audit entry through the port (used by immutability tests).</summary>
    public async Task RecordAuditAsync(AuditEntry entry)
    {
        using var scope = Factory.Services.CreateScope();
        var trail = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
        await trail.RecordAsync(entry);
    }

    /// <summary>
    /// Guarantees the workspace row exists and the user holds a membership — used by tests
    /// that exercise seeded workspaces through authenticated HTTP calls.
    /// </summary>
    public async Task EnsureWorkspaceMemberAsync(Guid workspaceId, Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        await using var context = scope.ServiceProvider
            .GetRequiredService<Qasedak.Modules.Identity.Infrastructure.Persistence.IdentityDbContext>();

        var memberships = await context.Memberships
            .Where(m => m.WorkspaceId == workspaceId)
            .ToListAsync();
        if (memberships.Count == 0)
        {
            var workspace = Qasedak.Modules.Identity.Domain.Workspaces.Workspace.FromState(
                workspaceId,
                Qasedak.Modules.Identity.Domain.Workspaces.WorkspaceName.Create("Integration Seeded Workspace"),
                [(Guid.CreateVersion7(), userId, Qasedak.Modules.Identity.Domain.Workspaces.MembershipRole.Owner)]);
            await context.Workspaces.AddAsync(workspace);
            await context.SaveChangesAsync();
        }
        else if (memberships.All(m => m.UserId != userId))
        {
            // Direct row insert: the membership aggregate internals stay module-private.
            // Role 3 = Member.
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO identity.memberships (\"Id\", \"WorkspaceId\", \"UserId\", \"Role\") " +
                "VALUES ({0}, {1}, {2}, 3)",
                [Guid.CreateVersion7(), workspaceId, userId]);
        }
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiTestEnvironment : ICollectionFixture<ApiPostgreSqlFixture>
{
    public const string Name = "api-postgres";
}

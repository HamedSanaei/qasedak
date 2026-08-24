using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Qasedak.BuildingBlocks.Application.Auditing;
using Qasedak.BuildingBlocks.Infrastructure.Auditing;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Qasedak.Modules.Billing.Application.Payments;
using Qasedak.Modules.Billing.Infrastructure.Persistence;
using Qasedak.Modules.Contacts.Infrastructure.Persistence;
using Qasedak.Modules.Conversations.Infrastructure.Persistence;
using Qasedak.Modules.Identity.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Application.Messaging;
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

    public HashSet<string> RejectRecipientsOutsideWindow { get; } = [];

    public Task<MessagingSendResult> SendTextAsync(
        string accessToken,
        string recipientProviderUserId,
        string text,
        CancellationToken cancellationToken = default)
    {
        Sends.Add((accessToken, recipientProviderUserId, text));
        return Task.FromResult(RejectRecipientsOutsideWindow.Contains(recipientProviderUserId)
            ? MessagingSendResult.Fail(MessagingFailureReason.MessagingWindowExpired, "recipient outside the 24h window (simulated 490)")
            : MessagingSendResult.Ok());
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

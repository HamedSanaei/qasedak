using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Qasedak.BuildingBlocks.Application.Auditing;
using Qasedak.BuildingBlocks.Infrastructure.Auditing;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
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
            // CI must never call live Meta APIs: the messaging port gets a deterministic
            // recording stand-in (the real Graph client stays registered underneath).
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInstagramMessagingClient>();
                services.AddSingleton(Messaging);
                services.AddSingleton<IInstagramMessagingClient>(sp => sp.GetRequiredService<RecordingInstagramMessagingClient>());
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

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
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

        Client = _factory.CreateClient();
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

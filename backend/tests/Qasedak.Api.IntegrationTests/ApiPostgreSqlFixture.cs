using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Identity.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

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

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Identity", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Instagram", _container.GetConnectionString());
            builder.UseSetting("Identity:Auth:TokenSigningKey", SigningKey);
            builder.UseSetting("Identity:Auth:TokenLifetimeHours", "12");
            builder.UseSetting("Instagram:Meta:AppSecret", MetaAppSecret);
            builder.UseSetting("Instagram:Meta:VerifyToken", MetaVerifyToken);
        });

        // Apply module migrations before any request hits persistence.
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<InstagramDbContext>().Database.MigrateAsync();

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

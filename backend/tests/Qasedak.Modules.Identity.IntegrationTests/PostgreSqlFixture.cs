using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Identity.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Qasedak.Modules.Identity.IntegrationTests;

/// <summary>
/// Starts one PostgreSQL 18 container for the whole test collection and applies the
/// identity module migrations once. Persistence semantics are exercised against a real
/// database engine on purpose; no database mocking is permitted by repository policy.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(image: "postgres:18-alpine")
        .WithDatabase("qasedak_identity_tests")
        .WithUsername("qasedak")
        .WithPassword("qasedak-tests")
        .Build();

    public IdentityDbContext Context { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
            .Options;

        Context = new IdentityDbContext(options);
        await Context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Context.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTestEnvironment : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "identity-postgres";
}

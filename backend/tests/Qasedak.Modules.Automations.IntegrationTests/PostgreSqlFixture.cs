using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Qasedak.Modules.Automations.IntegrationTests;

/// <summary>
/// Starts one PostgreSQL 18 container for the whole test collection and applies the
/// automations module migrations once. Persistence semantics are exercised against a real
/// database engine; no database mocking is permitted by repository policy.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(image: "postgres:18-alpine")
        .WithDatabase("qasedak_automations_tests")
        .WithUsername("qasedak")
        .WithPassword("qasedak-tests")
        .Build();

    public AutomationsDbContext Context { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<AutomationsDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AutomationsDbContext.Schema))
            .Options;

        Context = new AutomationsDbContext(options);
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
    public const string Name = "automations-postgres";
}

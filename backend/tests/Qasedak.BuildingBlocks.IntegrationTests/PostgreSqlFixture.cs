using Microsoft.EntityFrameworkCore;
using Qasedak.BuildingBlocks.Infrastructure.Scheduling;
using Testcontainers.PostgreSql;
using Xunit;

namespace Qasedak.BuildingBlocks.IntegrationTests;

/// <summary>
/// Starts one PostgreSQL 18 container for the whole test collection and applies the
/// platform scheduled-work migration once. Persistence semantics are exercised against
/// a real database engine; no database mocking is permitted by repository policy.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(image: "postgres:18-alpine")
        .WithDatabase("qasedak_platform_tests")
        .WithUsername("qasedak")
        .WithPassword("qasedak-tests")
        .Build();

    public ScheduledWorkDbContext Context { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<ScheduledWorkDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ScheduledWorkDbContext.Schema))
            .Options;

        Context = new ScheduledWorkDbContext(options);
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
    public const string Name = "platform-postgres";
}

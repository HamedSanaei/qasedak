using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Billing.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Qasedak.Modules.Billing.IntegrationTests;

/// <summary>One PostgreSQL 18 container per collection; billing migrations applied once.</summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(image: "postgres:18-alpine")
        .WithDatabase("qasedak_billing_tests")
        .WithUsername("qasedak")
        .WithPassword("qasedak-tests")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BillingDbContext.Schema))
            .Options;
        await using var context = new BillingDbContext(options);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresTestEnvironment : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "billing-postgres";
}

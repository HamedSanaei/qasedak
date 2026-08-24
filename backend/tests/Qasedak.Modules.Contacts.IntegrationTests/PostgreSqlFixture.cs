using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Contacts.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Qasedak.Modules.Contacts.IntegrationTests;

/// <summary>
/// Starts one PostgreSQL 18 container for the whole test collection and applies the
/// contacts module migrations once. Persistence semantics are exercised against a real
/// database engine; no database mocking is permitted by repository policy.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(image: "postgres:18-alpine")
        .WithDatabase("qasedak_contacts_tests")
        .WithUsername("qasedak")
        .WithPassword("qasedak-tests")
        .Build();

    public ContactsDbContext Context { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<ContactsDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ContactsDbContext.Schema))
            .Options;

        Context = new ContactsDbContext(options);
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
    public const string Name = "contacts-postgres";
}

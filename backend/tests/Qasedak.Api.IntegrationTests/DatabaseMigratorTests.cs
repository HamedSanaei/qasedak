using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qasedak.Api.Migrations;
using Qasedak.BuildingBlocks.Infrastructure.Auditing;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Qasedak.Modules.Billing.Infrastructure.Persistence;
using Qasedak.Modules.Contacts.Infrastructure.Persistence;
using Qasedak.Modules.Conversations.Infrastructure.Persistence;
using Qasedak.Modules.Identity.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// Proves the one-shot `--migrate` mechanism (DatabaseMigrator) creates all seven module
/// schemas on a FRESH empty database and is idempotent: a second run applies nothing.
/// Uses a real PostgreSQL 18 container — the production host never needs dotnet-ef.
/// </summary>
public sealed class DatabaseMigratorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(image: "postgres:18-alpine")
        .WithDatabase("qasedak_migrate")
        .WithUsername("qasedak")
        .WithPassword("qasedak-tests")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _container.DisposeAsync();
    }

    private WebApplicationFactory<Program> BuildFactory(bool includeAudit = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Identity", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Instagram", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Conversations", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Automations", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Contacts", _container.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Billing", _container.GetConnectionString());
            if (includeAudit)
            {
                builder.UseSetting("ConnectionStrings:Audit", _container.GetConnectionString());
            }
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
        });

    private async Task<int> RunMigrateAsync()
    {
        _factory = BuildFactory();
        using var scope = _factory.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("migrate-test");
        return await DatabaseMigrator.MigrateAsync(scope.ServiceProvider, logger);
    }

    [Fact]
    public async Task MigrateOnFreshDatabaseCreatesAllSevenSchemas()
    {
        var exit = await RunMigrateAsync();
        Assert.Equal(0, exit);

        using var scope = _factory.Services.CreateScope();
        var schemas = new string[]
        {
            "identity", "instagram", "conversations", "automations", "contacts", "billing", "audit",
        };
        foreach (var schema in schemas)
        {
            var context = scope.ServiceProvider.GetRequiredService(
                schema switch
                {
                    "identity" => typeof(IdentityDbContext),
                    "instagram" => typeof(InstagramDbContext),
                    "conversations" => typeof(ConversationsDbContext),
                    "automations" => typeof(AutomationsDbContext),
                    "contacts" => typeof(ContactsDbContext),
                    "billing" => typeof(BillingDbContext),
                    _ => typeof(AuditDbContext),
                }) as DbContext;
            Assert.NotNull(context);
            await context!.Database.OpenConnectionAsync();
            var exists = await context.Database.SqlQueryRaw<bool>(
                "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = {0})",
                schema).ToListAsync();
#pragma warning disable EF1002 // Schema names are selected from the fixed schema allow-list above.
            var applied = await context.Database.SqlQueryRaw<int>(
                $"SELECT count(*) FROM \"{schema}\".\"__EFMigrationsHistory\"").ToListAsync();
#pragma warning restore EF1002
            context.Database.CloseConnection();
            Assert.True(exists[0], $"schema '{schema}' must exist after migration");
            Assert.True(applied[0] > 0, $"schema '{schema}' must have applied migration history");
        }
    }

    [Fact]
    public async Task MigrateMissingRequiredContextReturnsFailure()
    {
        _factory = BuildFactory(includeAudit: false);
        using var scope = _factory.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("migrate-test");

        var exit = await DatabaseMigrator.MigrateAsync(scope.ServiceProvider, logger);

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task MigrateSecondRunIsIdempotent()
    {
        await RunMigrateAsync();
        using var firstScope = _factory.Services.CreateScope();
        var auditing = firstScope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var pendingBefore = auditing.Database.GetPendingMigrations().Count();

        // New factory (fresh host composition) still reports nothing pending to apply.
        _factory.Dispose();
        await RunMigrateAsync();
        using var secondScope = _factory.Services.CreateScope();
        var identity = secondScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var pendingAfter = identity.Database.GetPendingMigrations().Count();
        Assert.Equal(0, pendingAfter);
        Assert.Equal(0, pendingBefore);

        // Tables remain populated with data inserted by migrations? None insert seed data,
        // but the history tables must record the applied migrations.
        var applied = await identity.Database.SqlQueryRaw<int>(
            "SELECT count(*) FROM identity.\"__EFMigrationsHistory\"").ToListAsync();
        Assert.True(applied[0] > 0, "migration history must record applied migrations");
    }
}

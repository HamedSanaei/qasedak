using Microsoft.EntityFrameworkCore;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Qasedak.Modules.Conversations.Domain.Conversations;
using Qasedak.Modules.Conversations.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// M13-002 migration safety on real PostgreSQL 18:
/// 1. migrate a scratch database only up to the pre-M13-002 migrations;
/// 2. insert a legacy conversation row and a legacy automation row (no account);
/// 3. migrate to latest;
/// 4. prove both rows survive with documented legacy (NULL) semantics, the new
///    column/index exist, exact-account rows coexist per account, exact duplicate
///    quadruples are rejected at the database, and round-trips preserve identity.
/// No mocks — real Npgsql migrations and constraints.
/// </summary>
public sealed class ChannelAccountMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(image: "postgres:18-alpine")
        .WithDatabase("qasedak_m13_002_migrate")
        .WithUsername("qasedak")
        .WithPassword("qasedak-tests")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private ConversationsDbContext NewConversationsContext()
    {
        var options = new DbContextOptionsBuilder<ConversationsDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ConversationsDbContext.Schema))
            .Options;
        return new ConversationsDbContext(options);
    }

    private AutomationsDbContext NewAutomationsContext()
    {
        var options = new DbContextOptionsBuilder<AutomationsDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AutomationsDbContext.Schema))
            .Options;
        return new AutomationsDbContext(options);
    }

    [Fact]
    public async Task PreMigrationRowsSurviveUpgradeWithLegacyNullSemantics()
    {
        var workspaceId = Guid.CreateVersion7();

        // 1+2. Old world: migrate only to the pre-M13-002 shape and insert legacy rows.
        await using (var conversations = NewConversationsContext())
        {
            await conversations.Database.MigrateAsync("20260823204008_InitialConversationsCreation");
            await conversations.Database.ExecuteSqlRawAsync(
                "INSERT INTO conversations.conversations " +
                "(\"Id\", \"WorkspaceId\", \"Channel\", \"ParticipantId\", \"Status\", \"CreatedAtUtc\", \"LastMessageAtUtc\", \"UnreadCount\") " +
                "VALUES ({0}, {1}, 'instagram', 'legacy-participant', 1, now(), now(), 0)",
                Guid.CreateVersion7(), workspaceId);
        }

        await using (var automations = NewAutomationsContext())
        {
            await automations.Database.MigrateAsync("20260823222145_AddAutomationRuns");
            var legacyAutomationId = Guid.CreateVersion7();
            var legacyDefinition = AutomationDefinitionSerializer.Serialize(AutomationDefinition.Create(
                AutomationTrigger.CommentCreated(),
                [new AutomationAction(ActionKind.SendDirectMessage, "legacy body")]));
            await automations.Database.ExecuteSqlRawAsync(
                "INSERT INTO automations.automations " +
                "(\"Id\", \"WorkspaceId\", \"Name\", \"Status\", \"CreatedAtUtc\", \"CurrentVersionFrozen\") " +
                "VALUES ({0}, {1}, 'legacy automation', 1, now(), false)",
                legacyAutomationId, workspaceId);
            await automations.Database.ExecuteSqlRawAsync(
                "INSERT INTO automations.automation_versions " +
                "(\"AutomationId\", \"Number\", \"definition_json\", \"CreatedAtUtc\") " +
                "VALUES ({0}, 1, {1}, now())",
                legacyAutomationId, legacyDefinition);
        }

        // 3. Upgrade to latest (runs the real M13-002 migration SQL).
        await using (var conversations = NewConversationsContext())
        {
            await conversations.Database.MigrateAsync();
        }

        await using (var automations = NewAutomationsContext())
        {
            await automations.Database.MigrateAsync();
        }

        // 4a. New column + exact-thread unique index exist.
        await using (var conversations = NewConversationsContext())
        {
            var column = await conversations.Database.SqlQueryRaw<int>(
                "SELECT count(*) FROM information_schema.columns " +
                "WHERE table_schema = 'conversations' AND table_name = 'conversations' AND column_name = 'ChannelAccountId'").ToListAsync();
            Assert.Equal([1], column);
            var index = await conversations.Database.SqlQueryRaw<int>(
                "SELECT count(*) FROM pg_indexes WHERE schemaname = 'conversations' AND indexname = 'IX_conversations_exact_thread'").ToListAsync();
            Assert.Equal([1], index);
        }

        // 4b. Legacy conversation row survives with NULL account; legacy automation too.
        ChannelAccountId accountA = new(Guid.CreateVersion7());
        ChannelAccountId accountB = new(Guid.CreateVersion7());
        await using (var conversations = NewConversationsContext())
        {
            var legacy = await conversations.Conversations
                .SingleAsync(c => c.WorkspaceId == workspaceId && c.ParticipantId == "legacy-participant");
            Assert.Null(legacy.ChannelAccountId);
        }

        var repository = new EfAutomationRepository(NewAutomationsContext());
        var legacyAutomations = await repository.ListByWorkspaceAsync(workspaceId);
        var legacyAutomation = Assert.Single(legacyAutomations);
        Assert.Null(legacyAutomation.ChannelAccountId);
        // Unbound legacy automations are never returned for exact-account dispatch.
        Assert.Empty(await repository.ListByAccountAsync(workspaceId, accountA));

        // 4c. Same workspace/channel/participant coexists per exact account (incl. legacy).
        await using (var conversations = NewConversationsContext())
        {
            var now = DateTimeOffset.UtcNow;
            conversations.Conversations.Add(Conversation.Create(
                Guid.CreateVersion7(), workspaceId, "instagram", "legacy-participant", now, accountA));
            conversations.Conversations.Add(Conversation.Create(
                Guid.CreateVersion7(), workspaceId, "instagram", "legacy-participant", now, accountB));
            await conversations.SaveChangesAsync();
            Assert.Equal(3, await conversations.Conversations.CountAsync(c => c.WorkspaceId == workspaceId));
        }

        // 4d. Exact duplicate quadruple is rejected by the database (SQLSTATE 23505).
        await using (var conversations = NewConversationsContext())
        {
            conversations.Conversations.Add(Conversation.Create(
                Guid.CreateVersion7(), workspaceId, "instagram", "legacy-participant", DateTimeOffset.UtcNow, accountA));
            var violation = await Assert.ThrowsAsync<DbUpdateException>(() => conversations.SaveChangesAsync());
            Assert.Contains("23505", violation.InnerException?.Message ?? violation.Message);
        }

        // 4e. Round-trip preserves exact account identity through the converter.
        await using (var conversations = NewConversationsContext())
        {
            var reloaded = await conversations.Conversations
                .SingleAsync(c => c.WorkspaceId == workspaceId && c.ParticipantId == "legacy-participant" && c.ChannelAccountId == accountB);
            Assert.Equal(accountB, reloaded.ChannelAccountId);
        }
    }

    [Fact]
    public async Task AutomationsBindingColumnIsNullableAndIndexed()
    {
        await using var automations = NewAutomationsContext();
        await automations.Database.MigrateAsync();

        var column = await automations.Database.SqlQueryRaw<string>(
            "SELECT is_nullable FROM information_schema.columns " +
            "WHERE table_schema = 'automations' AND table_name = 'automations' AND column_name = 'ChannelAccountId'").ToListAsync();
        Assert.Equal(["YES"], column);
        var index = await automations.Database.SqlQueryRaw<int>(
            "SELECT count(*) FROM pg_indexes WHERE schemaname = 'automations' AND indexname = 'IX_automations_WorkspaceId_ChannelAccountId'").ToListAsync();
        Assert.Equal([1], index);
    }
}

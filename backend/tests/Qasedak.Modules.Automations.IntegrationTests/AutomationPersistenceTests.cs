using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Automations.IntegrationTests;

/// <summary>
/// Persistence round-trips over real PostgreSQL: versioned definitions survive storage
/// byte-for-byte in semantics, frozen history is immutable across loads, and lifecycle
/// state restores faithfully.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class AutomationPersistenceTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);

    private static AutomationDefinition Definition(string message = "v1 body") =>
        AutomationDefinition.Create(
            AutomationTrigger.CommentCreated("price", "cost"),
            [new AutomationCondition(ConditionField.CommentText, ConditionOperator.Contains, "price")],
            [
                new AutomationAction(ActionKind.SendDirectMessage, message),
                new AutomationAction(ActionKind.SendDirectMessage, "fallback"),
            ]);

    private EfAutomationRepository NewRepository()
    {
        // A fresh context per operation mimics request-scoped persistence honestly.
        var options = new DbContextOptionsBuilder<AutomationsDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AutomationsDbContext.Schema))
            .Options;
        return new EfAutomationRepository(new AutomationsDbContext(options));
    }

    [Fact]
    public async Task RoundTripPreservesAggregateWithFullVersionHistory()
    {
        var repository = NewRepository();
        var workspaceId = Guid.CreateVersion7();
        var automation = Automation.Create(Guid.CreateVersion7(), workspaceId, "Comment welcome", Definition("v1 body"), Now);
        await repository.SaveChangesAsync(automation);

        automation.Activate(Now.AddMinutes(1));
        automation.Unpublish(Now.AddMinutes(2));
        automation.ReviseDraftDefinition(Definition("v2 body"), Now.AddMinutes(3));
        await repository.SaveChangesAsync(automation);

        var loaded = await repository.FindByIdAsync(automation.Id);
        Assert.NotNull(loaded);
        Assert.Equal(AutomationStatus.Draft, loaded!.Status);
        Assert.Equal(2, loaded.Versions.Count);
        // The post-freeze draft revision (v2) is itself unfrozen.
        Assert.False(loaded.CurrentVersionFrozen);
        Assert.Equal(1, loaded.Versions[0].Number);
        Assert.Equal("v1 body", loaded.Versions[0].Definition.Actions[0].MessageText);
        // Conditions/keywords survive serialization exactly.
        var condition = Assert.Single(loaded.Versions[0].Definition.Conditions);
        Assert.Equal((ConditionField.CommentText, ConditionOperator.Contains, "price"),
            (condition.Field, condition.Operator, condition.ExpectedValue));
        Assert.Contains("price", loaded.Versions[0].Definition.Trigger.KeywordFilters);
        Assert.Equal(2, loaded.Versions[1].Number);
        Assert.Equal("v2 body", loaded.Versions[1].Definition.Actions[0].MessageText);
    }

    [Fact]
    public async Task FrozenActiveVersionIsStableAcrossReloadsWhileDraftsAdvance()
    {
        var repository = NewRepository();
        var automation = Automation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Support flow", Definition("active body"), Now);
        await repository.SaveChangesAsync(automation);

        // Activation must be persisted before a later unpublish can happen.
        var activated = await repository.FindByIdAsync(automation.Id);
        activated!.Activate(Now.AddMinutes(1));
        await repository.SaveChangesAsync(activated);

        // Draft revision happens on a freshly loaded aggregate.
        var loaded = await repository.FindByIdAsync(automation.Id);
        loaded!.Unpublish(Now.AddMinutes(2));
        loaded.ReviseDraftDefinition(Definition("draft body"), Now.AddMinutes(3));
        await repository.SaveChangesAsync(loaded);

        // The stored frozen version still carries what executions observed while active.
        var reloaded = await repository.FindByIdAsync(automation.Id);
        Assert.Equal("active body", reloaded!.Versions.Single(v => v.Number == 1).Definition.Actions[0].MessageText);
        Assert.Equal("draft body", reloaded.Versions.Single(v => v.Number == 2).Definition.Actions[0].MessageText);

        reloaded.Activate(Now.AddMinutes(4));
        reloaded.Disable(Now.AddMinutes(5));
        await repository.SaveChangesAsync(reloaded);

        var final = await repository.FindByIdAsync(automation.Id);
        Assert.Equal(AutomationStatus.Disabled, final!.Status);
        Assert.NotNull(final.DisabledAtUtc);
        Assert.False(final.CurrentVersionFrozen);
    }

    [Fact]
    public async Task WorkspaceListingReturnsNewestFirstAndScoped()
    {
        var repository = NewRepository();
        var workspaceId = Guid.CreateVersion7();
        var older = Automation.Create(Guid.CreateVersion7(), workspaceId, "older", Definition(), Now);
        var newer = Automation.Create(Guid.CreateVersion7(), workspaceId, "newer", Definition(), Now.AddMinutes(1));
        await repository.SaveChangesAsync(older);
        await repository.SaveChangesAsync(newer);

        var list = await repository.ListByWorkspaceAsync(workspaceId);

        Assert.Equal(["newer", "older"], list.Select(a => a.Name).ToArray());
        Assert.Empty(await repository.ListByWorkspaceAsync(Guid.CreateVersion7()));
    }
}

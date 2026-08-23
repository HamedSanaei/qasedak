using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Automations.IntegrationTests;

/// <summary>
/// Idempotency ledger semantics over real PostgreSQL: the unique (automation, trigger
/// event) index collapses concurrent deliveries; partially executed runs survive reloads
/// and resume without re-dispatching succeeded slots.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class AutomationRunLedgerTests(PostgreSqlFixture fixture)
{
    private const string EventId = "evt-concurrent-1";

    private static readonly DateTimeOffset Now = new(2026, 2, 20, 10, 0, 0, TimeSpan.Zero);

    private EfAutomationRepository NewAutomationRepository()
    {
        var options = new DbContextOptionsBuilder<AutomationsDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AutomationsDbContext.Schema))
            .Options;
        return new EfAutomationRepository(new AutomationsDbContext(options));
    }

    private EfAutomationRunRepository NewRunRepository()
    {
        var options = new DbContextOptionsBuilder<AutomationsDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AutomationsDbContext.Schema))
            .Options;
        return new EfAutomationRunRepository(new AutomationsDbContext(options));
    }

    private static async Task<Automation> SeedActiveAsync(EfAutomationRepository repository)
    {
        var automation = Automation.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "ledger flow",
            AutomationDefinition.Create(
                AutomationTrigger.CommentCreated(),
                [
                    new AutomationAction(ActionKind.SendDirectMessage, "dm one"),
                    new AutomationAction(ActionKind.SendDirectMessage, "dm two"),
                ]),
            Now);
        automation.Activate(Now);
        await repository.SaveChangesAsync(automation);
        return automation;
    }

    [Fact]
    public async Task ConcurrentDeliveriesProduceExactlyOneRun()
    {
        var automationRepo = NewAutomationRepository();
        var automation = await SeedActiveAsync(automationRepo);
        var barrier = new TaskCompletionSource();

        static ExecuteAutomationUseCase UseCase(EfAutomationRepository repo, EfAutomationRunRepository runs) =>
            new(repo, runs, new RecordingDispatcher());

        var first = Task.Run(async () =>
        {
            await barrier.Task;
            return await UseCase(NewAutomationRepository(), NewRunRepository())
                .ExecuteAsync(new ExecutionRequest(automation.Id, Trigger(EventId), "instagram"), default);
        });
        var second = Task.Run(async () =>
        {
            await barrier.Task;
            return await UseCase(NewAutomationRepository(), NewRunRepository())
                .ExecuteAsync(new ExecutionRequest(automation.Id, Trigger(EventId), "instagram"), default);
        });

        barrier.SetResult();
        var outcomes = await Task.WhenAll(first, second);
        var statuses = outcomes.Select(o => o.Status).ToArray();

        // Exactly one winner executed; the loser collapsed onto the ledger.
        Assert.Single(statuses, s => s == ExecutionStatus.Executed);
        Assert.Contains(statuses, s => s is ExecutionStatus.Executed or ExecutionStatus.AlreadyProcessed);

        var ledger = NewRunRepository();
        var run = await ledger.FindByTriggerEventAsync(automation.Id, EventId);
        Assert.NotNull(run);
        Assert.All(run!.Actions, a => Assert.Equal(Domain.AutomationActionStatus.Succeeded, a.Status));
    }

    [Fact]
    public async Task PartialFailurePersistsAndResumesAcrossProcessBoundaries()
    {
        var automationRepo = NewAutomationRepository();
        var automation = await SeedActiveAsync(automationRepo);
        var failingDispatcher = new RecordingDispatcher(failTextContaining: "dm one");
        var useCase = new ExecuteAutomationUseCase(automationRepo, NewRunRepository(), failingDispatcher);

        var failed = await useCase.ExecuteAsync(new ExecutionRequest(automation.Id, Trigger("evt-retry-1"), "instagram"), default);
        Assert.Equal(ExecutionStatus.Failed, failed.Status);

        // Fresh repositories simulate a new process picking the retry up.
        var retryUseCase = new ExecuteAutomationUseCase(NewAutomationRepository(), NewRunRepository(), new RecordingDispatcher());
        var retried = await retryUseCase.ExecuteAsync(new ExecutionRequest(automation.Id, Trigger("evt-retry-1"), "instagram"), default);

        Assert.Equal(ExecutionStatus.Executed, retried.Status);
        var finalRun = await NewRunRepository().FindByTriggerEventAsync(automation.Id, "evt-retry-1");
        Assert.Equal(AutomationRunStatus.Completed, finalRun!.Status);
        Assert.NotNull(finalRun.FinishedAtUtc);
    }

    private static TriggerContext Trigger(string eventId) =>
        new(eventId, TriggerKind.CommentCreated, $"comment-{eventId}", "customer-1", "hello price?", Now);

    /// <summary>Accepts every dispatch unless its text contains the fault marker.</summary>
    private sealed class RecordingDispatcher(string? failTextContaining = null) : IAutomationActionDispatcher
    {
        public Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default) =>
            Task.FromResult(failTextContaining is not null && dispatch.MessageText.Contains(failTextContaining, StringComparison.Ordinal)
                ? ActionResult.Rejected("instagram.unavailable")
                : ActionResult.Delivered());
    }
}

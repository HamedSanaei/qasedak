using Microsoft.EntityFrameworkCore;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Automations.IntegrationTests;

/// <summary>
/// Follow-up settlement and crash-safety over real PostgreSQL (M13-012 §52-59): the
/// durable Attempting marker, at-least-once convergence, action metadata persistence, and
/// concurrent settlement of one scheduled slot.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class AutomationFollowUpLedgerTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 2, 20, 10, 0, 0, TimeSpan.Zero);

    private EfAutomationRunRepository NewRunRepository()
    {
        var options = new DbContextOptionsBuilder<AutomationsDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AutomationsDbContext.Schema))
            .Options;
        return new EfAutomationRunRepository(new AutomationsDbContext(options));
    }

    private EfAutomationRepository NewAutomationRepository()
    {
        var options = new DbContextOptionsBuilder<AutomationsDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AutomationsDbContext.Schema))
            .Options;
        return new EfAutomationRepository(new AutomationsDbContext(options));
    }

    private static async Task<Automation> SeedActiveAsync(EfAutomationRepository repository)
    {
        var automation = Automation.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "follow-up flow",
            AutomationDefinition.Create(
                AutomationTrigger.InboundDirectMessage(),
                [
                    new AutomationAction(
                        ActionKind.ScheduleFollowUp,
                        "still there?",
                        new ActionExtras(Delay: TimeSpan.FromMinutes(30))),
                ]),
            Now,
            new ChannelAccountId(Guid.CreateVersion7()));
        automation.Activate(Now);
        await repository.SaveChangesAsync(automation);
        return automation;
    }

    [Fact]
    public async Task ScheduledSlotAndMetadataSurviveProcessBoundaries()
    {
        var automation = await SeedActiveAsync(NewAutomationRepository());
        var useCase = new ExecuteAutomationUseCase(
            NewAutomationRepository(), NewRunRepository(), new AcceptingDispatcher(), new FixedClock(Now));

        var outcome = await useCase.ExecuteAsync(
            new ExecutionRequest(automation.Id, Trigger("mid_1"), "instagram", automation.ChannelAccountId), default);

        Assert.Equal(ExecutionStatus.Executed, outcome.Status);
        var slot = Assert.Single(outcome.Actions);
        Assert.Equal(AutomationActionStatus.Scheduled, slot.Status);

        // Fresh repositories = a new process; the slot and metadata were durably stored.
        var reloaded = await NewRunRepository().FindByTriggerEventAsync(automation.Id, "mid_1");
        Assert.NotNull(reloaded);
        var storedSlot = Assert.Single(reloaded!.Actions);
        Assert.Equal(AutomationActionStatus.Scheduled, storedSlot.Status);
        Assert.NotNull(storedSlot.AttemptedAtUtc);
        Assert.Equal("customer-1", storedSlot.ProviderRecipientId);
    }

    [Fact]
    public async Task CrashAfterMarkerSettlesUncertainWithZeroTrafficAcrossRestart()
    {
        var automation = await SeedActiveAsync(NewAutomationRepository());
        var runs = NewRunRepository();
        var runId = await ExecuteAndGetRunIdAsync(automation, runs);

        // Simulate the handler crash AFTER the durable marker (dispatch never returned).
        var marked = await new FollowUpExecutionUseCase(runs, new FixedClock(Now.AddHours(1)))
            .MarkAttemptingAsync(runId, 0, default);
        Assert.Equal(FollowUpSettlementStatus.Settled, marked.Status);

        // A fresh process redelivers the job: never re-sends; settles Uncertain terminally.
        var restartRuns = NewRunRepository();
        var settled = await new FollowUpExecutionUseCase(restartRuns, new FixedClock(Now.AddHours(2)))
            .CompleteAsync(runId, 0, AutomationActionStatus.Uncertain, "followUp.attemptInterrupted", cancellationToken: default);

        Assert.Equal(FollowUpSettlementStatus.Settled, settled.Status);
        var stored = await NewRunRepository().FindByIdAsync(runId);
        Assert.Equal(AutomationActionStatus.Uncertain, stored!.Actions[0].Status);
        Assert.Equal(AutomationRunStatus.Finished, stored.Status);
    }

    [Fact]
    public async Task ConcurrentMarkerRaceLetsExactlyOneWorkerProceed()
    {
        var automation = await SeedActiveAsync(NewAutomationRepository());
        var runs = NewRunRepository();
        var runId = await ExecuteAndGetRunIdAsync(automation, runs);

        // Two scheduler workers race for the same scheduled slot (separate DbContexts).
        var outcomes = await Task.WhenAll(
            new FollowUpExecutionUseCase(NewRunRepository(), new FixedClock(Now.AddHours(1)))
                .MarkAttemptingAsync(runId, 0, default),
            new FollowUpExecutionUseCase(NewRunRepository(), new FixedClock(Now.AddHours(1)))
                .MarkAttemptingAsync(runId, 0, default));

        // Exactly one worker holds the marker and may call the provider; the loser must not.
        Assert.Single(outcomes, o => o.MarkerAcquired);
        Assert.Single(outcomes, o => !o.MarkerAcquired && o.FinalStatus == AutomationActionStatus.Attempting);

        // The winner settles; a late redelivery replays the settled outcome as a no-op.
        var winner = outcomes.Single(o => o.MarkerAcquired);
        var settled = await new FollowUpExecutionUseCase(NewRunRepository(), new FixedClock(Now.AddHours(1)))
            .CompleteAsync(runId, 0, AutomationActionStatus.Succeeded, null, "ig-9", "mid_42", default);
        Assert.Equal(FollowUpSettlementStatus.Settled, settled.Status);

        var late = await new FollowUpExecutionUseCase(NewRunRepository(), new FixedClock(Now.AddHours(1)))
            .MarkAttemptingAsync(runId, 0, default);
        Assert.Equal(FollowUpSettlementStatus.AlreadySettled, late.Status);

        var stored = await NewRunRepository().FindByIdAsync(runId);
        Assert.Equal(AutomationActionStatus.Succeeded, stored!.Actions[0].Status);
        Assert.Equal("mid_42", stored.Actions[0].ProviderMessageId);
        Assert.Equal(AutomationRunStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task LocalRejectionRetriesSafelyAcrossRestart()
    {
        var automation = await SeedActiveAsync(NewAutomationRepository());
        var runs = NewRunRepository();

        // The immediate action rejects BEFORE any external attempt (account unavailable).
        var failing = new ExecuteAutomationUseCase(
            NewAutomationRepository(), runs, new RejectingDispatcher("still there?"), new FixedClock(Now));
        var failed = await failing.ExecuteAsync(
            new ExecutionRequest(automation.Id, Trigger("mid_2"), "instagram", automation.ChannelAccountId), default);
        Assert.Equal(ExecutionStatus.Failed, failed.Status);

        // A fresh process retries; the slot is no longer Pending but Failed — safe to resume.
        var retried = await new ExecuteAutomationUseCase(
            NewAutomationRepository(), NewRunRepository(), new AcceptingDispatcher(), new FixedClock(Now.AddMinutes(1)))
            .ExecuteAsync(
                new ExecutionRequest(automation.Id, Trigger("mid_2"), "instagram", automation.ChannelAccountId), default);

        Assert.Equal(ExecutionStatus.Executed, retried.Status);
        var stored = await NewRunRepository().FindByTriggerEventAsync(automation.Id, "mid_2");
        Assert.Equal(AutomationActionStatus.Scheduled, stored!.Actions[0].Status);
    }

    private async Task<Guid> ExecuteAndGetRunIdAsync(Automation automation, EfAutomationRunRepository runs)
    {
        var outcome = await new ExecuteAutomationUseCase(
            NewAutomationRepository(), runs, new AcceptingDispatcher(), new FixedClock(Now))
            .ExecuteAsync(
                new ExecutionRequest(automation.Id, Trigger("mid_3"), "instagram", automation.ChannelAccountId), default);
        Assert.Equal(ExecutionStatus.Executed, outcome.Status);
        return (await runs.FindByTriggerEventAsync(automation.Id, "mid_3"))!.Id;
    }

    private static TriggerContext Trigger(string eventId) =>
        new(eventId, TriggerKind.InboundDirectMessage, eventId, "customer-1", "hello price?", Now);

    private sealed class AcceptingDispatcher : IAutomationActionDispatcher
    {
        public Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Scheduled());
    }

    private sealed class RejectingDispatcher(string failTextContaining) : IAutomationActionDispatcher
    {
        public Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default) =>
            Task.FromResult(dispatch.MessageText.Contains(failTextContaining, StringComparison.Ordinal)
                ? ActionResult.RejectedLocal("direct.accountUnavailable")
                : ActionResult.Scheduled());
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}

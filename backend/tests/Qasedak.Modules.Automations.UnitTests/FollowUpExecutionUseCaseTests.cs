using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Xunit;

namespace Qasedak.Modules.Automations.UnitTests;

/// <summary>
/// Delayed follow-up settlement semantics (M13-012 §52-59): the durable Attempting marker
/// before the provider mutation, at-least-once redelivery no-ops, and stale/out-of-order
/// jobs can never settle a slot they do not own.
/// </summary>
public sealed class FollowUpExecutionUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 20, 10, 0, 0, TimeSpan.Zero);

    private static AutomationRun ScheduledRun(out FakeRunRepository repository)
    {
        var automation = Automation.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "follow-up flow",
            AutomationDefinition.Create(
                AutomationTrigger.InboundDirectMessage(),
                [new AutomationAction(ActionKind.ScheduleFollowUp, "still there?", new ActionExtras(Delay: TimeSpan.FromMinutes(30)))]),
            Now,
            new ChannelAccountId(Guid.CreateVersion7()));
        automation.Activate(Now);

        var run = AutomationRun.Start(Guid.CreateVersion7(), automation, "mid_1", Now);
        run.RecordAttempt(0, Now);
        run.RecordScheduled(0, Now.AddSeconds(1), "participant-1");

        repository = new FakeRunRepository();
        repository.Save(run);
        return run;
    }

    [Fact]
    public async Task MarkAttemptingPersistsDurableMarkerBeforeProviderMutation()
    {
        var run = ScheduledRun(out var repository);
        var useCase = new FollowUpExecutionUseCase(repository, new FixedClock(Now.AddHours(1)));

        var marked = await useCase.MarkAttemptingAsync(run.Id, 0);

        Assert.Equal(FollowUpSettlementStatus.Settled, marked.Status);
        Assert.Equal(AutomationActionStatus.Attempting, marked.FinalStatus);
        Assert.True(marked.MarkerAcquired);
        var slot = repository.Runs.Single().Actions[0];
        Assert.Equal(AutomationActionStatus.Attempting, slot.Status);
        Assert.NotNull(slot.AttemptedAtUtc);
    }

    [Fact]
    public async Task ConcurrentMarkerRaceLetsExactlyOneWorkerSend()
    {
        var run = ScheduledRun(out var repository);
        var useCase = new FollowUpExecutionUseCase(repository, new FixedClock(Now.AddHours(1)));

        var first = await useCase.MarkAttemptingAsync(run.Id, 0);
        var second = await useCase.MarkAttemptingAsync(run.Id, 0);

        Assert.True(first.MarkerAcquired);
        Assert.False(second.MarkerAcquired);
        Assert.Equal(AutomationActionStatus.Attempting, second.FinalStatus);
    }

    [Fact]
    public async Task CompleteAfterAttemptRecordsSuccessAndClosesRunCompleted()
    {
        var run = ScheduledRun(out var repository);
        var useCase = new FollowUpExecutionUseCase(repository, new FixedClock(Now.AddHours(1)));
        await useCase.MarkAttemptingAsync(run.Id, 0);

        var settled = await useCase.CompleteAsync(run.Id, 0, AutomationActionStatus.Succeeded, null, "ig-9", "mid_42");

        Assert.Equal(FollowUpSettlementStatus.Settled, settled.Status);
        var stored = repository.Runs.Single();
        Assert.Equal(AutomationRunStatus.Completed, stored.Status);
        Assert.Equal(AutomationActionStatus.Succeeded, stored.Actions[0].Status);
        Assert.Equal("ig-9", stored.Actions[0].ProviderRecipientId);
        Assert.Equal("mid_42", stored.Actions[0].ProviderMessageId);
        Assert.NotNull(stored.FinishedAtUtc);
    }

    [Fact]
    public async Task RedeliveryAfterSettlementIsANoOp()
    {
        var run = ScheduledRun(out var repository);
        var useCase = new FollowUpExecutionUseCase(repository, new FixedClock(Now.AddHours(1)));
        await useCase.MarkAttemptingAsync(run.Id, 0);
        await useCase.CompleteAsync(run.Id, 0, AutomationActionStatus.Succeeded, null, "ig-9", "mid_42");
        var savesAfterFirst = repository.SaveCount;

        // At-least-once scheduler redelivery converges on the stored outcome.
        var again = await useCase.MarkAttemptingAsync(run.Id, 0);
        var settledAgain = await useCase.CompleteAsync(run.Id, 0, AutomationActionStatus.TerminalFailed, "direct.rejected");

        Assert.Equal(FollowUpSettlementStatus.AlreadySettled, again.Status);
        Assert.Equal(FollowUpSettlementStatus.AlreadySettled, settledAgain.Status);
        Assert.Equal(savesAfterFirst, repository.SaveCount);
        Assert.Equal(AutomationActionStatus.Succeeded, repository.Runs.Single().Actions[0].Status);
    }

    [Fact]
    public async Task InterruptedAttemptSettlesUncertainWithoutSecondMutation()
    {
        var run = ScheduledRun(out var repository);
        var useCase = new FollowUpExecutionUseCase(repository, new FixedClock(Now.AddHours(1)));
        await useCase.MarkAttemptingAsync(run.Id, 0);

        // Simulate a crash between marker and outcome: the same slot arrives again. The
        // marker is NOT re-acquired — the redelivered execution must never send.
        var useCase2 = new FollowUpExecutionUseCase(repository, new FixedClock(Now.AddHours(2)));
        var redelivered = await useCase2.MarkAttemptingAsync(run.Id, 0);

        Assert.Equal(FollowUpSettlementStatus.Settled, redelivered.Status);
        Assert.Equal(AutomationActionStatus.Attempting, redelivered.FinalStatus);
        Assert.False(redelivered.MarkerAcquired);

        var settled = await useCase2.CompleteAsync(run.Id, 0, AutomationActionStatus.Uncertain, "followUp.attemptInterrupted");
        Assert.Equal(FollowUpSettlementStatus.Settled, settled.Status);
        Assert.Equal(AutomationActionStatus.Uncertain, repository.Runs.Single().Actions[0].Status);
    }

    [Fact]
    public async Task ScheduledSlotCannotBeSettledWithoutTheAttemptMarker()
    {
        var run = ScheduledRun(out var repository);
        var useCase = new FollowUpExecutionUseCase(repository, new FixedClock(Now.AddHours(1)));

        // A stale/out-of-order job that never held the durable marker cannot settle.
        var result = await useCase.CompleteAsync(run.Id, 0, AutomationActionStatus.TerminalFailed, "direct.rejected");

        Assert.Equal(FollowUpSettlementStatus.NotSettlable, result.Status);
        Assert.Equal(AutomationActionStatus.Scheduled, repository.Runs.Single().Actions[0].Status);
    }

    private sealed class FakeRunRepository : IAutomationRunRepository
    {
        public List<AutomationRun> Runs { get; } = [];

        public int SaveCount { get; private set; }

        public void Save(AutomationRun run)
        {
            Runs.Add(run);
            SaveCount++;
        }

        public Task<AutomationRun?> FindByTriggerEventAsync(Guid automationId, string triggerEventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Runs.FirstOrDefault(r => r.AutomationId == automationId && r.TriggerEventId == triggerEventId));

        public Task<AutomationRun?> FindByIdAsync(Guid runId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Runs.FirstOrDefault(r => r.Id == runId));

        public Task<AttemptMarkerResult> MarkAttemptingAsync(Guid runId, int actionIndex, DateTimeOffset attemptedAtUtc, CancellationToken cancellationToken = default)
        {
            var run = Runs.FirstOrDefault(r => r.Id == runId);
            if (run is null || actionIndex < 0 || actionIndex >= run.Actions.Count)
            {
                return Task.FromResult(AttemptMarkerResult.NotFound);
            }

            var slot = run.Actions[actionIndex];
            if (slot.Status == AutomationActionStatus.Scheduled)
            {
                run.RecordAttempt(actionIndex, attemptedAtUtc);
                SaveCount++;
                return Task.FromResult(AttemptMarkerResult.Transitioned);
            }

            if (slot.Status == AutomationActionStatus.Attempting)
            {
                return Task.FromResult(AttemptMarkerResult.AlreadyAttempting);
            }

            return Task.FromResult(slot.Status is AutomationActionStatus.Succeeded
                or AutomationActionStatus.Suppressed
                or AutomationActionStatus.Uncertain
                or AutomationActionStatus.TerminalFailed
                ? AttemptMarkerResult.AlreadySettled
                : AttemptMarkerResult.SlotNotReady);
        }

        public Task<AttemptMarkerResult> SuppressAsync(Guid runId, int actionIndex, string failureCode, DateTimeOffset completedAtUtc, AutomationActionStatus status = AutomationActionStatus.Suppressed, CancellationToken cancellationToken = default)
        {
            var run = Runs.FirstOrDefault(r => r.Id == runId);
            if (run is null || actionIndex < 0 || actionIndex >= run.Actions.Count)
            {
                return Task.FromResult(AttemptMarkerResult.NotFound);
            }

            var slot = run.Actions[actionIndex];
            if (slot.Status is AutomationActionStatus.Scheduled or AutomationActionStatus.Attempting)
            {
                run.RecordTerminal(actionIndex, status, failureCode, completedAtUtc);
                SaveCount++;
                return Task.FromResult(AttemptMarkerResult.Transitioned);
            }

            return Task.FromResult(slot.Status is AutomationActionStatus.Succeeded
                or AutomationActionStatus.Suppressed
                or AutomationActionStatus.Uncertain
                or AutomationActionStatus.TerminalFailed
                ? AttemptMarkerResult.AlreadySettled
                : AttemptMarkerResult.SlotNotReady);
        }

        public Task SaveChangesAsync(AutomationRun run, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}

using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Xunit;

namespace Qasedak.Modules.Automations.UnitTests;

/// <summary>
/// Orchestration semantics over fakes: ordering, redelivery idempotency, partial-failure
/// resumption, disabled/stale refusals, exact-account binding enforcement.
/// </summary>
public sealed class ExecuteAutomationTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 15, 12, 0, 0, TimeSpan.Zero);

    private const string EventId = "inbox-event-42";

    private static Automation ActiveAutomation(int actionCount = 2, ChannelAccountId? account = null)
    {
        // A comment-triggered definition may consume the one Private Reply allowance once:
        // the first slot uses the legacy origin-aware action, later slots use the separate
        // public-reply effect (coexisting is valid per M13-009/M13-012 semantics).
        var actions = Enumerable.Range(1, actionCount)
            .Select(i => new AutomationAction(
                i == 1 ? ActionKind.SendDirectMessage : ActionKind.SendPublicReply,
                $"message-{i}"))
            .ToArray();
        var automation = Automation.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "flow", AutomationDefinition.Create(AutomationTrigger.CommentCreated(), actions), Now,
            account ?? new ChannelAccountId(Guid.CreateVersion7()));
        automation.Activate(Now);
        return automation;
    }

    private static TriggerContext Context(string? eventId = EventId, string? text = "price?") =>
        new(eventId!, TriggerKind.CommentCreated, "comment-77", "customer-9", text, Now);

    private static ExecutionRequest Request(Automation automation, TriggerContext? context = null) =>
        new(automation.Id, context ?? Context(), "instagram", automation.ChannelAccountId);

    private sealed class FakeAutomationRepository(params Automation[] automations) : IAutomationRepository
    {
        public List<Automation> Threads { get; } = automations.ToList();

        public Task<Automation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Threads.FirstOrDefault(a => a.Id == id));

        public Task<IReadOnlyList<Automation>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Automation>> ListByAccountAsync(Guid workspaceId, ChannelAccountId channelAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Automation>>(Threads.Where(a => a.ChannelAccountId == channelAccountId).ToList());

        public Task<AutomationReconciliationScope?> GetCommentReconciliationScopeAsync(Guid workspaceId, ChannelAccountId channelAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AutomationReconciliationScope?>(null);

        public Task SaveChangesAsync(Automation automation, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeRunRepository : IAutomationRunRepository
    {
        public Dictionary<(Guid AutomationId, string TriggerEventId), AutomationRun> Runs { get; } = [];

        public int SaveCount { get; private set; }

        /// <summary>When set, the next insert throws as if a concurrent worker won the ledger slot.</summary>
        public bool SimulateConcurrentInsertLoss { get; set; }

        public Task<AutomationRun?> FindByTriggerEventAsync(Guid automationId, string triggerEventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Runs.TryGetValue((automationId, triggerEventId), out var run) ? run : null);

        public Task<AutomationRun?> FindByIdAsync(Guid runId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Runs.Values.FirstOrDefault(r => r.Id == runId));

        public Task<AttemptMarkerResult> MarkAttemptingAsync(Guid runId, int actionIndex, DateTimeOffset attemptedAtUtc, CancellationToken cancellationToken = default)
        {
            var run = Runs.Values.FirstOrDefault(r => r.Id == runId);
            if (run is null || actionIndex < 0 || actionIndex >= run.Actions.Count)
            {
                return Task.FromResult(AttemptMarkerResult.NotFound);
            }

            var slot = run.Actions[actionIndex];
            if (slot.Status == Domain.AutomationActionStatus.Scheduled)
            {
                run.RecordAttempt(actionIndex, attemptedAtUtc);
                return Task.FromResult(AttemptMarkerResult.Transitioned);
            }

            if (slot.Status == Domain.AutomationActionStatus.Attempting)
            {
                return Task.FromResult(AttemptMarkerResult.AlreadyAttempting);
            }

            return Task.FromResult(slot.Status is Domain.AutomationActionStatus.Succeeded
                or Domain.AutomationActionStatus.Suppressed
                or Domain.AutomationActionStatus.Uncertain
                or Domain.AutomationActionStatus.TerminalFailed
                ? AttemptMarkerResult.AlreadySettled
                : AttemptMarkerResult.SlotNotReady);
        }

        public Task<AttemptMarkerResult> SuppressAsync(Guid runId, int actionIndex, string failureCode, DateTimeOffset completedAtUtc, Domain.AutomationActionStatus status = Domain.AutomationActionStatus.Suppressed, CancellationToken cancellationToken = default)
        {
            var run = Runs.Values.FirstOrDefault(r => r.Id == runId);
            if (run is null || actionIndex < 0 || actionIndex >= run.Actions.Count)
            {
                return Task.FromResult(AttemptMarkerResult.NotFound);
            }

            var slot = run.Actions[actionIndex];
            if (slot.Status is Domain.AutomationActionStatus.Scheduled or Domain.AutomationActionStatus.Attempting)
            {
                run.RecordTerminal(actionIndex, status, failureCode, completedAtUtc);
                return Task.FromResult(AttemptMarkerResult.Transitioned);
            }

            return Task.FromResult(slot.Status is Domain.AutomationActionStatus.Succeeded
                or Domain.AutomationActionStatus.Suppressed
                or Domain.AutomationActionStatus.Uncertain
                or Domain.AutomationActionStatus.TerminalFailed
                ? AttemptMarkerResult.AlreadySettled
                : AttemptMarkerResult.SlotNotReady);
        }

        public Task SaveChangesAsync(AutomationRun run, CancellationToken cancellationToken = default)
        {
            if (SimulateConcurrentInsertLoss && !Runs.ContainsKey((run.AutomationId, run.TriggerEventId)))
            {
                SimulateConcurrentInsertLoss = false;
                throw new InvalidOperationException("23505: duplicate key value violates unique constraint");
            }

            Runs[(run.AutomationId, run.TriggerEventId)] = run;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDispatcher(Func<ActionDispatch, ActionResult>? respond = null) : IAutomationActionDispatcher
    {
        public List<ActionDispatch> Dispatches { get; } = [];

        /// <summary>Fail dispatches whose text contains this marker with a PROVED-local rejection.</summary>
        public string? RejectLocallyTextContaining { get; set; }

        /// <summary>When set, the next dispatch throws (simulating a crash after the marker).</summary>
        public bool CrashOnNextDispatch { get; set; }

        public Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default)
        {
            Dispatches.Add(dispatch);
            if (CrashOnNextDispatch)
            {
                CrashOnNextDispatch = false;
                throw new InvalidOperationException("process died");
            }

            var result = respond?.Invoke(dispatch);
            if (result is not null)
            {
                return Task.FromResult(result);
            }

            return RejectLocallyTextContaining is not null && dispatch.MessageText.Contains(RejectLocallyTextContaining, StringComparison.Ordinal)
                ? Task.FromResult(ActionResult.RejectedLocal("instagram.unavailable"))
                : Task.FromResult(ActionResult.Delivered());
        }
    }

    /// <summary>Deterministic clock for reproducible time-dependent behavior.</summary>
    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    [Fact]
    public async Task FirstDeliveryExecutesAllActionsInOrderAndCompletes()
    {
        var automation = ActiveAutomation(actionCount: 3);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        var outcome = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Executed, outcome.Status);
        Assert.Equal(["message-1", "message-2", "message-3"], dispatcher.Dispatches.Select(d => d.MessageText));
        Assert.All(outcome.Actions, a => Assert.Equal(Domain.AutomationActionStatus.Succeeded, a.Status));
        // One save for the ledger insert + two per action slot (durable Attempting marker, outcome).
        Assert.Equal(7, runsRepo.SaveCount);
    }

    [Fact]
    public async Task RedeliveryNeverReDispatches()
    {
        var automation = ActiveAutomation();
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));
        await useCase.ExecuteAsync(Request(automation), default);
        var dispatchesAfterFirst = dispatcher.Dispatches.Count;

        var second = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.AlreadyProcessed, second.Status);
        Assert.Equal(dispatchesAfterFirst, dispatcher.Dispatches.Count);
    }

    [Fact]
    public async Task ConcurrentDuplicateStartMapsToAlreadyProcessed()
    {
        var automation = ActiveAutomation();
        var runsRepo = new FakeRunRepository { SimulateConcurrentInsertLoss = true };
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        // The probe misses (the other worker has not committed yet); our insert then loses
        // the ledger race exactly as the database's unique index would enforce.
        var loser = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.AlreadyProcessed, loser.Status);
        Assert.Empty(dispatcher.Dispatches);
    }

    [Fact]
    public async Task LocalRejectionIsRecordedFailedAndRetriesResumeOnlyPendingSlots()
    {
        var automation = ActiveAutomation(actionCount: 2);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher { RejectLocallyTextContaining = "message-1" };
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        var failed = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Failed, failed.Status);
        Assert.Equal(Domain.AutomationActionStatus.Failed, failed.Actions[0].Status);
        Assert.Equal("instagram.unavailable", failed.Actions[0].FailureCode);
        // The local rejection proved no external attempt; the run stays retryable.
        Assert.Equal(AutomationRunStatus.Failed, runsRepo.Runs.Values.Single().Status);

        // Retry with the fault removed: only the pending slots dispatch again.
        dispatcher.RejectLocallyTextContaining = null;
        var retried = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Executed, retried.Status);
        Assert.All(retried.Actions, a => Assert.Equal(Domain.AutomationActionStatus.Succeeded, a.Status));
        // Pass 2 resumed only the failed slot; the succeeded slot was never re-dispatched.
        Assert.Equal(3, dispatcher.Dispatches.Count);
        Assert.Equal(["message-1", "message-2", "message-1"], dispatcher.Dispatches.Select(d => d.MessageText));
    }

    [Fact]
    public async Task RejectionAfterExternalAttemptIsTerminalNeverRedispatched()
    {
        var automation = ActiveAutomation(actionCount: 1);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher(_ => ActionResult.Rejected("instagram.windowExpired"));
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        var outcome = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Finished, outcome.Status);
        Assert.Equal(Domain.AutomationActionStatus.TerminalFailed, outcome.Actions[0].Status);
        Assert.Equal("instagram.windowExpired", outcome.Actions[0].FailureCode);

        // Redelivery must never re-dispatch an externally attempted slot.
        var again = await useCase.ExecuteAsync(Request(automation), default);
        Assert.Equal(ExecutionStatus.Finished, again.Status);
        Assert.Single(dispatcher.Dispatches);
    }

    [Fact]
    public async Task CrashAfterAttemptMarkerRecoversAsUncertainWithZeroResend()
    {
        var automation = ActiveAutomation(actionCount: 1);
        var runsRepo = new FakeRunRepository();
        var crashing = new FakeDispatcher { CrashOnNextDispatch = true };
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, crashing, new FixedClock(Now));

        // The process dies between the durable marker and the outcome record.
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(Request(automation), default));
        var interrupted = runsRepo.Runs.Values.Single();
        Assert.Equal(Domain.AutomationActionStatus.Attempting, interrupted.Actions[0].Status);
        Assert.NotNull(interrupted.Actions[0].AttemptedAtUtc);

        // A fresh process resumes: the Attempting slot is settled Uncertain, zero traffic.
        var resumed = new FakeDispatcher();
        var retry = await new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, resumed, new FixedClock(Now))
            .ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Finished, retry.Status);
        Assert.Equal(Domain.AutomationActionStatus.Uncertain, retry.Actions[0].Status);
        Assert.Equal("action.attemptInterrupted", retry.Actions[0].FailureCode);
        Assert.Empty(resumed.Dispatches);
    }

    [Fact]
    public async Task AttemptMarkerIsPersistedBeforeDispatch()
    {
        var automation = ActiveAutomation(actionCount: 1);
        var runsRepo = new FakeRunRepository();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, new FakeDispatcher(), new FixedClock(Now));

        await useCase.ExecuteAsync(Request(automation), default);

        var slot = runsRepo.Runs.Values.Single().Actions[0];
        Assert.Equal(Domain.AutomationActionStatus.Succeeded, slot.Status);
        Assert.NotNull(slot.AttemptedAtUtc);
        Assert.NotNull(slot.CompletedAtUtc);
        // The Attempting marker was durable before the outcome (at least one save in between).
        Assert.True(runsRepo.SaveCount >= 3);
    }

    [Fact]
    public async Task ScheduledOutcomeMarksSlotScheduledAndCompletesRun()
    {
        var automation = ActiveAutomation(actionCount: 1);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher(_ => ActionResult.Scheduled());
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        var outcome = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Executed, outcome.Status);
        var slot = Assert.Single(outcome.Actions);
        Assert.Equal(Domain.AutomationActionStatus.Scheduled, slot.Status);
        // The follow-up settles later — the run stays Running until then.
        Assert.Equal(AutomationRunStatus.Running, runsRepo.Runs.Values.Single().Status);
        Assert.NotNull(slot.ProviderRecipientId);
    }

    [Fact]
    public async Task ContinuationStartedOutcomeMarksSlotAndCompletesRun()
    {
        var automation = ActiveAutomation(actionCount: 1);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher(_ => ActionResult.ContinuationStarted());
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        var outcome = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Executed, outcome.Status);
        Assert.Equal(Domain.AutomationActionStatus.ContinuationStarted, outcome.Actions[0].Status);
        // The reveal continuation outlives the dispatch — the run stays Running.
        Assert.Equal(AutomationRunStatus.Running, runsRepo.Runs.Values.Single().Status);
    }

    [Fact]
    public async Task DispatchCarriesRunIdAndExtras()
    {
        var automation = ActiveAutomation(actionCount: 1);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        await useCase.ExecuteAsync(Request(automation), default);

        var dispatch = Assert.Single(dispatcher.Dispatches);
        Assert.Equal(runsRepo.Runs.Values.Single().Id, dispatch.RunId);
        // Legacy actions carry no extras — the run ledger is the origin of the id.
        Assert.Null(dispatch.Extras);
    }

    [Fact]
    public async Task DisabledAutomationsRefuseWithoutAnyDispatch()
    {
        var automation = ActiveAutomation();
        automation.Disable(Now.AddMinutes(1));
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), new FakeRunRepository(), dispatcher, new FixedClock(Now));

        var refused = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.RefusedNotActive, refused.Status);
        Assert.Empty(dispatcher.Dispatches);
    }

    [Fact]
    public async Task UnknownOrForeignAutomationRefuses()
    {
        var automation = ActiveAutomation();
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), new FakeRunRepository(), dispatcher, new FixedClock(Now));

        var unknown = await useCase.ExecuteAsync(
            new ExecutionRequest(Guid.CreateVersion7(), Context(), "instagram", automation.ChannelAccountId), default);
        var foreign = await useCase.ExecuteAsync(
            new ExecutionRequest(automation.Id, Context(), "instagram", automation.ChannelAccountId, Guid.CreateVersion7()), default);

        Assert.Equal(ExecutionStatus.RefusedNotActive, unknown.Status);
        Assert.Equal(ExecutionStatus.RefusedNotActive, foreign.Status);
        Assert.Empty(dispatcher.Dispatches);
    }

    [Fact]
    public async Task MismatchedAccountRefusesWithoutDispatchOrLedgerWrite()
    {
        var automation = ActiveAutomation();
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        var otherAccount = new ChannelAccountId(Guid.CreateVersion7());
        var refused = await useCase.ExecuteAsync(
            new ExecutionRequest(automation.Id, Context(), "instagram", otherAccount), default);

        Assert.Equal(ExecutionStatus.RefusedNotActive, refused.Status);
        Assert.Empty(dispatcher.Dispatches);
        Assert.Empty(runsRepo.Runs);
    }

    [Fact]
    public async Task LegacyUnboundAutomationNeverMatchesExactAccountRequest()
    {
        var actions = new[] { new AutomationAction(ActionKind.SendDirectMessage, "legacy") };
        var legacy = Automation.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "legacy",
            AutomationDefinition.Create(AutomationTrigger.CommentCreated(), actions), Now);
        legacy.Activate(Now);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(legacy), runsRepo, dispatcher, new FixedClock(Now));

        var refused = await useCase.ExecuteAsync(
            new ExecutionRequest(legacy.Id, Context(), "instagram", new ChannelAccountId(Guid.CreateVersion7())), default);

        Assert.Equal(ExecutionStatus.RefusedNotActive, refused.Status);
        Assert.Empty(dispatcher.Dispatches);
        Assert.Empty(runsRepo.Runs);
    }

    [Fact]
    public async Task UnresolvedRequestAccountRefusesEvenBoundAutomation()
    {
        var automation = ActiveAutomation();
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), new FakeRunRepository(), dispatcher, new FixedClock(Now));

        var refused = await useCase.ExecuteAsync(
            new ExecutionRequest(automation.Id, Context(), "instagram", null), default);

        Assert.Equal(ExecutionStatus.RefusedNotActive, refused.Status);
        Assert.Empty(dispatcher.Dispatches);
    }

    [Fact]
    public async Task NonMatchingEventsDoNotTouchTheLedger()
    {
        var automation = ActiveAutomation();
        automation.Unpublish(Now); // pause → definition edit to a non-matching filter → reactivate
        automation.ReviseDraftDefinition(
            AutomationDefinition.Create(AutomationTrigger.CommentCreated("buy-now"), [new AutomationAction(ActionKind.SendDirectMessage, "promo")]), Now);
        automation.Activate(Now);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        var outcome = await useCase.ExecuteAsync(Request(automation, Context(text: "just saying hi")), default);

        Assert.Equal(ExecutionStatus.NotMatched, outcome.Status);
        Assert.Empty(runsRepo.Runs);
        Assert.Empty(dispatcher.Dispatches);
    }

    // ------------------------------------------------------------------ M13-009 terminal one-shot outcomes

    [Fact]
    public async Task DispatchCarriesTriggerOriginSemantics()
    {
        var automation = ActiveAutomation(actionCount: 1);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));
        var context = Context() with { IsLiveComment = true };

        await useCase.ExecuteAsync(Request(automation, context), default);

        var dispatch = Assert.Single(dispatcher.Dispatches);
        Assert.Equal(TriggerKind.CommentCreated, dispatch.TriggerKind);
        Assert.Equal("comment-77", dispatch.TriggerEntityId);
        Assert.Equal(Now, dispatch.TriggerOccurredAtUtc);
        Assert.True(dispatch.IsLiveComment);
        Assert.Equal(0, dispatch.ActionIndex);
        Assert.Equal(ActionKind.SendDirectMessage, dispatch.ActionKind);
    }

    [Fact]
    public async Task SuppressedOutcomeRecordsSuppressedSlotAndClosesRunFinished()
    {
        var automation = ActiveAutomation(actionCount: 1);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher(_ => ActionResult.TerminalSuppressed("privateReply.alreadyClaimed"));
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        var outcome = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Finished, outcome.Status);
        var slot = Assert.Single(outcome.Actions);
        Assert.Equal(Domain.AutomationActionStatus.Suppressed, slot.Status);
        Assert.Equal("privateReply.alreadyClaimed", slot.FailureCode);
        Assert.NotEqual(Domain.AutomationActionStatus.Succeeded, slot.Status);
    }

    [Fact]
    public async Task UncertainOutcomeRecordsUncertainSlotAndClosesRunFinished()
    {
        var automation = ActiveAutomation(actionCount: 1);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher(_ => ActionResult.TerminalUncertain("privateReply.uncertain"));
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        var outcome = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Finished, outcome.Status);
        var slot = Assert.Single(outcome.Actions);
        Assert.Equal(Domain.AutomationActionStatus.Uncertain, slot.Status);
        Assert.Equal("privateReply.uncertain", slot.FailureCode);
    }

    [Fact]
    public async Task TerminalProviderFailureRecordsTerminalFailedSlotAndClosesRunFinished()
    {
        var automation = ActiveAutomation(actionCount: 1);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher(_ => ActionResult.TerminalFailed("privateReply.terminalFailed.rejectedByMeta"));
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        var outcome = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Finished, outcome.Status);
        var slot = Assert.Single(outcome.Actions);
        Assert.Equal(Domain.AutomationActionStatus.TerminalFailed, slot.Status);
        Assert.Equal("privateReply.terminalFailed.rejectedByMeta", slot.FailureCode);
    }

    [Fact]
    public async Task FinishedRunIsNeverRedispatchedOrSaved()
    {
        var automation = ActiveAutomation(actionCount: 1);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher(_ => ActionResult.TerminalSuppressed("privateReply.alreadyClaimed"));
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        var first = await useCase.ExecuteAsync(Request(automation), default);
        Assert.Equal(ExecutionStatus.Finished, first.Status);
        var savesAfterFirst = runsRepo.SaveCount;

        // Webhook redelivery: a Finished run is immutable — no dispatch, no write.
        var again = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Finished, again.Status);
        Assert.Equal(savesAfterFirst, runsRepo.SaveCount);
        Assert.Single(dispatcher.Dispatches);
    }

    [Fact]
    public async Task MixedTerminalAndSucceededSlotsCloseRunFinishedNotCompleted()
    {
        var automation = ActiveAutomation(actionCount: 2);
        var runsRepo = new FakeRunRepository();
        var responses = new Queue<ActionResult>([
            ActionResult.TerminalSuppressed("privateReply.alreadyClaimed"),
            ActionResult.Delivered(),
        ]);
        var dispatcher = new FakeDispatcher(_ => responses.Dequeue());
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher, new FixedClock(Now));

        var outcome = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Finished, outcome.Status);
        Assert.Equal(Domain.AutomationActionStatus.Suppressed, outcome.Actions[0].Status);
        Assert.Equal(Domain.AutomationActionStatus.Succeeded, outcome.Actions[1].Status);
    }
}

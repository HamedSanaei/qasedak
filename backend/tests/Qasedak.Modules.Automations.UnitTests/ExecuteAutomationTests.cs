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
        var actions = Enumerable.Range(1, actionCount)
            .Select(i => new AutomationAction(ActionKind.SendDirectMessage, $"message-{i}"))
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

        /// <summary>Fail dispatches whose text contains this marker (for partial-failure tests).</summary>
        public string? FailTextContaining { get; set; }

        public Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default)
        {
            Dispatches.Add(dispatch);
            var result = respond?.Invoke(dispatch) ?? ActionResult.Delivered();
            if (!result.Accepted)
            {
                return Task.FromResult(result);
            }

            return FailTextContaining is not null && dispatch.MessageText.Contains(FailTextContaining, StringComparison.Ordinal)
                ? Task.FromResult(ActionResult.Rejected("instagram.unavailable"))
                : Task.FromResult(ActionResult.Delivered());
        }
    }

    [Fact]
    public async Task FirstDeliveryExecutesAllActionsInOrderAndCompletes()
    {
        var automation = ActiveAutomation(actionCount: 3);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher);

        var outcome = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Executed, outcome.Status);
        Assert.Equal(["message-1", "message-2", "message-3"], dispatcher.Dispatches.Select(d => d.MessageText));
        Assert.All(outcome.Actions, a => Assert.Equal(Domain.AutomationActionStatus.Succeeded, a.Status));
        // One save for the ledger insert + one per action slot.
        Assert.Equal(4, runsRepo.SaveCount);
    }

    [Fact]
    public async Task RedeliveryNeverReDispatches()
    {
        var automation = ActiveAutomation();
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher);
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
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher);

        // The probe misses (the other worker has not committed yet); our insert then loses
        // the ledger race exactly as the database's unique index would enforce.
        var loser = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.AlreadyProcessed, loser.Status);
        Assert.Empty(dispatcher.Dispatches);
    }

    [Fact]
    public async Task FailedActionIsRecordedAndRetriesResumeOnlyPendingSlots()
    {
        var automation = ActiveAutomation(actionCount: 2);
        var runsRepo = new FakeRunRepository();
        var dispatcher = new FakeDispatcher { FailTextContaining = "message-1" };
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher);

        var failed = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Failed, failed.Status);
        Assert.Equal(Domain.AutomationActionStatus.Failed, failed.Actions[0].Status);
        Assert.Equal("instagram.unavailable", failed.Actions[0].FailureCode);

        // Retry with the fault removed: only the pending slots dispatch again.
        dispatcher.FailTextContaining = null;
        var retried = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.Executed, retried.Status);
        Assert.All(retried.Actions, a => Assert.Equal(Domain.AutomationActionStatus.Succeeded, a.Status));
        // Pass 2 resumed only the failed slot; the succeeded slot was never re-dispatched.
        Assert.Equal(3, dispatcher.Dispatches.Count);
        Assert.Equal(["message-1", "message-2", "message-1"], dispatcher.Dispatches.Select(d => d.MessageText));
    }

    [Fact]
    public async Task DisabledAutomationsRefuseWithoutAnyDispatch()
    {
        var automation = ActiveAutomation();
        automation.Disable(Now.AddMinutes(1));
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), new FakeRunRepository(), dispatcher);

        var refused = await useCase.ExecuteAsync(Request(automation), default);

        Assert.Equal(ExecutionStatus.RefusedNotActive, refused.Status);
        Assert.Empty(dispatcher.Dispatches);
    }

    [Fact]
    public async Task UnknownOrForeignAutomationRefuses()
    {
        var automation = ActiveAutomation();
        var dispatcher = new FakeDispatcher();
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), new FakeRunRepository(), dispatcher);

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
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher);

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
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(legacy), runsRepo, dispatcher);

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
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), new FakeRunRepository(), dispatcher);

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
        var useCase = new ExecuteAutomationUseCase(new FakeAutomationRepository(automation), runsRepo, dispatcher);

        var outcome = await useCase.ExecuteAsync(Request(automation, Context(text: "just saying hi")), default);

        Assert.Equal(ExecutionStatus.NotMatched, outcome.Status);
        Assert.Empty(runsRepo.Runs);
        Assert.Empty(dispatcher.Dispatches);
    }
}

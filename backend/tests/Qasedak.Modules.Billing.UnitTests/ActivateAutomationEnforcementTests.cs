using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Xunit;

namespace Qasedak.Modules.Billing.UnitTests;

/// <summary>
/// Activation-policy enforcement at the application boundary: denials carry stable codes,
/// allowed activations persist, and the pending automation itself is excluded from the
/// active count.
/// </summary>
public sealed class ActivateAutomationUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 11, 0, 0, 0, TimeSpan.Zero);

    private static Automation NewDraft(Guid workspaceId) => Automation.Create(
        Guid.CreateVersion7(),
        workspaceId,
        "Reply to price questions",
        AutomationDefinition.Create(
            AutomationTrigger.CommentCreated("price"),
            [new AutomationAction(ActionKind.SendDirectMessage, "Thanks for asking!")]),
        Now);

    private sealed class FakeRepository(params Automation[] automations) : IAutomationRepository
    {
        public List<Automation> Store { get; } = [.. automations];

        public Task<Automation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Store.FirstOrDefault(a => a.Id == id));

        public Task<IReadOnlyList<Automation>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Automation>>(Store.Where(a => a.WorkspaceId == workspaceId).ToList());

        public async Task SaveChangesAsync(Automation automation, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var index = Store.FindIndex(a => a.Id == automation.Id);
            if (index >= 0)
            {
                Store[index] = automation;
            }
        }
    }

    private sealed class StubPolicy(string? denial) : IAutomationActivationPolicy
    {
        public int LastActiveCount { get; private set; }

        public Task<string?> CheckActivationAllowedAsync(Guid workspaceId, int currentlyActiveAutomations, CancellationToken cancellationToken = default)
        {
            LastActiveCount = currentlyActiveAutomations;
            return Task.FromResult(denial);
        }
    }

    [Fact]
    public async Task AllowedPolicyActivatesAndPersists()
    {
        var workspaceId = Guid.CreateVersion7();
        var automation = NewDraft(workspaceId);
        var repository = new FakeRepository(automation);
        var useCase = new ActivateAutomationUseCase(repository, new StubPolicy(null));

        var activated = await useCase.ExecuteAsync(workspaceId, automation.Id, Now.AddMinutes(1));

        Assert.Equal(AutomationStatus.Active, activated.Status);
        Assert.Equal(AutomationStatus.Active, repository.Store.Single().Status);
    }

    [Fact]
    public async Task PolicyDenialSurfacesStableCodeAndLeavesStateUntouched()
    {
        var workspaceId = Guid.CreateVersion7();
        var automation = NewDraft(workspaceId);
        var repository = new FakeRepository(automation);
        var useCase = new ActivateAutomationUseCase(repository, new StubPolicy(Billing.Application.EntitlementDecision.LimitExceededCode));

        var denied = await Assert.ThrowsAsync<AutomationsDomainException>(
            () => useCase.ExecuteAsync(workspaceId, automation.Id, Now.AddMinutes(1)));

        Assert.Equal(Billing.Application.EntitlementDecision.LimitExceededCode, denied.RuleCode);
        Assert.Equal(AutomationStatus.Draft, repository.Store.Single().Status);
    }

    [Fact]
    public async Task PendingAutomationIsExcludedFromActiveCount()
    {
        var workspaceId = Guid.CreateVersion7();
        var activePeer = NewDraft(workspaceId);
        activePeer.Activate(Now);
        var target = NewDraft(workspaceId);
        var repository = new FakeRepository(activePeer, target);
        var policy = new StubPolicy(null);
        var useCase = new ActivateAutomationUseCase(repository, policy);

        await useCase.ExecuteAsync(workspaceId, target.Id, Now.AddMinutes(1));

        // Only the already-active peer counts against the limit.
        Assert.Equal(1, policy.LastActiveCount);
    }

    [Fact]
    public async Task ForeignWorkspaceIsIndistinguishableFromMissing()
    {
        var automation = NewDraft(Guid.CreateVersion7());
        var useCase = new ActivateAutomationUseCase(new FakeRepository(automation), new StubPolicy(null));

        var denied = await Assert.ThrowsAsync<AutomationsDomainException>(
            () => useCase.ExecuteAsync(Guid.CreateVersion7(), automation.Id, Now));

        Assert.Equal(AutomationFailures.NotFound, denied.RuleCode);
    }
}

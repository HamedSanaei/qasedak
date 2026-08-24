using Qasedak.BuildingBlocks.Application.Auditing;
using Qasedak.Modules.Billing.Domain;

namespace Qasedak.Modules.Billing.Application;

/// <summary>Effective server-side entitlements for a workspace at a point in time.</summary>
public sealed record WorkspaceEntitlements(Guid WorkspaceId, bool Entitled, IReadOnlyDictionary<string, int> Limits)
{
    public static readonly WorkspaceEntitlements None = new(Guid.Empty, false, new Dictionary<string, int>());
}

/// <summary>
/// Resolves the effective entitlements for a workspace from its current subscription and
/// that subscription's plan. Server-owned by design: callers never pass entitlement data
/// in — it is always computed from persisted state.
/// </summary>
public sealed class ResolveWorkspaceEntitlementsUseCase(ISubscriptionRepository subscriptions, IPlanRepository plans)
{
    public async Task<WorkspaceEntitlements> ExecuteAsync(Guid workspaceId, DateTimeOffset atUtc, CancellationToken cancellationToken = default)
    {
        var subscription = await subscriptions.FindByWorkspaceAsync(workspaceId, cancellationToken);
        if (subscription is null || !subscription.IsEntitledAt(atUtc))
        {
            return new WorkspaceEntitlements(workspaceId, false, new Dictionary<string, int>());
        }

        var plan = await plans.FindByIdAsync(subscription.PlanId, cancellationToken);
        if (plan is null)
        {
            // A live subscription pointing at a missing plan fails CLOSED.
            return new WorkspaceEntitlements(workspaceId, false, new Dictionary<string, int>());
        }

        var limits = plan.Entitlements.ToDictionary(e => e.FeatureKey, e => e.Limit);
        return new WorkspaceEntitlements(workspaceId, true, limits);
    }
}

/// <summary>Starts a workspace's trial or paid subscription (one live per workspace).</summary>
public sealed class StartSubscriptionUseCase(
    ISubscriptionRepository subscriptions,
    IPlanRepository plans,
    IAuditTrail? audit = null)
{
    public async Task<Subscription> StartTrialAsync(Guid workspaceId, string planCode, DateTimeOffset startedAtUtc, TimeSpan trialLength, CancellationToken cancellationToken = default)
    {
        await EnsureNoLiveSubscriptionAsync(workspaceId, cancellationToken);
        var plan = await ResolvePlanAsync(planCode, cancellationToken);
        var subscription = Subscription.StartTrial(
            Guid.CreateVersion7(), workspaceId, plan.Id, startedAtUtc, startedAtUtc.Add(trialLength));
        await subscriptions.SaveChangesAsync(subscription, cancellationToken);

        if (audit is not null)
        {
            // Billing-sensitive action: recorded without any provider or payment detail.
            await audit.RecordAsync(AuditEntry.New(
                "billing.subscription.started",
                startedAtUtc,
                workspaceId: workspaceId,
                targetType: "subscription",
                targetId: subscription.Id.ToString(),
                detailsJson: System.Text.Json.JsonSerializer.Serialize(new { kind = "trial", planCode })), cancellationToken);
        }

        return subscription;
    }

    public async Task<Subscription> ActivateAsync(Guid workspaceId, string planCode, DateTimeOffset startedAtUtc, TimeSpan firstPeriod, CancellationToken cancellationToken = default)
    {
        await EnsureNoLiveSubscriptionAsync(workspaceId, cancellationToken);
        var plan = await ResolvePlanAsync(planCode, cancellationToken);
        var subscription = Subscription.Activate(
            Guid.CreateVersion7(), workspaceId, plan.Id, startedAtUtc, startedAtUtc.Add(firstPeriod));
        await subscriptions.SaveChangesAsync(subscription, cancellationToken);

        if (audit is not null)
        {
            await audit.RecordAsync(AuditEntry.New(
                "billing.subscription.started",
                startedAtUtc,
                workspaceId: workspaceId,
                targetType: "subscription",
                targetId: subscription.Id.ToString(),
                detailsJson: System.Text.Json.JsonSerializer.Serialize(new { kind = "active", planCode })), cancellationToken);
        }

        return subscription;
    }

    private async Task<Plan> ResolvePlanAsync(string planCode, CancellationToken cancellationToken)
    {
        var plan = await plans.FindByCodeAsync(planCode, cancellationToken)
            ?? throw new BillingDomainException("billing.planNotFound", $"Plan '{planCode}' does not exist.");
        return plan;
    }

    private async Task EnsureNoLiveSubscriptionAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var existing = await subscriptions.FindByWorkspaceAsync(workspaceId, cancellationToken);
        if (existing is not null && existing.Status is not (SubscriptionStatus.Canceled or SubscriptionStatus.Expired))
        {
            throw new BillingDomainException("billing.subscriptionExists", "The workspace already has a live subscription.");
        }
    }
}

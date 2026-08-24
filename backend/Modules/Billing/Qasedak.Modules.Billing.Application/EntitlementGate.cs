using Qasedak.Modules.Billing.Domain;

namespace Qasedak.Modules.Billing.Application;

/// <summary>Result of an entitlement check: allowed, or denied with a stable code.</summary>
public sealed record EntitlementDecision(bool Allowed, string? DenialCode = null, int? Limit = null)
{
    public static readonly EntitlementDecision Allow = new(true);

    public static EntitlementDecision Deny(string code, int? limit = null) => new(false, code, limit);

    public const string SubscriptionRequiredCode = "billing.subscriptionRequired";

    public const string LimitExceededCode = "billing.limitExceeded";
}

/// <summary>
/// Server-side entitlement enforcement boundary. Callers NEVER pass entitlement claims in;
/// every decision is computed from persisted subscription/plan state and fails closed
/// (no subscription, expired period, or missing plan ⇒ not entitled).
/// </summary>
public sealed class EntitlementGate(ISubscriptionRepository subscriptions, IPlanRepository plans)
{
    /// <summary>Whether the workspace holds any payable entitlements at the given instant.</summary>
    public async Task<EntitlementDecision> RequireEntitledAsync(Guid workspaceId, DateTimeOffset atUtc, CancellationToken cancellationToken = default)
    {
        var subscription = await subscriptions.FindByWorkspaceAsync(workspaceId, cancellationToken);
        if (subscription is null || !subscription.IsEntitledAt(atUtc))
        {
            return EntitlementDecision.Deny(EntitlementDecision.SubscriptionRequiredCode);
        }

        var plan = await plans.FindByIdAsync(subscription.PlanId, cancellationToken);
        return plan is null
            ? EntitlementDecision.Deny(EntitlementDecision.SubscriptionRequiredCode)
            : EntitlementDecision.Allow;
    }

    /// <summary>
    /// Count-limit check for a numeric feature (e.g. active automations). Unlimited (-1)
    /// passes any usage; disabled (0) denies everything; otherwise currentUsage must be
    /// below the limit to allow one more.
    /// </summary>
    public async Task<EntitlementDecision> CheckCountLimitAsync(Guid workspaceId, string featureKey, int currentUsage, DateTimeOffset atUtc, CancellationToken cancellationToken = default)
    {
        var subscription = await subscriptions.FindByWorkspaceAsync(workspaceId, cancellationToken);
        if (subscription is null || !subscription.IsEntitledAt(atUtc))
        {
            return EntitlementDecision.Deny(EntitlementDecision.SubscriptionRequiredCode);
        }

        var plan = await plans.FindByIdAsync(subscription.PlanId, cancellationToken);
        if (plan is null)
        {
            return EntitlementDecision.Deny(EntitlementDecision.SubscriptionRequiredCode);
        }

        var entitlement = plan.EntitlementFor(featureKey);
        return entitlement.Limit switch
        {
            Entitlement.Unlimited => EntitlementDecision.Allow,
            0 => EntitlementDecision.Deny(EntitlementDecision.LimitExceededCode, 0),
            _ when currentUsage >= entitlement.Limit => EntitlementDecision.Deny(EntitlementDecision.LimitExceededCode, entitlement.Limit),
            _ => EntitlementDecision.Allow,
        };
    }
}

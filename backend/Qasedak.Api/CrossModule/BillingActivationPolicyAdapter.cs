using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Billing.Application;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root adapter: automation activation is gated by the workspace's live
/// subscription and plan's `automations.active` count limit. Fails closed — no
/// subscription/expired/missing plan denies activation with stable codes
/// (`billing.subscriptionRequired`, `billing.limitExceeded`).
/// </summary>
public sealed class BillingActivationPolicyAdapter(EntitlementGate gate) : IAutomationActivationPolicy
{
    public const string ActiveAutomationsFeature = "automations.active";

    public async Task<string?> CheckActivationAllowedAsync(Guid workspaceId, int currentlyActiveAutomations, CancellationToken cancellationToken = default)
    {
        var decision = await gate.CheckCountLimitAsync(
            workspaceId, ActiveAutomationsFeature, currentlyActiveAutomations, DateTimeOffset.UtcNow, cancellationToken);
        return decision.Allowed ? null : decision.DenialCode;
    }
}

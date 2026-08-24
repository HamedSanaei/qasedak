using Qasedak.Modules.Automations.Application;

namespace Qasedak.Modules.Automations.Infrastructure;

/// <summary>
/// Default activation policy used when no composition-root override is present (e.g.
/// isolated module tests): everything is allowed. Production binds the billing-backed
/// policy so plan limits are enforced server-side.
/// </summary>
public sealed class PermissiveActivationPolicy : IAutomationActivationPolicy
{
    public Task<string?> CheckActivationAllowedAsync(Guid workspaceId, int currentlyActiveAutomations, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}

using Qasedak.BuildingBlocks.Application.Auditing;
using Qasedak.Modules.Automations.Domain;

namespace Qasedak.Modules.Automations.Application;

/// <summary>
/// Server-side policy seam consulted before an automation may be activated. The module
/// registers a permissive default; the composition root binds the billing-backed
/// entitlement enforcement (one live subscription + plan limits per workspace).
/// Returns null when activation is allowed, otherwise a stable denial code.
/// </summary>
public interface IAutomationActivationPolicy
{
    Task<string?> CheckActivationAllowedAsync(Guid workspaceId, int currentlyActiveAutomations, CancellationToken cancellationToken = default);
}

/// <summary>Activates a draft automation after consulting the workspace activation policy.</summary>
public sealed class ActivateAutomationUseCase(
    IAutomationRepository automations,
    IAutomationActivationPolicy policy,
    IAuditTrail? audit = null)
{
    public async Task<Automation> ExecuteAsync(Guid workspaceId, Guid automationId, DateTimeOffset activatedAtUtc, CancellationToken cancellationToken = default)
    {
        var automation = await automations.FindByIdAsync(automationId, cancellationToken)
            ?? throw new AutomationsDomainException(AutomationFailures.NotFound, "The automation does not exist.");

        if (automation.WorkspaceId != workspaceId)
        {
            // Foreign workspaces are indistinguishable from absent ones.
            throw new AutomationsDomainException(AutomationFailures.NotFound, "The automation does not exist.");
        }

        var activeCount = (await automations.ListByWorkspaceAsync(workspaceId, cancellationToken))
            .Count(a => a.Id != automationId && a.Status == AutomationStatus.Active);

        var denial = await policy.CheckActivationAllowedAsync(workspaceId, activeCount, cancellationToken);
        if (denial is not null)
        {
            // Sensitive-action audit: denied activations are recorded too.
            if (audit is not null)
            {
                await audit.RecordAsync(AuditEntry.New(
                    "automation.activation.denied",
                    activatedAtUtc,
                    workspaceId: workspaceId,
                    targetType: "automation",
                    targetId: automationId.ToString(),
                    detailsJson: System.Text.Json.JsonSerializer.Serialize(new { reason = denial })), cancellationToken);
            }

            throw new AutomationsDomainException(denial, $"Activation denied by workspace policy: {denial}");
        }

        automation.Activate(activatedAtUtc);
        await automations.SaveChangesAsync(automation, cancellationToken);

        if (audit is not null)
        {
            await audit.RecordAsync(AuditEntry.New(
                "automation.activated",
                activatedAtUtc,
                workspaceId: workspaceId,
                targetType: "automation",
                targetId: automationId.ToString()), cancellationToken);
        }

        return automation;
    }
}

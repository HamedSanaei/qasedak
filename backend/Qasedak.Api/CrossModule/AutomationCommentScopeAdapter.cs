using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Instagram.Application.Reconciliation;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root bridge (M13-013 §13): implements the Instagram module's
/// channel-neutral reconciliation-scope port by querying the Automations module.
/// No production module references the other; both meet here. Automations never
/// exposes message text/actions to Instagram — only the media scope, and only for
/// the exact connected account.
/// </summary>
public sealed class AutomationCommentScopeAdapter(IAutomationRepository automations) : ICommentReconciliationScopeQuery
{
    public async Task<CommentReconciliationScope?> ResolveAsync(
        Guid workspaceId, Guid connectedAccountId, CancellationToken cancellationToken = default)
    {
        var scope = await automations.GetCommentReconciliationScopeAsync(
            workspaceId, ChannelAccountId.From(connectedAccountId), cancellationToken);
        return scope is null
            ? null
            : new CommentReconciliationScope(scope.HasAnySourceCommentAutomation, scope.SpecificSourceMediaIds);
    }
}

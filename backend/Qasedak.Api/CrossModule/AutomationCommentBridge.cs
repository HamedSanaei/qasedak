using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root bridge: normalized Instagram comment events drive the Automations
/// module. Only automations bound to the exact connected account behind the event are
/// candidates — a workspace-wide fan-out would cross-execute sibling accounts. Exact-account
/// resolution happened upstream (M13-008), so the event's Qasedak account key is used
/// directly; events without a resolved account never reach this bridge. For each candidate
/// the deterministic evaluator decides; matched definitions execute through the idempotent
/// use case, whose ledger makes webhook redelivery and retries at-most-intended-effect.
/// </summary>
public sealed partial class AutomationCommentBridge(
    IAutomationRepository automations,
    ExecuteAutomationUseCase executor,
    ILogger<AutomationCommentBridge> logger) : IIntegrationEventDispatcher
{
    public async Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (integrationEvent is not InstagramCommentCreated comment)
        {
            LogSkipped(integrationEvent.EventId, integrationEvent.GetType().Name);
            return;
        }

        if (comment.WorkspaceId is null || comment.ConnectedAccountId is null)
        {
            LogUnbound(comment.EventId);
            return;
        }

        var channelAccountId = ChannelAccountId.From(comment.ConnectedAccountId.Value);
        var active = await automations.ListByAccountAsync(comment.WorkspaceId.Value, channelAccountId, cancellationToken);
        foreach (var automation in active.Where(a => a.Status == AutomationStatus.Active))
        {
            var trigger = new TriggerContext(
                comment.EventId,
                TriggerKind.CommentCreated,
                comment.CommentId,
                comment.FromId,
                comment.Text,
                comment.CreatedAtUtc,
                comment.IsLiveComment);

            // The workspace hint defends against cross-workspace id collisions; the use
            // case additionally refuses automations whose binding differs from the
            // event's exact account without dispatching.
            var outcome = await executor.ExecuteAsync(
                new ExecutionRequest(automation.Id, trigger, InstagramReplyGateway.Channel, channelAccountId, comment.WorkspaceId.Value),
                cancellationToken);

            LogOutcome(comment.CommentId, automation.Id, outcome.Status);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Automation executed for comment={CommentId} automation={AutomationId} status={Status}")]
    private partial void LogOutcome(string commentId, Guid automationId, ExecutionStatus status);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Integration event skipped eventId={EventId} kind={Kind}")]
    private partial void LogSkipped(string eventId, string kind);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Comment event dropped: no exact connected account on event eventId={EventId}")]
    private partial void LogUnbound(string eventId);
}

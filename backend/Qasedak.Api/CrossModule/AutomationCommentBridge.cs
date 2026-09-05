using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root bridge: normalized Instagram comment events drive the Automations
/// module. Only automations bound to the exact connected account behind the event are
/// candidates — a workspace-wide fan-out would cross-execute sibling accounts. For each
/// candidate the deterministic evaluator decides; matched definitions execute through
/// the idempotent use case, whose ledger makes webhook redelivery and retries
/// at-most-intended-effect. Unbound accounts and non-comment events are logged and
/// skipped.
/// </summary>
public sealed partial class AutomationCommentBridge(
    IConnectedAccountRepository accounts,
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

        if (comment.ProviderAccountId is null)
        {
            LogUnbound(comment.EventId);
            return;
        }

        var resolution = await accounts.ResolveActiveAccountAsync(comment.ProviderAccountId, cancellationToken);
        if (resolution.Status != AccountResolutionStatus.Resolved || resolution.Account is null)
        {
            LogUnresolved(comment.EventId, resolution.Status.ToString());
            return;
        }

        var account = resolution.Account;
        var channelAccountId = ChannelAccountId.From(account.Id);
        var active = await automations.ListByAccountAsync(account.WorkspaceId, channelAccountId, cancellationToken);
        foreach (var automation in active.Where(a => a.Status == AutomationStatus.Active))
        {
            var trigger = new TriggerContext(
                comment.EventId,
                TriggerKind.CommentCreated,
                comment.CommentId,
                comment.FromId,
                comment.Text,
                comment.CreatedAtUtc);

            // The workspace hint defends against cross-workspace id collisions; the use
            // case additionally refuses automations whose binding differs from the
            // event's exact account without dispatching.
            var outcome = await executor.ExecuteAsync(
                new ExecutionRequest(automation.Id, trigger, InstagramReplyGateway.Channel, channelAccountId, account.WorkspaceId),
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
        Message = "Comment event dropped: provider account not bound to a workspace eventId={EventId}")]
    private partial void LogUnbound(string eventId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Comment event dropped: account resolution {Status} eventId={EventId}")]
    private partial void LogUnresolved(string eventId, string status);
}

using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root bridge: normalized Instagram inbound direct messages (M13-008) drive
/// the InboundDirectMessage trigger (M13-012 §61). Only automations bound to the exact
/// connected account behind the event are candidates; events without a resolved account
/// never reach this bridge. The run-ledger idempotency key is the provider MESSAGE id
/// (never the transport fragment id): duplicate/re-enveloped deliveries and future
/// provider-history imports converge on one logical trigger; a missing provider message id
/// fails closed with zero evaluation and zero provider traffic. Ignored message shapes
/// (echo/self/deleted/unsupported/attachment-only) were already filtered by M13-008 — no
/// provider filtering is reimplemented here.
/// </summary>
public sealed partial class AutomationInboundMessageBridge(
    IAutomationRepository automations,
    ExecuteAutomationUseCase executor,
    ILogger<AutomationInboundMessageBridge> logger) : IIntegrationEventDispatcher
{
    public async Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (integrationEvent is not InstagramMessageReceived message)
        {
            LogSkipped(integrationEvent.EventId, integrationEvent.GetType().Name);
            return;
        }

        if (message.WorkspaceId is null || message.ConnectedAccountId is null)
        {
            LogUnbound(message.EventId);
            return;
        }

        if (string.IsNullOrWhiteSpace(message.ProviderMessageId))
        {
            // Fail closed: without the provider message identity there is no stable
            // semantic trigger identity to key the idempotent run ledger on.
            LogMissingProviderIdentity(message.EventId);
            return;
        }

        var channelAccountId = ChannelAccountId.From(message.ConnectedAccountId.Value);
        var active = await automations.ListByAccountAsync(message.WorkspaceId.Value, channelAccountId, cancellationToken);
        foreach (var automation in active.Where(a => a.Status == AutomationStatus.Active))
        {
            var trigger = new TriggerContext(
                message.ProviderMessageId,
                TriggerKind.InboundDirectMessage,
                message.ProviderMessageId,
                message.SenderId,
                message.Text,
                message.SentAtUtc);

            var outcome = await executor.ExecuteAsync(
                new ExecutionRequest(automation.Id, trigger, InstagramReplyGateway.Channel, channelAccountId, message.WorkspaceId.Value),
                cancellationToken);

            LogOutcome(message.ProviderMessageId, automation.Id, outcome.Status);
        }
    }

    [LoggerMessage(EventId = 7200, Level = LogLevel.Information,
        Message = "Automation executed for message={ProviderMessageId} automation={AutomationId} status={Status}")]
    private partial void LogOutcome(string providerMessageId, Guid automationId, ExecutionStatus status);

    [LoggerMessage(EventId = 7201, Level = LogLevel.Information,
        Message = "Integration event skipped eventId={EventId} kind={Kind}")]
    private partial void LogSkipped(string eventId, string kind);

    [LoggerMessage(EventId = 7202, Level = LogLevel.Warning,
        Message = "Message event dropped: no exact connected account on event eventId={EventId}")]
    private partial void LogUnbound(string eventId);

    [LoggerMessage(EventId = 7203, Level = LogLevel.Warning,
        Message = "Message event dropped: no provider message identity eventId={EventId}")]
    private partial void LogMissingProviderIdentity(string eventId);
}

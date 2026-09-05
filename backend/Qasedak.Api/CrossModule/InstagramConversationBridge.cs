using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Conversations.Application.Conversations;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Infrastructure.Webhooks;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root bridge: routes normalized Instagram integration events into the
/// Conversations module's inbound projection. This adapter is the explicit cross-module
/// contract required by the architecture rules — neither module references the other;
/// both meet here where all modules are already referenced. The exact connected
/// account behind the provider identity is resolved and converted to the opaque
/// channel-account identity before crossing the boundary; unbound, unknown or
/// disconnected accounts are logged and skipped — never guessed.
/// </summary>
public sealed partial class InstagramConversationBridge(
    IConnectedAccountRepository accounts,
    ProjectInboundMessageUseCase projection,
    WebhookMetrics metrics,
    ILogger<InstagramConversationBridge> logger) : IIntegrationEventDispatcher
{
    private const string Channel = "instagram";

    public async Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (integrationEvent is not InstagramMessageReceived message)
        {
            LogSkipped(integrationEvent.EventId, integrationEvent.GetType().Name);
            return;
        }

        if (message.ProviderAccountId is null)
        {
            LogUnbound(message.EventId);
            return;
        }

        var resolution = await accounts.ResolveActiveAccountAsync(message.ProviderAccountId, cancellationToken);
        if (resolution.Status != AccountResolutionStatus.Resolved || resolution.Account is null)
        {
            LogUnresolved(message.EventId, resolution.Status.ToString());
            return;
        }

        var account = resolution.Account;
        var result = await projection.ExecuteAsync(new InboundMessageProjection(
            account.WorkspaceId,
            Channel,
            ChannelAccountId.From(account.Id),
            message.SenderId,
            message.ProviderMessageId,
            message.SenderId,
            message.Text ?? string.Empty,
            message.SentAtUtc), cancellationToken);

        metrics.EventsDispatched.Add(1, new KeyValuePair<string, object?>("kind", result.Duplicate ? "message-duplicate" : "message"));
        LogProjected(result.ConversationId, message.ProviderMessageId ?? "(none)", result.Duplicate);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Instagram message projected conversation={ConversationId} providerMessage={ProviderMessage} duplicate={Duplicate}")]
    private partial void LogProjected(Guid conversationId, string providerMessage, bool duplicate);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Integration event skipped eventId={EventId} kind={Kind}")]
    private partial void LogSkipped(string eventId, string kind);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Message event dropped: provider account not bound to a workspace eventId={EventId}")]
    private partial void LogUnbound(string eventId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Message event dropped: account resolution {Status} eventId={EventId}")]
    private partial void LogUnresolved(string eventId, string status);
}

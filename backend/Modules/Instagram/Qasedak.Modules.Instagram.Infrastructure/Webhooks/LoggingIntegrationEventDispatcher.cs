using Microsoft.Extensions.Logging;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Modules.Instagram.Infrastructure.Webhooks;

/// <summary>
/// Dispatch boundary for normalized integration events. Until downstream milestones attach
/// real consumers, dispatch is a structured-log observation point carrying the correlation
/// identity (inbox event id + provider account) for traceability.
/// </summary>
public sealed partial class LoggingIntegrationEventDispatcher(
    ILogger<LoggingIntegrationEventDispatcher> logger,
    WebhookMetrics metrics) : IIntegrationEventDispatcher
{
    public Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var (kind, detail) = integrationEvent switch
        {
            InstagramMessageReceived message => ("message", $"sender={message.SenderId} textLength={message.Text?.Length ?? 0}"),
            InstagramCommentCreated comment => ("comment", $"commentId={comment.CommentId} textLength={comment.Text?.Length ?? 0}"),
            InstagramMentionCreated mention => ("mention", $"commentId={mention.CommentId}"),
            _ => ("unknown", string.Empty),
        };

        metrics.EventsDispatched.Add(1, new KeyValuePair<string, object?>("kind", kind));
        LogDispatched(kind, integrationEvent.EventId, integrationEvent.ProviderAccountId, detail);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Integration event dispatched {Kind} eventId={EventId} providerAccountId={ProviderAccountId} {Detail}")]
    private partial void LogDispatched(string kind, string eventId, string? providerAccountId, string detail);
}

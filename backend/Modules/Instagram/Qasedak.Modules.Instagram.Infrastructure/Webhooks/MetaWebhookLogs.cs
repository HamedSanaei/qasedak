using Microsoft.Extensions.Logging;

namespace Qasedak.Modules.Instagram.Infrastructure.Webhooks;

/// <summary>
/// Source-generated structured logs for the webhook receive path. Message templates and
/// tag names are stable observability contracts; request content is never logged.
/// </summary>
internal sealed partial class MetaWebhookLogs(ILogger logger)
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Webhook rejected oversized body bytes={Bytes} correlation={Correlation}")]
    public partial void OversizedBody(int bytes, string correlation);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Webhook signature verification failed bytes={Bytes} correlation={Correlation}")]
    public partial void SignatureFailed(int bytes, string correlation);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Webhook rejected non-JSON body bytes={Bytes} correlation={Correlation}")]
    public partial void NonJsonBody(int bytes, string correlation);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Webhook event {EventId} redelivered {Attempts} times correlation={Correlation} topic={Topic}")]
    public partial void RedeliveryAttention(string eventId, int attempts, string correlation, string topic);
}

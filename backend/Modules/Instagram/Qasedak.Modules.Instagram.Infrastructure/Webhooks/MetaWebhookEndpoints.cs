using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Modules.Instagram.Infrastructure.Webhooks;

/// <summary>
/// Meta webhook HTTP surface. GET performs the subscription handshake (challenge echoed
/// verbatim as text/plain); POST enforces the X-Hub-Signature-256 HMAC over the raw
/// received bytes before anything else happens — rejections never echo request content.
/// Body size is capped; oversized payloads are rejected with 413.
/// </summary>
public static class MetaWebhookEndpoints
{
    /// <summary>Defensive cap: Meta notification payloads are far smaller in practice.</summary>
    public const int MaxBodyBytes = 1_000_000;

    public static IEndpointRouteBuilder MapMetaWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/webhooks/instagram").WithTags("Webhooks").AllowAnonymous();

        group.MapGet(string.Empty, async (
            HttpRequest request,
            HttpResponse response,
            IWebhookSubscriptionValidator validator) =>
        {
            var result = validator.Validate(
                request.Query["hub.mode"].FirstOrDefault(),
                request.Query["hub.verify_token"].FirstOrDefault(),
                request.Query["hub.challenge"].FirstOrDefault());

            if (!result.IsValid)
            {
                response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "text/plain; charset=utf-8";
            await response.WriteAsync(result.Challenge);
        });

        group.MapPost(string.Empty, async (
            HttpContext context,
            IWebhookSignatureVerifier verifier,
            IMetaWebhookIngester ingester,
            IWebhookPostIngestProcessor processor,
            WebhookMetrics metrics,
            ILoggerFactory loggerFactory) =>
        {
            var logs = new MetaWebhookLogs(loggerFactory.CreateLogger("Qasedak.Instagram.Webhooks"));
            var startedAt = Stopwatch.GetTimestamp();
            // Correlation: honor the caller's id, mint one otherwise; always echoed back.
            var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                correlationId = Guid.CreateVersion7().ToString();
            }

            context.Response.Headers["X-Correlation-Id"] = correlationId;

            void Record(string outcome)
            {
                metrics.NotificationsReceived.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
                metrics.IngestionDuration.Record(
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                    new KeyValuePair<string, object?>("outcome", outcome));
            }

            // Read raw bytes first: the signature covers exactly what Meta sent.
            var rawLength = (int?)context.Request.Headers.ContentLength ?? 0;
            if (rawLength > MaxBodyBytes)
            {
                logs.OversizedBody(rawLength, correlationId);
                Record("rejected");
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            using var buffer = new MemoryStream(capacity: (int)Math.Max(rawLength, 256));
            await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
            if (buffer.Length > MaxBodyBytes)
            {
                logs.OversizedBody((int)buffer.Length, correlationId);
                Record("rejected");
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            var rawBody = buffer.ToArray();
            var signatureHeader = context.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (!verifier.Verify(rawBody, signatureHeader).IsValid)
            {
                // 401 with an empty body: no oracle, no content echo.
                logs.SignatureFailed(rawBody.Length, correlationId);
                Record("rejected");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            string bodyJson;
            try
            {
                bodyJson = JsonSerializer.Deserialize<JsonElement>(rawBody).GetRawText();
            }
            catch (JsonException)
            {
                // Signed but not JSON: not a Meta notification we can route.
                logs.NonJsonBody(rawBody.Length, correlationId);
                Record("rejected");
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var topic = "unknown";
            try
            {
                using var document = JsonDocument.Parse(rawBody);
                if (document.RootElement.TryGetProperty("object", out var objectElement))
                {
                    topic = objectElement.GetString() ?? "unknown";
                }
            }
            catch (JsonException)
            {
            }

            var ingestion = await ingester.IngestAsync(new MetaWebhookNotification(topic, bodyJson, correlationId), context.RequestAborted);
            Record(ingestion.Accepted ? "accepted" : "deferred");

            // Process what is durably pending right away: normalization + dispatch are
            // local work; a background dispatcher can take over later without contract
            // changes. Failures inside processing leave entries pending (retry visibility).
            try
            {
                await processor.ProcessPendingAsync(cancellationToken: context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // The notification is durably stored; processing retries on the next delivery.
                Record("processing-deferred");
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

            context.Response.StatusCode = ingestion.Accepted
                ? StatusCodes.Status200OK
                : StatusCodes.Status202Accepted;
        });

        return endpoints;
    }
}

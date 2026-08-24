using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Qasedak.BuildingBlocks.Infrastructure.Diagnostics;

/// <summary>
/// Establishes per-request correlation: honors an inbound X-Correlation-Id when it passes
/// validation, otherwise mints one; pushes it into the logger scope so every structured
/// log line carries it; echoes it on the response. Runs before routing.
/// </summary>
public sealed partial class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ICorrelationContextAccessor accessor)
    {
        var inbound = context.Request.Headers[CorrelationIds.HeaderName].ToString();
        var correlationId = CorrelationIds.IsValid(inbound) ? inbound : CorrelationIds.NewId();

        context.Items[CorrelationIds.HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIds.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["RequestPath"] = context.Request.Path.ToString(),
        });
        accessor.Current = new CorrelationContext(correlationId);

        await next(context);
    }
}

/// <summary>Mutating holder so middleware can publish the request's correlation context.</summary>
public interface ICorrelationContextAccessor
{
    ICorrelationContext? Current { get; set; }
}

public sealed class CorrelationContextAccessor : ICorrelationContextAccessor
{
    public ICorrelationContext? Current { get; set; }
}

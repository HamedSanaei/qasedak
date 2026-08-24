using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace Qasedak.BuildingBlocks.Infrastructure.RateLimiting;

/// <summary>Risk classes drive distinct abuse-control budgets.</summary>
public enum RiskClass
{
    /// <summary>Unauthenticated browsing of public endpoints.</summary>
    Public = 1,

    /// <summary>Authenticated API usage.</summary>
    Authenticated = 2,

    /// <summary>Provider webhook ingestion — highest volume, keyed per source IP.</summary>
    Webhook = 3,

    /// <summary>Credential/expensive operations (login, register) — tightest budgets.</summary>
    Sensitive = 4,
}

/// <summary>
/// Risk-class fixed-window rate limiting over the platform limiter. Limits are
/// configurable (`Qasedak:RateLimits:{ClassName}:Limit/WindowSeconds`) so deployments can
/// tune budgets without code changes; rejections answer 429 with Retry-After. Partition
/// keys are per remote IP for anonymous traffic and per user id for authenticated calls,
/// so one abusive tenant cannot starve others.
/// </summary>
public static class RateLimitPolicies
{
    private static readonly Dictionary<RiskClass, (int Limit, int WindowSeconds)> Defaults = new()
    {
        [RiskClass.Public] = (240, 60),
        [RiskClass.Authenticated] = (600, 60),
        [RiskClass.Webhook] = (2000, 60),
        [RiskClass.Sensitive] = (30, 60),
    };

    public static void Configure(RateLimiterOptions builder, IConfiguration configuration)
    {
        builder.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        builder.OnRejected = static async (context, cancellationToken) =>
        {
            // Retry-After hints half a window; exact reset tracking stays with the limiter.
            context.HttpContext.Response.Headers.RetryAfter = "30";
            await context.HttpContext.Response.WriteAsJsonAsync(
                new { code = "ratelimit.exceeded" },
                cancellationToken);
        };
        builder.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            var (riskClass, limit, windowSeconds) = ResolvePolicy(httpContext, configuration);

            var partitionKey = riskClass switch
            {
                RiskClass.Webhook => "webhook|" + ClientKey(httpContext),
                RiskClass.Authenticated => "user|" + (httpContext.User.FindFirst("sub")?.Value ?? ClientKey(httpContext)),
                RiskClass.Sensitive => "sensitive|" + ClientKey(httpContext),
                _ => "public|" + ClientKey(httpContext),
            };

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0,
                });
        });
    }

    private static (RiskClass Class, int Limit, int WindowSeconds) ResolvePolicy(HttpContext context, IConfiguration configuration)
    {
        var path = context.Request.Path.ToString();
        var riskClass =
            path.StartsWith("/api/v1/webhooks/", StringComparison.Ordinal) ? RiskClass.Webhook
            : path.StartsWith("/api/v1/identity/login", StringComparison.Ordinal)
                || path.StartsWith("/api/v1/identity/register", StringComparison.Ordinal) ? RiskClass.Sensitive
            : context.User.Identity?.IsAuthenticated == true ? RiskClass.Authenticated
            : RiskClass.Public;

        var (defaultLimit, defaultWindow) = Defaults[riskClass];
        var limit = configuration.GetValue<int?>($"Qasedak:RateLimits:{riskClass}:Limit") ?? defaultLimit;
        var windowSeconds = configuration.GetValue<int?>($"Qasedak:RateLimits:{riskClass}:WindowSeconds") ?? defaultWindow;
        return (riskClass, Math.Max(1, limit), Math.Max(1, windowSeconds));
    }

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

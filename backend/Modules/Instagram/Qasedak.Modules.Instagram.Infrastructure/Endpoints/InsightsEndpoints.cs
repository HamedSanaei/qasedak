using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Qasedak.Modules.Instagram.Application.Insights;

namespace Qasedak.Modules.Instagram.Infrastructure.Endpoints;

/// <summary>
/// M13-007 exact-account analytics surface: workspace-scoped overview + follower
/// history. Thin composition over the exact-account use cases; responses expose
/// Qasedak-owned records only — no token, no provider DTO, no raw provider error
/// body. Permission loss degrades analytics truthfully (per-metric states) without
/// affecting the media catalog or follower data.
/// </summary>
public static class InsightsEndpoints
{
    public static IEndpointRouteBuilder MapInsightsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var insights = endpoints.MapGroup("/api/v1/workspaces/{workspaceId:guid}/instagram")
            .WithTags("Instagram Insights")
            .RequireAuthorization("workspace-member");

        insights.MapGet("/connections/{accountId:guid}/overview", async (
            Guid workspaceId,
            Guid accountId,
            GetInstagramOverviewUseCase useCase,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var result = await useCase.ExecuteAsync(workspaceId, accountId, cancellationToken);
            if (result is not InstagramOverviewResult.Ok ok)
            {
                var code = ((InstagramOverviewResult.Refused)result).FailureCode;
                InsightsEndpointLogs.LogInsightsFailed(loggerFactory.CreateLogger("Qasedak.Modules.Instagram.Insights"), code);
                return ConnectionsFailureMapper.ToResult(code);
            }

            var overview = ok.Overview;
            return Results.Ok(new
            {
                accountId = overview.AccountId,
                analyticsAvailability = overview.AnalyticsAvailability.ToString(),
                currentFollowers = new
                {
                    value = overview.CurrentFollowers.Value,
                    state = overview.CurrentFollowers.State.ToString(),
                    observedAtUtc = overview.CurrentFollowers.ObservedAtUtc,
                    provenance = overview.CurrentFollowers.Provenance?.ToString(),
                },
                followerHistory = overview.FollowerHistory.Select(point => new
                {
                    date = point.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    value = point.Value,
                    provenance = point.Provenance.ToString(),
                }),
                media = new
                {
                    state = overview.Media.State.ToString(),
                    count = overview.Media.Count,
                    likeTotal = overview.Media.LikeTotal,
                    commentTotal = overview.Media.CommentTotal,
                    likeTotalComplete = overview.Media.LikeTotalComplete,
                    commentTotalComplete = overview.Media.CommentTotalComplete,
                    items = overview.Media.Items?.Select(item => new
                    {
                        mediaId = item.ProviderMediaId,
                        kind = item.Kind.ToString(),
                        likeCount = item.LikeCount,
                        commentCount = item.CommentCount,
                        insights = item.Insights.Select(ToMetricDto),
                    }),
                },
                accountInsights = overview.AccountInsights.Select(ToMetricDto),
            });
        });

        insights.MapGet("/connections/{accountId:guid}/followers/history", async (
            Guid workspaceId,
            Guid accountId,
            int? limit,
            GetFollowerHistoryUseCase useCase,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var historyLimit = limit ?? InsightsPolicy.DefaultHistoryLimit;
            if (historyLimit <= 0 || historyLimit > InsightsPolicy.MaxHistoryLimit)
            {
                return Results.Json(
                    new { code = "followers.invalidLimit" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await useCase.ExecuteAsync(workspaceId, accountId, historyLimit, cancellationToken);
            if (result is not FollowerHistoryResult.Ok ok)
            {
                var code = ((FollowerHistoryResult.Refused)result).FailureCode;
                InsightsEndpointLogs.LogInsightsFailed(loggerFactory.CreateLogger("Qasedak.Modules.Instagram.Insights"), code);
                return ConnectionsFailureMapper.ToResult(code);
            }

            return Results.Ok(new
            {
                items = ok.Points.Select(point => new
                {
                    date = point.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    value = point.Value,
                    provenance = point.Provenance.ToString(),
                }),
            });
        });

        return endpoints;
    }

    private static object ToMetricDto(MetricObservation observation) => new
    {
        metric = InsightMetricRegistry.ApiName(observation.Key),
        state = observation.Availability.ToString(),
        value = observation.Value,
    };
}

/// <summary>Outcome-only insights logs: stable failure codes, never identifiers or provider bodies.</summary>
internal static partial class InsightsEndpointLogs
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Instagram insights surface failed code={FailureCode}.")]
    internal static partial void LogInsightsFailed(ILogger logger, string failureCode);
}

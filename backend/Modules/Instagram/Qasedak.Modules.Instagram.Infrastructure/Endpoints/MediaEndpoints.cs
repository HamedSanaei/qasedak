using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Qasedak.Modules.Instagram.Application.Media;

namespace Qasedak.Modules.Instagram.Infrastructure.Endpoints;

/// <summary>
/// Workspace-scoped Instagram media catalog surface (M13-006). Thin composition
/// over the exact-account use case; provider identity comes only from the
/// validated server-side account. The route carries the ConnectedAccountId (never
/// the provider id as route authority). Responses expose Qasedak-owned records
/// only — no token, no Meta paging DTO, no raw provider error body.
/// </summary>
public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var media = endpoints.MapGroup("/api/v1/workspaces/{workspaceId:guid}/instagram")
            .WithTags("Instagram Media")
            .RequireAuthorization("workspace-member");

        media.MapGet("/connections/{accountId:guid}/media", async (
            Guid workspaceId,
            Guid accountId,
            int? limit,
            string? cursor,
            ListMediaPageUseCase useCase,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var pageSize = limit ?? MediaCatalogPolicy.DefaultPageSize;
            if (pageSize <= 0 || pageSize > MediaCatalogPolicy.MaxPageSize)
            {
                return Results.Json(
                    new { code = MediaCatalogFailures.InvalidLimit },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await useCase.ExecuteAsync(workspaceId, accountId, pageSize, cursor, cancellationToken);
            if (!result.Success)
            {
                MediaEndpointLogs.LogMediaFailed(loggerFactory.CreateLogger("Qasedak.Modules.Instagram.Media"), result.FailureCode!);
                return ConnectionsFailureMapper.ToResult(result.FailureCode!);
            }

            return Results.Ok(new
            {
                items = result.Page!.Items.Select(item => new
                {
                    mediaId = item.ProviderMediaId,
                    caption = item.Caption,
                    kind = item.Kind.ToString(),
                    mediaProductType = item.MediaProductType,
                    timestampUtc = item.CreatedAtUtc,
                    permalink = item.Permalink,
                    mediaUrl = item.MediaUrl,
                    thumbnailUrl = item.ThumbnailUrl,
                    hasMediaPreview = item.HasMediaPreview,
                    hasThumbnail = item.HasThumbnail,
                    likeCount = item.LikeCount,
                    commentCount = item.CommentCount,
                    children = item.Children?.Select(child => new
                    {
                        mediaId = child.ProviderMediaId,
                        kind = child.Kind.ToString(),
                        mediaUrl = child.MediaUrl,
                        thumbnailUrl = child.ThumbnailUrl,
                        permalink = child.Permalink,
                    }),
                }),
                nextCursor = result.Page.NextCursor,
                hasMore = result.Page.HasMore,
            });
        });

        return endpoints;
    }
}

/// <summary>
/// Outcome-only media logs: stable failure codes only — never account ids,
/// provider identities, cursors, tokens or provider payloads.
/// </summary>
internal static partial class MediaEndpointLogs
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Instagram media catalog failed code={FailureCode}.")]
    internal static partial void LogMediaFailed(ILogger logger, string failureCode);
}

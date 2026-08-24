using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.OAuth;

namespace Qasedak.Modules.Instagram.Infrastructure.Endpoints;

/// <summary>
/// Workspace-scoped Instagram connection surface for the M08-003 UI. Thin composition over
/// tested application use cases; token material never crosses this boundary. Every route
/// is workspace-scoped and guarded by the workspace-member policy.
/// </summary>
public static class ConnectionEndpoints
{
    public static IEndpointRouteBuilder MapConnectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var connections = endpoints.MapGroup("/api/v1/workspaces/{workspaceId:guid}/instagram")
            .WithTags("Instagram Connections")
            .RequireAuthorization("workspace-member");

        connections.MapGet("/connections", async (
            Guid workspaceId,
            bool? includeDisconnected,
            ListWorkspaceConnectionsUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var items = await useCase.ExecuteAsync(workspaceId, includeDisconnected ?? false, cancellationToken);
            return Results.Ok(new
            {
                items = items.Select(a => new
                {
                    accountId = a.AccountId,
                    providerIdentity = a.ProviderIdentity,
                    path = a.Path,
                    scopes = a.Scopes,
                    health = a.Health,
                    healthDetail = a.HealthDetail,
                    tokenExpiresAtUtc = a.ExpiresAtUtc,
                    connectedAtUtc = a.ConnectedAtUtc,
                    disconnectedAtUtc = a.DisconnectedAtUtc,
                }),
            });
        });

        // Starts the Business Login flow. The state value is generated server-side; the
        // redirect URI must match an allow-listed frontend callback.
        connections.MapGet("/authorize-url", async (
            Guid workspaceId,
            string redirectUri,
            IAuthorizationUrlBuilder builder) =>
        {
            if (string.IsNullOrWhiteSpace(redirectUri))
            {
                return Results.Json(
                    new { code = "account.oauthUnavailable" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var url = builder.Build(new AuthorizationUrlRequest(redirectUri, Guid.CreateVersion7().ToString()));
            return Results.Ok(new { url = url.Value });
        });

        connections.MapPost("/connections", async (
            Guid workspaceId,
            ConnectAccountRequest request,
            ConnectInstagramAccountUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var result = await useCase.ExecuteAsync(
                new ConnectInstagramAccountCommand(workspaceId, request.AuthorizationCode, request.RedirectUri),
                cancellationToken);

            return result.Success
                ? Results.Created($"/api/v1/workspaces/{workspaceId}/instagram/connections", new { accountId = result.AccountId })
                : ConnectionsFailureMapper.ToResult(result.FailureCode!);
        });

        connections.MapDelete("/connections/{accountId:guid}", async (
            Guid workspaceId,
            Guid accountId,
            DisconnectInstagramAccountUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var result = await useCase.ExecuteAsync(accountId, cancellationToken);
            return result.Success
                ? Results.NoContent()
                : ConnectionsFailureMapper.ToResult(result.FailureCode!);
        });

        return endpoints;
    }
}

public sealed record ConnectAccountRequest(string AuthorizationCode, string RedirectUri);

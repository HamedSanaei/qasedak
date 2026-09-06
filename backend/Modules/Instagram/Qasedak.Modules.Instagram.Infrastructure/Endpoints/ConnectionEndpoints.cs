using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Domain.Accounts;

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
                    username = a.Username,
                    displayName = a.DisplayName,
                    profilePictureUrl = a.ProfilePictureUrl,
                    accountType = a.AccountType,
                    profileUpdatedAtUtc = a.ProfileUpdatedAtUtc,
                    subscriptionHealth = a.SubscriptionHealth,
                    subscriptionDetail = a.SubscriptionDetail,
                    lastSubscriptionCheckUtc = a.LastSubscriptionCheckUtc,
                }),
            });
        });

        // Starts the Business Login flow. The state value is issued server-side, bound to
        // this workspace + redirect URI, short-lived and single-use; the completion
        // endpoint requires it back verbatim.
        connections.MapGet("/authorize-url", async (
            Guid workspaceId,
            string redirectUri,
            IAuthorizationUrlBuilder builder,
            IOAuthStateStore states) =>
        {
            if (string.IsNullOrWhiteSpace(redirectUri))
            {
                return Results.Json(
                    new { code = "account.oauthUnavailable" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var issuance = await states.IssueAsync(workspaceId, redirectUri, DateTimeOffset.UtcNow);
            var url = builder.Build(new AuthorizationUrlRequest(redirectUri, issuance.State));
            return Results.Ok(new { url = url.Value, state = issuance.State });
        });

        connections.MapPost("/connections", async (
            Guid workspaceId,
            ConnectAccountRequest request,
            ConnectInstagramAccountUseCase useCase,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Qasedak.Modules.Instagram.Connections");
            var result = await useCase.ExecuteAsync(
                new ConnectInstagramAccountCommand(workspaceId, request.AuthorizationCode, request.RedirectUri, request.State),
                cancellationToken);

            if (!result.Success)
            {
                ConnectionEndpointLogs.LogConnectFailed(logger, result.FailureCode!);
                return ConnectionsFailureMapper.ToResult(result.FailureCode!);
            }

            if (result.SubscriptionHealth is not SubscriptionHealth.Healthy)
            {
                ConnectionEndpointLogs.LogConnectDegraded(logger, result.SubscriptionHealth!.Value);
            }

            return Results.Created($"/api/v1/workspaces/{workspaceId}/instagram/connections", new { accountId = result.AccountId });
        });

        connections.MapDelete("/connections/{accountId:guid}", async (
            Guid workspaceId,
            Guid accountId,
            DisconnectInstagramAccountUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var result = await useCase.ExecuteAsync(workspaceId, accountId, cancellationToken);
            return result.Success
                ? Results.NoContent()
                : ConnectionsFailureMapper.ToResult(result.FailureCode!);
        });

        // Explicit subscription repair for one exact account. The server owns the
        // desired field set; the browser never supplies provider fields.
        connections.MapPost("/connections/{accountId:guid}/repair-subscription", async (
            Guid workspaceId,
            Guid accountId,
            RepairSubscriptionUseCase useCase,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Qasedak.Modules.Instagram.Connections");
            var result = await useCase.ExecuteAsync(workspaceId, accountId, cancellationToken);
            if (!result.Success)
            {
                ConnectionEndpointLogs.LogRepairFailed(logger, result.FailureCode!);
                return ConnectionsFailureMapper.ToResult(result.FailureCode!);
            }

            ConnectionEndpointLogs.LogRepaired(logger);
            return Results.Ok(new { subscriptionHealth = result.Health.ToString() });
        });

        return endpoints;
    }
}

public sealed record ConnectAccountRequest(string AuthorizationCode, string RedirectUri, string? State);

/// <summary>
/// Outcome-only connection logs: failure codes and health names only — never
/// OAuth state, authorization codes, tokens, identities or account ids.
/// </summary>
internal static partial class ConnectionEndpointLogs
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Instagram connect failed code={FailureCode}.")]
    internal static partial void LogConnectFailed(ILogger logger, string failureCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Instagram connect succeeded with degraded subscriptions health={SubscriptionHealth}.")]
    internal static partial void LogConnectDegraded(ILogger logger, SubscriptionHealth subscriptionHealth);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Instagram subscription repair failed code={FailureCode}.")]
    internal static partial void LogRepairFailed(ILogger logger, string failureCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Instagram subscription repaired.")]
    internal static partial void LogRepaired(ILogger logger);
}

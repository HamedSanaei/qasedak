using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Qasedak.Modules.Identity.Application.Authentication;
using Qasedak.Modules.Identity.Application.Workspaces;
using Qasedak.Modules.Identity.Infrastructure.Authentication;

namespace Qasedak.Modules.Identity.Infrastructure.Endpoints;

/// <summary>
/// Identity module HTTP surface: register, login, me, workspace creation and member
/// listing. Authorization is enforced here at the server boundary; use cases stay
/// transport-agnostic.
/// </summary>
public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var identity = endpoints.MapGroup("/api/v1/identity").WithTags("Identity");

        identity.MapPost("/register", async (
            RegisterUserRequest request,
            RegisterUserUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var result = await useCase.HandleAsync(
                new RegisterUserCommand(request.Email, request.DisplayName, request.Password),
                cancellationToken);

            return result.Success
                ? Results.Created($"/api/v1/identity/me", new { userId = result.UserId })
                : Failure(result.FailureCode!);
        });

        identity.MapPost("/login", async (
            LoginRequest request,
            AuthenticateUserUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var result = await useCase.HandleAsync(
                new AuthenticateUserCommand(request.Email, request.Password),
                cancellationToken);

            return result.Success
                ? Results.Ok(new { accessToken = result.Token.Value, expiresAtUtc = result.Token.ExpiresAtUtc })
                : Results.Json(new { code = result.FailureCode }, statusCode: StatusCodes.Status401Unauthorized);
        });

        identity.MapGet("/me", (ClaimsPrincipal principal) => Results.Ok(new
        {
            userId = principal.FindFirstValue(ClaimTypes.NameIdentifier),
            email = principal.FindFirstValue(ClaimTypes.Email),
        })).RequireAuthorization(RequireBearer());

        var workspaces = endpoints.MapGroup("/api/v1/workspaces").WithTags("Workspaces");

        workspaces.MapPost(string.Empty, async (
            CreateWorkspaceRequest request,
            ClaimsPrincipal principal,
            CreateWorkspaceUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var userId = RequireUserId(principal);
            if (userId is null)
            {
                return Results.Json(new { code = AuthenticationFailures.InvalidCredentials }, statusCode: 401);
            }

            var result = await useCase.ExecuteAsync(userId.Value, request.Name, cancellationToken);
            return result.Success
                ? Results.Created($"/api/v1/workspaces/{result.WorkspaceId}/members", new { workspaceId = result.WorkspaceId, name = result.Name })
                : Failure(result.FailureCode!);
        }).RequireAuthorization(RequireBearer());

        workspaces.MapGet("/{workspaceId:guid}/members", async (
            Guid workspaceId,
            ClaimsPrincipal principal,
            ListWorkspaceMembersUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var userId = RequireUserId(principal);
            if (userId is null)
            {
                return Results.Json(new { code = AuthenticationFailures.InvalidCredentials }, statusCode: 401);
            }

            var result = await useCase.ExecuteAsync(userId.Value, workspaceId, cancellationToken);
            return result.Success
                ? Results.Ok(new
                {
                    workspaceName = result.WorkspaceName,
                    members = result.Members.Select(m => new { userId = m.UserId, role = m.Role.ToString() }),
                })
                : result.FailureCode == WorkspaceFailures.NotFound
                    ? Results.Json(new { code = WorkspaceFailures.NotFound }, statusCode: 404)
                    : Results.Json(new { code = WorkspaceFailures.Forbidden }, statusCode: 403);
        }).RequireAuthorization(RequireBearer());

        return endpoints;
    }

    private static AuthorizeAttribute RequireBearer() => new()
    {
        AuthenticationSchemes = SecurityTokenAuthenticationHandler.SchemeName,
    };

    private static Guid? RequireUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static IResult Failure(string failureCode) => failureCode switch
    {
        AuthenticationFailures.InvalidEmail or
        AuthenticationFailures.InvalidDisplayName or
        AuthenticationFailures.WeakPassword or
        WorkspaceFailures.InvalidName => Results.Json(new { code = failureCode }, statusCode: 400),
        AuthenticationFailures.EmailTaken => Results.Json(new { code = failureCode }, statusCode: 409),
        _ => Results.Json(new { code = failureCode }, statusCode: 400),
    };
}

public sealed record RegisterUserRequest(string Email, string DisplayName, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record CreateWorkspaceRequest(string Name);

using Microsoft.AspNetCore.Authorization;
using Qasedak.Modules.Identity.Application.Workspaces;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Requires the authenticated caller to be a member of the workspace addressed by the
/// `workspaceId` route value. Applied to every workspace-scoped endpoint group so module
/// handlers stay membership-agnostic while the composition root enforces tenant isolation
/// uniformly. Non-members receive 403 — resource existence is not disclosed.
/// </summary>
public sealed class WorkspaceMemberRequirement : IAuthorizationRequirement;

public sealed class WorkspaceMembershipAuthorizationHandler(IWorkspaceAccessChecker access)
    : AuthorizationHandler<WorkspaceMemberRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WorkspaceMemberRequirement requirement)
    {
        if (context.Resource is not HttpContext http)
        {
            return;
        }

        var userIdClaim = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return;
        }

        if (!http.Request.RouteValues.TryGetValue("workspaceId", out var raw)
            || !Guid.TryParse(raw?.ToString(), out var workspaceId))
        {
            // No workspace addressed: nothing for this policy to gate.
            context.Succeed(requirement);
            return;
        }

        if (await access.IsMemberAsync(workspaceId, userId))
        {
            context.Succeed(requirement);
        }
    }
}

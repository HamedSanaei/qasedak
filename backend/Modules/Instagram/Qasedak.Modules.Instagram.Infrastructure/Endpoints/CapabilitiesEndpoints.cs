using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Qasedak.Modules.Instagram.Application.Capabilities;

namespace Qasedak.Modules.Instagram.Infrastructure.Endpoints;

/// <summary>
/// Token-free exact-account product capability projection. This endpoint never probes Meta;
/// it composes only persisted local state through <see cref="GetAccountCapabilitiesUseCase"/>.
/// </summary>
public static class CapabilitiesEndpoints
{
    public static IEndpointRouteBuilder MapCapabilitiesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/workspaces/{workspaceId:guid}/instagram")
            .WithTags("Instagram Capabilities")
            .RequireAuthorization("workspace-member");

        group.MapGet("/connections/{accountId:guid}/capabilities", async (
            Guid workspaceId,
            Guid accountId,
            GetAccountCapabilitiesUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var result = await useCase.ExecuteAsync(workspaceId, accountId, cancellationToken);
            if (result is not AccountCapabilitiesResult.Ok ok)
            {
                return Results.Json(
                    new { code = "account.notFound" },
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Ok(new
            {
                accountId = ok.Value.AccountId,
                capabilities = ok.Value.Capabilities.Select(item => new
                {
                    capability = item.Capability.ToString(),
                    state = item.State.ToString(),
                    reasonCode = item.ReasonCode,
                }),
            });
        });

        return endpoints;
    }
}

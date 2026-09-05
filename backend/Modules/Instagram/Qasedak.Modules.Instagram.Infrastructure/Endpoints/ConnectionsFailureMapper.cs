using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
namespace Qasedak.Modules.Instagram.Infrastructure.Endpoints;

/// <summary>
/// Maps stable account failure codes to HTTP results. Extracted as a pure type so the
/// mapping is unit-testable without a web host.
/// </summary>
public static class ConnectionsFailureMapper
{
    public static int StatusCodeFor(string failureCode) => failureCode switch
    {
        "account.notFound" or "account.alreadyDisconnected" => StatusCodes.Status404NotFound,
        "account.alreadyConnected" or "account.alreadyConnectedElsewhere" => StatusCodes.Status409Conflict,
        "account.oauthRejected" => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status503ServiceUnavailable, // account.oauthUnavailable
    };

    public static IResult ToResult(string failureCode) =>
        Results.Json(new { code = failureCode }, statusCode: StatusCodeFor(failureCode));
}

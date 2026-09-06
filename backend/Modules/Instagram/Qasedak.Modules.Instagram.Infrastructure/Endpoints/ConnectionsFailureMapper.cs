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
        "oauth.invalidState" or "oauth.expiredState" or "oauth.replayedState" or
            "oauth.workspaceMismatch" or "oauth.redirectMismatch" => StatusCodes.Status400BadRequest,
        "profile.identityMismatch" => StatusCodes.Status409Conflict,
        "account.tokenMissing" or "account.tokenExpired" or "subscription.permissionDenied" or
            "media.permissionDenied" => StatusCodes.Status409Conflict,
        "media.invalidCursor" or "media.invalidLimit" => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status503ServiceUnavailable, // account.oauthUnavailable, profile/subscription/media transient
    };

    public static IResult ToResult(string failureCode) =>
        Results.Json(new { code = failureCode }, statusCode: StatusCodeFor(failureCode));
}

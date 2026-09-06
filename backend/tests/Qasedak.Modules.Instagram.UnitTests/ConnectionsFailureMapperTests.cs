using Microsoft.AspNetCore.Http;
using Qasedak.Modules.Instagram.Infrastructure.Endpoints;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// M08-003: the connection endpoints' failure-code → HTTP status mapping is a contract the
/// frontend relies on; these tests pin it so UI error handling cannot silently drift.
/// </summary>
public class ConnectionsFailureMapperTests
{
    [Theory]
    [InlineData("account.notFound", StatusCodes.Status404NotFound)]
    [InlineData("account.alreadyDisconnected", StatusCodes.Status404NotFound)]
    [InlineData("account.alreadyConnected", StatusCodes.Status409Conflict)]
    [InlineData("account.alreadyConnectedElsewhere", StatusCodes.Status409Conflict)]
    [InlineData("account.oauthRejected", StatusCodes.Status400BadRequest)]
    [InlineData("account.oauthUnavailable", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("oauth.invalidState", StatusCodes.Status400BadRequest)]
    [InlineData("oauth.expiredState", StatusCodes.Status400BadRequest)]
    [InlineData("oauth.replayedState", StatusCodes.Status400BadRequest)]
    [InlineData("oauth.workspaceMismatch", StatusCodes.Status400BadRequest)]
    [InlineData("oauth.redirectMismatch", StatusCodes.Status400BadRequest)]
    [InlineData("profile.identityMismatch", StatusCodes.Status409Conflict)]
    [InlineData("profile.unavailable", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("account.tokenMissing", StatusCodes.Status409Conflict)]
    [InlineData("account.tokenExpired", StatusCodes.Status409Conflict)]
    [InlineData("subscription.permissionDenied", StatusCodes.Status409Conflict)]
    [InlineData("subscription.unavailable", StatusCodes.Status503ServiceUnavailable)]
    public void StatusCodeForMapsStableFailureCodes(string failureCode, int expectedStatus)
    {
        Assert.Equal(expectedStatus, ConnectionsFailureMapper.StatusCodeFor(failureCode));
    }

    [Fact]
    public void StatusCodeForUnknownCodeFailsClosedAsServiceUnavailable()
    {
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ConnectionsFailureMapper.StatusCodeFor("account.unknown"));
    }
}

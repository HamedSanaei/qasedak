using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Infrastructure.OAuth;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// Deterministic fixtures for the OQ-3 error-code taxonomy: Meta responses observed on
/// token use map to exactly one inspection kind; unknown shapes stay transient so account
/// health is never wrongly degraded.
/// </summary>
public sealed class MetaErrorTaxonomyTests
{
    [Theory]
    [InlineData(190, null, "The access token has expired", TokenInspectionKind.Expired)]
    [InlineData(190, 463, "Error validating access token: Session has expired", TokenInspectionKind.Revoked)]
    [InlineData(190, 467, "Error validating access token: The user has not authorized the application",
        TokenInspectionKind.Revoked)]
    [InlineData(190, null, "User deauthorized the app", TokenInspectionKind.Revoked)]
    [InlineData(190, 123, "Some other session problem", TokenInspectionKind.Revoked)]
    public void Code190MapsToExpiredOrRevoked(int code, int? subcode, string message, TokenInspectionKind expected)
    {
        var result = GraphInstagramTokenInspector.Classify(401, code, subcode, message);

        Assert.Equal(expected, result.Kind);
    }

    [Fact]
    public void PermissionErrorsMapToPermissionLoss()
    {
        var byCode10 = GraphInstagramTokenInspector.Classify(403, 10, null, "(#10) Application does not have permission");
        var byCode200 = GraphInstagramTokenInspector.Classify(403, 200, null, "Permissions have not been granted");

        Assert.Equal(TokenInspectionKind.PermissionLoss, byCode10.Kind);
        Assert.Equal(TokenInspectionKind.PermissionLoss, byCode200.Kind);
    }

    [Fact]
    public void WindowSignalStaysTransientForHealth()
    {
        // Code 10 + subcode 2534022 is a closed messaging window, not token death:
        // health must stay untouched so the inspector never degrades on it.
        var window = GraphInstagramTokenInspector.Classify(403, 10, 2534022, "This message is sent outside of allowed window.");

        Assert.Equal(TokenInspectionKind.Transient, window.Kind);
    }

    [Theory]
    [InlineData(429, 4)]
    [InlineData(500, 1)]
    [InlineData(503, 2)]
    public void RateLimitsAndServerErrorsStayTransient(int status, int code)
    {
        var result = GraphInstagramTokenInspector.Classify(status, code, null, "Please reduce request rate");

        Assert.Equal(TokenInspectionKind.Transient, result.Kind);
    }

    [Fact]
    public void UnknownShapesRemainTransient()
    {
        var unknownCode = GraphInstagramTokenInspector.Classify(400, 9999, null, "Something novel");
        var noPayload = GraphInstagramTokenInspector.Classify(400, null, null, string.Empty);

        Assert.Equal(TokenInspectionKind.Transient, unknownCode.Kind);
        Assert.Equal(TokenInspectionKind.Transient, noPayload.Kind);
    }
}

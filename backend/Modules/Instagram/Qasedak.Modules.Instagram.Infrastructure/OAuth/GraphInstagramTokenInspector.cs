using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Infrastructure.Graph;

namespace Qasedak.Modules.Instagram.Infrastructure.OAuth;

/// <summary>
/// Live token inspection over the shared Graph transport (M13-003): GET
/// {graph}/{version}/me?fields=id — the cheapest authenticated call. The central
/// classifier maps Meta's error payload to the OQ-3 taxonomy:
/// - code 190 "has expired" → Expired
/// - code 190 with invalidation subcodes (463/467) or "deauthorized" → Revoked
/// - code 10/200 permission errors → PermissionLoss (the window signal
///   10/2534022 is deliberately Transient: a closed window is not token death)
/// - rate limits (4/17/32/613), 429, 5xx, transport and non-JSON responses → Transient.
/// Token values never appear in returned details.
/// </summary>
public sealed class GraphInstagramTokenInspector(HttpClient http, IOptions<MetaGraphOptions> options) : IMetaTokenInspector
{
    /// <summary>Named HttpClient registration used by dependency injection.</summary>
    public const string HttpClientName = "MetaInstagramInspection";

    private readonly MetaGraphTransport _transport = new(http, options.Value.TimeoutSeconds);

    private readonly MetaGraphOptions _options = options.Value;

    public async Task<TokenInspection> InspectAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var endpoint = MetaGraphUris.Versioned(_options.GraphHost, _options.ApiVersion, "me", "fields=id").ToString();
        // The token travels as a query parameter exactly as the documented probe does;
        // it never enters returned details, logs or exceptions (transport redacts).
        var outcome = await _transport.GetAsync(
            endpoint + "&access_token=" + Uri.EscapeDataString(accessToken), cancellationToken);

        return outcome switch
        {
            MetaGraphCallResult.Success => TokenInspection.Healthy(),
            MetaGraphCallResult.Rejected rejected => FromFailure(
                MetaGraphClassifier.Classify(rejected.Error), rejected.Error),
            MetaGraphCallResult.Unreachable unreachable => TokenInspection.From(
                TokenInspectionKind.Transient, unreachable.Detail),
            _ => TokenInspection.From(TokenInspectionKind.Transient, "Meta endpoint unreachable."),
        };
    }

    private static TokenInspection FromFailure(MetaGraphFailure failure, MetaGraphError error) => failure switch
    {
        MetaGraphFailure.TokenExpired => TokenInspection.From(TokenInspectionKind.Expired, "The access token has expired."),
        MetaGraphFailure.Revoked or MetaGraphFailure.AuthenticationInvalid =>
            TokenInspection.From(TokenInspectionKind.Revoked, "The account owner revoked access."),
        MetaGraphFailure.PermissionLoss =>
            TokenInspection.From(TokenInspectionKind.PermissionLoss, "A required permission was removed or not granted."),
        _ => TokenInspection.From(TokenInspectionKind.Transient, $"Meta returned status {error.HttpStatusCode}."),
    };

    /// <summary>Pure taxonomy mapping, exercised directly by deterministic fixtures.</summary>
    public static TokenInspection Classify(int statusCode, int? code, int? subcode, string message) =>
        FromFailure(
            MetaGraphClassifier.Classify(statusCode, code, subcode, message ?? string.Empty),
            new MetaGraphError(statusCode, code, subcode, null, message ?? string.Empty, null));
}

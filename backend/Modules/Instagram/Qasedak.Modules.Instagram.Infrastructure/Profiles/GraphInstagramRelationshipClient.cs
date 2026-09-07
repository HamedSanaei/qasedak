using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.RevealFlow;
using Qasedak.Modules.Instagram.Infrastructure.Graph;

namespace Qasedak.Modules.Instagram.Infrastructure.Profiles;

/// <summary>
/// Adapter for the official User Profile relationship field over the shared Graph
/// transport (M13-003): <c>GET {graph}/{version}/{IGSID}?fields=is_user_follow_business</c>
/// with a Bearer Instagram User token (Instagram API with Instagram Login; permissions
/// instagram_business_basic + instagram_business_manage_messages, verified 2026-09-07).
///
/// Tri-state by design: the provider boolean is authoritative (true = Follows, false =
/// DoesNotFollow); a missing field, malformed payload or provider rejection is
/// UnknownUnavailable with a bounded reason — NEVER fabricated into false. M13-011 only
/// calls this port after a proven messaging-consent basis (a qualifying inbound user
/// message); consent errors are contract drift, never a discovery mechanism.
/// </summary>
public sealed class GraphInstagramRelationshipClient(
    HttpClient http,
    IOptions<MetaGraphOptions> graphOptions) : IInstagramRelationshipClient
{
    public const string HttpClientName = "MetaInstagramRelationship";

    private const string Fields = "is_user_follow_business";

    private readonly MetaGraphTransport _transport = new(http, graphOptions.Value.TimeoutSeconds);

    private readonly MetaGraphOptions _graph = graphOptions.Value;

    public async Task<FollowStateResult> GetFollowStateAsync(
        string accessToken,
        string participantIGSId,
        CancellationToken cancellationToken = default)
    {
        var endpoint = MetaGraphUris.Versioned(
            _graph.GraphHost,
            _graph.ApiVersion,
            Uri.EscapeDataString(participantIGSId)).ToString()
            + "?fields=" + Uri.EscapeDataString(Fields);

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        if (outcome is MetaGraphCallResult.Success success)
        {
            return InterpretSuccess(success.Document);
        }

        return outcome switch
        {
            MetaGraphCallResult.Rejected rejected => MapFailure(rejected.Error),
            _ => FollowStateResult.Unavailable(FollowStateUnavailableReason.Transient),
        };
    }

    private static FollowStateResult InterpretSuccess(JsonDocument document)
    {
        using (document)
        {
            var node = document.RootElement;
            if (node.ValueKind == JsonValueKind.Object
                && node.TryGetProperty("is_user_follow_business", out var follows)
                && follows.ValueKind == JsonValueKind.True)
            {
                return FollowStateResult.Follows();
            }

            if (node.ValueKind == JsonValueKind.Object
                && node.TryGetProperty("is_user_follow_business", out var notFollowing)
                && notFollowing.ValueKind == JsonValueKind.False)
            {
                return FollowStateResult.DoesNotFollow();
            }

            // Missing/invalid field: no authoritative data — never false.
            return FollowStateResult.Unavailable(FollowStateUnavailableReason.Malformed);
        }
    }

    private static FollowStateResult MapFailure(MetaGraphError error) =>
        MetaGraphClassifier.Classify(error) switch
        {
            // Consent-required errors surface as PermissionLoss in the shared taxonomy;
            // both are "no data", never "does not follow".
            MetaGraphFailure.PermissionLoss => FollowStateResult.Unavailable(FollowStateUnavailableReason.PermissionUnavailable),
            MetaGraphFailure.RateLimited or MetaGraphFailure.Transient or MetaGraphFailure.TransportFailure =>
                FollowStateResult.Unavailable(FollowStateUnavailableReason.Transient),
            _ => FollowStateResult.Unavailable(FollowStateUnavailableReason.ConsentUnavailable),
        };
}

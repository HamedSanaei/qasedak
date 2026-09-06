using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Subscriptions;
using Qasedak.Modules.Instagram.Infrastructure.Graph;

namespace Qasedak.Modules.Instagram.Infrastructure.Subscriptions;

/// <summary>
/// Adapter for the professional-account webhook subscription edge over the shared
/// Graph transport (M13-005): <c>POST {graph}/{version}/{IG_ID}/subscribed_apps</c>
/// with Bearer token and <c>subscribed_fields</c> — the exact shape of the verified
/// official setup guide. No read endpoint is assumed:
/// the official IG-Login contract documents subscribe only, so verification means
/// comparing the confirmed outcome against the desired set — never faking it.
/// </summary>
public sealed class GraphSubscriptionClient(
    HttpClient http,
    IOptions<MetaGraphOptions> graphOptions) : ISubscriptionClient
{
    public const string HttpClientName = "MetaInstagramSubscriptions";

    private readonly MetaGraphTransport _transport = new(http, graphOptions.Value.TimeoutSeconds);

    private readonly MetaGraphOptions _graph = graphOptions.Value;

    public GraphSubscriptionClient(HttpClient http)
        : this(http, Microsoft.Extensions.Options.Options.Create(new MetaGraphOptions()))
    {
    }

    public async Task<SubscriptionResult> SubscribeAsync(
        string accessToken,
        string professionalAccountId,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(professionalAccountId))
        {
            return SubscriptionResult.Failed(SubscriptionFailures.Unavailable, transient: false);
        }

        if (fields.Count == 0)
        {
            return SubscriptionResult.Failed(SubscriptionFailures.Unavailable, transient: false);
        }

        var endpoint = MetaGraphUris.Versioned(
            _graph.GraphHost, _graph.ApiVersion, Uri.EscapeDataString(professionalAccountId.Trim()) + "/subscribed_apps").ToString();
        // Bearer authorization keeps the token out of URLs; the documented query-param
        // style is avoided deliberately (URLs are more likely to be logged upstream).
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["subscribed_fields"] = string.Join(',', fields),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        if (outcome is MetaGraphCallResult.Success success)
        {
            using (success.Document)
            {
                // Documented success: {"success":true}. Anything else is not proof.
                if (success.Document.RootElement.ValueKind == JsonValueKind.Object
                    && success.Document.RootElement.TryGetProperty("success", out var ok)
                    && ok.ValueKind == JsonValueKind.True)
                {
                    return SubscriptionResult.Subscribed(fields);
                }
            }

            return SubscriptionResult.Failed(SubscriptionFailures.Unavailable, transient: false);
        }

        return outcome switch
        {
            MetaGraphCallResult.Rejected rejected => MapFailure(rejected.Error),
            _ => SubscriptionResult.Failed(SubscriptionFailures.Unavailable, transient: true),
        };
    }

    private static SubscriptionResult MapFailure(MetaGraphError error) =>
        MetaGraphClassifier.Classify(error) switch
        {
            MetaGraphFailure.RateLimited or MetaGraphFailure.Transient or MetaGraphFailure.TransportFailure =>
                SubscriptionResult.Failed(SubscriptionFailures.Unavailable, transient: true),
            MetaGraphFailure.PermissionLoss =>
                SubscriptionResult.Failed(SubscriptionFailures.PermissionDenied, transient: false),
            _ => SubscriptionResult.Failed(SubscriptionFailures.Unavailable, transient: false),
        };
}

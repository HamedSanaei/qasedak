using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Graph;

namespace Qasedak.Modules.Instagram.Infrastructure.Profiles;

/// <summary>
/// Adapter for the verified professional-profile endpoint over the shared Graph
/// transport (M13-005): <c>GET {graph}/{version}/me?fields=id,username,…</c>
/// with a Bearer Instagram User token (<c>/me</c> is the token owner's
/// professional account per the official overview). Maps into Qasedak-owned
/// records; Graph DTOs and tokens never escape.
///
/// Field set verified against the official IG User reference (2026-04-22):
/// only <c>id</c>, <c>username</c>, <c>name</c> and <c>profile_picture_url</c>
/// are requested. The reference exposes no <c>user_id</c> and no
/// <c>account_type</c> on this node, so identity is proven with <c>id</c> and
/// account type stays unpopulated until a verified source exists.
/// </summary>
public sealed class GraphAccountProfileClient(
    HttpClient http,
    IOptions<MetaGraphOptions> graphOptions) : IAccountProfileClient
{
    public const string HttpClientName = "MetaInstagramProfile";

    private const string Fields = "id,username,name,profile_picture_url";

    private readonly MetaGraphTransport _transport = new(http, graphOptions.Value.TimeoutSeconds);

    private readonly MetaGraphOptions _graph = graphOptions.Value;

    public GraphAccountProfileClient(HttpClient http)
        : this(http, Microsoft.Extensions.Options.Options.Create(new MetaGraphOptions()))
    {
    }

    public async Task<AccountProfileOutcome> GetProfileAsync(
        string accessToken, string expectedProviderAccountId, CancellationToken cancellationToken = default)
    {
        var endpoint = MetaGraphUris.Versioned(_graph.GraphHost, _graph.ApiVersion, "me").ToString()
            + "?fields=" + Uri.EscapeDataString(Fields);

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        if (outcome is MetaGraphCallResult.Success success)
        {
            return InterpretSuccess(success.Document, expectedProviderAccountId);
        }

        return outcome switch
        {
            MetaGraphCallResult.Rejected rejected => MapFailure(rejected.Error),
            _ => new AccountProfileOutcome.Unavailable(ProfileFailures.Unavailable, Transient: true),
        };
    }

    private static AccountProfileOutcome InterpretSuccess(JsonDocument document, string expectedProviderAccountId)
    {
        using (document)
        {
            // Documented shape is a bare object; a top-level "data" array is
            // tolerated for forward compatibility, never invented.
            var node = document.RootElement;
            if (node.ValueKind == JsonValueKind.Object
                && node.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                && data.GetArrayLength() > 0)
            {
                node = data[0];
            }

            if (node.ValueKind != JsonValueKind.Object
                || !node.TryGetProperty("id", out var idElement)
                || idElement.GetString() is not { } accountId
                || string.IsNullOrWhiteSpace(accountId))
            {
                return new AccountProfileOutcome.Unavailable(ProfileFailures.Unavailable, Transient: false);
            }

            if (!string.Equals(accountId.Trim(), expectedProviderAccountId.Trim(), StringComparison.Ordinal))
            {
                return new AccountProfileOutcome.IdentityMismatch(expectedProviderAccountId, accountId.Trim());
            }

            if (!node.TryGetProperty("username", out var usernameElement)
                || string.IsNullOrWhiteSpace(usernameElement.GetString()))
            {
                return new AccountProfileOutcome.Unavailable(ProfileFailures.Unavailable, Transient: false);
            }

            return new AccountProfileOutcome.Ok(new InstagramAccountProfile(
                accountId.Trim(),
                usernameElement.GetString()!.Trim(),
                Optional(node, "name"),
                Optional(node, "profile_picture_url"),
                AccountType: null));
        }
    }

    private static AccountProfileOutcome.Unavailable MapFailure(MetaGraphError error) =>
        MetaGraphClassifier.Classify(error) switch
        {
            MetaGraphFailure.RateLimited or MetaGraphFailure.Transient or MetaGraphFailure.TransportFailure =>
                new AccountProfileOutcome.Unavailable(ProfileFailures.Unavailable, Transient: true),
            MetaGraphFailure.PermissionLoss =>
                new AccountProfileOutcome.Unavailable(ProfileFailures.Unavailable, Transient: false),
            _ => new AccountProfileOutcome.Unavailable(ProfileFailures.Unavailable, Transient: false),
        };

    private static string? Optional(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
}

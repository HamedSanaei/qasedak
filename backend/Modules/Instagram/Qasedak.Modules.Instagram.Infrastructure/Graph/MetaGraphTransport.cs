using System.Text.Json;

namespace Qasedak.Modules.Instagram.Infrastructure.Graph;

/// <summary>Outcome of one executed Graph HTTP call; never throws for provider behavior.</summary>
public abstract record MetaGraphCallResult
{
    /// <summary>2xx with a parsed JSON document (caller owns disposal).</summary>
    public sealed record Success(JsonDocument Document) : MetaGraphCallResult;

    /// <summary>Meta answered with an error payload (any non-2xx with or without JSON).</summary>
    public sealed record Rejected(MetaGraphError Error) : MetaGraphCallResult;

    /// <summary>Network failure, timeout or cancellation; Meta never answered.</summary>
    public sealed record Unreachable(string Detail) : MetaGraphCallResult;
}

/// <summary>
/// Shared Graph HTTP executor (M13-003): per-request timeout (distinguishing Meta
/// slowness from caller cancellation), safe JSON reads, canonical error parsing.
/// Secret-bearing values never appear in results: only bounded, redacted metadata.
/// </summary>
public sealed class MetaGraphTransport(HttpClient http, int timeoutSeconds)
{
    private readonly HttpClient _http = http;

    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));

    public Task<MetaGraphCallResult> GetAsync(string url, CancellationToken cancellationToken = default) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);

    public Task<MetaGraphCallResult> PostJsonAsync(string url, HttpContent content, CancellationToken cancellationToken = default) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = content }, cancellationToken);

    public Task<MetaGraphCallResult> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken = default) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) }, cancellationToken);

    /// <summary>Sends a caller-built request (e.g. with an Authorization header).</summary>
    public Task<MetaGraphCallResult> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(request, cancellationToken);

    private Task<MetaGraphCallResult> SendAsync(Func<HttpRequestMessage> factory, CancellationToken cancellationToken) =>
        ExecuteAsync(factory(), cancellationToken);

    private async Task<MetaGraphCallResult> ExecuteAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new MetaGraphCallResult.Unreachable("Meta request timed out.");
            }
            catch (HttpRequestException)
            {
                return new MetaGraphCallResult.Unreachable("HTTP request failed.");
            }

            using (response)
            {
                var document = await ReadJsonAsync(response);
                if (!response.IsSuccessStatusCode)
                {
                    return new MetaGraphCallResult.Rejected(MetaGraphErrorParser.Parse((int)response.StatusCode, document));
                }

                if (document is null)
                {
                    return new MetaGraphCallResult.Rejected(
                        new MetaGraphError((int)response.StatusCode, null, null, null, "Payload did not match the documented shape.", null));
                }

                return new MetaGraphCallResult.Success(document);
            }
        }
    }

    private static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

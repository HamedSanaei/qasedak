using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Effects;

/// <summary>
/// Deterministic tests for the focused IG Comment reference read (creation timestamp —
/// the authoritative 7-day policy input). GET on the comment node with fields=timestamp,
/// Bearer header (never URL/query token), ISO 8601 parsing, NotFound and unavailable
/// degradation, redaction.
/// </summary>
public sealed class GraphCommentReferenceReaderTests
{
    private const string AccessToken = "IG-USER-TOKEN-SECRET";
    private const string CommentId = "17895695792288124";

    private static (GraphCommentReferenceReader Client, ScriptedHttpHandler Handler) NewClient(HttpResponseMessage response)
    {
        var handler = new ScriptedHttpHandler(_ => response);
        var client = new GraphCommentReferenceReader(new HttpClient(handler), Options.Create(new MetaGraphOptions()));
        return (client, handler);
    }

    [Fact]
    public async Task ReadsTheCommentNodeWithTimestampFieldAndBearerToken()
    {
        var (client, handler) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"timestamp":"2017-05-19T23:27:28+0000","id":"17895695792288124"}""", Encoding.UTF8, "application/json"),
        });

        var result = await client.ReadCreatedAtUtcAsync(AccessToken, CommentId, default);

        var found = Assert.IsType<CommentReferenceReadResult.Found>(result);
        Assert.Equal(new DateTimeOffset(2017, 5, 19, 23, 27, 28, TimeSpan.Zero), found.CreatedAtUtc);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal(new Uri($"https://graph.instagram.com/v26.0/{CommentId}?fields=timestamp"), handler.LastRequest.RequestUri);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal(AccessToken, handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task TokenNeverAppearsInUrl()
    {
        var (client, handler) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"timestamp":"2017-05-19T23:27:28+0000"}""", Encoding.UTF8, "application/json"),
        });

        await client.ReadCreatedAtUtcAsync(AccessToken, CommentId, default);

        Assert.DoesNotContain(AccessToken, handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ParsesOffsetFormats()
    {
        var (client, _) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"timestamp":"2019-05-10T19:06:54+0000"}""", Encoding.UTF8, "application/json"),
        });

        var result = await client.ReadCreatedAtUtcAsync(AccessToken, CommentId, default);

        var found = Assert.IsType<CommentReferenceReadResult.Found>(result);
        Assert.Equal(new DateTimeOffset(2019, 5, 10, 19, 6, 54, TimeSpan.Zero), found.CreatedAtUtc);
    }

    [Fact]
    public async Task MissingTimestampIsUnavailableNeverInvented()
    {
        var (client, _) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"17895695792288124"}""", Encoding.UTF8, "application/json"),
        });

        var result = await client.ReadCreatedAtUtcAsync(AccessToken, CommentId, default);

        Assert.IsType<CommentReferenceReadResult.Unavailable>(result);
    }

    [Fact]
    public async Task InvalidTimestampIsUnavailable()
    {
        var (client, _) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"timestamp":"not-a-date"}""", Encoding.UTF8, "application/json"),
        });

        var result = await client.ReadCreatedAtUtcAsync(AccessToken, CommentId, default);

        Assert.IsType<CommentReferenceReadResult.Unavailable>(result);
    }

    [Fact]
    public async Task Http404IsNotFound()
    {
        var (client, _) = NewClient(new(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error":{"message":"invalid comment id","code":100}}""", Encoding.UTF8, "application/json"),
        });

        var result = await client.ReadCreatedAtUtcAsync(AccessToken, CommentId, default);

        Assert.IsType<CommentReferenceReadResult.NotFound>(result);
    }

    [Fact]
    public async Task ProviderErrorIsUnavailableWithNoTokenLeak()
    {
        var (client, _) = NewClient(new(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":{"message":"expired token","code":190}}""", Encoding.UTF8, "application/json"),
        });

        var result = await client.ReadCreatedAtUtcAsync(AccessToken, CommentId, default);

        Assert.IsType<CommentReferenceReadResult.Unavailable>(result);
    }

    [Fact]
    public async Task TransportFailureIsUnavailable()
    {
        var handler = new ScriptedHttpHandler(_ => throw new HttpRequestException("refused"));
        var client = new GraphCommentReferenceReader(new HttpClient(handler), Options.Create(new MetaGraphOptions()));

        var result = await client.ReadCreatedAtUtcAsync(AccessToken, CommentId, default);

        Assert.IsType<CommentReferenceReadResult.Unavailable>(result);
    }
}

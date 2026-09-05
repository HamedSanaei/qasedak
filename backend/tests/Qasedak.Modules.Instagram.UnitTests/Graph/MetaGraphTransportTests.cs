using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.OAuth;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// Deterministic contract tests for the central Graph foundation (M13-003):
/// canonical error envelope parsing (both official shapes), the shared failure
/// taxonomy incl. the official window signal, retryability, secret redaction,
/// trace correlation, URI construction and transport timeout semantics.
/// </summary>
public sealed class MetaGraphTransportTests
{
    private static JsonDocument Envelope(string json) => JsonDocument.Parse(json);

    [Fact]
    public void NestedEnvelopeParsesCodeSubcodeTypeMessageAndTrace()
    {
        using var document = Envelope(
            """{"error":{"message":"This message is sent outside of allowed window.","type":"OAuthException","code":10,"error_subcode":2534022,"fbtrace_id":"t-1"}}""");

        var error = MetaGraphErrorParser.Parse(403, document);

        Assert.Equal(403, error.HttpStatusCode);
        Assert.Equal(10, error.Code);
        Assert.Equal(2534022, error.Subcode);
        Assert.Equal("OAuthException", error.Type);
        Assert.Equal("t-1", error.FbTraceId);
        Assert.True(error.HasJsonBody);
    }

    [Fact]
    public void FlatOAuthShapeParsesErrorTypeAndMessage()
    {
        using var document = Envelope(
            """{"error_type":"OAuthException","code":400,"error_message":"Matching code was not found"}""");

        var error = MetaGraphErrorParser.Parse(400, document);

        Assert.Equal(400, error.Code);
        Assert.Equal("OAuthException", error.Type);
        Assert.Contains("not found", error.Message);
    }

    [Fact]
    public void MissingDocumentYieldsStatusOnlyEnvelope()
    {
        var error = MetaGraphErrorParser.Parse(502, null);

        Assert.False(error.HasJsonBody);
        Assert.Null(error.Code);
        Assert.Contains("502", error.Message);
    }

    [Fact]
    public void CredentialShapedMessagesAreWithheld()
    {
        using var secret = Envelope("""{"error":{"message":"client_secret=oops leaked","code":400}}""");
        using var token = Envelope("""{"error":{"message":"EAACEdEose0secret-token","code":400}}""");
        using var assigned = Envelope("""{"error":{"message":"access_token=abc","code":400}}""");

        Assert.Equal("(message withheld)", MetaGraphErrorParser.Parse(400, secret).Message);
        Assert.Equal("(message withheld)", MetaGraphErrorParser.Parse(400, token).Message);
        Assert.Equal("(message withheld)", MetaGraphErrorParser.Parse(400, assigned).Message);
    }

    [Fact]
    public void LongMessagesAreBounded()
    {
        using var document = Envelope($@"{{""error"":{{""message"":""{new string('x', 500)}"",""code"":100}}}}");

        Assert.Equal(300, MetaGraphErrorParser.Parse(400, document).Message.Length);
    }

    [Theory]
    [InlineData(10, 2534022, "outside of allowed window", MetaGraphFailure.MessagingWindowExpired)]
    [InlineData(190, null, "Session has expired", MetaGraphFailure.TokenExpired)]
    [InlineData(190, 463, "Session has expired", MetaGraphFailure.Revoked)]
    [InlineData(190, 467, "not authorized", MetaGraphFailure.Revoked)]
    [InlineData(190, null, "deauthorized", MetaGraphFailure.Revoked)]
    [InlineData(190, 123, "other session problem", MetaGraphFailure.Revoked)]
    [InlineData(10, null, "no permission for this action", MetaGraphFailure.PermissionLoss)]
    [InlineData(200, null, "permission error", MetaGraphFailure.PermissionLoss)]
    [InlineData(4, null, "rate", MetaGraphFailure.RateLimited)]
    [InlineData(17, null, "rate", MetaGraphFailure.RateLimited)]
    [InlineData(32, null, "rate", MetaGraphFailure.RateLimited)]
    [InlineData(613, null, "rate", MetaGraphFailure.RateLimited)]
    [InlineData(100, null, "bad parameter", MetaGraphFailure.InvalidRequest)]
    [InlineData(9999, null, "novel", MetaGraphFailure.Unknown)]
    public void ClassifierMapsCodeSubcodeMessage(int? code, int? subcode, string message, MetaGraphFailure expected)
    {
        Assert.Equal(expected, MetaGraphClassifier.Classify(400, code, subcode, message));
    }

    [Theory]
    [InlineData(429, null, MetaGraphFailure.RateLimited)]
    [InlineData(500, null, MetaGraphFailure.Transient)]
    [InlineData(404, null, MetaGraphFailure.NotFound)]
    [InlineData(400, null, MetaGraphFailure.Unknown)]
    public void ClassifierUsesStatusWhenCodeIsAbsent(int status, int? code, MetaGraphFailure expected)
    {
        Assert.Equal(expected, MetaGraphClassifier.Classify(status, code, null, string.Empty));
    }

    [Fact]
    public void WindowSignalRequiresExactSubcode()
    {
        // Bare code 10 without the window subcode stays a permission loss, never a window.
        Assert.Equal(MetaGraphFailure.PermissionLoss, MetaGraphClassifier.Classify(403, 10, null, "denied"));
        Assert.Equal(MetaGraphFailure.PermissionLoss, MetaGraphClassifier.Classify(403, 10, 9999, "denied"));
    }

    [Theory]
    [InlineData(MetaGraphFailure.RateLimited, true)]
    [InlineData(MetaGraphFailure.Transient, true)]
    [InlineData(MetaGraphFailure.TransportFailure, true)]
    [InlineData(MetaGraphFailure.MessagingWindowExpired, false)]
    [InlineData(MetaGraphFailure.PermissionLoss, false)]
    [InlineData(MetaGraphFailure.Revoked, false)]
    [InlineData(MetaGraphFailure.TokenExpired, false)]
    [InlineData(MetaGraphFailure.AuthenticationInvalid, false)]
    [InlineData(MetaGraphFailure.Unknown, false)]
    public void RetryabilityIsExplicitPerBucket(MetaGraphFailure failure, bool expected)
    {
        Assert.Equal(expected, MetaGraphError.IsRetryable(failure));
    }

    [Fact]
    public void TraceSuffixCorrelatesWithoutLeaking()
    {
        Assert.Equal("detail (trace t-9)", MetaGraphErrorParser.WithTrace("detail", "t-9"));
        Assert.Equal("detail", MetaGraphErrorParser.WithTrace("detail", null));
    }

    [Fact]
    public void VersionedUrisComposeHostVersionAndPath()
    {
        Assert.Equal(
            new Uri("https://graph.instagram.com/v26.0/me/messages"),
            MetaGraphUris.Versioned("https://graph.instagram.com", "v26.0", "me/messages"));
        Assert.Equal(
            new Uri("https://graph.instagram.com/v99.9/me?fields=id"),
            MetaGraphUris.Versioned("https://graph.instagram.com/", "/v99.9/", "/me", "fields=id"));
    }

    [Fact]
    public async Task TransportDistinguishesTimeoutFromCallerCancellation()
    {
        var hanging = new HttpClient(new HangingHandler());
        var transport = new MetaGraphTransport(hanging, timeoutSeconds: 1);

        var timeout = await transport.GetAsync("https://graph.instagram.com/v26.0/me", default);
        var timedOut = Assert.IsType<MetaGraphCallResult.Unreachable>(timeout);
        Assert.Contains("timed out", timedOut.Detail);

        // Caller cancellation propagates instead of masquerading as a timeout.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.GetAsync("https://graph.instagram.com/v26.0/me", cancelled.Token));
    }

    [Fact]
    public async Task TransportNetworkFailureIsUnreachable()
    {
        var transport = new MetaGraphTransport(
            new HttpClient(new ThrowingTransportHandler()), timeoutSeconds: 30);

        var result = await transport.GetAsync("https://graph.instagram.com/v26.0/me", default);

        var unreachable = Assert.IsType<MetaGraphCallResult.Unreachable>(result);
        Assert.Equal("HTTP request failed.", unreachable.Detail);
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ThrowingTransportHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("socket down");
    }

    [Fact]
    public async Task InspectorProbesVersionedMeAndKeepsTokenOutOfDetails()
    {
        const string token = "INSPECT-TOKEN-material";
        HttpRequestMessage? last = null;
        var handler = new ScriptedInspectorHandler(r => last = r,
            new(HttpStatusCode.OK) { Content = new StringContent("""{"id":"123"}""") });
        var inspector = new GraphInstagramTokenInspector(
            new HttpClient(handler), Options.Create(new MetaGraphOptions()));

        var healthy = await inspector.InspectAsync(token, default);

        Assert.Equal(TokenInspectionKind.Healthy, healthy.Kind);
        Assert.StartsWith("https://graph.instagram.com/v26.0/me?fields=id&access_token=", last!.RequestUri!.ToString());

        var rejecting = new GraphInstagramTokenInspector(
            new HttpClient(new ScriptedInspectorHandler(
                _ => { },
                new(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("""{"error":{"code":190,"error_message":"expired INSPECT-TOKEN-material"}}"""),
                })),
            Options.Create(new MetaGraphOptions()));
        var rejected = await rejecting.InspectAsync(token, default);

        Assert.Equal(TokenInspectionKind.Expired, rejected.Kind);
        Assert.DoesNotContain(token, rejected.Detail ?? string.Empty);
    }

    private sealed class ScriptedInspectorHandler(Action<HttpRequestMessage> capture, HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture(request);
            return Task.FromResult(response);
        }
    }
}

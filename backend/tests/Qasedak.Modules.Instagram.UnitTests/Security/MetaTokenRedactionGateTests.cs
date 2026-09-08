using System.Net;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.OAuth;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

public sealed class MetaTokenRedactionGateTests
{
    private const string Sentinel = "M13_015_SENTINEL_TOKEN_DO_NOT_LEAK";

    [Theory]
    [InlineData(401, "{\"error\":{\"code\":190,\"message\":\"expired M13_015_SENTINEL_TOKEN_DO_NOT_LEAK\"}}", TokenInspectionKind.Expired)]
    [InlineData(403, "{\"error\":{\"code\":10,\"message\":\"permission M13_015_SENTINEL_TOKEN_DO_NOT_LEAK\"}}", TokenInspectionKind.PermissionLoss)]
    [InlineData(429, "{\"error\":{\"code\":4,\"message\":\"rate M13_015_SENTINEL_TOKEN_DO_NOT_LEAK\"}}", TokenInspectionKind.Transient)]
    [InlineData(500, "{\"error\":{\"code\":2,\"message\":\"server M13_015_SENTINEL_TOKEN_DO_NOT_LEAK\"}}", TokenInspectionKind.Transient)]
    [InlineData(200, "not-json-M13_015_SENTINEL_TOKEN_DO_NOT_LEAK", TokenInspectionKind.Transient)]
    public async Task InspectorNeverLeaksSentinelAcrossProviderOutcomes(int status, string body, TokenInspectionKind expected)
    {
        HttpRequestMessage? captured = null;
        var inspector = NewInspector(new DelegateHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });
        }));
        var result = await inspector.InspectAsync(Sentinel);

        Assert.Equal(expected, result.Kind);
        Assert.DoesNotContain(Sentinel, result.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.NotNull(captured);
        Assert.DoesNotContain(Sentinel, captured!.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal(Sentinel, captured.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task InspectorSuccessKeepsSentinelHeaderOnly()
    {
        HttpRequestMessage? captured = null;
        var inspector = NewInspector(new DelegateHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"123\"}") });
        }));

        var result = await inspector.InspectAsync(Sentinel);

        Assert.Equal(TokenInspectionKind.Healthy, result.Kind);
        Assert.DoesNotContain(Sentinel, captured!.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Equal(Sentinel, captured.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task TimeoutAndUnexpectedHandlerExceptionNeverLeakSentinel()
    {
        var timeoutInspector = NewInspector(new DelegateHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }), timeoutSeconds: 1);
        var timeout = await timeoutInspector.InspectAsync(Sentinel);
        Assert.Equal(TokenInspectionKind.Transient, timeout.Kind);
        Assert.DoesNotContain(Sentinel, timeout.Detail ?? string.Empty, StringComparison.Ordinal);

        var unexpectedInspector = NewInspector(new DelegateHandler((_, _) =>
            throw new InvalidOperationException(Sentinel)));
        var unexpected = await unexpectedInspector.InspectAsync(Sentinel);
        Assert.Equal(TokenInspectionKind.Transient, unexpected.Kind);
        Assert.Equal("HTTP request failed.", unexpected.Detail);
        Assert.DoesNotContain(Sentinel, unexpected.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    private static GraphInstagramTokenInspector NewInspector(HttpMessageHandler handler, int timeoutSeconds = 30) =>
        new(new HttpClient(handler), Options.Create(new MetaGraphOptions { TimeoutSeconds = timeoutSeconds }));

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}

using System.Security.Cryptography;
using System.Text;
using Qasedak.Modules.Instagram.Infrastructure.Webhooks;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// End-to-end Meta webhook verification over real HTTP: subscription handshake and
/// raw-byte HMAC enforcement, including every negative path (bad signature never echoes
/// content; oversized payloads rejected before signature work).
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class MetaWebhookEndpointTests(ApiPostgreSqlFixture fixture)
{
    private const string Endpoint = "/api/v1/webhooks/instagram";

    private static string Signed(byte[] body) => "sha256=" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiPostgreSqlFixture.MetaAppSecret), body)).ToLowerInvariant();

    [Fact]
    public async Task HandshakeEchoesChallengeAsPlainText()
    {
        var response = await fixture.Client.GetAsync(
            $"{Endpoint}?hub.mode=subscribe&hub.verify_token={ApiPostgreSqlFixture.MetaVerifyToken}&hub.challenge=CH-115599");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("CH-115599", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HandshakeWithWrongTokenIsForbidden()
    {
        var response = await fixture.Client.GetAsync(
            $"{Endpoint}?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=x");

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HandshakeWithWrongModeIsForbidden()
    {
        var response = await fixture.Client.GetAsync(
            $"{Endpoint}?hub.mode=probe&hub.verify_token={ApiPostgreSqlFixture.MetaVerifyToken}&hub.challenge=x");

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NotificationWithValidSignatureIsAccepted()
    {
        // Escaped-unicode serialization mirrors what Meta signs.
        var body = Encoding.UTF8.GetBytes("{\"object\":\"instagram\",\"entry\":[]}");
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", Signed(body));
        content.Headers.Add("X-Correlation-Id", "corr-e2e-42");

        var response = await fixture.Client.PostAsync(Endpoint, content);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        // Caller-supplied correlation id is honored and echoed for traceability.
        Assert.Equal("corr-e2e-42", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task NotificationWithoutCorrelationIdGetsOneMinted()
    {
        var body = Encoding.UTF8.GetBytes("{\"object\":\"instagram\",\"entry\":[]}");
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", Signed(body));

        var response = await fixture.Client.PostAsync(Endpoint, content);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var correlation));
        Assert.False(string.IsNullOrWhiteSpace(correlation!.Single()));
    }

    [Fact]
    public async Task NotificationWithBadSignatureIsUnauthorizedWithoutEcho()
    {
        var body = Encoding.UTF8.GetBytes("{\"object\":\"instagram\",\"entry\":[{\"secret\":\"leak-me\"}]}");
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", "sha256=" + new string('0', 64));

        var response = await fixture.Client.PostAsync(Endpoint, content);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NotificationWithoutSignatureHeaderIsUnauthorized()
    {
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes("{}"));

        var response = await fixture.Client.PostAsync(Endpoint, content);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("?hub.verify_token=api-integration-meta-verify-token&hub.challenge=x")]
    [InlineData("?hub.mode=subscribe&hub.verify_token=api-integration-meta-verify-token")]
    [InlineData("?hub.mode=%00&hub.verify_token=api-integration-meta-verify-token&hub.challenge=x")]
    public async Task InvalidHandshakeShapesAreForbidden(string query)
    {
        var response = await fixture.Client.GetAsync(Endpoint + query);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SignedEmptyBodyIsRejectedAsBadRequestAfterAuthenticityCheck()
    {
        var body = Array.Empty<byte>();
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", Signed(body));
        var response = await fixture.Client.PostAsync(Endpoint, content);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignedInvalidJsonIsRejectedAsBadRequest()
    {
        var body = Encoding.UTF8.GetBytes("not-json");
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", Signed(body));
        var response = await fixture.Client.PostAsync(Endpoint, content);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BodyChangedAfterSigningIsUnauthorized()
    {
        var signed = Encoding.UTF8.GetBytes("{\"object\":\"instagram\",\"entry\":[]}");
        var modified = Encoding.UTF8.GetBytes("{\"object\":\"instagram\",\"entry\":[{}]}");
        using var content = new ByteArrayContent(modified);
        content.Headers.Add("X-Hub-Signature-256", Signed(signed));
        var response = await fixture.Client.PostAsync(Endpoint, content);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MalformedSignaturePrefixIsUnauthorized()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", "sha1=" + new string('0', 64));
        var response = await fixture.Client.PostAsync(Endpoint, content);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidJsonAtBodyLimitIsAcceptedThroughNormalIngestion()
    {
        const string prefix = "{\"object\":\"instagram\",\"padding\":\"";
        const string suffix = "\",\"entry\":[]}";
        var padding = new string('x', MetaWebhookEndpoints.MaxBodyBytes - prefix.Length - suffix.Length);
        var body = Encoding.UTF8.GetBytes(prefix + padding + suffix);
        Assert.Equal(MetaWebhookEndpoints.MaxBodyBytes, body.Length);
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", Signed(body));
        var response = await fixture.Client.PostAsync(Endpoint, content);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OversizedNotificationIsRejectedBeforeSignatureWork()
    {
        var oversized = new byte[MetaWebhookEndpoints.MaxBodyBytes + 1];
        using var content = new ByteArrayContent(oversized);
        content.Headers.Add("X-Hub-Signature-256", "sha256=" + new string('a', 64));

        var response = await fixture.Client.PostAsync(Endpoint, content);

        Assert.Equal(System.Net.HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
}

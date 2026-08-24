using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// Correlation middleware behavior: fresh ids are minted and echoed, well-formed inbound
/// ids are honored verbatim, malformed ones are replaced.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class CorrelationEndpointTests(ApiPostgreSqlFixture fixture)
{
    [Fact]
    public async Task FreshRequestsGetACorrelationIdEcho()
    {
        var response = await fixture.Client.GetAsync("/api/v1/system");

        response.EnsureSuccessStatusCode();
        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        Assert.True(BuildingBlocks.Infrastructure.Diagnostics.CorrelationIds.IsValid(correlationId), correlationId);
    }

    [Fact]
    public async Task WellFormedInboundIdsAreHonoredAndMalformedOnesReplaced()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system");
        request.Headers.Add("X-Correlation-Id", "integration-test-corr-0001");
        var honored = await fixture.Client.SendAsync(request);
        Assert.Equal("integration-test-corr-0001", honored.Headers.GetValues("X-Correlation-Id").Single());

        using var bad = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system");
        bad.Headers.Add("X-Correlation-Id", "not valid!");
        var replaced = await fixture.Client.SendAsync(bad);
        var minted = replaced.Headers.GetValues("X-Correlation-Id").Single();
        Assert.NotEqual("not valid!", minted);
        Assert.True(BuildingBlocks.Infrastructure.Diagnostics.CorrelationIds.IsValid(minted), minted);
    }
}

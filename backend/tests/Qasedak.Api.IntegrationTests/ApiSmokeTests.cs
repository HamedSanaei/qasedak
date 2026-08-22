using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

public sealed class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiSmokeTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/api/v1/system")]
    public async Task ScaffoldEndpointsAreAvailable(string path)
    {
        using var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

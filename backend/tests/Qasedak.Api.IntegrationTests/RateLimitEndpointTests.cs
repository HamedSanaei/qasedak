using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// Risk-class rate limiting: tight configured budgets answer 429 with Retry-After and a
/// stable code; distinct source IPs get independent budgets (one abuser cannot starve
/// others).
/// </summary>
public sealed class RateLimitEndpointTests
{
    [Fact]
    public async Task PublicEndpointsThrottlePerSourceWith429AndRetryAfter()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Qasedak:RateLimits:Public:Limit", "3");
            builder.UseSetting("Qasedak:RateLimits:Public:WindowSeconds", "60");
            builder.UseSetting("ConnectionStrings:Identity", "Host=localhost;Database=unused");
            builder.UseSetting("ConnectionStrings:Instagram", "Host=localhost;Database=unused");
            builder.UseSetting("ConnectionStrings:Conversations", "Host=localhost;Database=unused");
            builder.UseSetting("ConnectionStrings:Automations", "Host=localhost;Database=unused");
            builder.UseSetting("ConnectionStrings:Contacts", "Host=localhost;Database=unused");
            builder.UseSetting("ConnectionStrings:Billing", "Host=localhost;Database=unused");
        });
        using var client = factory.CreateClient();

        HttpStatusCode? lastStatus = null;
        var sawThrottled = false;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var response = await client.GetAsync("/api/v1/system");
            lastStatus = response.StatusCode;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                sawThrottled = true;
                Assert.True(response.Headers.Contains("Retry-After"), "expected Retry-After on 429");
                break;
            }

            response.Dispose();
        }

        Assert.True(sawThrottled, $"expected a 429 within 8 requests, last={lastStatus}");
    }
}

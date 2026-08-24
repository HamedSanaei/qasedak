using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>Shared webhook signing helper for gate tests.</summary>
internal static class GateSigning
{
    public static string Signature(string secret, string body) =>
        "sha256=" + Convert.ToHexString(new HMACSHA256(Encoding.UTF8.GetBytes(secret))
            .ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
}

/// <summary>
/// Targeted security regressions through the real host: protected endpoints demand
/// tokens, webhook ingestion rejects forged signatures, and cross-workspace access stays
/// denied uniformly (403, no existence leak).
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class SecurityGateTests(ApiPostgreSqlFixture fixture)
{
    internal async Task<(string Token, Guid WorkspaceId)> RegisterAndLoginAsync(string label)
    {
        var email = $"{label}-{Guid.CreateVersion7():N}@example.com";
        using var register = await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new
        {
            email,
            displayName = label,
            password = "strong-enough-pass-1",
        });
        register.EnsureSuccessStatusCode();

        using var createWorkspace = new HttpRequestMessage(HttpMethod.Post, "/api/v1/workspaces")
        {
            Content = new StringContent($$"""{"name":"{{label}} workspace"}""", Encoding.UTF8, "application/json"),
        };
        createWorkspace.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            (await LoginAsync(email)).Token);
        using var created = await fixture.Client.SendAsync(createWorkspace);
        created.EnsureSuccessStatusCode();
        var workspaceId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("workspaceId").GetGuid();

        return await LoginAsync(email) is { } login ? (login.Token, workspaceId) : throw new InvalidOperationException();
    }

    private async Task<(string Token, Guid UserId)> LoginAsync(string email)
    {
        using var login = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login", new
        {
            email,
            password = "strong-enough-pass-1",
        });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("accessToken").GetString()!;

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var meResponse = await fixture.Client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var userId = (await meResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!;
        return (token, Guid.Parse(userId));
    }

    [Fact]
    public async Task ProtectedEndpointsRejectAnonymousCallers()
    {
        using var anonymous = await fixture.Client.GetAsync($"/api/v1/workspaces/{Guid.CreateVersion7()}/contacts");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task ForgedWebhookSignaturesAreRejected()
    {
        var body = """{"object":"instagram","entry":[]}""";
        var forgedSignature = Convert.ToHexString(new HMACSHA256("forged-key"u8.ToArray()).ComputeHash("x"u8.ToArray()))
            .ToLowerInvariant();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/instagram")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Hub-Signature-256", $"sha256={forgedSignature}");

        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CrossWorkspaceContactAccessIsInvisible()
    {
        // A workspace owns an Instagram account; a signed webhook projects a customer
        // contact into it. Account ids mirror Meta's numeric shape.
        var ownerWorkspace = Guid.CreateVersion7();
        var accountId = "1784" + Random.Shared.NextInt64(100000000000, 999999999999);
        await SeedBoundAccountAsync(ownerWorkspace, accountId);
        var sender = $"sec-sender-{Guid.CreateVersion7():N}";
        var sentAtSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new
        {
            @object = "instagram",
            entry = new[]
            {
                new
                {
                    id = accountId,
                    messaging = new[]
                    {
                        new
                        {
                            sender = new { id = sender },
                            recipient = new { id = accountId },
                            timestamp = sentAtSeconds,
                            message = new
                            {
                                mid = $"sec-mid-{Guid.CreateVersion7():N}",
                                text = "isolation probe",
                            },
                        },
                    },
                },
            },
        };
        var body = System.Text.Json.JsonSerializer.Serialize(payload);
        using var signed = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/instagram")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        signed.Headers.TryAddWithoutValidation("X-Hub-Signature-256",
            GateSigning.Signature(ApiPostgreSqlFixture.MetaAppSecret, body));
        using var accepted = await fixture.Client.SendAsync(signed);
        Assert.True(accepted.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted,
            $"webhook rejected with {accepted.StatusCode}");

        // The projection is synchronous on the happy path; allow a short settle window
        // for deferred processing before reading the store.
        Guid contactId = Guid.Empty;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var scope = fixture.Factory.Services.CreateScope();
            await using (var contacts =
                scope.ServiceProvider.GetRequiredService<Qasedak.Modules.Contacts.Infrastructure.Persistence.ContactsDbContext>())
            {
                var identity = await contacts.ContactIdentities
                    .SingleOrDefaultAsync(i => i.ProviderIdentity == sender);
                if (identity is not null)
                {
                    contactId = identity.ContactId;
                    break;
                }
            }

            await Task.Delay(250);
        }

        Assert.NotEqual(Guid.Empty, contactId);

        // An unrelated authenticated user is denied at the policy layer for the owning
        // workspace (uniform 403 - membership checked before any resource lookup), while
        // their own workspace simply does not contain the contact (404).
        var (tokenStranger, strangerWorkspace) = await RegisterAndLoginAsync("sec-stranger");

        using var ownProbe = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/workspaces/{strangerWorkspace}/contacts/{contactId}");
        ownProbe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenStranger);
        using var ownResponse = await fixture.Client.SendAsync(ownProbe);
        Assert.Equal(HttpStatusCode.NotFound, ownResponse.StatusCode);

        using var ownerProbe = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/workspaces/{ownerWorkspace}/contacts/{contactId}");
        ownerProbe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenStranger);
        using var ownerResponse = await fixture.Client.SendAsync(ownerProbe);
        Assert.Equal(HttpStatusCode.Forbidden, ownerResponse.StatusCode);
    }

    private async Task SeedBoundAccountAsync(Guid workspaceId, string providerAccountId)
    {
        var scope = fixture.Factory.Services.CreateScope();
        await using var context = scope.ServiceProvider
            .GetRequiredService<Qasedak.Modules.Instagram.Infrastructure.Persistence.InstagramDbContext>();
        if (await context.Accounts.AnyAsync(a => a.ProviderUserId == providerAccountId))
        {
            return;
        }

        await context.Accounts.AddAsync(
            Qasedak.Modules.Instagram.Domain.Accounts.ConnectedAccount.Create(
                Guid.CreateVersion7(),
                workspaceId,
                providerAccountId,
                Qasedak.Modules.Instagram.Domain.Accounts.ConnectionPath.InstagramLogin,
                ["instagram_business_manage_messages"],
                DateTimeOffset.UtcNow.AddDays(30),
                DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }
}

/// <summary>
/// Representative load gates on the two hottest paths (CI-safe, sequential): webhook
/// ingestion keeps accepting bursts within a time budget and inbox queries answer fast
/// after the burst.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class LoadGateTests(ApiPostgreSqlFixture fixture)
{
    [Fact]
    public async Task WebhookIngestSustainsABurstWithinBudget()
    {
        const int burstSize = 40;
        var sender = $"load-{Guid.CreateVersion7():N}";
        var ingestStopwatch = Stopwatch.StartNew();

        for (var i = 0; i < burstSize; i++)
        {
            var payload = new
            {
                @object = "instagram",
                entry = new[]
                {
                    new
                    {
                        id = sender,
                        messaging = new[]
                        {
                            new
                            {
                                sender = new { id = sender },
                                recipient = new { id = "qasedak-page" },
                                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                message = new
                                {
                                    mid = $"load-mid-{Guid.CreateVersion7():N}",
                                    text = $"load message {i}",
                                },
                            },
                        },
                    },
                },
            };
            var body = System.Text.Json.JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/instagram")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("X-Hub-Signature-256",
                GateSigning.Signature(ApiPostgreSqlFixture.MetaAppSecret, body));
            using var response = await fixture.Client.SendAsync(request);

            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted or HttpStatusCode.NoContent,
                $"event {i} rejected with {response.StatusCode}");
        }

        ingestStopwatch.Stop();

        // Budgets are generous on purpose: they catch order-of-magnitude regressions
        // (accidental per-event container spins, N+1 storms), not millisecond noise.
        Assert.True(ingestStopwatch.ElapsedMilliseconds < burstSize * 1000,
            $"webhook burst took {ingestStopwatch.ElapsedMilliseconds}ms");

        // Representative inbox query: an authenticated user listing their own inbox.
        var security = new SecurityGateTests(fixture);
        var (token, workspaceId) = await security.RegisterAndLoginAsync("load-user");
        var inboxWatch = Stopwatch.StartNew();
        using var inboxRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/workspaces/{workspaceId}/conversations");
        inboxRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var inboxResponse = await fixture.Client.SendAsync(inboxRequest);
        inboxWatch.Stop();

        Assert.Equal(HttpStatusCode.OK, inboxResponse.StatusCode);
        Assert.True(inboxWatch.ElapsedMilliseconds < 2000,
            $"inbox query took {inboxWatch.ElapsedMilliseconds}ms");
    }
}

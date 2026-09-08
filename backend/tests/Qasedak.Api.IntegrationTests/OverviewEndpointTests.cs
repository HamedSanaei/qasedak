using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Qasedak.Modules.Instagram.Application.Insights;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Infrastructure.Insights;
using Qasedak.Modules.Instagram.Infrastructure.Media;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// M13-007 exact-account analytics surface: workspace ownership before any token read
/// or provider call, truthful metric availability (real zero vs NoData vs permission
/// loss), media catalog surviving analytics degradation, bounded per-media fan-out
/// stopping on account-level permission loss, partial per-media failure degrading only
/// that media, and follower history with exact-account isolation and secret-free
/// responses. Insights + media edges are scripted in the fixture; no live Meta call.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class OverviewEndpointTests(ApiPostgreSqlFixture fixture)
{
    private const string Callback = "https://app.test.local/cb";

    private static Guid FreshWorkspace() => Guid.CreateVersion7();

    private sealed record LoginResponse([property: System.Text.Json.Serialization.JsonPropertyName("accessToken")] string AccessToken);

    private async Task<(HttpClient Client, Guid Workspace, Guid AccountId, string ProviderIdentity)> ConnectedAccountAsync(
        string email, Guid workspace, string providerSuffix)
    {
        fixture.Media.Reset();
        fixture.Insights.Reset();
        var client = await AuthedClientAsync(email, workspace);
        var state = await AuthorizeAsync(client, workspace);
        var connect = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/instagram/connections",
            new { authorizationCode = $"code-{providerSuffix}", redirectUri = Callback, state });
        Assert.Equal(HttpStatusCode.Created, connect.StatusCode);
        var accountId = Guid.Parse((await connect.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountId").GetString()!);

        var listed = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/workspaces/{workspace}/instagram/connections");
        var item = listed.GetProperty("items").EnumerateArray()
            .Single(i => Guid.Parse(i.GetProperty("accountId").GetString()!) == accountId);
        var providerIdentity = item.GetProperty("providerIdentity").GetString()!;
        return (client, workspace, accountId, providerIdentity);
    }

    private async Task<HttpClient> AuthedClientAsync(string email, params Guid[] workspaces)
    {
        await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new { email, password = "Passw0rd!23", displayName = "Overview Tester" });
        var login = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login", new { email, password = "Passw0rd!23" });
        var payload = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        var token = payload.AccessToken;

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        me.Headers.Authorization = new("Bearer", token);
        using var meResponse = await fixture.Client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var userId = Guid.Parse((await meResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!);
        foreach (var workspace in workspaces)
        {
            await fixture.EnsureWorkspaceMemberAsync(workspace, userId);
        }

        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<string> AuthorizeAsync(HttpClient client, Guid workspace)
    {
        var response = await client.GetAsync(
            $"/api/v1/workspaces/{workspace}/instagram/authorize-url?redirectUri={Uri.EscapeDataString(Callback)}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString()!;
    }

    private static string OverviewUrl(Guid workspace, Guid accountId) =>
        $"/api/v1/workspaces/{workspace}/instagram/connections/{accountId}/overview";

    private static string HistoryUrl(Guid workspace, Guid accountId, string query = "") =>
        $"/api/v1/workspaces/{workspace}/instagram/connections/{accountId}/followers/history{query}";

    private async Task SeedSnapshotAsync(Guid accountId, DateOnly day, long count, FollowerSnapshotProvenance provenance)
    {
        // Seed through the real persistence layer in the test host's database.
        using var scope = fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        await context.FollowerSnapshots.AddAsync(new FollowerSnapshotRow(
            Guid.CreateVersion7(), accountId, day, count, provenance,
            day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc),
            day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc),
            day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc)));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task UnauthenticatedOverviewAndHistoryAre401()
    {
        var workspace = FreshWorkspace();
        var accountId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync(OverviewUrl(workspace, accountId))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync(HistoryUrl(workspace, accountId))).StatusCode);
    }

    [Fact]
    public async Task WorkspaceMemberReadsOwnOverviewWithTruthfulStates()
    {
        var (client, workspace, accountId, providerIdentity) = await ConnectedAccountAsync("overview-own@example.com", FreshWorkspace(), "own");

        // Real zero, ordinary value and NoData must remain distinct (§12).
        fixture.Insights.AccountResult = () => new AccountInsightsResult.Ok(
        [
            MetricObservation.Available(InsightMetricKey.Reach, 1_200),
            MetricObservation.Available(InsightMetricKey.Likes, 0),
            MetricObservation.Unavailable(InsightMetricKey.Comments, MetricAvailability.NoData),
        ]);
        fixture.Media.PageFor = (_, _) => new MediaCatalogPage(
        [
            new MediaCatalogItem("m1", null, MediaKind.Image, "Feed", null, null, null, null, true, false, 10, 3, null),
            new MediaCatalogItem("m2", null, MediaKind.Reel, "Clips", null, null, null, null, true, false, null, 2, null),
        ], null, false);
        fixture.Insights.MediaResult = (mediaId, _) => mediaId == "m1"
            ? new MediaInsightsResult.Ok([MetricObservation.Available(InsightMetricKey.Saves, 5)])
            : new MediaInsightsResult.Ok([MetricObservation.Unavailable(InsightMetricKey.Shares, MetricAvailability.NoData)]);

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        await SeedSnapshotAsync(accountId, today, 10_000, FollowerSnapshotProvenance.Observed);
        await SeedSnapshotAsync(accountId, today.AddDays(-1), 9_900, FollowerSnapshotProvenance.Observed);

        var response = await client.GetAsync(OverviewUrl(workspace, accountId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(accountId.ToString(), payload.GetProperty("accountId").GetString());
        Assert.Equal("Available", payload.GetProperty("analyticsAvailability").GetString());

        // Current followers: directly observed today, with provenance metadata.
        var current = payload.GetProperty("currentFollowers");
        Assert.Equal(10_000, current.GetProperty("value").GetInt64());
        Assert.Equal("Available", current.GetProperty("state").GetString());
        Assert.Equal("Observed", current.GetProperty("provenance").GetString());

        // History newest-first with provenance.
        var history = payload.GetProperty("followerHistory");
        Assert.Equal(2, history.GetArrayLength());
        Assert.Equal(today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), history[0].GetProperty("date").GetString());
        Assert.Equal(9_900, history[1].GetProperty("value").GetInt64());
        Assert.Equal("Observed", history[1].GetProperty("provenance").GetString());

        // Media section: counts and totals with truthfulness flags.
        var media = payload.GetProperty("media");
        Assert.Equal("Available", media.GetProperty("state").GetString());
        Assert.Equal(2, media.GetProperty("count").GetInt32());
        // m2 lacks a like count → the like total is not complete and stays null; comments are complete.
        Assert.Equal(JsonValueKind.Null, media.GetProperty("likeTotal").ValueKind);
        Assert.Equal(JsonValueKind.False, media.GetProperty("likeTotalComplete").ValueKind);
        Assert.Equal(5, media.GetProperty("commentTotal").GetInt64());
        Assert.Equal(JsonValueKind.True, media.GetProperty("commentTotalComplete").ValueKind);

        // Per-media insights: availability per media, never provider DTOs.
        var items = media.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("m1", items[0].GetProperty("mediaId").GetString());
        Assert.Equal(10, items[0].GetProperty("likeCount").GetInt32());
        var m1Metric = items[0].GetProperty("insights")[0];
        Assert.Equal("saves", m1Metric.GetProperty("metric").GetString());
        Assert.Equal("Available", m1Metric.GetProperty("state").GetString());
        Assert.Equal(5, m1Metric.GetProperty("value").GetInt64());
        var m2Metric = items[1].GetProperty("insights")[0];
        Assert.Equal("shares", m2Metric.GetProperty("metric").GetString());
        Assert.Equal("NoData", m2Metric.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, m2Metric.GetProperty("value").ValueKind);

        // Account insights: real zero stays 0, NoData stays null.
        var accountMetrics = payload.GetProperty("accountInsights");
        Assert.Equal("reach", accountMetrics[0].GetProperty("metric").GetString());
        Assert.Equal("Available", accountMetrics[0].GetProperty("state").GetString());
        Assert.Equal(1_200, accountMetrics[0].GetProperty("value").GetInt64());
        Assert.Equal("likes", accountMetrics[1].GetProperty("metric").GetString());
        Assert.Equal("Available", accountMetrics[1].GetProperty("state").GetString());
        Assert.Equal(0, accountMetrics[1].GetProperty("value").GetInt64());
        Assert.Equal("comments", accountMetrics[2].GetProperty("metric").GetString());
        Assert.Equal("NoData", accountMetrics[2].GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, accountMetrics[2].GetProperty("value").ValueKind);

        // Exact account addressed, never a guessed one; tokens never leave the host.
        Assert.Equal(providerIdentity, fixture.Insights.AccountCalls.Single().ProviderAccountId);
        Assert.Equal(providerIdentity, fixture.Media.Calls.Single().ProviderAccountId);
        Assert.Contains("LONG-E2E", fixture.Insights.SeenTokens);
        var raw = payload.GetRawText();
        Assert.DoesNotContain("LONG-E2E", raw);
        Assert.DoesNotContain("SHORT-E2E", raw);
        Assert.DoesNotContain("accessToken", raw);
        Assert.DoesNotContain("paging", raw);
    }

    [Fact]
    public async Task PermissionLossDegradesOnlyAnalyticsAndStopsMediaFanOut()
    {
        var (client, workspace, accountId, _) = await ConnectedAccountAsync("overview-perm@example.com", FreshWorkspace(), "perm");
        fixture.Insights.AccountResult = () => new AccountInsightsResult.Failed(InsightsFailureKind.PermissionLoss, InsightsFailures.PermissionDenied);
        fixture.Media.PageFor = (_, _) => new MediaCatalogPage(
        [
            ScriptedMediaCatalogClient.Item("m1", "Image"),
            ScriptedMediaCatalogClient.Item("m2", "Reel"),
        ], null, false);

        var response = await client.GetAsync(OverviewUrl(workspace, accountId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PermissionRequired", payload.GetProperty("analyticsAvailability").GetString());
        // Media catalog and basic counts survive the analytics permission loss (§60).
        Assert.Equal("Available", payload.GetProperty("media").GetProperty("state").GetString());
        Assert.Equal(2, payload.GetProperty("media").GetProperty("count").GetInt32());
        // Every analytics metric is PermissionRequired — never fabricated values.
        Assert.All(payload.GetProperty("accountInsights").EnumerateArray(),
            m => Assert.Equal("PermissionRequired", m.GetProperty("state").GetString()));
        Assert.All(payload.GetProperty("media").GetProperty("items").EnumerateArray(),
            item => Assert.All(item.GetProperty("insights").EnumerateArray(),
                m => Assert.Equal("PermissionRequired", m.GetProperty("state").GetString())));
        // One account-level failure stops the per-media fan-out: zero media insight calls.
        Assert.Empty(fixture.Insights.MediaCalls);
        Assert.Single(fixture.Insights.AccountCalls);
    }

    [Fact]
    public async Task MediaCatalogSurvivesInsightsFailure()
    {
        var (client, workspace, accountId, _) = await ConnectedAccountAsync("overview-mbase@example.com", FreshWorkspace(), "mb");
        fixture.Insights.AccountResult = () => new AccountInsightsResult.Failed(InsightsFailureKind.PermissionLoss, InsightsFailures.PermissionDenied);
        fixture.Media.PageFor = (_, _) => new MediaCatalogPage(
            [ScriptedMediaCatalogClient.Item("m1", "Image")], null, false);

        var overview = await client.GetAsync(OverviewUrl(workspace, accountId));
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);

        // The M13-006 media catalog endpoint is untouched by analytics degradation.
        var mediaResponse = await client.GetAsync($"/api/v1/workspaces/{workspace}/instagram/connections/{accountId}/media");
        Assert.Equal(HttpStatusCode.OK, mediaResponse.StatusCode);
        var mediaPayload = await mediaResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("m1", mediaPayload.GetProperty("items")[0].GetProperty("mediaId").GetString());
        Assert.Empty(fixture.Insights.MediaCalls);
    }

    [Fact]
    public async Task PartialMediaInsightFailureDegradesOnlyThatMedia()
    {
        var (client, workspace, accountId, _) = await ConnectedAccountAsync("overview-partial@example.com", FreshWorkspace(), "part");
        fixture.Media.PageFor = (_, _) => new MediaCatalogPage(
        [
            ScriptedMediaCatalogClient.Item("good", "Image"),
            ScriptedMediaCatalogClient.Item("slow", "Reel"),
            ScriptedMediaCatalogClient.Item("bad", "Image"),
        ], null, false);
        fixture.Insights.MediaResult = (mediaId, _) => mediaId switch
        {
            "slow" => new MediaInsightsResult.Failed(InsightsFailureKind.RateLimited, InsightsFailures.RateLimited),
            "bad" => new MediaInsightsResult.Failed(InsightsFailureKind.ContractDrift, InsightsFailures.ContractDrift),
            _ => new MediaInsightsResult.Ok([MetricObservation.Available(InsightMetricKey.Saves, 3)]),
        };

        var response = await client.GetAsync(OverviewUrl(workspace, accountId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("media").GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        // Rate-limited media degrades to TemporarilyUnavailable; others survive.
        Assert.Equal("TemporarilyUnavailable", items[1].GetProperty("insights")[0].GetProperty("state").GetString());
        Assert.Equal("NoData", items[2].GetProperty("insights")[0].GetProperty("state").GetString());
        Assert.Equal("Available", items[0].GetProperty("insights")[0].GetProperty("state").GetString());
        Assert.Equal(3, items[0].GetProperty("insights")[0].GetProperty("value").GetInt64());
        Assert.Equal(3, fixture.Insights.MediaCalls.Count);
    }

    [Fact]
    public async Task ForeignWorkspaceOverviewIs404WithZeroTokenReadsAndZeroProviderCalls()
    {
        var workspaceA = FreshWorkspace();
        var workspaceB = FreshWorkspace();
        var (_, _, accountB, _) = await ConnectedAccountAsync("overview-b@example.com", workspaceB, "b");
        var clientA = await AuthedClientAsync("overview-a@example.com", workspaceA);
        var tokenGetsBefore = fixture.Tokens.TokenGets.Count;
        var callsBefore = fixture.Insights.CallCount + fixture.Media.CallCount;

        var response = await clientA.GetAsync(OverviewUrl(workspaceA, accountB));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("account.notFound",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(tokenGetsBefore, fixture.Tokens.TokenGets.Count);
        Assert.DoesNotContain(accountB, fixture.Tokens.TokenGets);
        Assert.Equal(callsBefore, fixture.Insights.CallCount + fixture.Media.CallCount);
    }

    [Fact]
    public async Task UnknownAccountOverviewIs404WithoutProviderCall()
    {
        var (client, workspace, _, _) = await ConnectedAccountAsync("overview-unknown@example.com", FreshWorkspace(), "u");
        var callsBefore = fixture.Insights.CallCount + fixture.Media.CallCount;

        var response = await client.GetAsync(OverviewUrl(workspace, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(callsBefore, fixture.Insights.CallCount + fixture.Media.CallCount);
    }

    [Fact]
    public async Task DisconnectedAccountOverviewFailsLocallyWithZeroProviderCalls()
    {
        var (client, workspace, accountId, _) = await ConnectedAccountAsync("overview-disc@example.com", FreshWorkspace(), "d");
        var disconnected = await client.DeleteAsync(
            $"/api/v1/workspaces/{workspace}/instagram/connections/{accountId}");
        Assert.Equal(HttpStatusCode.NoContent, disconnected.StatusCode);
        var callsBefore = fixture.Insights.CallCount + fixture.Media.CallCount;

        var response = await client.GetAsync(OverviewUrl(workspace, accountId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("account.alreadyDisconnected",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(callsBefore, fixture.Insights.CallCount + fixture.Media.CallCount);
    }

    [Fact]
    public async Task MissingTokenOverviewFailsSafelyWithOneTokenReadAndZeroProviderCalls()
    {
        var (client, workspace, accountId, _) = await ConnectedAccountAsync("overview-notoken@example.com", FreshWorkspace(), "nt");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProtectedTokenStore>().DeleteAsync(accountId);
        }

        var callsBefore = fixture.Insights.CallCount + fixture.Media.CallCount;
        var getsBefore = fixture.Tokens.TokenGets.Count;

        var response = await client.GetAsync(OverviewUrl(workspace, accountId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("account.tokenMissing",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(callsBefore, fixture.Insights.CallCount + fixture.Media.CallCount);
        Assert.Equal(getsBefore + 1, fixture.Tokens.TokenGets.Count);
        Assert.Contains(accountId, fixture.Tokens.TokenGets);
    }

    [Fact]
    public async Task FollowerHistoryIsExactAccountBoundedAndSecretFree()
    {
        var workspace = FreshWorkspace();
        var (client, _, accountA, _) = await ConnectedAccountAsync("overview-hist@example.com", workspace, "h");
        var (_, _, accountB, _) = await ConnectedAccountAsync("overview-hist2@example.com", workspace, "h2");

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        await SeedSnapshotAsync(accountA, today, 10_000, FollowerSnapshotProvenance.Observed);
        await SeedSnapshotAsync(accountA, today.AddDays(-1), 9_900, FollowerSnapshotProvenance.Observed);
        await SeedSnapshotAsync(accountA, today.AddDays(-2), 9_800, FollowerSnapshotProvenance.Backfilled);
        await SeedSnapshotAsync(accountB, today, 42_000, FollowerSnapshotProvenance.Observed);

        // Account A sees exactly its own history, newest first, secret-free.
        var response = await client.GetAsync(HistoryUrl(workspace, accountA));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = payload.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal(today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), items[0].GetProperty("date").GetString());
        Assert.Equal(10_000, items[0].GetProperty("value").GetInt64());
        Assert.Equal("Observed", items[0].GetProperty("provenance").GetString());
        Assert.Equal("Backfilled", items[2].GetProperty("provenance").GetString());
        var raw = payload.GetRawText();
        Assert.DoesNotContain("LONG-E2E", raw);
        Assert.DoesNotContain("SHORT-E2E", raw);
        Assert.DoesNotContain("accessToken", raw);

        // Account B's history never leaks into account A's read (§59).
        var other = await client.GetFromJsonAsync<JsonElement>(HistoryUrl(workspace, accountB));
        Assert.Equal(1, other.GetProperty("items").GetArrayLength());
        Assert.Equal(42_000, other.GetProperty("items")[0].GetProperty("value").GetInt64());

        // Limit bounds are enforced before any query.
        var zero = await client.GetAsync(HistoryUrl(workspace, accountA, "?limit=0"));
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal("followers.invalidLimit",
            (await zero.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var huge = await client.GetAsync(HistoryUrl(workspace, accountA, "?limit=1000"));
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);

        // Foreign workspace: 404 before any history query/token/provider access.
        fixture.Tokens.TokenGets.Clear();
        fixture.Media.Reset();
        fixture.Insights.Reset();
        var foreignWorkspace = FreshWorkspace();
        var clientB = await AuthedClientAsync("overview-hist-foreign@example.com", foreignWorkspace);
        var foreign = await clientB.GetAsync(HistoryUrl(foreignWorkspace, accountA));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("account.notFound",
            (await foreign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Empty(fixture.Tokens.TokenGets);
        Assert.Equal(0, fixture.Media.CallCount);
        Assert.Equal(0, fixture.Insights.CallCount);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

[Collection(ApiTestEnvironment.Name)]
public sealed class IdentityAuthorizationTests(ApiPostgreSqlFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<(string Email, string Token)> RegisterAndLoginAsync(string email)
    {
        var register = await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new
        {
            email,
            displayName = "Integration User",
            password = "correct-horse-battery",
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login", new
        {
            email,
            password = "correct-horse-battery",
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>(Json);
        return (email, payload!.AccessToken);
    }

    [Fact]
    public async Task RegisterLoginAndMeRoundTrip()
    {
        var (email, token) = await RegisterAndLoginAsync("roundtrip@example.com");

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        me.Headers.Authorization = new("Bearer", token);
        using var response = await fixture.Client.SendAsync(me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>(Json);
        Assert.Equal(email, body!.Email);
    }

    [Fact]
    public async Task MeWithoutTokenIsUnauthorized()
    {
        using var response = await fixture.Client.GetAsync("/api/v1/identity/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MeWithGarbageTokenIsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        request.Headers.Authorization = new("Bearer", "not.a.realtoken");
        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginWithWrongPasswordIsUnauthorized()
    {
        await RegisterAndLoginAsync("wrongpw@example.com");

        using var login = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login", new
        {
            email = "wrongpw@example.com",
            password = "totally-wrong-password",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task WorkspaceMembersEndpointEnforcesMembership()
    {
        var (_, ownerToken) = await RegisterAndLoginAsync("owner@example.com");
        var (_, outsiderToken) = await RegisterAndLoginAsync("outsider@example.com");

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/workspaces")
        {
            Content = JsonContent.Create(new { name = "Owner HQ" }),
        };
        create.Headers.Authorization = new("Bearer", ownerToken);
        using var createResponse = await fixture.Client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedWorkspace>(Json);

        // Owner sees the member list.
        using var ownerList = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/workspaces/{created!.WorkspaceId}/members");
        ownerList.Headers.Authorization = new("Bearer", ownerToken);
        using var ownerResponse = await fixture.Client.SendAsync(ownerList);
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        var members = await ownerResponse.Content.ReadFromJsonAsync<MembersResponse>(Json);
        Assert.Single(members!.Members);

        // Outsider is authenticated but not a member: 403.
        using var outsiderList = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/workspaces/{created.WorkspaceId}/members");
        outsiderList.Headers.Authorization = new("Bearer", outsiderToken);
        using var outsiderResponse = await fixture.Client.SendAsync(outsiderList);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderResponse.StatusCode);

        // Unknown workspace id: 404.
        using var missing = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/workspaces/{Guid.CreateVersion7()}/members");
        missing.Headers.Authorization = new("Bearer", ownerToken);
        using var missingResponse = await fixture.Client.SendAsync(missing);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        // No token at all on a workspace route: 401.
        using var anonymous = await fixture.Client.GetAsync($"/api/v1/workspaces/{created.WorkspaceId}/members");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);

    private sealed record MeResponse(string UserId, string Email);

    private sealed record CreatedWorkspace(Guid WorkspaceId, string Name);

    private sealed record MembersResponse(string WorkspaceName, MemberDto[] Members);

    private sealed record MemberDto(string UserId, string Role);
}

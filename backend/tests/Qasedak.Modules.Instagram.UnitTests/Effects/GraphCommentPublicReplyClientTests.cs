using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Effects;

/// <summary>
/// Deterministic contract tests for the public comment reply adapter (M13-009 §77):
/// exact comment-replies edge, POST, message form field, Bearer token, no messaging
/// recipient object, success mapping, provider rejection, cancellation, redaction.
/// </summary>
public sealed class GraphCommentPublicReplyClientTests
{
    private const string AccessToken = "IG-USER-TOKEN-SECRET";
    private const string CommentId = "17895695792288124";

    private static (GraphCommentPublicReplyClient Client, ScriptedHttpHandler Handler) NewClient(
        HttpResponseMessage response,
        string apiVersion = "v26.0")
    {
        var handler = new ScriptedHttpHandler(_ => response);
        var client = new GraphCommentPublicReplyClient(
            new HttpClient(handler),
            Options.Create(new MetaGraphOptions { ApiVersion = apiVersion }));
        return (client, handler);
    }

    [Fact]
    public async Task PostsToTheCommentRepliesEdge()
    {
        var (client, handler) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"17873440459141021"}""", Encoding.UTF8, "application/json"),
        });

        var result = await client.SendPublicReplyAsync(AccessToken, CommentId, "public note", default);

        Assert.True(result.Succeeded);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(new Uri($"https://graph.instagram.com/v26.0/{CommentId}/replies"), handler.LastRequest.RequestUri);
    }

    [Fact]
    public async Task SendsMessageAsFormFieldWithBearerToken()
    {
        var (client, handler) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"reply-1"}""", Encoding.UTF8, "application/json"),
        });

        await client.SendPublicReplyAsync(AccessToken, CommentId, "public note", default);

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal(AccessToken, handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Equal("message=public+note", handler.LastBody);
    }

    [Fact]
    public async Task NoMessagingRecipientObjectIsEverSent()
    {
        var (client, handler) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"reply-1"}""", Encoding.UTF8, "application/json"),
        });

        await client.SendPublicReplyAsync(AccessToken, CommentId, "note", default);

        Assert.DoesNotContain("recipient", handler.LastBody);
        Assert.DoesNotContain("comment_id", handler.LastBody);
    }

    [Fact]
    public async Task TokenNeverAppearsInUrlOrBody()
    {
        var (client, handler) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"reply-1"}""", Encoding.UTF8, "application/json"),
        });

        await client.SendPublicReplyAsync(AccessToken, CommentId, "note", default);

        Assert.DoesNotContain(AccessToken, handler.LastRequest!.RequestUri!.ToString());
        Assert.DoesNotContain(AccessToken, handler.LastBody);
    }

    [Fact]
    public async Task SuccessMapsTheCreatedReplyCommentId()
    {
        var (client, _) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"17873440459141021"}""", Encoding.UTF8, "application/json"),
        });

        var result = await client.SendPublicReplyAsync(AccessToken, CommentId, "note", default);

        Assert.True(result.Succeeded);
        Assert.Equal("17873440459141021", result.ReplyCommentId);
    }

    [Fact]
    public async Task MissingReplyIdIsMalformedSuccess()
    {
        var (client, _) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"success":true}""", Encoding.UTF8, "application/json"),
        });

        var result = await client.SendPublicReplyAsync(AccessToken, CommentId, "note", default);

        Assert.False(result.Succeeded);
        Assert.Equal(PublicReplyFailureReason.MalformedResponse, result.Failure!.Reason);
    }

    [Fact]
    public async Task ProviderRejectionSurfacesWithRedactedDetail()
    {
        var (client, _) = NewClient(new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"message":"cannot reply to hidden comment","code":100}}""", Encoding.UTF8, "application/json"),
        });

        var result = await client.SendPublicReplyAsync(AccessToken, CommentId, "note", default);

        Assert.False(result.Succeeded);
        Assert.Equal(PublicReplyFailureReason.RejectedByMeta, result.Failure!.Reason);
        Assert.DoesNotContain(AccessToken, result.Failure!.Detail);
    }

    [Fact]
    public async Task TransportTimeoutIsTransportFailure()
    {
        var handler = new ScriptedHttpHandler(_ => throw new TaskCanceledException("timeout"));
        var client = new GraphCommentPublicReplyClient(new HttpClient(handler), Options.Create(new MetaGraphOptions()));

        var result = await client.SendPublicReplyAsync(AccessToken, CommentId, "note", default);

        Assert.False(result.Succeeded);
        Assert.Equal(PublicReplyFailureReason.TransportFailure, result.Failure!.Reason);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        var handler = new ScriptedHttpHandler(_ => throw new TaskCanceledException("cancelled"));
        var client = new GraphCommentPublicReplyClient(new HttpClient(handler), Options.Create(new MetaGraphOptions()));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendPublicReplyAsync(AccessToken, CommentId, "note", cts.Token));
    }
}

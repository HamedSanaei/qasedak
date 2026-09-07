using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Messaging;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// M13-010 postback round trip: an outbound postback-button payload is opaque
/// application data (never interpreted by the messaging layer) and must survive the
/// round trip through the current M13-008 messaging_postbacks webhook normalization —
/// the exact contract M13-011 will consume for reveal correlation. No reveal/correlation
/// behavior exists here.
/// </summary>
public sealed class PostbackRoundTripTests
{
    private const string AccessToken = "IGSVCTOKEN-roundtrip";

    private const string AccountId = "17841408147298714";

    [Fact]
    public async Task OutboundPostbackPayloadSurvivesWebhookNormalizationRoundTrip()
    {
        // 1. Serialize a postback-button template exactly as the adapter would send it.
        const string opaquePayload = "opening:automation-1|version-3|run-77|action-0";
        var handler = new ScriptedHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"recipient_id":"user-42","message_id":"m_t1"}"""),
        });
        var client = new GraphInstagramMessagingClient(
            new HttpClient(handler),
            Options.Create(new MetaMessagingOptions()),
            Options.Create(new MetaGraphOptions()));
        var send = await client.SendDirectAsync(
            AccessToken,
            "user-42",
            new InstagramMessageContent.ButtonTemplate(
                "See your answer",
                [new InstagramMessageButton.Postback("Reveal", opaquePayload)]),
            default);
        Assert.True(send.Succeeded);

        var outboundPayload = ExtractPostbackPayload(handler.LastBody!);

        // 2. Feed a representative current Meta messaging_postbacks webhook fixture that
        //    carries the same opaque payload through the M13-008 normalizer.
        var raw =
            "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + AccountId + "\",\"time\":1502905976963,\"messaging\":[" +
            "{\"sender\":{\"id\":\"user-42\"},\"recipient\":{\"id\":\"" + AccountId + "\"},\"timestamp\":1502905976377," +
            "\"postback\":{\"mid\":\"m-pb-rt-1\",\"title\":\"Reveal\",\"payload\":\"" + outboundPayload + "\"}}]}]}";
        var outcome = MetaPayloadNormalizer.Normalize("evt-rt-1", "instagram", raw);

        var postback = Assert.IsType<InstagramPostbackReceived>(Assert.Single(outcome.Entries.SelectMany(e => e.Events)));
        Assert.Equal(AccountId, postback.ProviderAccountId);
        Assert.Equal("user-42", postback.SenderId);
        Assert.Equal("m-pb-rt-1", postback.ProviderMessageId);
        Assert.Equal("Reveal", postback.Title);
        Assert.Equal(outboundPayload, postback.Payload);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1502905976377), postback.OccurredAtUtc);
    }

    private static string ExtractPostbackPayload(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement
            .GetProperty("message").GetProperty("attachment").GetProperty("payload")
            .GetProperty("buttons")[0].GetProperty("payload").GetString()!;
    }
}

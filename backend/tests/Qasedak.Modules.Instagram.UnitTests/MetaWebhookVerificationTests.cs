using System.Text;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Infrastructure.Webhooks;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

public sealed class MetaWebhookVerificationTests
{
    private const string TestAppSecret = "qasedak_test_secret_0123456789abcdef";

    private const string TestVerifyToken = "qasedak_verify_token_7f3a";

    private const string CommentsSignature =
        "sha256=1b3a08424e62c39253caa0d88cd09f542d4c20993102c19299a7e9bafc0a2302";

    // Signed over raw bytes that contain escaped unicode (\u00e4, \u00e5): Meta signs the
    // escaped serialization, so this fixture locks the no-re-serialization contract.
    private const string EscapedUnicodeSignature =
        "sha256=492bba989ef3513b6318a47bf617e7e5100400af2cb45612e1639ebb90d6c7dc";

    [Fact]
    public void ValidSignaturePassesForCommentsPayload()
    {
        var verifier = NewVerifier(TestAppSecret);
        var body = ReadFixture("comments-payload.json");

        var result = verifier.Verify(body, CommentsSignature);

        Assert.True(result.IsValid);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void ValidSignaturePassesForEscapedUnicodePayload()
    {
        var verifier = NewVerifier(TestAppSecret);
        var body = ReadFixture("messages-payload-escaped-unicode.json");

        var result = verifier.Verify(body, EscapedUnicodeSignature);

        Assert.True(result.IsValid);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void TamperedBodyFailsSignatureValidation()
    {
        var verifier = NewVerifier(TestAppSecret);
        var body = ReadFixture("comments-payload.json");
        body[^2] = (byte)(body[^2] == (byte)'?' ? (byte)'!' : (byte)'?');

        var result = verifier.Verify(body, CommentsSignature);

        Assert.False(result.IsValid);
        Assert.Equal(WebhookSignatureFailure.SignatureMismatch, result.Failure);
    }

    [Fact]
    public void WrongSecretFailsSignatureValidation()
    {
        var verifier = NewVerifier("another_app_secret_totally_different");
        var body = ReadFixture("comments-payload.json");

        var result = verifier.Verify(body, CommentsSignature);

        Assert.False(result.IsValid);
        Assert.Equal(WebhookSignatureFailure.SignatureMismatch, result.Failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1b3a08424e62c39253caa0d88cd09f542d4c20993102c19299a7e9bafc0a2302")]
    [InlineData("sha1=1b3a08424e62c39253caa0d88cd09f542d4c20993102c19299a7e9bafc0a2302")]
    [InlineData("sha256=GB3A08424E62C39253CAA0D88CD09F542D4C20993102C19299A7E9BAFC0A2302")]
    [InlineData("sha256=zz3a08424e62c39253caa0d88cd09f542d4c20993102c19299a7e9bafc0a2302")]
    [InlineData("sha256=1b3a")]
    public void MalformedSignatureHeadersAreRejected(string? header)
    {
        var verifier = NewVerifier(TestAppSecret);
        var body = ReadFixture("comments-payload.json");

        var result = verifier.Verify(body, header);

        Assert.False(result.IsValid);
        Assert.Equal(WebhookSignatureFailure.InvalidSignatureHeader, result.Failure);
    }

    [Fact]
    public void SubscriptionHandshakeEchoesChallengeOnMatchingToken()
    {
        var validator = NewValidator();

        var result = validator.Validate("subscribe", TestVerifyToken, "1158201444");

        Assert.True(result.IsValid);
        Assert.Equal("1158201444", result.Challenge);
    }

    [Theory]
    [InlineData("denied", "qasedak_verify_token_7f3a", "1158201444", WebhookSubscriptionFailure.InvalidMode)]
    [InlineData(null, "qasedak_verify_token_7f3a", "1158201444", WebhookSubscriptionFailure.InvalidMode)]
    [InlineData("subscribe", "wrong_token", "1158201444", WebhookSubscriptionFailure.TokenMismatch)]
    [InlineData("subscribe", null, "1158201444", WebhookSubscriptionFailure.TokenMismatch)]
    [InlineData("subscribe", "qasedak_verify_token_7f3a", null, WebhookSubscriptionFailure.MissingChallenge)]
    [InlineData("subscribe", "qasedak_verify_token_7f3a", "", WebhookSubscriptionFailure.MissingChallenge)]
    public void InvalidSubscriptionRequestsAreRejected(
        string? mode,
        string? verifyToken,
        string? challenge,
        WebhookSubscriptionFailure expectedFailure)
    {
        var validator = NewValidator();

        var result = validator.Validate(mode, verifyToken, challenge);

        Assert.False(result.IsValid);
        Assert.Equal(expectedFailure, result.Failure);
        Assert.Equal(string.Empty, result.Challenge);
    }

    [Fact]
    public void UnconfiguredVerifyTokenRejectsEveryRequest()
    {
        var validator = new MetaWebhookSubscriptionValidator(
            Options.Create(new MetaWebhookOptions { AppSecret = TestAppSecret, VerifyToken = "" }));

        var result = validator.Validate("subscribe", TestVerifyToken, "1158201444");

        Assert.False(result.IsValid);
        Assert.Equal(WebhookSubscriptionFailure.TokenMismatch, result.Failure);
    }

    private static HmacWebhookSignatureVerifier NewVerifier(string appSecret) =>
        new(Options.Create(new MetaWebhookOptions { AppSecret = appSecret, VerifyToken = TestVerifyToken }));

    private static MetaWebhookSubscriptionValidator NewValidator() =>
        new(Options.Create(new MetaWebhookOptions { AppSecret = TestAppSecret, VerifyToken = TestVerifyToken }));

    private static byte[] ReadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "webhook", fileName);
        return File.ReadAllBytes(path);
    }
}

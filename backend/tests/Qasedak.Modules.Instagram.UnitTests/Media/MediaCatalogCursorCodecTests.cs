using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Infrastructure.Media;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Media;

/// <summary>
/// Cursor envelope validation (M13-006): account binding, versioning, length
/// bounds, malformed input and the never-a-URL invariant. No live Meta calls.
/// </summary>
public sealed class MediaCatalogCursorCodecTests
{
    private static readonly Guid AccountA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid AccountB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly MediaCatalogCursorCodec _codec = new();

    [Fact]
    public void NullCursorIsAValidStart()
    {
        var result = _codec.Decode(null, AccountA);

        Assert.True(result.Valid);
        Assert.Null(result.ProviderAfterCursor);
    }

    [Fact]
    public void EncodeDecodeRoundTripsForSameAccount()
    {
        var encoded = _codec.Encode(AccountA, "provider-after-token");

        var result = _codec.Decode(encoded, AccountA);

        Assert.True(result.Valid);
        Assert.Equal("provider-after-token", result.ProviderAfterCursor);
    }

    [Fact]
    public void CursorIssuedForAccountAIsRejectedForAccountB()
    {
        var encoded = _codec.Encode(AccountA, "provider-after-token");

        Assert.False(_codec.Decode(encoded, AccountB).Valid);
    }

    [Fact]
    public void EmptyAndWhitespaceCursorsAreInvalid()
    {
        Assert.False(_codec.Decode("", AccountA).Valid);
        Assert.False(_codec.Decode("   ", AccountA).Valid);
    }

    [Fact]
    public void OversizedCursorIsInvalid()
    {
        var oversized = new string('x', MediaCatalogPolicy.MaxEncodedCursorLength + 1);

        Assert.False(_codec.Decode(oversized, AccountA).Valid);
    }

    [Fact]
    public void MalformedBase64IsInvalid()
    {
        Assert.False(_codec.Decode("!!!not-base64url!!!", AccountA).Valid);
    }

    [Fact]
    public void UnknownCursorVersionIsInvalid()
    {
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { v = 99, a = AccountA, c = "after" });
        var encoded = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(payload);

        Assert.False(_codec.Decode(encoded, AccountA).Valid);
    }

    [Fact]
    public void UrlShapedProviderComponentIsInvalid()
    {
        var encoded = _codec.Encode(AccountA, "https://evil.example/steal?token=x");

        Assert.False(_codec.Decode(encoded, AccountA).Valid);
    }

    [Fact]
    public void ProviderComponentWithUrlSchemeEmbeddedIsInvalid()
    {
        var encoded = _codec.Encode(AccountA, "abc://host/path");

        Assert.False(_codec.Decode(encoded, AccountA).Valid);
    }

    [Fact]
    public void OversizedProviderComponentIsInvalid()
    {
        var encoded = _codec.Encode(AccountA, new string('c', MediaCatalogPolicy.MaxProviderCursorLength + 1));

        Assert.False(_codec.Decode(encoded, AccountA).Valid);
    }

    [Fact]
    public void TokenMaterialNeverAppearsInEncodedCursor()
    {
        const string token = "SUPER-SECRET-TOKEN";
        var encoded = _codec.Encode(AccountA, "after");

        Assert.DoesNotContain(token, encoded);
        Assert.DoesNotContain("after", encoded); // opaque: provider component is not readable
    }

    [Fact]
    public void RandomLookingPayloadWithoutEnvelopeIsInvalid()
    {
        var encoded = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            "not a json envelope"u8.ToArray());

        Assert.False(_codec.Decode(encoded, AccountA).Valid);
    }
}

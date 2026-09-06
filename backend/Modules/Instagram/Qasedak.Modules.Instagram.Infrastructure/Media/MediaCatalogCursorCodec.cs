using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Qasedak.Modules.Instagram.Application.Media;

namespace Qasedak.Modules.Instagram.Infrastructure.Media;

/// <summary>
/// Opaque Qasedak cursor envelope (M13-006). A cursor is a versioned, base64url
/// JSON envelope bound to the ConnectedAccount that issued it; it carries only
/// the opaque provider cursor component. A client cursor is never interpreted as
/// a URL, never overrides host/path/version, and decoding with a different
/// expected account fails. No token material ever enters the envelope.
/// </summary>
public sealed class MediaCatalogCursorCodec : IMediaCursorCodec
{
    public string Encode(Guid accountId, string providerAfterCursor) =>
        WebEncoders.Base64UrlEncode(
            JsonSerializer.SerializeToUtf8Bytes(new CursorEnvelope(MediaCatalogPolicy.CursorVersion, accountId, providerAfterCursor)));

    public MediaCursorDecodeResult Decode(string? cursor, Guid expectedAccountId)
    {
        if (cursor is null)
        {
            // Absent cursor means "start at the most recent media" — valid.
            return MediaCursorDecodeResult.Ok(providerAfterCursor: null);
        }

        if (cursor.Length == 0 || cursor.Length > MediaCatalogPolicy.MaxEncodedCursorLength)
        {
            return MediaCursorDecodeResult.Invalid();
        }

        byte[] bytes;
        try
        {
            bytes = WebEncoders.Base64UrlDecode(cursor);
        }
        catch (FormatException)
        {
            return MediaCursorDecodeResult.Invalid();
        }

        if (bytes.Length > 4096)
        {
            return MediaCursorDecodeResult.Invalid();
        }

        CursorEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CursorEnvelope>(bytes)
                ?? throw new JsonException("null envelope");
        }
        catch (JsonException)
        {
            return MediaCursorDecodeResult.Invalid();
        }

        if (envelope.Version != MediaCatalogPolicy.CursorVersion
            || envelope.AccountId != expectedAccountId
            || string.IsNullOrWhiteSpace(envelope.ProviderAfter)
            || envelope.ProviderAfter.Length > MediaCatalogPolicy.MaxProviderCursorLength
            || LooksLikeUrl(envelope.ProviderAfter))
        {
            return MediaCursorDecodeResult.Invalid();
        }

        return MediaCursorDecodeResult.Ok(envelope.ProviderAfter);
    }

    /// <summary>Defense in depth: the provider cursor component is never a URL.</summary>
    private static bool LooksLikeUrl(string value) =>
        value.Contains("://", StringComparison.Ordinal)
        || value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("//", StringComparison.Ordinal);

    private sealed record CursorEnvelope(int Version, Guid AccountId, string ProviderAfter);
}

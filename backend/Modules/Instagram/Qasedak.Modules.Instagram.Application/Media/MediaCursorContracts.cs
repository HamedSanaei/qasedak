namespace Qasedak.Modules.Instagram.Application.Media;

/// <summary>
/// Decoded, validated Qasedak cursor envelope (M13-006). A cursor is logically
/// bound to the ConnectedAccount that issued it: decoding with a different
/// expected account fails. The payload carries only the opaque provider cursor
/// component — never a URL, never token material.
/// </summary>
public sealed record MediaCatalogCursorData(Guid AccountId, string ProviderAfterCursor);

/// <summary>Outcome of cursor decoding; failure codes are stable (<see cref="MediaCatalogFailures"/>).</summary>
public sealed record MediaCursorDecodeResult(bool Valid, string? ProviderAfterCursor)
{
    /// <summary>Null provider cursor means "start at the most recent media".</summary>
    public static MediaCursorDecodeResult Ok(string? providerAfterCursor) => new(true, providerAfterCursor);

    public static MediaCursorDecodeResult Invalid() => new(false, null);
}

/// <summary>
/// Encode/decode for the opaque Qasedak media cursor. Implementations must:
/// bound lengths, reject malformed/oversized/unknown-version input without
/// throwing parser exceptions, enforce the expected-account binding and never
/// interpret a client cursor as a URL or query source.
/// </summary>
public interface IMediaCursorCodec
{
    string Encode(Guid accountId, string providerAfterCursor);

    MediaCursorDecodeResult Decode(string? cursor, Guid expectedAccountId);
}

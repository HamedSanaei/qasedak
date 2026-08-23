using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Accounts;

namespace Qasedak.Modules.Instagram.Infrastructure.Protection;

/// <summary>Token-protection configuration, bound from "Instagram:Protection".</summary>
public sealed class TokenProtectionOptions
{
    public const string SectionName = "Instagram:Protection";

    /// <summary>
    /// Base64-encoded 256-bit key. Injected at runtime per deployment secret policy;
    /// never committed or baked into images.
    /// </summary>
    public string KeyBase64 { get; set; } = string.Empty;
}

/// <summary>
/// AES-GCM (256-bit key, random 96-bit nonce, 128-bit tag) authenticated encryption.
/// Blob format: base64(nonce || ciphertext || tag). Configuration is resolved per use so
/// an unconfigured host boots; first protect/unprotect call fails loudly.
/// </summary>
public sealed class AesGcmTokenProtector(IOptions<TokenProtectionOptions> options) : ITokenProtector
{
    private const int NonceSize = 12;

    private const int TagSize = 16;

    public string Protect(string plaintext)
    {
        var key = ResolveKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var blob = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, blob, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, blob, NonceSize + ciphertext.Length, TagSize);
        return Convert.ToBase64String(blob);
    }

    public string Unprotect(string ciphertext)
    {
        var key = ResolveKey();
        var blob = Convert.FromBase64String(ciphertext);
        if (blob.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Protected blob is truncated.");
        }

        var nonce = blob[..NonceSize];
        var tag = blob[^TagSize..];
        var encryptedBytes = blob[NonceSize..^TagSize];
        var plaintextBytes = new byte[encryptedBytes.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, encryptedBytes, tag, plaintextBytes);
        return Encoding.UTF8.GetString(plaintextBytes);
    }

    private byte[] ResolveKey()
    {
        if (string.IsNullOrWhiteSpace(options.Value.KeyBase64))
        {
            throw new InvalidOperationException("Instagram:Protection:KeyBase64 must be configured.");
        }

        var key = Convert.FromBase64String(options.Value.KeyBase64);
        if (key.Length != 32)
        {
            throw new InvalidOperationException("Instagram:Protection:KeyBase64 must decode to exactly 32 bytes.");
        }

        return key;
    }
}

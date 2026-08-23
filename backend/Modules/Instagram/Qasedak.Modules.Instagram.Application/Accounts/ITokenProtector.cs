namespace Qasedak.Modules.Instagram.Application.Accounts;

/// <summary>
/// Symmetric protection boundary for token material at rest. Implementations must provide
/// authenticated encryption (e.g. AES-GCM) with a runtime-injected key; keys are never
/// stored in the repository, images, or configuration files checked in.
/// </summary>
public interface ITokenProtector
{
    /// <summary>Encrypts plaintext into an opaque, self-describing ciphertext blob.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts a blob produced by <see cref="Protect"/>. Throws on tamper.</summary>
    string Unprotect(string ciphertext);
}

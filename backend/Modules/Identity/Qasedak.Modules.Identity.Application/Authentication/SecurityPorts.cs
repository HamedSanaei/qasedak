using Qasedak.Modules.Identity.Domain.Users;

namespace Qasedak.Modules.Identity.Application.Authentication;

/// <summary>
/// Password hashing contract. Hashes are self-describing (algorithm + parameters embedded)
/// so verification stays possible as parameters evolve.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password for storage. Never returns the input.</summary>
    string Hash(string password);

    /// <summary>Verifies a plaintext password against a stored hash in constant time.</summary>
    bool Verify(string password, string storedHash);
}

/// <summary>Issues and validates opaque signed security tokens for authenticated users.</summary>
public interface ISecurityTokenIssuer
{
    /// <summary>Issues a signed token bound to the user identity.</summary>
    SecurityToken Issue(Guid userId, EmailAddress email);

    /// <summary>
    /// Validates a token's signature and lifetime. Returns the bound identity on success;
    /// never throws for malformed input.
    /// </summary>
    TokenValidationResult Validate(string token);
}

/// <summary>A freshly issued security token.</summary>
public readonly record struct SecurityToken(string Value, DateTimeOffset ExpiresAtUtc);

/// <summary>Outcome of validating a presented token.</summary>
public readonly record struct TokenValidationResult(bool IsValid, Guid UserId, string Email);

namespace Qasedak.Modules.Instagram.Application.Accounts;

/// <summary>
/// Qasedak-owned professional profile snapshot. Only officially verified fields;
/// never tokens, metrics or history (M13-007 owns analytics).
/// </summary>
public sealed record InstagramAccountProfile(
    string ProviderAccountId,
    string Username,
    string? DisplayName,
    string? ProfilePictureUrl,
    string? AccountType);

/// <summary>Outcome of one profile fetch against the verified identity endpoint.</summary>
public abstract record AccountProfileOutcome
{
    /// <summary>Identity proven and profile mapped.</summary>
    public sealed record Ok(InstagramAccountProfile Profile) : AccountProfileOutcome;

    /// <summary>
    /// Hard identity failure: Meta's proven account id differs from the expected
    /// routing identity. Callers must fail closed, never rebind silently.
    /// </summary>
    public sealed record IdentityMismatch(string ExpectedAccountId, string ActualAccountId) : AccountProfileOutcome;

    /// <summary>Provider/transport failure with a stable log-safe code.</summary>
    public sealed record Unavailable(string FailureCode, bool Transient) : AccountProfileOutcome;
}

/// <summary>
/// Port to the verified professional-profile endpoint. Implementations use the
/// M13-003 Graph transport, map into Qasedak-owned records and never leak Graph
/// DTOs or tokens.
///
/// Degradation boundary: identity fields (provider account id, username) are
/// required — without them the outcome is Unavailable/IdentityMismatch and the
/// caller must not persist anything. Optional display fields (display name,
/// avatar URL, account type) tolerate absence: the profile still maps and the
/// connection succeeds with partial metadata rather than destroying a valid
/// token/account over a missing avatar.
/// </summary>
public interface IAccountProfileClient
{
    /// <summary>
    /// Fetches the professional profile for the token's own account and checks it
    /// against <paramref name="expectedProviderAccountId"/>.
    /// </summary>
    Task<AccountProfileOutcome> GetProfileAsync(
        string accessToken, string expectedProviderAccountId, CancellationToken cancellationToken = default);
}

/// <summary>Stable failure codes for profile enrichment.</summary>
public static class ProfileFailures
{
    public const string IdentityMismatch = "profile.identityMismatch";

    public const string Unavailable = "profile.unavailable";
}

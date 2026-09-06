namespace Qasedak.Modules.Instagram.Application.Accounts;
/// <summary>
/// Central token-refresh scheduling policy (M13-005), derived from the current
/// official contract: long-lived tokens last ~60 days, refresh requires a token
/// at least 24h old that is still valid with basic granted. Qasedak refreshes
/// comfortably before expiry and never schedules past it as a happy path.
/// </summary>
public static class TokenRefreshPolicy
{
    /// <summary>Stable scheduled-work type for Instagram token refresh.</summary>
    public const string JobType = "instagram.token-refresh";

    /// <summary>Refresh when this much validity remains (comfortably before expiry).</summary>
    public static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromDays(7);

    /// <summary>Provider minimum token age for refresh eligibility.</summary>
    public static readonly TimeSpan MinimumTokenAge = TimeSpan.FromHours(24);

    /// <summary>
    /// Attempt budget for refresh occurrences. Mirrors the scheduler default by
    /// intent; declared here so the enqueue site owns its retry contract.
    /// </summary>
    public const int DefaultMaxAttempts = 8;

    /// <summary>
    /// Idempotency key binding one refresh occurrence to one token generation:
    /// retries of the same occurrence dedupe, while the next rotation (new expiry)
    /// schedules a distinct future occurrence.
    /// </summary>
    public static string IdempotencyKey(Guid connectedAccountId, DateTimeOffset tokenExpiresAtUtc) =>
        $"instagram-token-refresh:{connectedAccountId:D}:{tokenExpiresAtUtc.UtcTicks}";

    /// <summary>Next due time for a token expiring at the given instant.</summary>
    public static DateTimeOffset NextDueAt(DateTimeOffset tokenExpiresAtUtc, DateTimeOffset now)
    {
        var due = tokenExpiresAtUtc.Subtract(RefreshBeforeExpiry);
        return due <= now ? now : due;
    }

    /// <summary>Whether a token should have a refresh occurrence outstanding.</summary>
    public static bool NeedsRefresh(DateTimeOffset tokenExpiresAtUtc, DateTimeOffset now) =>
        tokenExpiresAtUtc.Subtract(now) <= RefreshBeforeExpiry;

    /// <summary>
    /// Whether the provider age rule is satisfied. Unknown issuance (legacy rows)
    /// counts as satisfied: such tokens long predate any refresh window.
    /// </summary>
    public static bool SatisfiesAgeRule(DateTimeOffset? lastIssuedAtUtc, DateTimeOffset now) =>
        lastIssuedAtUtc is null || now.Subtract(lastIssuedAtUtc.Value) >= MinimumTokenAge;

    /// <summary>
    /// Refresh job payload: identifiers only, never token material. Versioned
    /// alongside the handler that reads it.
    /// </summary>
    public static string RefreshPayload(Guid connectedAccountId) =>
        System.Text.Json.JsonSerializer.Serialize(new RefreshPayloadBody(connectedAccountId));

    /// <summary>Extracts the account id from a refresh payload; null when malformed.</summary>
    public static Guid? ParseRefreshPayload(string payloadJson)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("connectedAccountId", out var id)
                && Guid.TryParse(id.GetString(), out var accountId)
                && accountId != Guid.Empty)
            {
                return accountId;
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return null;
    }

    private sealed record RefreshPayloadBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("connectedAccountId")] Guid ConnectedAccountId);
}

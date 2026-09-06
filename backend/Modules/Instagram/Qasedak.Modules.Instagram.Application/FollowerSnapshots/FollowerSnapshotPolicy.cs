using System.Text.Json;

namespace Qasedak.Modules.Instagram.Application.FollowerSnapshots;

/// <summary>
/// Central daily follower-snapshot scheduling policy (M13-007). UTC day semantics are
/// defined here — <see cref="UtcDay"/> derives the snapshot key from the injected
/// clock's UTC instant, never from the server/local timezone. Idempotency keys are
/// occurrence-specific (account + UTC day) so M13-004 dedupe never blocks future days.
/// </summary>
public static class FollowerSnapshotPolicy
{
    /// <summary>Stable scheduled-work type for the daily follower snapshot.</summary>
    public const string JobType = "instagram.follower-snapshot";

    /// <summary>Attempt budget per occurrence (mirrors the scheduler default).</summary>
    public const int DefaultMaxAttempts = 8;

    /// <summary>Fixed UTC hour when chained daily occurrences become due.</summary>
    private const int DailyDueHourUtc = 1;

    /// <summary>
    /// The UTC calendar day a snapshot at <paramref name="now"/> belongs to. Central
    /// definition: the snapshot key must not move with container/server timezones.
    /// </summary>
    public static DateOnly UtcDay(DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);

    /// <summary>
    /// Occurrence-specific idempotency key: one logical job per account/UTC day.
    /// Deliberately NOT account-only, which would block every future day under the
    /// M13-004 one-key-one-job semantics.
    /// </summary>
    public static string IdempotencyKey(Guid connectedAccountId, DateOnly snapshotDateUtc) =>
        $"instagram-follower-snapshot:{connectedAccountId:D}:{snapshotDateUtc:yyyy-MM-dd}";

    /// <summary>
    /// Daily job payload: identifiers only (ConnectedAccountId + snapshot UTC day).
    /// Never token material, ciphertext, username or profile payload — the handler
    /// resolves the protected token at execution time.
    /// </summary>
    public static string Payload(Guid connectedAccountId, DateOnly snapshotDateUtc) =>
        JsonSerializer.Serialize(new FollowerSnapshotPayloadBody(connectedAccountId, snapshotDateUtc));

    /// <summary>Extracts (accountId, snapshot UTC day); null when malformed.</summary>
    public static (Guid AccountId, DateOnly SnapshotDateUtc)? ParsePayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("connectedAccountId", out var id)
                && Guid.TryParse(id.GetString(), out var accountId)
                && accountId != Guid.Empty
                && root.TryGetProperty("snapshotDateUtc", out var date)
                && DateOnly.TryParse(date.GetString(), out var snapshotDateUtc))
            {
                return (accountId, snapshotDateUtc);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>
    /// Due instant for the occurrence covering <paramref name="snapshotDayUtc"/>: the
    /// next UTC day at 01:00 UTC (provider data is delayed up to 48h; a fixed early
    /// hour keeps one snapshot per day without tight loops).
    /// </summary>
    public static DateTimeOffset NextOccurrenceDueAt(DateOnly snapshotDayUtc) =>
        new(snapshotDayUtc.AddDays(1).ToDateTime(new TimeOnly(DailyDueHourUtc, 0), DateTimeKind.Utc));

    private sealed record FollowerSnapshotPayloadBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("connectedAccountId")] Guid ConnectedAccountId,
        [property: System.Text.Json.Serialization.JsonPropertyName("snapshotDateUtc")] DateOnly SnapshotDateUtc);
}

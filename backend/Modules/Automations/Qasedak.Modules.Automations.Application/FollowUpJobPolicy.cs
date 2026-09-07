using System.Text.Json;

namespace Qasedak.Modules.Automations.Application;

/// <summary>
/// Scheduled-work contract for durable delayed follow-ups (M13-012 §42-44): the payload
/// carries identifiers only — the run id and action index — never tokens, message text,
/// raw trigger bodies or usernames. The message content is resolved at due time from the
/// frozen automation version pinned by the run, so an unpublished/revised automation can
/// never change what was scheduled.
/// </summary>
public static class FollowUpJobPolicy
{
    public const string WorkType = "automations.followUp";

    public const string IdempotencyPrefix = "automations.followUp";

    public const int PayloadVersion = 1;

    public const int DefaultMaxAttempts = 8;

    public static string IdempotencyKey(Guid runId, int actionIndex) => $"{IdempotencyPrefix}|{runId}|{actionIndex}";

    public static string Payload(Guid runId, int actionIndex) =>
        JsonSerializer.Serialize(new FollowUpJobPayload(runId, actionIndex));

    /// <summary>Returns the parsed payload, or null when it is malformed/out of bounds.</summary>
    public static FollowUpJobPayload? Parse(string payloadJson)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<FollowUpJobPayload>(payloadJson);
            if (payload is null || payload.RunId == Guid.Empty || payload.ActionIndex < 0 || payload.ActionIndex > 15)
            {
                return null;
            }

            return payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public sealed record FollowUpJobPayload(Guid RunId, int ActionIndex);
}

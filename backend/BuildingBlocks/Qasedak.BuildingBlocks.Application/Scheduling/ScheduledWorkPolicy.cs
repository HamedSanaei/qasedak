namespace Qasedak.BuildingBlocks.Application.Scheduling;

/// <summary>Backoff and runtime policy for durable scheduled work (M13-004).</summary>
public sealed class ScheduledWorkOptions
{
    public const string SectionName = "Platform:ScheduledWork";

    /// <summary>How often the dispatcher polls for due records.</summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>Max records claimed per poll cycle.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>How long one claim protects a record before another worker may reclaim it.</summary>
    public int LeaseSeconds { get; set; } = 300;

    /// <summary>Default attempt budget for records enqueued without an explicit one.</summary>
    public int MaxAttemptsDefault { get; set; } = 8;

    /// <summary>First retry delay; doubles per attempt up to <see cref="BackoffMaxSeconds"/>.</summary>
    public int BackoffBaseSeconds { get; set; } = 30;

    /// <summary>Upper bound for any computed retry delay.</summary>
    public int BackoffMaxSeconds { get; set; } = 3600;
}

/// <summary>Deterministic bounded exponential backoff (no jitter: same inputs, same delay).</summary>
public static class ScheduledWorkBackoff
{
    public static DateTimeOffset NextAttemptAt(DateTimeOffset now, int attemptNumber, int baseSeconds, int maxSeconds)
    {
        var shift = Math.Clamp(attemptNumber - 1, 0, 20);
        var delaySeconds = Math.Min((long)baseSeconds << shift, maxSeconds);
        return now.AddSeconds(Math.Max(1, delaySeconds));
    }
}

/// <summary>
/// Defense-in-depth scan rejecting token-shaped material from durable payloads.
/// Payloads are caller-owned JSON; this guard cannot prove absence of secrets, it only
/// rejects the known Meta/credential shapes. Real protection is structural: handlers
/// resolve protected tokens at execution time and never persist them.
/// </summary>
public static class ScheduledWorkPayloadGuard
{
    public static void ThrowIfSuspicious(string payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson))
        {
            return;
        }

        if (payloadJson.Contains("EAAC", StringComparison.Ordinal)
            || payloadJson.Contains("client_secret", StringComparison.OrdinalIgnoreCase)
            || payloadJson.Contains("access_token", StringComparison.OrdinalIgnoreCase)
            || payloadJson.Contains("IGAA", StringComparison.Ordinal))
        {
            throw new ScheduledWorkException(
                ScheduledWorkFailures.SecretMaterial,
                "Scheduled-work payloads must never contain token or secret material.");
        }
    }
}

using Qasedak.BuildingBlocks.Application.Scheduling;
using Xunit;

namespace Qasedak.BuildingBlocks.UnitTests;

/// <summary>Scheduler policy: deterministic backoff and payload secret screening.</summary>
public sealed class ScheduledWorkPolicyTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(6, 960)]
    public void BackoffDoublesPerAttempt(int attempt, int expectedSeconds)
    {
        var now = new DateTimeOffset(2026, 9, 5, 6, 0, 0, TimeSpan.Zero);

        Assert.Equal(now.AddSeconds(expectedSeconds), ScheduledWorkBackoff.NextAttemptAt(now, attempt, 30, 3600));
    }

    [Fact]
    public void BackoffIsCapped()
    {
        var now = new DateTimeOffset(2026, 9, 5, 6, 0, 0, TimeSpan.Zero);

        Assert.Equal(now.AddSeconds(3600), ScheduledWorkBackoff.NextAttemptAt(now, 40, 30, 3600));
        Assert.Equal(now.AddSeconds(3600), ScheduledWorkBackoff.NextAttemptAt(now, 8, 3600, 3600));
    }

    [Theory]
    [InlineData("""{"job":"token-refresh","account":"c0ffee"}""")]
    [InlineData("""{"next":"2026-09-05T07:00:00Z"}""")]
    [InlineData("")]
    public void BenignPayloadsPassTheGuard(string payload)
    {
        var exception = Record.Exception(() => ScheduledWorkPayloadGuard.ThrowIfSuspicious(payload));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("""{"access_token":"EAACEdEose0secret"}""")]
    [InlineData("""{"client_secret":"shhh"}""")]
    [InlineData("""{"x":"access_token=abc"}""")]
    [InlineData("""{"x":"IGAAsecret"}""")]
    public void TokenShapedPayloadsAreRejectedWithStableCode(string payload)
    {
        var exception = Assert.Throws<ScheduledWorkException>(
            () => ScheduledWorkPayloadGuard.ThrowIfSuspicious(payload));

        Assert.Equal(ScheduledWorkFailures.SecretMaterial, exception.Code);
    }
}

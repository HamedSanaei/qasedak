using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.FollowerSnapshots;

/// <summary>
/// M13-007: follower-snapshot scheduling policy — UTC day semantics, occurrence-
/// specific idempotency keys, identifiers-only payloads and deterministic due times.
/// </summary>
public sealed class FollowerSnapshotPolicyTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public void UtcDayIsDerivedFromTheUtcInstantNotLocalTime()
    {
        // 2026-09-06 23:30 UTC is 2026-09-07 in UTC+02: the key must stay 09-06.
        var lateEveningUtc = new DateTimeOffset(2026, 9, 6, 23, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 9, 6), FollowerSnapshotPolicy.UtcDay(lateEveningUtc));

        var earlyMorningUtc = new DateTimeOffset(2026, 9, 6, 0, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 9, 6), FollowerSnapshotPolicy.UtcDay(earlyMorningUtc));
    }

    [Fact]
    public void IdempotencyKeyIsOccurrenceSpecificPerAccountAndDay()
    {
        var dayA = new DateOnly(2026, 9, 6);
        var dayB = new DateOnly(2026, 9, 7);
        var other = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

        Assert.Equal(
            $"instagram-follower-snapshot:{AccountId:D}:2026-09-06",
            FollowerSnapshotPolicy.IdempotencyKey(AccountId, dayA));
        // Same account, different days → distinct keys (future days never blocked).
        Assert.NotEqual(FollowerSnapshotPolicy.IdempotencyKey(AccountId, dayA), FollowerSnapshotPolicy.IdempotencyKey(AccountId, dayB));
        // Different accounts, same day → distinct keys.
        Assert.NotEqual(FollowerSnapshotPolicy.IdempotencyKey(AccountId, dayA), FollowerSnapshotPolicy.IdempotencyKey(other, dayA));
    }

    [Fact]
    public void PayloadCarriesIdentifiersOnlyAndRoundTrips()
    {
        var payload = FollowerSnapshotPolicy.Payload(AccountId, new DateOnly(2026, 9, 6));

        Assert.DoesNotContain("token", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EAAC", payload);
        Assert.DoesNotContain("IGAA", payload);
        Assert.DoesNotContain("username", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((AccountId, new DateOnly(2026, 9, 6)), FollowerSnapshotPolicy.ParsePayload(payload));
    }

    [Theory]
    [InlineData("""{"connectedAccountId":"not-a-guid","snapshotDateUtc":"2026-09-06"}""")]
    [InlineData("""{"snapshotDateUtc":"2026-09-06"}""")]
    [InlineData("""{"connectedAccountId":"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"}""")]
    [InlineData("""{"connectedAccountId":"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa","snapshotDateUtc":"not-a-date"}""")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void MalformedPayloadsParseToNull(string payload)
    {
        Assert.Null(FollowerSnapshotPolicy.ParsePayload(payload));
    }

    [Fact]
    public void NextOccurrenceIsDueNextUtcDayAt0100()
    {
        var due = FollowerSnapshotPolicy.NextOccurrenceDueAt(new DateOnly(2026, 9, 6));

        Assert.Equal(new DateTimeOffset(2026, 9, 7, 1, 0, 0, TimeSpan.Zero), due);
    }
}

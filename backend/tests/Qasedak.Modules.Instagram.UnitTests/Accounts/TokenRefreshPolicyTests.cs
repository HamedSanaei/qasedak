using Qasedak.Modules.Instagram.Application.Accounts;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Accounts;

/// <summary>
/// M13-005: refresh scheduling policy — window/age rules, per-generation
/// idempotency keys and secret-free payload roundtrip.
/// </summary>
public sealed class TokenRefreshPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void JobTypeIsStable()
    {
        Assert.Equal("instagram.token-refresh", TokenRefreshPolicy.JobType);
    }

    [Fact]
    public void DueDateSitsInsideTheValidWindow()
    {
        var expiry = Now.AddDays(60);

        Assert.Equal(expiry.AddDays(-7), TokenRefreshPolicy.NextDueAt(expiry, Now));
    }

    [Fact]
    public void AlreadyDueTokensScheduleImmediately()
    {
        Assert.Equal(Now, TokenRefreshPolicy.NextDueAt(Now.AddDays(3), Now));
    }

    [Fact]
    public void NeedsRefreshMirrorsTheWindow()
    {
        Assert.False(TokenRefreshPolicy.NeedsRefresh(Now.AddDays(8), Now));
        Assert.True(TokenRefreshPolicy.NeedsRefresh(Now.AddDays(7), Now));
        Assert.True(TokenRefreshPolicy.NeedsRefresh(Now.AddDays(-1), Now));
    }

    [Fact]
    public void MinimumAgeRuleRejectsFreshTokens()
    {
        Assert.False(TokenRefreshPolicy.SatisfiesAgeRule(Now.AddHours(-1), Now));
        Assert.True(TokenRefreshPolicy.SatisfiesAgeRule(Now.AddHours(-24), Now));
        // Legacy rows without issuance metadata count as eligible.
        Assert.True(TokenRefreshPolicy.SatisfiesAgeRule(null, Now));
    }

    [Fact]
    public void IdempotencyKeyBindsOneOccurrenceToOneTokenGeneration()
    {
        var accountId = Guid.CreateVersion7();
        var firstExpiry = Now.AddDays(60);
        var secondExpiry = Now.AddDays(120);

        var first = TokenRefreshPolicy.IdempotencyKey(accountId, firstExpiry);
        Assert.Equal(first, TokenRefreshPolicy.IdempotencyKey(accountId, firstExpiry));
        // A completed occurrence never blocks the next generation.
        Assert.NotEqual(first, TokenRefreshPolicy.IdempotencyKey(accountId, secondExpiry));
        Assert.NotEqual(first, TokenRefreshPolicy.IdempotencyKey(Guid.CreateVersion7(), firstExpiry));
        Assert.DoesNotContain(" ", first);
    }

    [Fact]
    public void PayloadRoundTripsTheAccountIdAndNothingElse()
    {
        var accountId = Guid.CreateVersion7();

        var payload = TokenRefreshPolicy.RefreshPayload(accountId);

        Assert.Equal(accountId, TokenRefreshPolicy.ParseRefreshPayload(payload));
        Assert.DoesNotContain("accessToken", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"connectedAccountId":"not-a-guid"}""")]
    [InlineData("""{"connectedAccountId":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("""{"other":1}""")]
    [InlineData("not-json")]
    public void MalformedPayloadsParseToNull(string payload)
    {
        Assert.Null(TokenRefreshPolicy.ParseRefreshPayload(payload));
    }
}

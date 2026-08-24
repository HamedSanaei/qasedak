using Qasedak.BuildingBlocks.Infrastructure.Diagnostics;
using Xunit;

namespace Qasedak.BuildingBlocks.UnitTests;

/// <summary>Correlation id rules: strict validation, URL-safe generation.</summary>
public sealed class CorrelationIdTests
{
    [Fact]
    public void GeneratedIdsAreValidAndUrlSafe()
    {
        for (var i = 0; i < 50; i++)
        {
            var id = CorrelationIds.NewId();
            Assert.True(CorrelationIds.IsValid(id), id);
            Assert.DoesNotContain('+', id);
            Assert.DoesNotContain('/', id);
            Assert.DoesNotContain('=', id);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has spaces in it")]
    [InlineData("<script>alert(1)</script>")]
    public void InboundValidationRejectsGarbage(string? candidate)
    {
        Assert.False(CorrelationIds.IsValid(candidate));
        Assert.False(CorrelationIds.IsValid(new string('x', 129))); // over the length cap
    }

    [Fact]
    public void InboundWellFormedIdsAreHonored()
    {
        Assert.True(CorrelationIds.IsValid("abc123_-XYZ987"));
    }

    [Fact]
    public void ContextRejectsInvalidId()
    {
        Assert.Throws<ArgumentException>(() => new CorrelationContext("bad id"));
        var context = new CorrelationContext(CorrelationIds.NewId());
        Assert.Equal(context.CorrelationId, new CorrelationContext(context.CorrelationId).CorrelationId);
    }
}

/// <summary>Redaction never reveals secret content and is deterministic where required.</summary>
public sealed class SensitiveTests
{
    [Fact]
    public void RedactHidesContentButKeepsLengthClass()
    {
        var redacted = Sensitive.Redact("EAAG-super-secret-token-value");
        Assert.StartsWith(Sensitive.MarkerPrefix, redacted);
        Assert.Contains("len=29", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", redacted, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(string.Empty, Sensitive.Redact(null));
        Assert.Equal(string.Empty, Sensitive.Redact(""));
    }

    [Fact]
    public void MaskTailShowsOnlySuffix()
    {
        var masked = Sensitive.MaskTail("17841400000000000");
        Assert.Equal("*************0000", masked);
        Assert.Equal(17, masked.Length);
        // Short identifiers are fully redacted instead of leaking.
        Assert.StartsWith(Sensitive.MarkerPrefix, Sensitive.MaskTail("abc"));
    }

    [Fact]
    public void FingerprintsAreDeterministicWithoutReversibility()
    {
        var first = Sensitive.Fingerprint("same-secret");
        var second = Sensitive.Fingerprint("same-secret");

        Assert.Equal(first, second);
        Assert.Equal(15, first.Length); // fp_ + 12 hex
        Assert.NotEqual(Sensitive.Fingerprint("other-secret"), first);
        Assert.DoesNotContain("same", first, StringComparison.OrdinalIgnoreCase);
    }
}

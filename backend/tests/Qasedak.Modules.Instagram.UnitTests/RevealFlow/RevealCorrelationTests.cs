using Qasedak.Modules.Instagram.Application.RevealFlow;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.RevealFlow;

public sealed class RevealCorrelationTests
{
    [Fact]
    public void CreatedTokenIsPurposeBoundWithSufficientEntropyAndUniqueHashes()
    {
        var a = RevealCorrelation.Create();
        var b = RevealCorrelation.Create();

        Assert.StartsWith(RevealCorrelation.TokenPurpose + ".", a.Raw, StringComparison.Ordinal);
        Assert.NotEqual(a.Raw, b.Raw);
        Assert.NotEqual(a.Sha256Hash, b.Sha256Hash);
        Assert.Equal(64, a.Sha256Hash.Length);

        // Decoded entropy: 36 raw bytes minus padding/base64 overhead = at least 256 bits.
        Assert.True(RevealCorrelation.TryParse(a.Raw, out var parsed));
        Assert.Equal(a.Raw, parsed);
        Assert.Equal(a.Sha256Hash, RevealCorrelation.Hash(parsed));
        Assert.True(a.Raw.Length <= RevealCorrelation.MaxRawTokenLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("rv2.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("other.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("rv1.")]
    [InlineData("rv1.short")]
    [InlineData("rv1." + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("rv1.!!!not-base64url!!!")]
    public void MalformedOrForeignPayloadsAreRejected(string payload)
    {
        Assert.False(RevealCorrelation.TryParse(payload, out _));
    }

    [Fact]
    public void NullPayloadIsRejected()
    {
        Assert.False(RevealCorrelation.TryParse(null!, out _));
    }

    [Fact]
    public void SingleBitTamperingChangesTheHashAndFailsLookup()
    {
        var token = RevealCorrelation.Create();
        var tampered = (token.Raw[..^1]) + (token.Raw[^1] == 'A' ? 'B' : 'A');
        Assert.NotEqual(tampered, token.Raw);
        Assert.NotEqual(token.Sha256Hash, RevealCorrelation.Hash(tampered));
    }

    [Fact]
    public void HashIsDeterministicAndCanonical()
    {
        var token = RevealCorrelation.Create();
        Assert.Equal(token.Sha256Hash, RevealCorrelation.Hash(token.Raw));
        Assert.Equal(64, RevealCorrelation.Hash(token.Raw).Length);
    }
}

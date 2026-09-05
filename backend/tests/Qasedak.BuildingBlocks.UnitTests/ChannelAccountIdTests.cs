using Qasedak.BuildingBlocks.Domain;
using Xunit;

namespace Qasedak.BuildingBlocks.UnitTests;

/// <summary>
/// Opaque channel-account identity semantics: real accounts resolve, empty never does.
/// </summary>
public sealed class ChannelAccountIdTests
{
    [Fact]
    public void FromAcceptsNonEmptyAndReportsResolved()
    {
        var id = Guid.CreateVersion7();
        var account = ChannelAccountId.From(id);

        Assert.Equal(id, account.Value);
        Assert.True(account.IsResolved);
        Assert.Equal(id.ToString("D"), account.ToString());
    }

    [Fact]
    public void FromRejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => ChannelAccountId.From(Guid.Empty));
        Assert.False(default(ChannelAccountId).IsResolved);
    }

    [Fact]
    public void TryParseRoundTripsAndRejectsGarbage()
    {
        var id = Guid.CreateVersion7();
        Assert.Equal(new ChannelAccountId(id), ChannelAccountId.TryParse(id.ToString("D")));
        Assert.Null(ChannelAccountId.TryParse(null));
        Assert.Null(ChannelAccountId.TryParse(""));
        Assert.Null(ChannelAccountId.TryParse(Guid.Empty.ToString("D")));
        Assert.Null(ChannelAccountId.TryParse("not-a-guid"));
    }

    [Fact]
    public void DistinctAccountsAreDistinctIdentities()
    {
        var first = new ChannelAccountId(Guid.CreateVersion7());
        var second = new ChannelAccountId(Guid.CreateVersion7());

        Assert.NotEqual(first, second);
        Assert.Equal(first, new ChannelAccountId(first.Value));
    }
}

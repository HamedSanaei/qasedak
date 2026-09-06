using Qasedak.Modules.Instagram.Application.Insights;
using Qasedak.Modules.Instagram.Application.Media;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Insights;

/// <summary>
/// M13-007: the registry is the single verified source of metric truth. These tests
/// pin the exact account/media sets, provider names per surface and the unknown-kind
/// empty set so metric selection can never drift across adapters/use cases.
/// </summary>
public sealed class InsightMetricRegistryTests
{
    [Fact]
    public void AccountMetricSetIsExactAndOrdered()
    {
        Assert.Equal(
            [
                InsightMetricKey.Reach,
                InsightMetricKey.AccountsEngaged,
                InsightMetricKey.Likes,
                InsightMetricKey.Comments,
                InsightMetricKey.Saves,
                InsightMetricKey.Shares,
                InsightMetricKey.Views,
                InsightMetricKey.TotalInteractions,
                InsightMetricKey.Reposts,
                InsightMetricKey.FollowsAndUnfollows,
            ],
            InsightMetricRegistry.AccountMetrics);
    }

    [Fact]
    public void FeedFamilyMediaUsesExactFeedSetWithoutReelOnlyMetrics()
    {
        foreach (var kind in new[] { MediaKind.Image, MediaKind.Video, MediaKind.Carousel })
        {
            Assert.Equal(
                [
                    InsightMetricKey.Likes,
                    InsightMetricKey.Comments,
                    InsightMetricKey.Reach,
                    InsightMetricKey.Saves,
                    InsightMetricKey.Shares,
                    InsightMetricKey.Views,
                    InsightMetricKey.TotalInteractions,
                    InsightMetricKey.Reposts,
                ],
                InsightMetricRegistry.ForMedia(kind));
        }
    }

    [Fact]
    public void ReelMediaExtendsFeedSetWithVerifiedReelMetrics()
    {
        Assert.Equal(
            [
                InsightMetricKey.Likes,
                InsightMetricKey.Comments,
                InsightMetricKey.Reach,
                InsightMetricKey.Saves,
                InsightMetricKey.Shares,
                InsightMetricKey.Views,
                InsightMetricKey.TotalInteractions,
                InsightMetricKey.Reposts,
                InsightMetricKey.IgReelsAvgWatchTime,
                InsightMetricKey.IgReelsVideoViewTotalTime,
                InsightMetricKey.ReelsSkipRate,
            ],
            InsightMetricRegistry.ForMedia(MediaKind.Reel));
    }

    [Fact]
    public void UnknownMediaKindHasEmptySetNoUnsafeRequests()
    {
        Assert.Empty(InsightMetricRegistry.ForMedia(MediaKind.Unknown));
    }

    [Fact]
    public void MediaSurfaceSpellsSavesAsSavedAccountSurfaceSpellsSaves()
    {
        Assert.Equal("saved", InsightMetricRegistry.MediaProviderName(InsightMetricKey.Saves));
        Assert.Equal("saves", InsightMetricRegistry.AccountProviderName(InsightMetricKey.Saves));
    }

    [Fact]
    public void ProviderNamesMatchTheVerifiedContractSpelling()
    {
        Assert.Equal("reach", InsightMetricRegistry.AccountProviderName(InsightMetricKey.Reach));
        Assert.Equal("accounts_engaged", InsightMetricRegistry.AccountProviderName(InsightMetricKey.AccountsEngaged));
        Assert.Equal("follows_and_unfollows", InsightMetricRegistry.AccountProviderName(InsightMetricKey.FollowsAndUnfollows));
        Assert.Equal("ig_reels_avg_watch_time", InsightMetricRegistry.MediaProviderName(InsightMetricKey.IgReelsAvgWatchTime));
        Assert.Equal("ig_reels_video_view_total_time", InsightMetricRegistry.MediaProviderName(InsightMetricKey.IgReelsVideoViewTotalTime));
        Assert.Equal("reels_skip_rate", InsightMetricRegistry.MediaProviderName(InsightMetricKey.ReelsSkipRate));
    }

    [Fact]
    public void ApiNamesAreStableCamelCaseQasedakOwned()
    {
        Assert.Equal("followsAndUnfollows", InsightMetricRegistry.ApiName(InsightMetricKey.FollowsAndUnfollows));
        Assert.Equal("accountsEngaged", InsightMetricRegistry.ApiName(InsightMetricKey.AccountsEngaged));
        Assert.Equal("igReelsAvgWatchTime", InsightMetricRegistry.ApiName(InsightMetricKey.IgReelsAvgWatchTime));
        Assert.Equal("reelsSkipRate", InsightMetricRegistry.ApiName(InsightMetricKey.ReelsSkipRate));
    }
}

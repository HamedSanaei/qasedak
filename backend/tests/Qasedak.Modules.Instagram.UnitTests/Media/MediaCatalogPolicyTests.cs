using Qasedak.Modules.Instagram.Application.Media;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Media;

/// <summary>Sanity bounds for the central media catalog policy (M13-006).</summary>
public sealed class MediaCatalogPolicyTests
{
    [Fact]
    public void PageBoundsAreSaneAndProviderCompatible()
    {
        Assert.True(MediaCatalogPolicy.DefaultPageSize > 0);
        Assert.True(MediaCatalogPolicy.MaxPageSize >= MediaCatalogPolicy.DefaultPageSize);
        // Provider hard limit is 100 per the Graph API results guide; Qasedak never exceeds it.
        Assert.True(MediaCatalogPolicy.MaxPageSize <= 100);
    }

    [Fact]
    public void TraversalCeilingsAreBoundedAndConsistent()
    {
        Assert.True(MediaCatalogPolicy.MaxRecentItems >= MediaCatalogPolicy.DefaultPageSize);
        Assert.True(MediaCatalogPolicy.MaxPages > 0);
        // Worst-case traversal stays bounded: pages * page size cannot explode.
        Assert.True(MediaCatalogPolicy.MaxPages * MediaCatalogPolicy.MaxPageSize < 100_000);
    }

    [Fact]
    public void CursorBoundsAreFinite()
    {
        Assert.True(MediaCatalogPolicy.MaxEncodedCursorLength > 64);
        Assert.True(MediaCatalogPolicy.MaxEncodedCursorLength <= 4096);
        Assert.True(MediaCatalogPolicy.MaxProviderCursorLength > 16);
        Assert.True(MediaCatalogPolicy.MaxProviderCursorLength <= 1024);
    }
}

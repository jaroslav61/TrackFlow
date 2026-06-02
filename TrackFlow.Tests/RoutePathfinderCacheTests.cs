using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

[Collection("RoutePathfinder cache")]
public class RoutePathfinderCacheTests
{
    [Fact]
    public void FindAllRoutes_SameLayout_SecondCallUsesCache()
    {
        RoutePathfinder.ClearCacheForTests();
        var layout = new TrackLayout();

        var pf1 = new RoutePathfinder(layout, null);
        _ = pf1.FindAllRoutes();

        Assert.Equal(1, RoutePathfinder.CacheMissCount);
        Assert.Equal(0, RoutePathfinder.CacheHitCount);

        var pf2 = new RoutePathfinder(layout, null);
        _ = pf2.FindAllRoutes();

        Assert.Equal(1, RoutePathfinder.CacheMissCount);
        Assert.Equal(1, RoutePathfinder.CacheHitCount);
    }

    [Fact]
    public void FindAllRoutes_WhenTopologyChanges_CacheMissesAgain()
    {
        RoutePathfinder.ClearCacheForTests();
        var layout = new TrackLayout();

        _ = new RoutePathfinder(layout, null).FindAllRoutes();
        Assert.Equal(1, RoutePathfinder.CacheMissCount);

        layout.Elements.Add(new BlockElement
        {
            MarkerKey = "Block",
            X = 24,
            Y = 24,
            Rotation = 0
        });

        _ = new RoutePathfinder(layout, null).FindAllRoutes();

        Assert.Equal(2, RoutePathfinder.CacheMissCount);
        Assert.Equal(0, RoutePathfinder.CacheHitCount);
    }

    [Fact]
    public void FindAllRoutes_WhenLimitsChange_CacheMissesAgain()
    {
        RoutePathfinder.ClearCacheForTests();
        var layout = new TrackLayout();

        _ = new RoutePathfinder(layout, null).FindAllRoutes();

        var settings = new ProjectSettingsData
        {
            MaxPathElements = 99,
            MaxTurnoutsInPath = 9
        };

        _ = new RoutePathfinder(layout, settings).FindAllRoutes();

        Assert.Equal(2, RoutePathfinder.CacheMissCount);
        Assert.Equal(0, RoutePathfinder.CacheHitCount);
    }

    [Fact]
    public void FindAllRoutes_ReturnsClonedResults_NotMutableCacheReference()
    {
        RoutePathfinder.ClearCacheForTests();
        var layout = new TrackLayout();

        var first = new RoutePathfinder(layout, null).FindAllRoutes();
        first.Add(new FoundRoute { FromBlockId = "X", ToBlockId = "Y" });

        var second = new RoutePathfinder(layout, null).FindAllRoutes();

        Assert.DoesNotContain(second, r => r.FromBlockId == "X" && r.ToBlockId == "Y");
    }
}


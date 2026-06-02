using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.ViewModels.Editor;
using Xunit;

namespace TrackFlow.Tests;

[Collection("RoutePathfinder cache")]
public class RoutesManagerViewModelCacheTests
{
    [Fact]
    public void RegenerateRoutes_UsesRoutePathfinderCache_OnRepeatedRun()
    {
        RoutePathfinder.ClearCacheForTests();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = settings.CurrentProject!.Layout;
        layout.Elements.Add(new BlockElement { MarkerKey = "Block", Label = "B1", X = 24, Y = 24 });
        layout.Elements.Add(new BlockElement { MarkerKey = "Block", Label = "B2", X = 240, Y = 24 });

        var vm = new RoutesManagerViewModel(settings);

        var hitAfterCtor = RoutePathfinder.CacheHitCount;
        var missAfterCtor = RoutePathfinder.CacheMissCount;

        // Druhé spustenie na rovnakej topológii má trafiť cache.
        vm.RegenerateRoutesCommand.Execute(null);

        Assert.True(RoutePathfinder.CacheMissCount >= missAfterCtor);
        Assert.True(RoutePathfinder.CacheHitCount > hitAfterCtor);
    }
}


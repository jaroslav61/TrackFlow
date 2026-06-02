using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.ViewModels.Editor;
using Xunit;

namespace TrackFlow.Tests;

public class RoutesManagerViewModelDirectionTests
{
    [Fact]
    public void LoadFromLayout_UsesCanonicalDirectionSymbolsInRouteNames()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = settings.CurrentProject!.Layout;
        layout.Elements.Add(new BlockElement { Id = "b1", MarkerKey = "Block", Label = "B1", Rotation = 0, BlockLengthCells = 4 });
        layout.Elements.Add(new BlockElement { Id = "b2", MarkerKey = "Block", Label = "B2", Rotation = 0, BlockLengthCells = 4 });

        layout.Routes.Add(new RouteDefinition
        {
            Id = "r-dir",
            Name = "Route",
            FromBlockId = "b1",
            ToBlockId = "b2",
            FromBlockDirection = RouteDirection.Up,
            ToBlockDirection = RouteDirection.Left,
            StartNavigationDirection = RouteDirection.Up
        });

        var vm = new RoutesManagerViewModel(settings);
        var route = Assert.Single(vm.Routes);

        Assert.EndsWith(" ↑", route.FromBlockName);
        Assert.StartsWith("← ", route.ToBlockName);
    }

    [Fact]
    public void SaveCommand_NormalizesDirectionsToCanonicalValues_AndKeepsStopFallback()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = settings.CurrentProject!.Layout;
        layout.Routes.Add(new RouteDefinition
        {
            Id = "r-save",
            Name = "Route",
            FromBlockId = "b1",
            ToBlockId = "b2",
            FromBlockDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right,
            StartNavigationDirection = RouteDirection.Right
        });

        var vm = new RoutesManagerViewModel(settings);
        var vmRoute = Assert.Single(vm.Routes);

        vmRoute.FromBlockDirection = RouteDirection.LegacyBackward;
        vmRoute.ToBlockDirection = "not-a-direction";
        vmRoute.StartNavigationDirection = RouteDirection.Up;
        vmRoute.SafetyFallbackAspect = "UnsupportedAspect";

        vm.SaveCommand.Execute(null);

        var model = Assert.Single(layout.Routes);
        Assert.Equal(RouteDirection.Left, model.FromBlockDirection);
        Assert.Equal(RouteDirection.Right, model.ToBlockDirection);
        Assert.Equal(RouteDirection.Up, model.StartNavigationDirection);
        Assert.Equal("Stop", model.SafetyFallbackAspect);
    }
}



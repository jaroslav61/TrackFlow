using TrackFlow.Models;
using TrackFlow.Models.Layout;
using Xunit;

namespace TrackFlow.Tests;

public class TrackLayoutRouteDefinitionTests
{
    [Fact]
    public void TrackFlowProject_DefaultSchemaVersion_IsV3()
    {
        var project = new TrackFlowProject();

        Assert.Equal(3, project.SchemaVersion);
    }

    [Fact]
    public void TrackLayout_DefaultSchemaVersion_IsV3()
    {
        var layout = new TrackLayout();

        Assert.Equal(3, layout.SchemaVersion);
    }

    [Fact]
    public void RouteDefinition_Defaults_UseDirectionalStrings_AndStopFallback()
    {
        var route = new RouteDefinition();

        Assert.Equal(RouteDirection.Right, route.FromBlockDirection);
        Assert.Equal(RouteDirection.Right, route.ToBlockDirection);
        Assert.Equal(RouteDirection.Right, route.StartNavigationDirection);
        Assert.Equal("Stop", route.SafetyFallbackAspect);
        Assert.Empty(route.RouteSignalIds);
    }

    [Theory]
    [InlineData(RouteDirection.Left)]
    [InlineData(RouteDirection.Right)]
    [InlineData(RouteDirection.Up)]
    [InlineData(RouteDirection.Down)]
    public void RouteDefinition_DirectionProperties_AcceptOnlyCanonicalStrings(string direction)
    {
        var route = new RouteDefinition();

        route.FromBlockDirection = direction;
        route.ToBlockDirection = direction;
        route.StartNavigationDirection = direction;

        Assert.Equal(direction, route.FromBlockDirection);
        Assert.Equal(direction, route.ToBlockDirection);
        Assert.Equal(direction, route.StartNavigationDirection);
    }

    [Fact]
    public void RouteDefinition_InvalidDirections_FallbackToRight()
    {
        var route = new RouteDefinition
        {
            FromBlockDirection = "Invalid",
            ToBlockDirection = "",
            StartNavigationDirection = "Diagonal"
        };

        Assert.Equal(RouteDirection.Right, route.FromBlockDirection);
        Assert.Equal(RouteDirection.Right, route.ToBlockDirection);
        Assert.Equal(RouteDirection.Right, route.StartNavigationDirection);
    }

    [Fact]
    public void RouteDefinition_LegacyDirections_AreNormalizedToCanonicalStrings()
    {
        var route = new RouteDefinition
        {
            FromBlockDirection = RouteDirection.LegacyForward,
            ToBlockDirection = RouteDirection.LegacyBackward,
            StartNavigationDirection = RouteDirection.LegacyForward
        };

        Assert.Equal(RouteDirection.Right, route.FromBlockDirection);
        Assert.Equal(RouteDirection.Left, route.ToBlockDirection);
        Assert.Equal(RouteDirection.Right, route.StartNavigationDirection);
    }

    [Fact]
    public void RouteDefinition_SafetyFallbackAspect_AlwaysStoresStop()
    {
        var route = new RouteDefinition();

        route.SafetyFallbackAspect = "UnsupportedAspect";

        Assert.Equal("Stop", route.SafetyFallbackAspect);
    }
}



using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class SignalSystemRegistryRuntimeTests
{
    [Theory]
    [InlineData("2-aspect-shunt", true, false, SignalAspect.Stop)]
    [InlineData("2-aspect-shunt", false, true, SignalAspect.ShuntingPermitted)]
    [InlineData("2-aspect-route", true, false, SignalAspect.Stop)]
    [InlineData("2-aspect-route", false, false, SignalAspect.ShuntingPermitted)]
    [InlineData("2-aspect-route", false, true, SignalAspect.ShuntingPermitted)]
    [InlineData("2-aspect", true, false, SignalAspect.Caution)]
    [InlineData("2-aspect", false, true, SignalAspect.Proceed)]
    [InlineData("2-aspect-main", true, false, SignalAspect.Stop)]
    [InlineData("2-aspect-main", false, true, SignalAspect.Caution)]
    [InlineData("3-aspect-entry", true, false, SignalAspect.Stop)]
    [InlineData("3-aspect-entry", false, false, SignalAspect.Proceed)]
    [InlineData("3-aspect-entry", false, true, SignalAspect.ShuntingPermitted)]
    [InlineData("3-aspect", false, false, SignalAspect.Proceed)]
    [InlineData("4-aspect-departure", true, false, SignalAspect.Stop)]
    [InlineData("4-aspect-departure", false, false, SignalAspect.Proceed)]
    [InlineData("4-aspect-departure", false, true, SignalAspect.Caution)]
    [InlineData("5-aspect-departure", true, false, SignalAspect.Stop)]
    [InlineData("5-aspect-departure", false, false, SignalAspect.Proceed)]
    [InlineData("5-aspect-departure", false, true, SignalAspect.Caution)]
    [InlineData(null, true, false, SignalAspect.Stop)]
    public void ResolveRuntimeAspect_ReturnsExpected(
        string? profile,
        bool isOccupied,
        bool requestYellow,
        SignalAspect expected)
    {
        var result = SignalSystemRegistry.ResolveRuntimeAspect(profile, isOccupied, requestYellow);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveRuntimeAspect_NormalizesHeadSuffix()
    {
        var result = SignalSystemRegistry.ResolveRuntimeAspect("2-head", true, false);
        Assert.Equal(SignalAspect.Caution, result);
    }
}


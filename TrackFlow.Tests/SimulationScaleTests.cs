using System;
using System.Reflection;
using TrackFlow.Models.Layout;
using TrackFlow.Services.Simulation;
using TrackFlow.ViewModels.Operation;
using Xunit;

namespace TrackFlow.Tests;

public class SimulationScaleTests
{
    [Theory]
    [InlineData("H0", 87.0)]
    [InlineData("HO", 87.0)]
    [InlineData("TT", 120.0)]
    [InlineData("N", 160.0)]
    [InlineData("1:87", 87.0)]
    [InlineData("120", 120.0)]
    [InlineData("1:160", 160.0)]
    public void ResolveScaleDivisor_MapsPersistedScaleToNumericDivisor(string scale, double expected)
    {
        Assert.Equal(expected, SimulationScaleResolver.ResolveScaleDivisor(scale));
    }

    [Fact]
    public void Update_UsesScaleAwareDeltaFormula()
    {
        var engine = new LocomotiveSimulationEngine(
            10_000,
            1_000,
            0,
            87);

        var result = engine.Update(87, 1.0);

        // DeltaMm = (87 / 3.6) / 87 * 1s * 1000 = 277.777... mm
        Assert.InRange(result.DeltaMm, 277.77, 277.78);
        Assert.InRange(engine.CurrentDistanceMm, 277.77, 277.78);
    }

    [Fact]
    public void Update_WhenStopped_DoesNotApplyArtificialMinimumMovement()
    {
        var engine = new LocomotiveSimulationEngine(
            10_000,
            10,
            0,
            87);

        var result = engine.Update(0, 1.0);

        Assert.Equal(0, result.DeltaMm);
        Assert.Equal(0, engine.CurrentDistanceMm);
    }

    [Fact]
    public void ResolveEffectiveMarkerProfile_InSimulation_MapsMarkersProportionallyToVirtualLength()
    {
        var targetBlock = new BlockElement { lengthMm = 2000 };
        var rawProfile = CreateMarkerProfile(0, 100, 180);

        var effective = ResolveEffectiveMarkerProfile(rawProfile, targetBlock, 2_000, true);

        Assert.Equal(0, GetMarkerValue(effective, "DistanceCm"));
        Assert.Equal(10, GetMarkerValue(effective, "BrakingCm"));
        Assert.Equal(18, GetMarkerValue(effective, "StopCm"));
    }

    [Fact]
    public void ResolveEffectiveMarkerProfile_InSimulation_WithoutBlockLength_ReturnsEmptyProfileForFallback()
    {
        var targetBlock = new BlockElement { lengthMm = 0 };
        var rawProfile = CreateMarkerProfile(0, 100, 180);

        var effective = ResolveEffectiveMarkerProfile(rawProfile, targetBlock, 2_000, true);

        Assert.Equal(0, GetMarkerValue(effective, "DistanceCm"));
        Assert.Equal(0, GetMarkerValue(effective, "BrakingCm"));
        Assert.Equal(0, GetMarkerValue(effective, "StopCm"));
    }

    [Fact]
    public void CreateSimulationFallbackMarkerProfile_UsesSixtyAndNinetyPercentOfVirtualLength()
    {
        var fallback = CreateSimulationFallbackMarkerProfile(2_000);

        Assert.Equal(0, GetMarkerValue(fallback, "DistanceCm"));
        Assert.Equal(120, GetMarkerValue(fallback, "BrakingCm"));
        Assert.Equal(180, GetMarkerValue(fallback, "StopCm"));
    }

    private static object CreateMarkerProfile(double distanceCm, double brakingCm, double stopCm)
    {
        var type = GetMarkerProfileType();
        return Activator.CreateInstance(type, distanceCm, brakingCm, stopCm)
               ?? throw new InvalidOperationException("MarkerSpeedProfile could not be created.");
    }

    private static object ResolveEffectiveMarkerProfile(object rawProfile, BlockElement targetBlock,
        double blockLengthMm, bool isSimulationMode)
    {
        var method = typeof(OperationViewModel).GetMethod(
                         "ResolveEffectiveMarkerProfile",
                         BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new MissingMethodException(nameof(OperationViewModel), "ResolveEffectiveMarkerProfile");

        return method.Invoke(null, new[] { rawProfile, targetBlock, blockLengthMm, isSimulationMode })
               ?? throw new InvalidOperationException("ResolveEffectiveMarkerProfile returned null.");
    }

    private static object CreateSimulationFallbackMarkerProfile(double blockLengthMm)
    {
        var method = typeof(OperationViewModel).GetMethod(
                         "CreateSimulationFallbackMarkerProfile",
                         BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new MissingMethodException(nameof(OperationViewModel),
                         "CreateSimulationFallbackMarkerProfile");

        return method.Invoke(null, new object[] { blockLengthMm })
               ?? throw new InvalidOperationException("CreateSimulationFallbackMarkerProfile returned null.");
    }

    private static double GetMarkerValue(object profile, string propertyName)
    {
        var property = GetMarkerProfileType().GetProperty(propertyName)
                       ?? throw new MissingMemberException(GetMarkerProfileType().Name, propertyName);

        return (double)(property.GetValue(profile) ?? 0.0);
    }

    private static Type GetMarkerProfileType()
    {
        return typeof(OperationViewModel).GetNestedType("MarkerSpeedProfile", BindingFlags.NonPublic)
               ?? throw new MissingMemberException(nameof(OperationViewModel), "MarkerSpeedProfile");
    }
}
using System;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public sealed class TimeServiceTests
{
    [Fact]
    public void AdvanceModelTime_UsesSimulationSpeedFactor()
    {
        using var service = new TimeService(new DateTime(2026, 5, 10, 8, 0, 0), autoStart: false)
        {
            SimulationSpeedFactor = 3.0
        };

        var delta = service.AdvanceModelTime(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(6), delta);
        Assert.Equal(new DateTime(2026, 5, 10, 8, 0, 6), service.CurrentModelTime);
    }

    [Fact]
    public void AdvanceModelTime_WhenPaused_DoesNotMoveClock()
    {
        using var service = new TimeService(new DateTime(2026, 5, 10, 8, 0, 0), autoStart: false)
        {
            SimulationSpeedFactor = 5.0
        };
        service.Pause();
        var pausedAt = service.CurrentModelTime;

        var delta = service.AdvanceModelTime(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.Zero, delta);
        Assert.Equal(pausedAt, service.CurrentModelTime);
        Assert.True(service.IsPaused);
    }

    [Fact]
    public void GetModelDeltaSecondsSince_ReturnsElapsedModelSeconds()
    {
        using var service = new TimeService(new DateTime(2026, 5, 10, 8, 0, 0), autoStart: false)
        {
            SimulationSpeedFactor = 2.0
        };
        var previous = service.CurrentModelTime;
        service.AdvanceModelTime(TimeSpan.FromSeconds(1.5));

        var dtSec = service.GetModelDeltaSecondsSince(ref previous);

        Assert.Equal(3.0, dtSec, precision: 3);
        Assert.Equal(service.CurrentModelTime, previous);
    }
}



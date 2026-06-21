using System;
using System.Linq;
using System.Threading.Tasks;
using TrackFlow.Models;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

/// <summary>
/// Unit testy pre LocomotiveMotionController.
/// Používajú existujúci TestDccCentralClient ktorý zaznamenáva všetky DCC príkazy.
/// Testy čakajú niekoľko tickov (100 ms každý) cez Task.Delay a potom overujú
/// zaznamenané príkazy.
/// </summary>
public sealed class LocomotiveMotionControllerTests : IDisposable
{
    private readonly TestDccCentralClient _dcc;
    private readonly LocomotiveMotionController _controller;

    public LocomotiveMotionControllerTests()
    {
        _dcc        = new TestDccCentralClient { IsConnected = true };
        _controller = new LocomotiveMotionController(_dcc);
    }

    public void Dispose()
        => _controller.Dispose();

    // ── Pomocné metódy ────────────────────────────────────────────────────────

    private static LocoRecord MakeRecord(int address, int cv3 = 10, int cv4 = 10)
        => new()
        {
            DccAddress     = address,
            AccelerationCv = cv3,
            BrakingCv      = cv4,
        };

    /// <summary>Čaká aspoň N tickov (každý 100 ms) + malý buffer.</summary>
    private static Task WaitTicksAsync(int ticks)
        => Task.Delay(ticks * 100 + 50);

    // ── Testy matematiky mapovania ────────────────────────────────────────────

    [Theory]
    [InlineData(0,    0)]    // stoj
    [InlineData(1000, 126)]  // plný výkon
    [InlineData(500,  63)]   // stred – aproximácia
    public void MapChartStepToDcc_BoundaryValues_AreCorrect(int virtualSpeed, int expectedDccApprox)
    {
        // Otestujeme cez GetVirtualSpeed + overíme MapChartStepToDcc priamo
        // keďže MapChartStepToDcc je public static na LocomotiveSpeedEditorViewModel
        var position   = virtualSpeed * 27.0 / 1000.0;
        var lower      = (int)Math.Floor(Math.Min(position, 27));
        var upper      = Math.Min(lower + 1, 27);
        var fraction   = position >= 27.0 ? 0.0 : position - lower;

        var dccLower   = TrackFlow.ViewModels.Library.LocomotiveSpeedEditorViewModel.MapChartStepToDcc(lower);
        var dccUpper   = TrackFlow.ViewModels.Library.LocomotiveSpeedEditorViewModel.MapChartStepToDcc(upper);
        var dccStep    = Math.Clamp((int)Math.Round(dccLower + fraction * (dccUpper - dccLower)), 0, 126);

        Assert.InRange(dccStep, expectedDccApprox - 5, expectedDccApprox + 5);
    }

    [Fact]
    public void MapChartStepToDcc_Point1_Returns4()
        => Assert.Equal(4, TrackFlow.ViewModels.Library.LocomotiveSpeedEditorViewModel.MapChartStepToDcc(1));

    [Fact]
    public void MapChartStepToDcc_Point28_Returns126()
        => Assert.Equal(126, TrackFlow.ViewModels.Library.LocomotiveSpeedEditorViewModel.MapChartStepToDcc(28));

    [Fact]
    public void MapChartStepToDcc_IsMonotonicallyIncreasing()
    {
        var steps = Enumerable.Range(0, 29)
            .Select(TrackFlow.ViewModels.Library.LocomotiveSpeedEditorViewModel.MapChartStepToDcc)
            .ToList();

        for (var i = 1; i < steps.Count; i++)
            Assert.True(steps[i] >= steps[i - 1],
                $"MapChartStepToDcc nie je monotónne: krok {i - 1}={steps[i - 1]}, krok {i}={steps[i]}");
    }

    // ── Testy tick logiky ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetTarget_FromStop_SendsIncreasingDccSteps()
    {
        // CV3=10 → accelerationPerTick = 1000/(10×10) = 10.0 za tick
        // Po 5 tickoch: VirtualSpeed ≈ 50 → DCC > 0
        var loco = MakeRecord(address: 3, cv3: 10, cv4: 10);
        _controller.SetTarget(loco, targetVirtualSpeed: 1000, forward: true);

        await WaitTicksAsync(5);

        var commands = _dcc.LocomotiveSpeedCommands
            .Where(c => c.Address == 3)
            .ToList();

        Assert.NotEmpty(commands);
        // Prvý odoslaný krok musí byť > 0
        Assert.True(commands[0].Speed > 0, "Prvý DCC krok musí byť väčší ako 0");
        // Kroky musia byť neklesajúce (zrýchľovanie)
        for (var i = 1; i < commands.Count; i++)
            Assert.True(commands[i].Speed >= commands[i - 1].Speed,
                $"DCC kroky musia rásť: [{i - 1}]={commands[i - 1].Speed}, [{i}]={commands[i].Speed}");
    }

    [Fact]
    public async Task SetTarget_FullSpeed_EventuallyReachesDcc126()
    {
        // CV3=1 → accelerationPerTick = 1000/(1×10) = 100 za tick → 10 tickov stačí
        var loco = MakeRecord(address: 4, cv3: 1, cv4: 1);
        _controller.SetTarget(loco, targetVirtualSpeed: 1000, forward: true);

        await WaitTicksAsync(15);

        var last = _dcc.LocomotiveSpeedCommands
            .Where(c => c.Address == 4)
            .LastOrDefault();

        Assert.Equal(126, last.Speed);
    }

    [Fact]
    public async Task SetTarget_Stop_SendsZeroSpeed()
    {
        var loco = MakeRecord(address: 5, cv3: 1, cv4: 1);

        // Najprv rozbehni
        _controller.SetTarget(loco, targetVirtualSpeed: 1000, forward: true);
        await WaitTicksAsync(15);

        // Potom zastav
        _dcc.LocomotiveSpeedCommands.Clear();
        _controller.SetTarget(loco, targetVirtualSpeed: 0, forward: true);
        await WaitTicksAsync(15);

        var last = _dcc.LocomotiveSpeedCommands
            .Where(c => c.Address == 5)
            .LastOrDefault();

        Assert.Equal(0, last.Speed);
    }

    [Fact]
    public async Task SetTarget_WhenTargetReached_StopsEmittingCommands()
    {
        // CV3=1 → dosiahne 1000 za ~10 tickov
        var loco = MakeRecord(address: 6, cv3: 1, cv4: 1);
        _controller.SetTarget(loco, targetVirtualSpeed: 1000, forward: true);

        await WaitTicksAsync(15);

        // Zistíme počet príkazov po dosiahnutí cieľa
        var countAfterReached = _dcc.LocomotiveSpeedCommands.Count(c => c.Address == 6);
        await WaitTicksAsync(5);
        var countAfterWaiting = _dcc.LocomotiveSpeedCommands.Count(c => c.Address == 6);

        // Po dosiahnutí cieľa controller stíchne — žiadne ďalšie príkazy
        Assert.Equal(countAfterReached, countAfterWaiting);
    }

    [Fact]
    public async Task SetTarget_HigherCv3_SlowerAcceleration()
    {
        // CV3=5 → rýchlejší rozjazd, CV3=20 → pomalší rozjazd
        var fastLoco = MakeRecord(address: 7, cv3: 5,  cv4: 10);
        var slowLoco = MakeRecord(address: 8, cv3: 20, cv4: 10);

        _controller.SetTarget(fastLoco, 1000, forward: true);
        _controller.SetTarget(slowLoco, 1000, forward: true);

        await WaitTicksAsync(8);

        var fastSpeed = _dcc.LocomotiveSpeedCommands
            .Where(c => c.Address == 7).LastOrDefault().Speed;
        var slowSpeed = _dcc.LocomotiveSpeedCommands
            .Where(c => c.Address == 8).LastOrDefault().Speed;

        Assert.True(fastSpeed > slowSpeed,
            $"CV3=5 musí dosahovať vyšší DCC krok než CV3=20 po rovnakom čase. fast={fastSpeed}, slow={slowSpeed}");
    }

    [Fact]
    public async Task SetTarget_ForwardFalse_SendsBackwardCommands()
    {
        var loco = MakeRecord(address: 9, cv3: 1, cv4: 1);
        _controller.SetTarget(loco, targetVirtualSpeed: 500, forward: false);

        await WaitTicksAsync(8);

        var commands = _dcc.LocomotiveSpeedCommands.Where(c => c.Address == 9).ToList();
        Assert.NotEmpty(commands);
        Assert.All(commands, c => Assert.False(c.Forward, "Všetky príkazy musia mať forward=false"));
    }

    [Fact]
    public async Task MultipleLocomotives_AreControlledIndependently()
    {
        var loco1 = MakeRecord(address: 10, cv3: 1, cv4: 1);
        var loco2 = MakeRecord(address: 11, cv3: 5, cv4: 5);

        _controller.SetTarget(loco1, 1000, forward: true);
        _controller.SetTarget(loco2, 500,  forward: false);

        await WaitTicksAsync(15);

        var last1 = _dcc.LocomotiveSpeedCommands.Where(c => c.Address == 10).LastOrDefault();
        var last2 = _dcc.LocomotiveSpeedCommands.Where(c => c.Address == 11).LastOrDefault();

        Assert.Equal(126,  last1.Speed);
        Assert.True(last2.Speed > 0 && last2.Speed < 126);
        Assert.True(last1.Forward);
        Assert.False(last2.Forward);
    }

    [Fact]
    public async Task EmergencyStop_ImmediatelySendsZero()
    {
        var loco = MakeRecord(address: 12, cv3: 10, cv4: 10);
        _controller.SetTarget(loco, 1000, forward: true);
        await WaitTicksAsync(3);

        _dcc.LocomotiveSpeedCommands.Clear();
        await _controller.EmergencyStopAsync(loco);

        var first = _dcc.LocomotiveSpeedCommands.FirstOrDefault(c => c.Address == 12);
        Assert.Equal(0, first.Speed);
    }

    [Fact]
    public async Task GetVirtualSpeed_ReflectsCurrentState()
    {
        var loco = MakeRecord(address: 13, cv3: 1, cv4: 1);
        _controller.SetTarget(loco, 1000, forward: true);

        await WaitTicksAsync(5);

        var vs = _controller.GetVirtualSpeed(13);
        Assert.NotNull(vs);
        Assert.InRange(vs!.Value, 1.0, 1000.0);
    }

    [Fact]
    public void GetVirtualSpeed_UnknownAddress_ReturnsNull()
        => Assert.Null(_controller.GetVirtualSpeed(999));
}

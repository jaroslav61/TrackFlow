using System.Threading.Tasks;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class OperationRuntimeSafetyServiceTests
{
    [Fact]
    public void RefreshSignals_ObsadenyChranenyBlok_NastaviStopAjBezView()
    {
        var service = new OperationRuntimeSafetyService();
        var block = new BlockElement { MarkerKey = "Block", IsOccupied = true };
        var signal = new SignalElement
        {
            MarkerKey = "Signal",
            ProtectsBlockId = block.Id,
            DccAddress = 42,
            Aspect = SignalAspect.Proceed
        };

        var changedCount = service.RefreshSignals(new LayoutElement[] { block, signal });

        Assert.Equal(1, changedCount);
        Assert.Equal(SignalAspect.Stop, signal.Aspect);
    }

    [Fact]
    public async Task RefreshSignalsAsync_PriZmenePosleDccPrikaz()
    {
        var service = new OperationRuntimeSafetyService();
        var client = new TestDccCentralClient { IsConnected = true };
        var block = new BlockElement { MarkerKey = "Block", IsOccupied = true };
        var signal = new SignalElement
        {
            MarkerKey = "Signal",
            ProtectsBlockId = block.Id,
            DccAddress = 42,
            Aspect = SignalAspect.Proceed
        };

        var changedCount = await service.RefreshSignalsAsync(new LayoutElement[] { block, signal }, client);

        Assert.Equal(1, changedCount);
        Assert.Single(client.TurnoutCommands);
        Assert.Contains((169, false, true), client.TurnoutCommands);
    }

    [Fact]
    public void EvaluateBlockEntry_TargetLocked_VratiBlocked()
    {
        var service = new OperationRuntimeSafetyService();
        var block = new BlockElement
        {
            MarkerKey = "Block",
            IsLocked = true
        };

        var result = service.EvaluateBlockEntry(new LayoutElement[] { block }, block.Id, "754", safetyDistanceBlocks: 0);

        Assert.False(result.IsSafe);
        Assert.Equal("target-block-locked", result.Reason);
    }
}


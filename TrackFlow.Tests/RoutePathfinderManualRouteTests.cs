using System.Linq;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class RoutePathfinderManualRouteTests
{
    [Fact]
    public void FindRouteBetweenBlocks_ReturnsBlocksSwitchStatesAndOrientedSignals()
    {
        RoutePathfinder.ClearCacheForTests();

        var layout = new TrackLayout();

        var blockA = new BlockElement
        {
            Id = "blk_a",
            MarkerKey = "Block",
            X = 24,
            Y = 24,
            Rotation = 0,
            BlockLengthCells = 4,
            SignalRightId = "sig_a"
        };

        var blockB = new BlockElement
        {
            Id = "blk_b",
            MarkerKey = "Block",
            X = 144,
            Y = 24,
            Rotation = 0,
            BlockLengthCells = 4,
            SignalRightId = "sig_b"
        };

        var blockC = new BlockElement
        {
            Id = "blk_c",
            MarkerKey = "Block",
            X = 264,
            Y = 24,
            Rotation = 0,
            BlockLengthCells = 4
        };

        var turnout = new TurnoutElement
        {
            Id = "sw_1",
            MarkerKey = "Turnout_R",
            X = 120,
            Y = 24,
            Rotation = 90,
            State = TurnoutState.Diverge
        };

        var seg2 = new TrackSegmentElement
        {
            Id = "seg_2",
            MarkerKey = "TrackSegment",
            X = 240,
            Y = 24,
            Rotation = 0
        };

        var sigA = new SignalElement { Id = "sig_a", MarkerKey = "Signal", Rotation = 90 };
        var sigB = new SignalElement { Id = "sig_b", MarkerKey = "Signal", Rotation = 90 };

        layout.Elements.Add(blockA);
        layout.Elements.Add(blockB);
        layout.Elements.Add(blockC);
        layout.Elements.Add(turnout);
        layout.Elements.Add(seg2);
        layout.Elements.Add(sigA);
        layout.Elements.Add(sigB);

        var pf = new RoutePathfinder(layout);
        var result = pf.FindRouteBetweenBlocks("blk_a", "blk_c");

        Assert.NotNull(result);
        Assert.Equal("blk_a", result!.BlockIds.First());
        Assert.Equal("blk_c", result.BlockIds.Last());
        Assert.Contains("blk_b", result.BlockIds);
        Assert.Contains(result.SwitchStates, s => s.TurnoutId == "sw_1");
        Assert.Contains(result.Signals, s => s.Id == "sig_a");
        Assert.Contains(result.Signals, s => s.Id == "sig_b");
    }
}


using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class RouteActivationServiceTests
{
    [Fact]
    public async Task TryActivateAsync_BezKonfliktu_NastaviVyhybkuALocky()
    {
        var service = new RouteActivationService();
        var layout = CreateLayout();
        var active = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var turnout = layout.Elements.OfType<TurnoutElement>().Single(t => t.Id == "sw_1");
        turnout.State = TurnoutState.Straight;

        var result = await service.TryActivateAsync(layout, "r_main", active);

        Assert.True(result.IsSuccess);
        Assert.Contains("r_main", active);

        Assert.Equal(TurnoutState.Straight, turnout.State);

        var blockA = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var blockB = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        var blockC = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_c");

        Assert.True(blockA.IsLocked);
        Assert.True(blockB.IsLocked);
        Assert.False(blockC.IsLocked);
    }

    [Fact]
    public async Task TryActivateAsync_KonfliktVyhybky_PovoliAktivaciuAKonfliktPonechaVDiagnostike()
    {
        var service = new RouteActivationService();
        var layout = CreateLayout();
        var active = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "r_main" };

        await service.TryActivateAsync(layout, "r_main", active);

        var result = await service.TryActivateAsync(layout, "r_branch", active);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Conflict);
        Assert.True(result.Conflict!.HasConflict);
        Assert.Contains(result.Conflict.Conflicts, c => c.Type == RouteConflictType.TurnoutStateMismatch);
        Assert.Equal(2, active.Count);
        Assert.Contains("r_main", active);
        Assert.Contains("r_branch", active);
    }

    [Fact]
    public async Task TryActivateAsync_PriAktivaciiNehybeVyhybkouEager()
    {
        var service = new RouteActivationService();
        var layout = CreateLayout();
        var active = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var turnout = layout.Elements.OfType<TurnoutElement>().Single(t => t.Id == "sw_1");
        turnout.State = TurnoutState.Straight;

        var result = await service.TryActivateAsync(layout, "r_branch", active);

        Assert.True(result.IsSuccess);
        Assert.Equal(TurnoutState.Straight, turnout.State);
        Assert.Contains("r_branch", active);
    }

    [Fact]
    public async Task Deactivate_UdrziLockyPreZostavajuceAktivneTrasy()
    {
        var service = new RouteActivationService();
        var layout = CreateLayoutForSharedBlock();
        var active = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        await service.TryActivateAsync(layout, "r_left", active);
        await service.TryActivateAsync(layout, "r_right", active);

        service.Deactivate(layout, "r_left", active);

        var blkA = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var blkB = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        var blkC = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_c");

        Assert.False(blkA.IsLocked);
        Assert.False(blkB.IsLocked);
        Assert.True(blkC.IsLocked);
    }

    private static TrackLayout CreateLayout()
    {
        var blkA = new BlockElement { Id = "blk_a", MarkerKey = "Block" };
        var blkB = new BlockElement { Id = "blk_b", MarkerKey = "Block" };
        var blkC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };
        var sw = new TurnoutElement { Id = "sw_1", MarkerKey = "Turnout_R" };

        var routeMain = new RouteDefinition
        {
            Id = "r_main",
            Name = "Main"
        };
        routeMain.BlockIds.AddRange(new[] { "blk_a", "blk_b" });
        routeMain.PathElementIds.Add("sw_1");
        routeMain.TurnoutSettings.Add(new RouteTurnoutSetting
        {
            TurnoutId = "sw_1",
            RequiredState = TurnoutState.Straight
        });

        var routeBranch = new RouteDefinition
        {
            Id = "r_branch",
            Name = "Branch"
        };
        routeBranch.BlockIds.AddRange(new[] { "blk_a", "blk_c" });
        routeBranch.PathElementIds.Add("sw_1");
        routeBranch.TurnoutSettings.Add(new RouteTurnoutSetting
        {
            TurnoutId = "sw_1",
            RequiredState = TurnoutState.Diverge
        });

        var layout = new TrackLayout();
        layout.Elements.AddRange(new LayoutElement[] { blkA, blkB, blkC, sw });
        layout.Routes.Add(routeMain);
        layout.Routes.Add(routeBranch);
        return layout;
    }

    private static TrackLayout CreateLayoutForSharedBlock()
    {
        var blkA = new BlockElement { Id = "blk_a", MarkerKey = "Block" };
        var blkB = new BlockElement { Id = "blk_b", MarkerKey = "Block" };
        var blkC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };

        var routeLeft = new RouteDefinition
        {
            Id = "r_left",
            Name = "Left"
        };
        routeLeft.BlockIds.AddRange(new[] { "blk_a", "blk_b" });

        var routeRight = new RouteDefinition
        {
            Id = "r_right",
            Name = "Right"
        };
        routeRight.BlockIds.Add("blk_c");

        var layout = new TrackLayout();
        layout.Elements.AddRange(new LayoutElement[] { blkA, blkB, blkC });
        layout.Routes.Add(routeLeft);
        layout.Routes.Add(routeRight);
        return layout;
    }
}


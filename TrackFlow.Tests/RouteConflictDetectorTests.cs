using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class RouteConflictDetectorTests
{
    [Fact]
    public void EvaluateActivation_BezKonfliktu_VratiNoConflict()
    {
        var detector = new RouteConflictDetector();

        var active = CreateRoute(
            id: "r_active",
            blockIds: new[] { "blk_a", "blk_b" },
            pathIds: new[] { "seg_1", "seg_2" },
            turnoutSettings: new[] { ("sw_1", TurnoutState.Straight) });

        var candidate = CreateRoute(
            id: "r_candidate",
            blockIds: new[] { "blk_c", "blk_d" },
            pathIds: new[] { "seg_9", "seg_10" },
            turnoutSettings: new[] { ("sw_2", TurnoutState.Diverge) });

        var result = detector.EvaluateActivation(candidate, new[] { active });

        Assert.False(result.HasConflict);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void EvaluateActivation_KonfliktVyhybky_VratiTurnoutStateMismatch()
    {
        var detector = new RouteConflictDetector();

        var active = CreateRoute(
            id: "r_active",
            blockIds: new[] { "blk_a", "blk_b" },
            pathIds: new[] { "seg_1", "seg_2" },
            turnoutSettings: new[] { ("sw_1", TurnoutState.Straight) });

        var candidate = CreateRoute(
            id: "r_candidate",
            blockIds: new[] { "blk_c", "blk_d" },
            pathIds: new[] { "seg_9", "seg_10" },
            turnoutSettings: new[] { ("sw_1", TurnoutState.Diverge) });

        var result = detector.EvaluateActivation(candidate, new[] { active });

        Assert.True(result.HasConflict);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(RouteConflictType.TurnoutStateMismatch, conflict.Type);
        Assert.Equal("sw_1", conflict.ResourceId);
    }

    [Fact]
    public void EvaluateActivation_KonfliktBlokuAPrvku_VratiObaKonflikty()
    {
        var detector = new RouteConflictDetector();

        var active = CreateRoute(
            id: "r_active",
            blockIds: new[] { "blk_a", "blk_b" },
            pathIds: new[] { "seg_1", "seg_2" },
            turnoutSettings: new (string, TurnoutState)[] { });

        var candidate = CreateRoute(
            id: "r_candidate",
            blockIds: new[] { "blk_b", "blk_c" },
            pathIds: new[] { "seg_2", "seg_3" },
            turnoutSettings: new (string, TurnoutState)[] { });

        var result = detector.EvaluateActivation(candidate, new[] { active });

        Assert.True(result.HasConflict);
        Assert.Contains(result.Conflicts, c => c.Type == RouteConflictType.SharedBlock && c.ResourceId == "blk_b");
        Assert.Contains(result.Conflicts, c => c.Type == RouteConflictType.SharedPathElement && c.ResourceId == "seg_2");
    }

    [Fact]
    public void EvaluateActivation_ByIds_PouzijeMapuTras()
    {
        var detector = new RouteConflictDetector();

        var active = CreateRoute(
            id: "r_active",
            blockIds: new[] { "blk_a", "blk_b" },
            pathIds: new[] { "seg_1", "seg_2" },
            turnoutSettings: new[] { ("sw_1", TurnoutState.Straight) });

        var candidate = CreateRoute(
            id: "r_candidate",
            blockIds: new[] { "blk_c", "blk_d" },
            pathIds: new[] { "seg_9", "seg_10" },
            turnoutSettings: new[] { ("sw_1", TurnoutState.Diverge) });

        var all = new[] { active, candidate };

        var result = detector.EvaluateActivation("r_candidate", new[] { "r_active" }, all);

        Assert.True(result.HasConflict);
        Assert.Single(result.Conflicts, c => c.Type == RouteConflictType.TurnoutStateMismatch);
    }

    private static RouteDefinition CreateRoute(
        string id,
        IEnumerable<string> blockIds,
        IEnumerable<string> pathIds,
        IEnumerable<(string turnoutId, TurnoutState state)> turnoutSettings)
    {
        var route = new RouteDefinition
        {
            Id = id,
            Name = id
        };

        route.BlockIds.AddRange(blockIds);
        route.PathElementIds.AddRange(pathIds);

        foreach (var (turnoutId, state) in turnoutSettings)
        {
            route.TurnoutSettings.Add(new RouteTurnoutSetting
            {
                TurnoutId = turnoutId,
                RequiredState = state
            });
        }

        return route;
    }
}



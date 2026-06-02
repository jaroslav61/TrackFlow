using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using TrackFlow.Views.Shared;
using Xunit;

namespace TrackFlow.Tests;

/// <summary>
/// Testy pre <see cref="BlockTrainRenderer.ComputePlan"/> — čistú logiku rozloženia
/// vlaku v bloku. Validujú, že jednotné "canonical + whole-stack transform" pravidlá
/// produkujú konzistentné výsledky pre všetky 4 orientácie a edge-case vstupy.
/// </summary>
public class BlockTrainRendererTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // FLIP & ROTATION MATRIX (lokomotíva bez vagónov, bez user-flipu)
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(TrainOrientation.HForward,  false, 0,   HorizontalAlignment.Stretch, VerticalAlignment.Center,  Dock.Right, BlockTrainRenderer.DotCorner.TopRight)]
    [InlineData(TrainOrientation.HBackward, true,  0,   HorizontalAlignment.Stretch, VerticalAlignment.Center,  Dock.Left,  BlockTrainRenderer.DotCorner.TopLeft)]
    [InlineData(TrainOrientation.VDown,     false, 90,  HorizontalAlignment.Center,  VerticalAlignment.Stretch, Dock.Right, BlockTrainRenderer.DotCorner.TopRight)]
    [InlineData(TrainOrientation.VUp,       true,  90,  HorizontalAlignment.Center,  VerticalAlignment.Stretch, Dock.Left,  BlockTrainRenderer.DotCorner.TopLeft)]
    public void ComputePlan_BaseCases_NoWagons_NoUserFlip(
        TrainOrientation orientation,
        bool expectedFlip,
        double expectedRotation,
        HorizontalAlignment expectedH,
        VerticalAlignment expectedV,
        Dock expectedDock,
        BlockTrainRenderer.DotCorner expectedDot)
    {
        var plan = BlockTrainRenderer.ComputePlan(
            orientation, isLocoUserFlipped: false, locoPosition: 0, totalWagons: 0);

        Assert.Equal(expectedFlip, plan.ShouldFlipLocoIcon);
        Assert.Equal(expectedRotation, plan.StackRotationDeg);
        Assert.Equal(expectedH, plan.HorizontalAlignment);
        Assert.Equal(expectedV, plan.VerticalAlignment);
        Assert.Equal(expectedDock, plan.LocoDock);
        Assert.Equal(expectedDot, plan.DotCorner);
        Assert.Empty(plan.WagonIndicesInOrder);
        Assert.Equal(0, plan.HiddenWagonsCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // USER FLIP (XOR s canonical flip) — kritická invariantná vlastnosť
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(TrainOrientation.HForward,  true,  BlockTrainRenderer.DotCorner.TopLeft)]
    [InlineData(TrainOrientation.HBackward, false, BlockTrainRenderer.DotCorner.TopRight)]
    [InlineData(TrainOrientation.VDown,     true,  BlockTrainRenderer.DotCorner.TopLeft)]
    [InlineData(TrainOrientation.VUp,       false, BlockTrainRenderer.DotCorner.TopRight)]
    public void ComputePlan_UserFlipInverts_ShouldFlipAndDotCorner(
        TrainOrientation orientation,
        bool expectedShouldFlip,
        BlockTrainRenderer.DotCorner expectedDot)
    {
        var plan = BlockTrainRenderer.ComputePlan(
            orientation, isLocoUserFlipped: true, locoPosition: 0, totalWagons: 0);

        Assert.Equal(expectedShouldFlip, plan.ShouldFlipLocoIcon);
        Assert.Equal(expectedDot, plan.DotCorner);
    }

    [Fact]
    public void ComputePlan_XOR_Idempotence_CanonicalAndUserFlipCancelOut()
    {
        var plan = BlockTrainRenderer.ComputePlan(
            TrainOrientation.HBackward, isLocoUserFlipped: true, locoPosition: 0, totalWagons: 0);
        Assert.False(plan.ShouldFlipLocoIcon);
        Assert.Equal(BlockTrainRenderer.DotCorner.TopRight, plan.DotCorner);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // WAGON ORDERING (loko prvá, vagóny "od loky von")
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputePlan_LocoFirst_WagonsInCanonicalOrder()
    {
        // locoPosition=0, total=4 → after=[0,1,2,3], before=[]
        // poradie "od loky von": [0, 1, 2, 3]
        var plan = BlockTrainRenderer.ComputePlan(
            TrainOrientation.HForward, false, locoPosition: 0, totalWagons: 4);

        Assert.Equal(new[] { 0, 1, 2, 3 }, plan.WagonIndicesInOrder);
    }

    [Fact]
    public void ComputePlan_LocoInMiddle_AfterFirstThenBeforeReversed()
    {
        // locoPosition=2, total=4: W0 W1 | loco | W2 W3
        // poradie "od loky von": [W2, W3, W1, W0]
        var plan = BlockTrainRenderer.ComputePlan(
            TrainOrientation.HForward, false, locoPosition: 2, totalWagons: 4);

        Assert.Equal(new[] { 2, 3, 1, 0 }, plan.WagonIndicesInOrder);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DOCKING (anchoring loky na head-edge)
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(TrainOrientation.HForward,  Dock.Right)]
    [InlineData(TrainOrientation.HBackward, Dock.Left)]
    [InlineData(TrainOrientation.VDown,     Dock.Right)]  // canonical Right → screen Bottom po 90° CW
    [InlineData(TrainOrientation.VUp,       Dock.Left)]   // canonical Left  → screen Top    po 90° CW
    public void ComputePlan_LocoDock_AnchorsLocoOnHeadEdge(
        TrainOrientation o, Dock expected)
    {
        var plan = BlockTrainRenderer.ComputePlan(o, false, 0, 0);
        Assert.Equal(expected, plan.LocoDock);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // OVERFLOW (+X indikátor)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputePlan_Overflow_HidesExcessWagons()
    {
        var plan = BlockTrainRenderer.ComputePlan(
            TrainOrientation.HForward, false, locoPosition: 0, totalWagons: 6, maxVisibleWagons: 4);

        Assert.Equal(4, plan.WagonIndicesInOrder.Count);
        Assert.Equal(2, plan.HiddenWagonsCount);
    }

    [Fact]
    public void ComputePlan_Overflow_ConfigurableMaxVisible()
    {
        var plan = BlockTrainRenderer.ComputePlan(
            TrainOrientation.HForward, false, locoPosition: 0, totalWagons: 10, maxVisibleWagons: 2);

        Assert.Equal(2, plan.WagonIndicesInOrder.Count);
        Assert.Equal(8, plan.HiddenWagonsCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EDGE CASES
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputePlan_ClampsNegativeLocoPosition()
    {
        var plan = BlockTrainRenderer.ComputePlan(
            TrainOrientation.HForward, false, locoPosition: -5, totalWagons: 3);

        Assert.Equal(new[] { 0, 1, 2 }, plan.WagonIndicesInOrder);
    }

    [Fact]
    public void ComputePlan_ClampsLocoPositionAboveTotal()
    {
        var plan = BlockTrainRenderer.ComputePlan(
            TrainOrientation.HForward, false, locoPosition: 100, totalWagons: 3);

        Assert.Equal(new[] { 2, 1, 0 }, plan.WagonIndicesInOrder);
    }

    [Fact]
    public void ComputePlan_NoWagons_EmptyLists_NoHidden()
    {
        var plan = BlockTrainRenderer.ComputePlan(
            TrainOrientation.VUp, false, locoPosition: 0, totalWagons: 0);

        Assert.Empty(plan.WagonIndicesInOrder);
        Assert.Equal(0, plan.HiddenWagonsCount);
    }

    [Fact]
    public void ComputePlan_NegativeTotal_IsClampedToZero()
    {
        var plan = BlockTrainRenderer.ComputePlan(
            TrainOrientation.HForward, false, locoPosition: 0, totalWagons: -10);

        Assert.Empty(plan.WagonIndicesInOrder);
        Assert.Equal(0, plan.HiddenWagonsCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CONSISTENCY INVARIANTS (regression guards)
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(TrainOrientation.HForward)]
    [InlineData(TrainOrientation.HBackward)]
    [InlineData(TrainOrientation.VDown)]
    [InlineData(TrainOrientation.VUp)]
    public void ComputePlan_VisibleCount_PlusHidden_EqualsTotalWagons(TrainOrientation o)
    {
        const int total = 7;
        const int max = 4;
        var plan = BlockTrainRenderer.ComputePlan(o, false, locoPosition: 3, totalWagons: total, maxVisibleWagons: max);

        int visible = plan.WagonIndicesInOrder.Count;
        Assert.Equal(total, visible + plan.HiddenWagonsCount);
        Assert.True(visible <= max);
    }

    [Theory]
    [InlineData(TrainOrientation.HForward,  false)]
    [InlineData(TrainOrientation.HBackward, false)]
    [InlineData(TrainOrientation.VDown,     false)]
    [InlineData(TrainOrientation.VUp,       false)]
    [InlineData(TrainOrientation.HForward,  true)]
    [InlineData(TrainOrientation.HBackward, true)]
    [InlineData(TrainOrientation.VDown,     true)]
    [InlineData(TrainOrientation.VUp,       true)]
    public void ComputePlan_AllWagonIndicesAreUniqueAndInRange(TrainOrientation o, bool flipped)
    {
        const int total = 5;
        var plan = BlockTrainRenderer.ComputePlan(o, flipped, locoPosition: 2, totalWagons: total);

        var all = plan.WagonIndicesInOrder.ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(all, idx => Assert.InRange(idx, 0, total - 1));
    }

    [Theory]
    [InlineData(TrainOrientation.HForward)]
    [InlineData(TrainOrientation.VDown)]
    public void ComputePlan_VerticalUsesWholeStackRotation_HorizontalDoesNot(TrainOrientation o)
    {
        var plan = BlockTrainRenderer.ComputePlan(o, false, 0, 0);
        double expected = o.IsVertical() ? 90 : 0;
        Assert.Equal(expected, plan.StackRotationDeg);
    }

    [Fact]
    public void ComputePlan_DotCorner_AlwaysMatchesShouldFlipLoco()
    {
        foreach (var o in new[] { TrainOrientation.HForward, TrainOrientation.HBackward, TrainOrientation.VDown, TrainOrientation.VUp })
        foreach (var flipped in new[] { false, true })
        {
            var plan = BlockTrainRenderer.ComputePlan(o, flipped, 0, 0);
            var expected = plan.ShouldFlipLocoIcon
                ? BlockTrainRenderer.DotCorner.TopLeft
                : BlockTrainRenderer.DotCorner.TopRight;
            Assert.Equal(expected, plan.DotCorner);
        }
    }
}






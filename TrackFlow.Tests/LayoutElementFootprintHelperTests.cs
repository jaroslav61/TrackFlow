using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class LayoutElementFootprintHelperTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(360, 0)]
    [InlineData(-90, 270)]
    [InlineData(89.6, 90)]
    public void NormalizeMarkerAngle_Works(double input, int expected)
    {
        Assert.Equal(expected, LayoutElementFootprintHelper.NormalizeMarkerAngle(input));
    }

    [Fact]
    public void GetBlockLength_Clamps()
    {
        var shortBlock = new BlockElement { BlockLengthCells = 0 };
        var longBlock = new BlockElement { BlockLengthCells = 999 };

        Assert.Equal(1, LayoutElementFootprintHelper.GetBlockLength(shortBlock));
        Assert.Equal(20, LayoutElementFootprintHelper.GetBlockLength(longBlock));
    }

    [Fact]
    public void GetFootprint_Block_Uses_Length_And_Rotation()
    {
        var horizontal = new BlockElement { BlockLengthCells = 4, Rotation = 0 };
        var vertical = new BlockElement { BlockLengthCells = 4, Rotation = 90 };

        Assert.Equal((96d, 24d), LayoutElementFootprintHelper.GetFootprint(horizontal, 24));
        Assert.Equal((24d, 96d), LayoutElementFootprintHelper.GetFootprint(vertical, 24));
    }

    [Fact]
    public void GetFootprint_Text_Uses_Cell_Dimensions()
    {
        var text = new TextElement { WidthInCells = 3, HeightInCells = 2 };

        Assert.Equal((72d, 48d), LayoutElementFootprintHelper.GetFootprint(text, 24));
    }

    [Fact]
    public void IsPointInside_Uses_HalfOpen_Bounds()
    {
        var element = new TrackSegmentElement { X = 24, Y = 24 };

        Assert.True(LayoutElementFootprintHelper.IsPointInside(element, 24, 24, 24));
        Assert.True(LayoutElementFootprintHelper.IsPointInside(element, 47.999, 47.999, 24));
        Assert.False(LayoutElementFootprintHelper.IsPointInside(element, 48, 24, 24));
        Assert.False(LayoutElementFootprintHelper.IsPointInside(element, 24, 48, 24));
    }

    [Fact]
    public void GetFootprint_Signal_Respects_Compact_Flag()
    {
        var signal = new SignalElement { SignalProfile = "2-aspect-main", Rotation = 0 };

        var compact = LayoutElementFootprintHelper.GetFootprint(signal, 24, compactTwoAspectSignals: true);
        var nonCompact = LayoutElementFootprintHelper.GetFootprint(signal, 24, compactTwoAspectSignals: false);

        Assert.Equal((24d, 24d), compact);
        Assert.Equal((24d, 48d), nonCompact);
    }
}


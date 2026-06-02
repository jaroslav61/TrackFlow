using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class SignalFootprintHelperTests
{
    [Theory]
    [InlineData("2-aspect", 2)]
    [InlineData("2-head", 2)]
    [InlineData("SK:5-aspect", 5)]
    [InlineData("profile_4", 4)]
    [InlineData(null, 3)]
    [InlineData("", 3)]
    [InlineData("unknown", 3)]
    public void ParseSignCount_Parses_Expected_Value(string? profile, int expected)
    {
        Assert.Equal(expected, SignalFootprintHelper.ParseSignCount(profile));
    }

    [Fact]
    public void GetFootprint_CompactTwoAspect_Returns_OneCell()
    {
        var signal = new SignalElement { SignalProfile = "2-aspect-main", Rotation = 0 };

        var fp = SignalFootprintHelper.GetFootprint(signal, 24, compactTwoAspect: true);

        Assert.Equal((24d, 24d), fp);
    }

    [Fact]
    public void GetFootprint_NonCompactTwoAspect_Returns_TwoCells_By_Rotation()
    {
        var signalV = new SignalElement { SignalProfile = "2-aspect-main", Rotation = 0 };
        var signalH = new SignalElement { SignalProfile = "2-aspect-main", Rotation = 90 };

        var fpV = SignalFootprintHelper.GetFootprint(signalV, 24, compactTwoAspect: false);
        var fpH = SignalFootprintHelper.GetFootprint(signalH, 24, compactTwoAspect: false);

        Assert.Equal((24d, 48d), fpV);
        Assert.Equal((48d, 24d), fpH);
    }

    [Fact]
    public void IsPointInside_Uses_Compact_Default()
    {
        var signal = new SignalElement { X = 48, Y = 72, SignalProfile = "2-aspect-shunt", Rotation = 0 };

        Assert.True(SignalFootprintHelper.IsPointInside(signal, 48, 72, 24));
        Assert.False(SignalFootprintHelper.IsPointInside(signal, 72, 72, 24));
    }
}


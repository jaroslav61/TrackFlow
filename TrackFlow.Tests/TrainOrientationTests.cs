using TrackFlow.Views.Shared;
using Xunit;

namespace TrackFlow.Tests;

public class TrainOrientationTests
{
    [Theory]
    [InlineData(TrainOrientation.HForward,  false, true)]
    [InlineData(TrainOrientation.HBackward, false, false)]
    [InlineData(TrainOrientation.VDown,     true,  true)]
    [InlineData(TrainOrientation.VUp,       true,  false)]
    public void Orientation_Properties_Match(TrainOrientation o, bool expectedVertical, bool expectedForward)
    {
        Assert.Equal(expectedVertical, o.IsVertical());
        Assert.Equal(expectedForward, o.IsForward());
    }

    [Theory]
    [InlineData(false, true,  TrainOrientation.HForward)]
    [InlineData(false, false, TrainOrientation.HBackward)]
    [InlineData(true,  true,  TrainOrientation.VDown)]
    [InlineData(true,  false, TrainOrientation.VUp)]
    public void From_BoolPair_ReturnsCorrectEnum(bool isVertical, bool isForward, TrainOrientation expected)
    {
        Assert.Equal(expected, TrainOrientationExtensions.From(isVertical, isForward));
    }

    [Fact]
    public void From_Roundtrip_PreservesBooleans()
    {
        foreach (var isV in new[] { false, true })
        foreach (var isF in new[] { false, true })
        {
            var o = TrainOrientationExtensions.From(isV, isF);
            Assert.Equal(isV, o.IsVertical());
            Assert.Equal(isF, o.IsForward());
        }
    }
}


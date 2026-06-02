using TrackFlow.Helpers;
using Xunit;

namespace TrackFlow.Tests;

public sealed class TelemetryFormattingTests
{
    [Fact]
    public void ResolveMainTrackCurrentMaximum_UsesSmallZ21Fallback_WhenConfiguredLimitIsNull()
    {
        var result = TelemetryFormatting.ResolveMainTrackCurrentMaximum(isBlackZ21: false, configuredLimitAmperes: null);

        Assert.Equal(TelemetryFormatting.SmallZ21MaxTrackCurrent, result);
    }

    [Fact]
    public void ResolveMainTrackCurrentMaximum_UsesBlackZ21Fallback_WhenConfiguredLimitIsNull()
    {
        var result = TelemetryFormatting.ResolveMainTrackCurrentMaximum(isBlackZ21: true, configuredLimitAmperes: null);

        Assert.Equal(TelemetryFormatting.BlackZ21MaxTrackCurrent, result);
    }

    [Fact]
    public void ResolveMainTrackCurrentMaximum_PrefersExplicitConfiguredOverride_WhenPositive()
    {
        var result = TelemetryFormatting.ResolveMainTrackCurrentMaximum(isBlackZ21: false, configuredLimitAmperes: 4.5d);

        Assert.Equal(4.5d, result);
    }
}


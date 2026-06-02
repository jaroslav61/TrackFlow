using System;
using System.Globalization;

namespace TrackFlow.Helpers;

public static class TelemetryFormatting
{
    public const double SmallZ21MaxTrackCurrent = 2.0d;
    public const double BlackZ21MaxTrackCurrent = 3.2d;

    private static readonly CultureInfo UiCulture = CultureInfo.InvariantCulture;

    public static double GetMainTrackCurrentMaximum(bool isBlackZ21)
        => isBlackZ21 ? BlackZ21MaxTrackCurrent : SmallZ21MaxTrackCurrent;

    public static double ResolveMainTrackCurrentMaximum(bool isBlackZ21, double? configuredLimitAmperes)
        => configuredLimitAmperes.HasValue && configuredLimitAmperes.Value > 0d
            ? configuredLimitAmperes.Value
            : GetMainTrackCurrentMaximum(isBlackZ21);

    public static string FormatCurrentAdaptive(double? currentInAmperes)
    {
        if (!currentInAmperes.HasValue)
            return "—";

        var val = currentInAmperes.Value;
        if (Math.Abs(val) < 1.0)
        {
            var ma = (int)Math.Round(val * 1000);
            return $"{ma} mA";
        }

        return $"{val.ToString("F2", UiCulture)} A";
    }

    public static string FormatCurrentLimit(double currentLimitInAmperes)
        => $"{currentLimitInAmperes.ToString("F1", UiCulture)} A";
}


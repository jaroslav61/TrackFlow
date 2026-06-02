using System;
using System.Globalization;

namespace TrackFlow.Services.Simulation;

/// <summary>
/// Resolves persisted layout scale labels to model scale divisors used by the simulator.
/// The returned value is the denominator in expressions like 1:87.
/// </summary>
public static class SimulationScaleResolver
{
    public const double DefaultScaleDivisor = 87.0;

    public static double ResolveScaleDivisor(string? scale)
    {
        if (string.IsNullOrWhiteSpace(scale))
            return DefaultScaleDivisor;

        var normalized = scale.Trim();
        return normalized.ToUpperInvariant() switch
        {
            "H0" or "HO" => 87.0,
            "TT" => 120.0,
            "N" => 160.0,
            "Z" => 220.0,
            _ => TryParseScaleDivisor(normalized, out var divisor) ? divisor : DefaultScaleDivisor
        };
    }

    private static bool TryParseScaleDivisor(string value, out double divisor)
    {
        divisor = 0;

        var ratioSeparatorIndex = value.IndexOf(':');
        if (ratioSeparatorIndex >= 0 && ratioSeparatorIndex < value.Length - 1)
            value = value[(ratioSeparatorIndex + 1)..];

        value = value.Trim().Replace(',', '.');
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return false;

        if (!double.IsFinite(parsed) || parsed < 1.0)
            return false;

        divisor = parsed;
        return true;
    }
}

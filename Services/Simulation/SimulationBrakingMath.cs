using System;

namespace TrackFlow.Services.Simulation;

/// <summary>
/// Stateless helper s čistou matematikou brzdenia / maximálnej vstupnej rýchlosti
/// pre simulačný engine. Extrahované 1:1 z
/// <see cref="TrackFlow.ViewModels.Operation.OperationViewModel"/> – mechanický presun
/// bez zmeny správania.
/// </summary>
internal static class SimulationBrakingMath
{
    /// <summary>
    /// Vypočíta brzdnú vzdialenosť v modelových milimetroch v aktuálnej mierke.
    /// </summary>
    /// <param name="speedKmh">Aktuálna rýchlosť v km/h.</param>
    /// <param name="decelerationKmhPerSec">Decelerácia v km/h za sekundu.</param>
    /// <param name="distanceScale">Mierka modelu (delitelˇ).</param>
    /// <returns>Brzdná vzdialenosť v milimetroch.</returns>
    public static double CalculateBrakingDistanceMm(double speedKmh, double decelerationKmhPerSec, double distanceScale)
    {
        if (speedKmh <= 0 || decelerationKmhPerSec <= 0)
            return 0;

        // t = v / a (čas do zastavenia)
        var timeToStopSec = speedKmh / decelerationKmhPerSec;

        // s = v * t / 2 (priemerná rýchlosť * čas)
        // v km/h -> modelové mm v mierke: (v * 1_000_000 / 3600) * t / 2 / Scale
        const double MmPerSecondPerKmh = 1_000_000.0 / 3600.0;
        var safeDistanceScale = NormalizeSimulationDistanceScale(distanceScale);

        var brakingDistanceMm = (speedKmh * MmPerSecondPerKmh * timeToStopSec / 2.0) / safeDistanceScale;
        return brakingDistanceMm;
    }

    /// <summary>
    /// FÁZA 3: Vypočíta maximálnu bezpečnú vstupnú rýchlosť na základe dostupnej brzdnej vzdialenosti.
    /// Inverzná funkcia k <see cref="CalculateBrakingDistanceMm"/>.
    /// </summary>
    /// <param name="availableBrakingDistanceMm">Dostupná brzdná vzdialenosť v milimetroch.</param>
    /// <param name="decelerationKmhPerSec">Decelerácia v km/h za sekundu.</param>
    /// <param name="distanceScale">Mierka modelu (delitelˇ).</param>
    /// <returns>Maximálna bezpečná vstupná rýchlosť v km/h.</returns>
    public static double CalculateMaxEntrySpeed(double availableBrakingDistanceMm, double decelerationKmhPerSec, double distanceScale)
    {
        if (availableBrakingDistanceMm <= 0 || decelerationKmhPerSec <= 0)
            return 0;

        const double MmPerSecondPerKmh = 1_000_000.0 / 3600.0;
        var safeDistanceScale = NormalizeSimulationDistanceScale(distanceScale);

        // s = (v * MmPerSecondPerKmh * t / 2) / Scale
        // t = v / a
        // s = (v * MmPerSecondPerKmh * v / a / 2) / Scale
        // s * Scale * 2 * a = v^2 * MmPerSecondPerKmh
        // v = sqrt((s * Scale * 2 * a) / MmPerSecondPerKmh)

        var vSquared = (availableBrakingDistanceMm * safeDistanceScale * 2.0 * decelerationKmhPerSec) / MmPerSecondPerKmh;
        var maxSpeedKmh = Math.Sqrt(Math.Max(0, vSquared));

        // Bezpečnostná rezerva: vráť 90% vypočítanej hodnoty
        return maxSpeedKmh * 0.90;
    }

    public static double NormalizeSimulationDistanceScale(double distanceScale)
        => double.IsFinite(distanceScale) && distanceScale >= 1.0
            ? distanceScale
            : SimulationScaleResolver.DefaultScaleDivisor;
}


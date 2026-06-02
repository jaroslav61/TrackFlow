using System;

namespace TrackFlow.Services.Simulation;

/// <summary>
/// Čistý matematický engine pre simuláciu plynulého pohybu lokomotívy.
/// Neobsahuje žiadne závislosti na DCC, UI alebo layout elementoch.
/// </summary>
public sealed class LocomotiveSimulationEngine
{
    private const double MmPerSecondPerKmh = 1_000_000.0 / 3600.0;

    /// <summary>
    /// Celková dĺžka dráhy v milimetroch.
    /// </summary>
    public double TotalLengthMm { get; }

    /// <summary>
    /// Krok akcelerácie/decelerácie v km/h za sekundu.
    /// </summary>
    public double AccelerationStepKmh { get; }

    /// <summary>
    /// Modelová mierka pohybu. Hodnota je menovateľ mierky, napr. H0 = 87 pre 1:87.
    /// </summary>
    public double DistanceScale { get; }

    /// <summary>
    /// Aktuálna vzdialenosť prejdená v milimetroch.
    /// </summary>
    public double CurrentDistanceMm { get; set; }

    /// <summary>
    /// Aktuálna rýchlosť v km/h.
    /// </summary>
    public double CurrentSpeedKmh { get; set; }

    /// <summary>
    /// Vytvorí novú inštanciu simulačného enginu.
    /// </summary>
    /// <param name="totalLengthMm">Celková dĺžka segmentu v milimetroch.</param>
    /// <param name="accelerationStepKmh">Krok akcelerácie/decelerácie v km/h za sekundu.</param>
    /// <param name="initialSpeedKmh">Počiatočná rýchlosť v km/h.</param>
    /// <param name="distanceScale">Menovateľ modelovej mierky, napr. H0 = 87.</param>
    public LocomotiveSimulationEngine(
        double totalLengthMm,
        double accelerationStepKmh,
        double initialSpeedKmh = 0.0,
        double distanceScale = SimulationScaleResolver.DefaultScaleDivisor)
    {
        TotalLengthMm = Math.Max(0.0, totalLengthMm);
        AccelerationStepKmh = Math.Max(0.0, accelerationStepKmh);
        CurrentSpeedKmh = Math.Clamp(initialSpeedKmh, 0.0, 100.0);
        DistanceScale = double.IsFinite(distanceScale) && distanceScale >= 1.0
            ? distanceScale
            : SimulationScaleResolver.DefaultScaleDivisor;
        CurrentDistanceMm = 0.0;
    }

    /// <summary>
    /// Aktualizuje stav simulácie o jeden časový krok.
    /// </summary>
    /// <param name="targetSpeedKmh">Cieľová rýchlosť v km/h.</param>
    /// <param name="deltaTimeSec">Časový krok v sekundách.</param>
    /// <returns>Výsledok aktualizácie simulácie.</returns>
    public SimulationUpdateResult Update(double targetSpeedKmh, double deltaTimeSec)
    {
        targetSpeedKmh = Math.Clamp(targetSpeedKmh, 0.0, 100.0);
        deltaTimeSec = Math.Max(0.0, deltaTimeSec);

        // Ramping - plynulá akcelerácia/decelerácia
        var maxStep = AccelerationStepKmh * deltaTimeSec;
        CurrentSpeedKmh = Approach(CurrentSpeedKmh, targetSpeedKmh, maxStep);

        // Výpočet prejdenej vzdialenosti v modelovej mierke:
        // DeltaMm = (RýchlosťKmH / 3.6) / Scale * DeltaTimeSec * 1000
        var deltaMm = (CurrentSpeedKmh * MmPerSecondPerKmh * deltaTimeSec) / DistanceScale;

        CurrentDistanceMm += deltaMm;

        var isAtEnd = CurrentDistanceMm >= TotalLengthMm;
        var progressRatio = TotalLengthMm > 0 ? Math.Clamp(CurrentDistanceMm / TotalLengthMm, 0.0, 1.0) : 1.0;

        return new SimulationUpdateResult(
            DistanceMm: CurrentDistanceMm,
            SpeedKmh: CurrentSpeedKmh,
            DeltaMm: deltaMm,
            IsAtEnd: isAtEnd,
            ProgressRatio: progressRatio);
    }

    /// <summary>
    /// Resetuje simuláciu na počiatočný stav.
    /// </summary>
    /// <param name="initialSpeedKmh">Počiatočná rýchlosť v km/h.</param>
    public void Reset(double initialSpeedKmh = 0.0)
    {
        CurrentDistanceMm = 0.0;
        CurrentSpeedKmh = Math.Clamp(initialSpeedKmh, 0.0, 100.0);
    }

    private static double Approach(double current, double target, double maxStep)
    {
        if (current < target)
            return Math.Min(current + maxStep, target);
        if (current > target)
            return Math.Max(current - maxStep, target);
        return current;
    }
}

/// <summary>
/// Výsledok jednej aktualizácie simulácie.
/// </summary>
/// <param name="DistanceMm">Aktuálna prejdená vzdialenosť v milimetroch.</param>
/// <param name="SpeedKmh">Aktuálna rýchlosť v km/h.</param>
/// <param name="DeltaMm">Delta vzdialenosti za tento krok v milimetroch.</param>
/// <param name="IsAtEnd">Príznak, či vlak dosiahol koniec dráhy.</param>
/// <param name="ProgressRatio">Pomer prejdenej dráhy (0.0 - 1.0).</param>
public readonly record struct SimulationUpdateResult(
    double DistanceMm,
    double SpeedKmh,
    double DeltaMm,
    bool IsAtEnd,
    double ProgressRatio);


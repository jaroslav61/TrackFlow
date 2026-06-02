using System;
using System.Collections.Generic;

namespace TrackFlow.Models;

public sealed class ProjectSettingsData
{
    public const double MinSimulationSpeedFactor = 1.0;
    public const double MaxSimulationSpeedFactor = 5.0;
    public const double DefaultSimulationSpeedFactor = 3.0;

    private double _simulationSpeedFactor = DefaultSimulationSpeedFactor;

    public int SchemaVersion { get; set; } = 1;

    // Override hodnoty: null = nepoužiť, prebrať z AppSettings defaultov
    public DccCentralType? DccCentralType { get; set; }
    public string? DccCentralHost { get; set; }
    public int? DccCentralPort { get; set; }
    public string? DccSerialPort { get; set; }
    public int? DccBaudRate { get; set; }
    public bool? AutoConnect { get; set; }
    /// <summary>
    /// Null = použiť globálne profily z AppSettings.
    /// Prázdny zoznam = projekt zámerne neobsahuje žiadne DCC profily.
    /// </summary>
    public List<DccCentralProfile>? DccCentralProfiles { get; set; }
    public Guid? SelectedDccCentralProfileId { get; set; }
    public string? Scale { get; set; }
    
    // Routes
    public bool? AutoRegenerateRoutes { get; set; }
    
    // RoutePathfinder settings
    /// <summary>Maximálny počet prvkov trasy medzi blokmi (predvolené: 15).</summary>
    public int MaxPathElements { get; set; } = 15;
    
    /// <summary>Maximálny počet výhybiek v trase (predvolené: 5).</summary>
    public int MaxTurnoutsInPath { get; set; } = 5;

    /// <summary>Koeficient zrýchlenia simulačného času pohybu vlakov (1.0 až 5.0, predvolené: 3.0).</summary>
    public double SimulationSpeedFactor
    {
        get => _simulationSpeedFactor;
        set => _simulationSpeedFactor = NormalizeSimulationSpeedFactor(value);
    }

    public static double NormalizeSimulationSpeedFactor(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return DefaultSimulationSpeedFactor;

        return Math.Clamp(value, MinSimulationSpeedFactor, MaxSimulationSpeedFactor);
    }

    // CORE: lokomotívy v projekte

}

using System;
using System.Collections.Generic;

namespace TrackFlow.Models;

public sealed class AppSettingsData
{
    public int SchemaVersion { get; set; } = 1;

    // UI preferencie používateľa
    public string Language { get; set; } = "sk-SK";
    public string AccentColor { get; set; } = "#1E88E5";
    public bool OpenLastProjectOnStartup { get; set; } = false;
    public int VisibleWagonsInTrain { get; set; } = 0; // 0 = všetky, 1-4 = konkrétny počet

    // Operation hint messages (route activation info/warnings)
    public bool EnableTransientRouteMessages { get; set; } = true;
    public bool ShowTelemetryInStatusBar { get; set; } = true;
    public int RouteMessageTtlSuccessMs { get; set; } = 1800;
    public int RouteMessageTtlInfoMs { get; set; } = 2500;
    public int RouteMessageTtlWarningMs { get; set; } = 3500;

    // Zoznam skonfigurovaných DCC centrál
    public List<DccCentralProfile> DccCentralProfiles { get; set; } = new();

    /// <summary>ID poslednej vybranej centrály v nastaveniach.</summary>
    public Guid? SelectedDccCentralProfileId { get; set; }

    // Staré defaulty – zachované pre spätnú kompatibilitu pri migrácii.
    public DccCentralType DefaultDccCentralType { get; set; } = DccCentralType.Z21Legacy;
    public string DefaultDccCentralHost { get; set; } = "192.168.0.111";
    public int DefaultDccCentralPort { get; set; } = 21105;
    public string DefaultDccSerialPort { get; set; } = string.Empty;
    public int DefaultDccBaudRate { get; set; } = 19200;
    public bool DefaultAutoConnect { get; set; } = false;
    public string DefaultScale { get; set; } = "H0";

    // Projektová navigácia
    public string? LastProjectPath { get; set; }
    public List<string> RecentProjectPaths { get; set; } = new();
}



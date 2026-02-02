namespace TrackFlow.Models;

public sealed class AppSettingsData
{
    public int SchemaVersion { get; set; } = 1;

    // UI preferencie používateľa
    public string Language { get; set; } = "sk-SK";
    public string AccentColor { get; set; } = "#1E88E5";

    // Defaulty (môžu byť neskôr prekrývané projektom)
    public DccCentralType DefaultDccCentralType { get; set; } = DccCentralType.Z21;
    public string DefaultDccCentralHost { get; set; } = "192.168.0.111";
    public int DefaultDccCentralPort { get; set; } = 21105;
    public bool DefaultAutoConnect { get; set; } = false;
    public string DefaultScale { get; set; } = "H0";

    // Projektová navigácia
    public string? LastProjectPath { get; set; }
}

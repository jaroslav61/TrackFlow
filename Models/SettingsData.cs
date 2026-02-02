namespace TrackFlow.Models;

public sealed class SettingsData
{
    public string DccCentralHost { get; set; } = "192.168.0.111";
    public int DccCentralPort { get; set; } = 21105;
    public bool AutoConnect { get; set; } = false;

    public string Language { get; set; } = "sk-SK";
    public string Scale { get; set; } = "H0";
    public string AccentColor { get; set; } = "#1E88E5";
}

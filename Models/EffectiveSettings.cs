namespace TrackFlow.Models;

public sealed class EffectiveSettings
{
    public string Language { get; init; } = "sk-SK";
    public string AccentColor { get; init; } = "#1E88E5";

    public DccCentralType DccCentralType { get; init; } = DccCentralType.Z21;

    public string DccCentralHost { get; init; } = "192.168.0.111";
    public int DccCentralPort { get; init; } = 21105;
    public bool AutoConnect { get; init; }
    public string Scale { get; init; } = "H0";

    public static EffectiveSettings Merge(AppSettingsData app, ProjectSettingsData? project)
    {
        return new EffectiveSettings
        {
            Language = app.Language,
            AccentColor = app.AccentColor,
            DccCentralType = project?.DccCentralType ?? app.DefaultDccCentralType,
            DccCentralHost = project?.DccCentralHost ?? app.DefaultDccCentralHost,
            DccCentralPort = project?.DccCentralPort ?? app.DefaultDccCentralPort,
            AutoConnect = project?.AutoConnect ?? app.DefaultAutoConnect,
            Scale = project?.Scale ?? app.DefaultScale
        };
    }
}

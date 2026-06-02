namespace TrackFlow.Models;

public sealed class LocoSpeedProfilePoint
{
    public int Step { get; set; }
    public string Direction { get; set; } = string.Empty;
    public double TimeSeconds { get; set; }
    public double RawSpeedKmh { get; set; }
    public double CalculatedSpeedKmh { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsManual { get; set; }
}


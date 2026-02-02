namespace TrackFlow.Models;

public sealed class CabAssignment
{
    public string CabId { get; set; } = "cab-1";

    /// <summary>
    /// Referencia na lokomotívu (LocoRecord.Id). Null = neobsadené.
    /// </summary>
    public string? LocoId { get; set; }
}
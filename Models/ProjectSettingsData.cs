using System.Collections.Generic;

namespace TrackFlow.Models;

public sealed class ProjectSettingsData
{
    public int SchemaVersion { get; set; } = 1;

    // Override hodnoty: null = nepoužiť, prebrať z AppSettings defaultov
    public DccCentralType? DccCentralType { get; set; }
    public string? DccCentralHost { get; set; }
    public int? DccCentralPort { get; set; }
    public bool? AutoConnect { get; set; }
    public string? Scale { get; set; }

    // CORE: lokomotívy v projekte
    public List<LocoRecord> Locomotives { get; set; } = new();
    public List<Wagon> Wagons { get; set; } = new();
}

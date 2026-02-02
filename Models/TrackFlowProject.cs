using System.Collections.Generic;

namespace TrackFlow.Models;

public sealed class TrackFlowProject
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Indikuje neuložené zmeny projektu (UI môže zobraziť * pri názve).
    /// </summary>
    public bool IsDirty { get; set; }

    public ProjectSettingsData Settings { get; set; } = new();

    public List<LocoRecord> Locomotives { get; set; } = new();
    public LayoutStub Layout { get; set; } = new();
    public List<CabAssignment> CabAssignments { get; set; } = new();
}
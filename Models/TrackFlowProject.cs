using System.Collections.Generic;
using TrackFlow.Models.Layout;

namespace TrackFlow.Models;

public sealed class TrackFlowProject
{
    public int SchemaVersion { get; set; } = 3;

    /// <summary>Indikuje neuložené zmeny projektu (UI môže zobraziť * pri názve).</summary>
    public bool IsDirty { get; set; }

    public ProjectSettingsData Settings { get; set; } = new();

    public List<LocoRecord> Locomotives { get; set; } = new();
    public List<Wagon> Wagons { get; set; } = new();

    /// <summary>Model koľajiska (editor rozloženia + prevádzka).</summary>
    public TrackLayout Layout { get; set; } = new();

    public List<CabAssignment> CabAssignments { get; set; } = new();
}
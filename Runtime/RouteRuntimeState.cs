using System;
using System.Collections.Generic;

namespace TrackFlow.Runtime;

internal enum RouteRuntimeLifecycleState
{
    Active,
    Waiting,
    Yielding,
    Completed,
    Failed,
    EmergencyStopped
}

internal sealed class RouteTraversalWaitState
{
    public string RouteId { get; init; } = string.Empty;
    public string BlockId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime EnteredAtUtc { get; set; }
}

internal sealed class RouteReservationWindowState
{
    public HashSet<string> PathElementIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> BlockIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int? LeadSegmentIndex { get; set; }
    public bool KeepPreviousSegmentActive { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

internal sealed class RouteTailClearRuntimeState
{
    public string? SourceBlockId { get; set; }
    public string? TargetBlockId { get; set; }
    public bool BoundaryEntryTriggered { get; set; }
    public DateTime? BoundaryEntryTriggeredAtUtc { get; set; }
    public bool TailClearTriggered { get; set; }
    public DateTime? TailClearTriggeredAtUtc { get; set; }
    public DateTime? LastResetAtUtc { get; set; }
}

internal enum RouteFrontierPublisherKind
{
    Initialize,
    TraversalAdvance,
    TraversalWindowRefresh,
    WaitEnter,
    WaitExit,
    TailStateUpdate,
    Clear,
    DirectSet
}

internal sealed class TraversalFrontierSnapshot
{
    public string RouteId { get; set; } = string.Empty;
    public IReadOnlyList<string> TraversalBlockIds { get; set; } = Array.Empty<string>();
    public int CurrentTraversalIndex { get; set; }
    public string? CurrentBlockId { get; set; }
    public int LeadTraversalIndex { get; set; }
    public IReadOnlyList<string> FrontierBlockIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> FrontierPathElementIds { get; set; } = Array.Empty<string>();
    public bool IsWaiting { get; set; }
    public string? WaitingBlockId { get; set; }
    public string? WaitingReason { get; set; }
    public string? TailClearSourceBlockId { get; set; }
    public string? TailClearTargetBlockId { get; set; }
    public bool TailClearTriggered { get; set; }
    public bool BoundaryEntryTriggered { get; set; }
    public IReadOnlyList<string> ProceedCorridorBlockIds { get; set; } = Array.Empty<string>();
    public RouteFrontierPublisherKind PublisherKind { get; set; }
}

internal sealed class RouteFrontierState
{
    public string RouteId { get; set; } = string.Empty;
    public IReadOnlyList<string> TraversalBlockIds { get; set; } = Array.Empty<string>();
    public int CurrentTraversalIndex { get; set; }
    public string? CurrentBlockId { get; set; }
    public int LeadTraversalIndex { get; set; }
    public IReadOnlyList<string> FrontierBlockIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> FrontierPathElementIds { get; set; } = Array.Empty<string>();
    public bool IsWaiting { get; set; }
    public string? WaitingBlockId { get; set; }
    public string? WaitingReason { get; set; }
    public string? TailClearSourceBlockId { get; set; }
    public string? TailClearTargetBlockId { get; set; }
    public bool TailClearTriggered { get; set; }
    public bool BoundaryEntryTriggered { get; set; }
    public IReadOnlyList<string> ProceedCorridorBlockIds { get; set; } = Array.Empty<string>();
    public long Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public RouteFrontierPublisherKind PublisherKind { get; set; }
}

internal sealed class RouteRuntimeDiagnosticsState
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? TraversalUpdatedAtUtc { get; set; }
    public DateTime? WaitStateChangedAtUtc { get; set; }
    public DateTime? ReservationWindowUpdatedAtUtc { get; set; }
    public DateTime? TailStateChangedAtUtc { get; set; }
}

internal sealed class RouteRuntimeState
{
    public string RouteId { get; init; } = string.Empty;
    public string? OwnerLocomotiveId { get; set; }
    public RouteRuntimeLifecycleState LifecycleState { get; set; } = RouteRuntimeLifecycleState.Active;
    public List<string> TraversalBlockIds { get; set; } = new();
    public int CurrentTraversalIndex { get; set; }
    public string? CurrentBlockId { get; set; }
    public RouteTraversalWaitState? WaitState { get; set; }
    public RouteReservationWindowState ReservationWindow { get; set; } = new();
    public RouteTailClearRuntimeState TailClearState { get; set; } = new();
    public bool ReservationAdvanceInProgress { get; set; }
    public string? LastAdvanceCurrentBlockId { get; set; }
    public string? LastAdvanceNextBlockId { get; set; }
    public RouteRuntimeDiagnosticsState Diagnostics { get; set; } = new();

    public string? WaitingBlockId => WaitState?.BlockId;
    public string? WaitingReason => WaitState?.Reason;
    public DateTime? WaitingSinceUtc => WaitState?.EnteredAtUtc;
}


using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using TrackFlow.Services;

namespace TrackFlow.Runtime;

internal sealed class RuntimeStateRegistry
{
    private readonly HashSet<string> _activeRouteIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RouteRuntimeState> _runtimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _frontierSync = new();
    private readonly Dictionary<string, RouteFrontierState> _routeFrontiers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> ActiveRouteIds => _activeRouteIds;
    public int ActiveRouteCount => _activeRouteIds.Count;
    public IEnumerable<string> WaitingRouteIds => _runtimes.Values
        .Where(runtime => runtime.WaitState != null)
        .Select(runtime => runtime.RouteId)
        .ToList();

    internal ISet<string> MutableActiveRouteIds => _activeRouteIds;

    public void Clear()
    {
        _activeRouteIds.Clear();
        _runtimes.Clear();
        ClearAllRouteFrontiers();
    }

    public bool IsRouteActive(string routeId)
        => !string.IsNullOrWhiteSpace(routeId) && _activeRouteIds.Contains(routeId);

    public RouteRuntimeState RegisterOrCreateRuntime(
        string routeId,
        string? ownerLocomotiveId = null,
        IEnumerable<string>? traversalBlockIds = null,
        int currentTraversalIndex = 0,
        string? currentBlockId = null)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            throw new ArgumentException("Route id must not be empty.", nameof(routeId));

        _activeRouteIds.Add(routeId);
        var runtime = EnsureRuntime(routeId);
        runtime.LifecycleState = RouteRuntimeLifecycleState.Active;
        runtime.CurrentTraversalIndex = currentTraversalIndex;

        if (!string.IsNullOrWhiteSpace(ownerLocomotiveId))
            runtime.OwnerLocomotiveId = ownerLocomotiveId;

        if (traversalBlockIds != null)
        {
            runtime.TraversalBlockIds = traversalBlockIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(currentBlockId))
            runtime.CurrentBlockId = currentBlockId;

        Touch(runtime, touchedAtUtc: DateTime.UtcNow, touchTraversal: true);
        return runtime;
    }

    public RouteRuntimeState? GetRuntime(string routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return null;

        return _runtimes.TryGetValue(routeId, out var runtime)
            ? runtime
            : null;
    }

    public bool TryGetRuntime(string routeId, out RouteRuntimeState? runtime)
    {
        runtime = GetRuntime(routeId);
        return runtime != null;
    }

    public bool RemoveRuntime(string routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return false;

        _activeRouteIds.Remove(routeId);
        ClearRouteFrontier(routeId);
        return _runtimes.Remove(routeId);
    }

    public RouteFrontierState? GetRouteFrontierState(string routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return null;

        lock (_frontierSync)
        {
            return _routeFrontiers.TryGetValue(routeId, out var state)
                ? CloneFrontierState(state)
                : null;
        }
    }

    public bool TryGetRouteFrontierState(string routeId, out RouteFrontierState? state)
    {
        state = GetRouteFrontierState(routeId);
        return state != null;
    }

    public IReadOnlyCollection<RouteFrontierState> EnumerateActiveFrontiers()
    {
        lock (_frontierSync)
        {
            return _routeFrontiers.Values
                .Select(CloneFrontierState)
                .ToList()
                .AsReadOnly();
        }
    }

    public bool SetRouteFrontierState(RouteFrontierState state)
    {
        if (state == null)
            return false;

        return PublishFrontierCore(
            NormalizeFrontierState(state, state.PublisherKind == default ? RouteFrontierPublisherKind.DirectSet : state.PublisherKind),
            diagnosticsSource: "direct-set");
    }

    public bool PublishTraversalFrontier(TraversalFrontierSnapshot snapshot)
    {
        if (snapshot == null)
            return false;

        var normalized = NormalizeFrontierState(new RouteFrontierState
        {
            RouteId = snapshot.RouteId,
            TraversalBlockIds = snapshot.TraversalBlockIds,
            CurrentTraversalIndex = snapshot.CurrentTraversalIndex,
            CurrentBlockId = snapshot.CurrentBlockId,
            LeadTraversalIndex = snapshot.LeadTraversalIndex,
            FrontierBlockIds = snapshot.FrontierBlockIds,
            FrontierPathElementIds = snapshot.FrontierPathElementIds,
            IsWaiting = snapshot.IsWaiting,
            WaitingBlockId = snapshot.WaitingBlockId,
            WaitingReason = snapshot.WaitingReason,
            TailClearSourceBlockId = snapshot.TailClearSourceBlockId,
            TailClearTargetBlockId = snapshot.TailClearTargetBlockId,
            TailClearTriggered = snapshot.TailClearTriggered,
            BoundaryEntryTriggered = snapshot.BoundaryEntryTriggered,
            ProceedCorridorBlockIds = snapshot.ProceedCorridorBlockIds,
            PublisherKind = snapshot.PublisherKind
        }, snapshot.PublisherKind);

        return PublishFrontierCore(normalized, diagnosticsSource: "traversal-publish");
    }

    public bool ClearRouteFrontier(string routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return false;

        lock (_frontierSync)
        {
            if (!_routeFrontiers.Remove(routeId))
                return false;
        }

        DiagnoseFrontier(
            routeId,
            "frontier-clear",
            "source=[clear-route-frontier]",
            DiagnosticLevel.Info);
        return true;
    }

    public void ClearAllRouteFrontiers()
    {
        string[] clearedRouteIds;
        lock (_frontierSync)
        {
            if (_routeFrontiers.Count == 0)
                return;

            clearedRouteIds = _routeFrontiers.Keys.ToArray();
            _routeFrontiers.Clear();
        }

        foreach (var routeId in clearedRouteIds)
        {
            DiagnoseFrontier(
                routeId,
                "frontier-clear",
                "source=[clear-all-frontiers]",
                DiagnosticLevel.Info);
        }
    }

    public IReadOnlyList<string> ValidateFrontierSnapshot(RouteFrontierState? state)
    {
        var errors = ValidateFrontierSnapshotCore(state);
        if (errors.Count == 0)
            return Array.Empty<string>();

        var routeId = state?.RouteId ?? string.Empty;
        var detail = string.Join("; ", errors);
        DiagnoseFrontier(routeId, "frontier-snapshot-invalid", detail, DiagnosticLevel.Warning);
        Debug.Assert(false, $"Invalid frontier snapshot for route '{routeId}': {detail}");
        return errors.AsReadOnly();
    }

    public IEnumerable<RouteRuntimeState> EnumerateActiveRuntimes()
    {
        foreach (var routeId in _activeRouteIds)
        {
            if (_runtimes.TryGetValue(routeId, out var runtime))
                yield return runtime;
        }
    }

    public void UpdateTraversalState(
        string routeId,
        int currentTraversalIndex,
        string? currentBlockId,
        string? ownerLocomotiveId = null,
        IEnumerable<string>? traversalBlockIds = null)
    {
        var runtime = EnsureRuntime(routeId);
        _activeRouteIds.Add(routeId);
        runtime.LifecycleState = RouteRuntimeLifecycleState.Active;
        runtime.CurrentTraversalIndex = currentTraversalIndex;
        runtime.CurrentBlockId = currentBlockId;

        if (!string.IsNullOrWhiteSpace(ownerLocomotiveId))
            runtime.OwnerLocomotiveId = ownerLocomotiveId;

        if (traversalBlockIds != null)
        {
            runtime.TraversalBlockIds = traversalBlockIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
        }

        Touch(runtime, touchedAtUtc: DateTime.UtcNow, touchTraversal: true);
    }

    public void SetLifecycleState(string routeId, RouteRuntimeLifecycleState lifecycleState)
    {
        var runtime = EnsureRuntime(routeId);
        runtime.LifecycleState = lifecycleState;
        Touch(runtime, touchedAtUtc: DateTime.UtcNow);
    }

    public void SetOwnerLocomotiveId(string routeId, string? ownerLocomotiveId)
    {
        var runtime = EnsureRuntime(routeId);
        runtime.OwnerLocomotiveId = ownerLocomotiveId;
        Touch(runtime, touchedAtUtc: DateTime.UtcNow);
    }

    public void SetReservationWindow(
        string routeId,
        IEnumerable<string> pathElementIds,
        IEnumerable<string> blockIds,
        int? leadSegmentIndex,
        bool keepPreviousSegmentActive)
    {
        var runtime = EnsureRuntime(routeId);
        _activeRouteIds.Add(routeId);
        var touchedAtUtc = DateTime.UtcNow;
        runtime.ReservationWindow = new RouteReservationWindowState
        {
            PathElementIds = pathElementIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            BlockIds = blockIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            LeadSegmentIndex = leadSegmentIndex,
            KeepPreviousSegmentActive = keepPreviousSegmentActive,
            UpdatedAtUtc = touchedAtUtc
        };

        Touch(runtime, touchedAtUtc, touchReservationWindow: true);
    }

    public bool TryGetReservationWindow(string routeId, out RouteReservationWindowState? reservationWindow)
    {
        reservationWindow = null;
        var runtime = GetRuntime(routeId);
        if (runtime == null)
            return false;

        reservationWindow = runtime.ReservationWindow;
        return reservationWindow.PathElementIds.Count > 0 || reservationWindow.BlockIds.Count > 0;
    }

    public bool TryGetWaitState(string routeId, out RouteTraversalWaitState? waitState)
    {
        waitState = GetRuntime(routeId)?.WaitState;
        return waitState != null;
    }

    public bool HasWaitState(string routeId)
        => GetRuntime(routeId)?.WaitState != null;

    public DateTime? GetWaitSinceUtc(string routeId)
        => GetRuntime(routeId)?.WaitState?.EnteredAtUtc;

    public bool EnterWaitState(string routeId, string blockId, string reason, DateTime enteredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(routeId) || string.IsNullOrWhiteSpace(blockId))
            return false;

        var runtime = EnsureRuntime(routeId);
        _activeRouteIds.Add(routeId);

        if (runtime.WaitState != null
            && string.Equals(runtime.WaitState.BlockId, blockId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(runtime.WaitState.Reason, reason, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        runtime.LifecycleState = RouteRuntimeLifecycleState.Waiting;
        runtime.WaitState = new RouteTraversalWaitState
        {
            RouteId = routeId,
            BlockId = blockId,
            Reason = reason,
            EnteredAtUtc = enteredAtUtc
        };

        Touch(runtime, touchedAtUtc: enteredAtUtc, touchWaitState: true);
        return true;
    }

    public bool ExitWaitState(string routeId)
    {
        var runtime = GetRuntime(routeId);
        if (runtime?.WaitState == null)
            return false;

        runtime.WaitState = null;
        Touch(runtime, touchedAtUtc: DateTime.UtcNow, touchWaitState: true);
        return true;
    }

    public void ResetWaitStateToActive(string routeId)
    {
        var runtime = EnsureRuntime(routeId);
        runtime.LifecycleState = RouteRuntimeLifecycleState.Active;
        runtime.WaitState = null;
        Touch(runtime, touchedAtUtc: DateTime.UtcNow, touchWaitState: true);
    }

    public void ResetTailClearState(string routeId, string? sourceBlockId, string? targetBlockId)
    {
        var runtime = EnsureRuntime(routeId);
        var touchedAtUtc = DateTime.UtcNow;
        runtime.TailClearState.SourceBlockId = sourceBlockId;
        runtime.TailClearState.TargetBlockId = targetBlockId;
        runtime.TailClearState.BoundaryEntryTriggered = false;
        runtime.TailClearState.BoundaryEntryTriggeredAtUtc = null;
        runtime.TailClearState.TailClearTriggered = false;
        runtime.TailClearState.TailClearTriggeredAtUtc = null;
        runtime.TailClearState.LastResetAtUtc = touchedAtUtc;
        Touch(runtime, touchedAtUtc, touchTailState: true);
    }

    public void MarkBoundaryEntry(string routeId, string? sourceBlockId, string? targetBlockId, DateTime enteredAtUtc)
    {
        var runtime = EnsureRuntime(routeId);
        runtime.TailClearState.SourceBlockId = sourceBlockId;
        runtime.TailClearState.TargetBlockId = targetBlockId;
        runtime.TailClearState.BoundaryEntryTriggered = true;
        runtime.TailClearState.BoundaryEntryTriggeredAtUtc = enteredAtUtc;
        Touch(runtime, enteredAtUtc, touchTailState: true);
    }

    public void MarkTailClear(string routeId, string? sourceBlockId, string? targetBlockId, DateTime clearedAtUtc)
    {
        var runtime = EnsureRuntime(routeId);
        runtime.TailClearState.SourceBlockId = sourceBlockId;
        runtime.TailClearState.TargetBlockId = targetBlockId;
        runtime.TailClearState.TailClearTriggered = true;
        runtime.TailClearState.TailClearTriggeredAtUtc = clearedAtUtc;
        Touch(runtime, clearedAtUtc, touchTailState: true);
    }

    public void BeginReservationAdvance(string routeId, string? currentBlockId, string? nextBlockId)
    {
        var runtime = EnsureRuntime(routeId);
        runtime.ReservationAdvanceInProgress = true;
        runtime.LastAdvanceCurrentBlockId = currentBlockId;
        runtime.LastAdvanceNextBlockId = nextBlockId;
        Touch(runtime, touchedAtUtc: DateTime.UtcNow);
    }

    public bool TryBeginReservationAdvance(string routeId, string? currentBlockId, string? nextBlockId)
    {
        var runtime = GetRuntime(routeId);
        if (runtime == null)
            return false;

        if (runtime.ReservationAdvanceInProgress)
            return false;

        if (string.Equals(runtime.LastAdvanceCurrentBlockId, currentBlockId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(runtime.LastAdvanceNextBlockId, nextBlockId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        BeginReservationAdvance(routeId, currentBlockId, nextBlockId);
        return true;
    }

    public void EndReservationAdvance(string routeId)
    {
        var runtime = GetRuntime(routeId);
        if (runtime == null)
            return;

        runtime.ReservationAdvanceInProgress = false;
        Touch(runtime, touchedAtUtc: DateTime.UtcNow);
    }

    private RouteRuntimeState EnsureRuntime(string routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            throw new ArgumentException("Route id must not be empty.", nameof(routeId));

        if (_runtimes.TryGetValue(routeId, out var runtime))
            return runtime;

        runtime = new RouteRuntimeState
        {
            RouteId = routeId
        };

        _runtimes[routeId] = runtime;
        return runtime;
    }

    private bool PublishFrontierCore(RouteFrontierState candidate, string diagnosticsSource)
    {
        var errors = ValidateFrontierSnapshotCore(candidate);
        if (errors.Count > 0)
        {
            var detail = new StringBuilder()
                .Append("source=[").Append(diagnosticsSource).Append("]")
                .Append(", errors=[").Append(string.Join("; ", errors)).Append(']')
                .ToString();
            DiagnoseFrontier(candidate.RouteId, "frontier-snapshot-invalid", detail, DiagnosticLevel.Warning);
            Debug.Assert(false, $"Invalid frontier snapshot for route '{candidate.RouteId}': {string.Join("; ", errors)}");
            return false;
        }

        RouteFrontierState published;
        lock (_frontierSync)
        {
            var nextVersion = _routeFrontiers.TryGetValue(candidate.RouteId, out var existing)
                ? existing.Version + 1
                : 1;

            published = CloneFrontierState(candidate);
            published.Version = nextVersion;
            published.UpdatedAtUtc = DateTime.UtcNow;
            _routeFrontiers[candidate.RouteId] = published;
        }

        DiagnoseFrontier(
            published.RouteId,
            "frontier-publish-snapshot",
            $"source=[{diagnosticsSource}], publisher=[{published.PublisherKind}], version=[{published.Version}], current=[{published.CurrentBlockId ?? string.Empty}], lead=[{published.LeadTraversalIndex}], frontier=[{string.Join(",", published.FrontierBlockIds)}]",
            DiagnosticLevel.Info);
        return true;
    }

    private static RouteFrontierState NormalizeFrontierState(RouteFrontierState source, RouteFrontierPublisherKind publisherKind)
    {
        var traversalBlockIds = NormalizeSequence(source.TraversalBlockIds);
        var frontierBlockIds = NormalizeSequence(source.FrontierBlockIds);
        var frontierPathElementIds = NormalizeSequence(source.FrontierPathElementIds);
        var proceedCorridorBlockIds = NormalizeSequence(source.ProceedCorridorBlockIds);

        return new RouteFrontierState
        {
            RouteId = source.RouteId,
            TraversalBlockIds = WrapReadOnly(traversalBlockIds),
            CurrentTraversalIndex = source.CurrentTraversalIndex,
            CurrentBlockId = source.CurrentBlockId,
            LeadTraversalIndex = source.LeadTraversalIndex,
            FrontierBlockIds = WrapReadOnly(frontierBlockIds),
            FrontierPathElementIds = WrapReadOnly(frontierPathElementIds),
            IsWaiting = source.IsWaiting,
            WaitingBlockId = source.WaitingBlockId,
            WaitingReason = source.WaitingReason,
            TailClearSourceBlockId = source.TailClearSourceBlockId,
            TailClearTargetBlockId = source.TailClearTargetBlockId,
            TailClearTriggered = source.TailClearTriggered,
            BoundaryEntryTriggered = source.BoundaryEntryTriggered,
            ProceedCorridorBlockIds = WrapReadOnly(proceedCorridorBlockIds),
            Version = source.Version,
            UpdatedAtUtc = source.UpdatedAtUtc,
            PublisherKind = publisherKind
        };
    }

    private static RouteFrontierState CloneFrontierState(RouteFrontierState source)
        => NormalizeFrontierState(source, source.PublisherKind);

    private static List<string> NormalizeSequence(IReadOnlyList<string>? values)
    {
        if (values == null || values.Count == 0)
            return new List<string>();

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> WrapReadOnly(List<string> values)
        => new ReadOnlyCollection<string>(values);

    private static List<string> ValidateFrontierSnapshotCore(RouteFrontierState? state)
    {
        var errors = new List<string>();
        if (state == null)
        {
            errors.Add("state-null");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(state.RouteId))
            errors.Add("route-id-empty");

        var traversalBlockIds = state.TraversalBlockIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (traversalBlockIds.Count == 0)
            errors.Add("traversal-block-ids-empty");

        if (traversalBlockIds.Count > 0)
        {
            if (state.CurrentTraversalIndex < 0 || state.CurrentTraversalIndex >= traversalBlockIds.Count)
                errors.Add("current-traversal-index-out-of-range");
            if (state.LeadTraversalIndex < 0 || state.LeadTraversalIndex >= traversalBlockIds.Count)
                errors.Add("lead-traversal-index-out-of-range");

            if (!string.IsNullOrWhiteSpace(state.CurrentBlockId)
                && state.CurrentTraversalIndex >= 0
                && state.CurrentTraversalIndex < traversalBlockIds.Count
                && !string.Equals(traversalBlockIds[state.CurrentTraversalIndex], state.CurrentBlockId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("current-block-id-mismatch");
            }

            if (!string.IsNullOrWhiteSpace(state.CurrentBlockId)
                && !traversalBlockIds.Any(id => string.Equals(id, state.CurrentBlockId, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("current-block-id-not-in-traversal");
            }
        }

        var frontierBlockIds = state.FrontierBlockIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (frontierBlockIds.Count == 0)
            errors.Add("frontier-block-ids-empty");

        if (!string.IsNullOrWhiteSpace(state.CurrentBlockId)
            && frontierBlockIds.Count > 0
            && !frontierBlockIds.Any(id => string.Equals(id, state.CurrentBlockId, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("current-block-id-not-in-frontier");
        }

        if (state.IsWaiting)
        {
            if (string.IsNullOrWhiteSpace(state.WaitingReason))
                errors.Add("waiting-reason-empty");

            if (!string.IsNullOrWhiteSpace(state.WaitingBlockId)
                && frontierBlockIds.Count > 0
                && !frontierBlockIds.Any(id => string.Equals(id, state.WaitingBlockId, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("waiting-block-id-outside-frontier");
            }
        }
        else if (!string.IsNullOrWhiteSpace(state.WaitingBlockId) || !string.IsNullOrWhiteSpace(state.WaitingReason))
        {
            errors.Add("waiting-fields-present-while-not-waiting");
        }

        if (traversalBlockIds.Count > 0 && frontierBlockIds.Count > 0)
        {
            var lastIndex = -1;
            foreach (var frontierBlockId in frontierBlockIds)
            {
                var currentIndex = traversalBlockIds.FindIndex(id => string.Equals(id, frontierBlockId, StringComparison.OrdinalIgnoreCase));
                if (currentIndex < 0)
                {
                    errors.Add($"frontier-block-not-in-traversal:{frontierBlockId}");
                    continue;
                }

                if (currentIndex < lastIndex)
                    errors.Add("frontier-block-ids-unordered");

                lastIndex = currentIndex;
            }
        }

        return errors;
    }

    private static void DiagnoseFrontier(string? routeId, string state, string detail, DiagnosticLevel level)
    {
        _ = routeId;
        _ = state;
        _ = detail;
        _ = level;
    }

    private static void Touch(
        RouteRuntimeState runtime,
        DateTime touchedAtUtc,
        bool touchTraversal = false,
        bool touchWaitState = false,
        bool touchReservationWindow = false,
        bool touchTailState = false)
    {
        runtime.Diagnostics.LastUpdatedAtUtc = touchedAtUtc;

        if (touchTraversal)
            runtime.Diagnostics.TraversalUpdatedAtUtc = touchedAtUtc;
        if (touchWaitState)
            runtime.Diagnostics.WaitStateChangedAtUtc = touchedAtUtc;
        if (touchReservationWindow)
            runtime.Diagnostics.ReservationWindowUpdatedAtUtc = touchedAtUtc;
        if (touchTailState)
            runtime.Diagnostics.TailStateChangedAtUtc = touchedAtUtc;
    }
}


using System;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models.Layout;
using TrackFlow.Runtime;

namespace TrackFlow.Services.Runtime;

internal sealed class TraversalEngineCallbacks
{
    public required Func<TrackLayout, RouteDefinition, string, string, IEnumerable<string>> ResolveSegmentPathIds { get; init; }
}

internal sealed record InitializeTraversalRequest(
    string RouteId,
    string? CurrentBlockId,
    IEnumerable<string>? TraversalBlockIds = null,
    string? OwnerLocomotiveId = null);

internal sealed record AdvanceTraversalRequest(
    string RouteId,
    int SegmentIndex,
    string CurrentBlockId,
    string? OwnerLocomotiveId = null,
    IEnumerable<string>? TraversalBlockIds = null);

internal sealed record TraversalWindowRequest(
    TrackLayout Layout,
    RouteDefinition Route,
    IReadOnlyList<string> TraversalBlockIds,
    int LeadSegmentIndex,
    bool KeepPreviousSegmentActive);

internal sealed class TraversalEngine
{
    private readonly RuntimeStateRegistry _runtimeRegistry;
    private readonly TraversalEngineCallbacks _callbacks;

    public TraversalEngine(
        RuntimeStateRegistry runtimeRegistry,
        TraversalEngineCallbacks callbacks)
    {
        _runtimeRegistry = runtimeRegistry;
        _callbacks = callbacks;
    }

    public List<string> BuildTraversalBlockSequence(RouteDefinition route, string sourceBlockId, string targetBlockId)
    {
        var result = new List<string>();
        if (route.BlockIds.Count == 0)
            return result;

        var sourceIndex = route.BlockIds.FindIndex(id => string.Equals(id, sourceBlockId, StringComparison.OrdinalIgnoreCase));
        var targetIndex = route.BlockIds.FindIndex(id => string.Equals(id, targetBlockId, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0 || targetIndex < 0)
            return result;

        var step = sourceIndex <= targetIndex ? 1 : -1;
        for (int i = sourceIndex; ; i += step)
        {
            result.Add(route.BlockIds[i]);
            if (i == targetIndex)
                break;
        }

        return result;
    }

    public void InitializeTraversal(InitializeTraversalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RouteId))
            return;

        _runtimeRegistry.RegisterOrCreateRuntime(
            request.RouteId,
            request.OwnerLocomotiveId,
            NormalizeTraversalBlockIds(request.TraversalBlockIds),
            currentTraversalIndex: 0,
            request.CurrentBlockId);
    }

    public void AdvanceTraversal(AdvanceTraversalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RouteId) || string.IsNullOrWhiteSpace(request.CurrentBlockId))
            return;

        var runtime = _runtimeRegistry.GetRuntime(request.RouteId);
        var traversalBlockIds = runtime?.TraversalBlockIds.Count > 0
            ? runtime.TraversalBlockIds
            : NormalizeTraversalBlockIds(request.TraversalBlockIds);

        _runtimeRegistry.UpdateTraversalState(
            request.RouteId,
            request.SegmentIndex,
            request.CurrentBlockId,
            request.OwnerLocomotiveId,
            traversalBlockIds);
    }

    public void SetTraversalLifecycleState(string routeId, RouteRuntimeLifecycleState state)
        => _runtimeRegistry.SetLifecycleState(routeId, state);

    public void ResetTailClearState(string routeId, string? sourceBlockId, string? targetBlockId)
        => _runtimeRegistry.ResetTailClearState(routeId, sourceBlockId, targetBlockId);

    public void MarkBoundaryEntry(string routeId, string? sourceBlockId, string? targetBlockId, DateTime enteredAtUtc)
        => _runtimeRegistry.MarkBoundaryEntry(routeId, sourceBlockId, targetBlockId, enteredAtUtc);

    public void MarkTailClear(string routeId, string? sourceBlockId, string? targetBlockId, DateTime clearedAtUtc)
        => _runtimeRegistry.MarkTailClear(routeId, sourceBlockId, targetBlockId, clearedAtUtc);

    public void SetTraversalWindow(TraversalWindowRequest request)
    {
        if (request.TraversalBlockIds.Count < 2)
            return;

        var pathIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frontierIndex = Math.Clamp(request.LeadSegmentIndex, 0, request.TraversalBlockIds.Count - 1);

        var frontierBlockId = request.TraversalBlockIds[frontierIndex];
        if (!string.IsNullOrWhiteSpace(frontierBlockId))
            blockIds.Add(frontierBlockId);

        void AddSegment(int segmentIndex)
        {
            if (segmentIndex < 0 || segmentIndex >= request.TraversalBlockIds.Count - 1)
                return;

            var fromBlockId = request.TraversalBlockIds[segmentIndex];
            var toBlockId = request.TraversalBlockIds[segmentIndex + 1];
            blockIds.Add(fromBlockId);
            blockIds.Add(toBlockId);

            foreach (var pathId in _callbacks.ResolveSegmentPathIds(request.Layout, request.Route, fromBlockId, toBlockId))
            {
                if (!string.IsNullOrWhiteSpace(pathId))
                    pathIds.Add(pathId);
            }
        }

        AddSegment(frontierIndex);
        if (request.KeepPreviousSegmentActive)
            AddSegment(frontierIndex - 1);

        // Ak je frontier posledný blok (vlak vstúpil do cieľa a ešte brzdí),
        // pridaj aj segment DO tohto bloku aby spojnica svietila až do zastavenia.
        var isAtLastBlock = frontierIndex == request.TraversalBlockIds.Count - 1;
        if (isAtLastBlock && frontierIndex > 0)
            AddSegment(frontierIndex - 1);

        _runtimeRegistry.SetReservationWindow(
            request.Route.Id,
            pathIds,
            blockIds,
            frontierIndex,
            request.KeepPreviousSegmentActive);
    }

    public List<string> GetTraversalBlockOrder(string routeId, RouteDefinition route)
    {
        var runtime = _runtimeRegistry.GetRuntime(routeId);
        if (runtime?.TraversalBlockIds.Count > 0)
            return runtime.TraversalBlockIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

        return route.BlockIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
    }

    public string? ResolveNextBlock(string routeId, RouteDefinition route, string currentBlockId)
    {
        if (string.IsNullOrWhiteSpace(currentBlockId))
            return null;

        var traversalBlockIds = GetTraversalBlockOrder(routeId, route);
        return ResolveNextBlockIdInOrder(traversalBlockIds, currentBlockId);
    }

    public bool OwnsSegment(string routeId, RouteDefinition route, string fromBlockId, string toBlockId)
    {
        if (string.IsNullOrWhiteSpace(fromBlockId) || string.IsNullOrWhiteSpace(toBlockId))
            return false;

        var traversalBlockIds = GetTraversalBlockOrder(routeId, route);
        for (int i = 0; i < traversalBlockIds.Count - 1; i++)
        {
            if (string.Equals(traversalBlockIds[i], fromBlockId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(traversalBlockIds[i + 1], toBlockId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public string? ResolveActiveRouteForSegment(TrackLayout layout, string fromBlockId, string toBlockId)
    {
        if (string.IsNullOrWhiteSpace(fromBlockId) || string.IsNullOrWhiteSpace(toBlockId))
            return null;

        foreach (var routeId in _runtimeRegistry.ActiveRouteIds)
        {
            var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
            if (route == null)
                continue;

            if (OwnsSegment(routeId, route, fromBlockId, toBlockId))
                return routeId;
        }

        return null;
    }

    public bool IsTraversalComplete(string routeId, RouteDefinition route)
    {
        var runtime = _runtimeRegistry.GetRuntime(routeId);
        if (runtime == null)
            return false;

        var traversalBlockIds = GetTraversalBlockOrder(routeId, route);
        if (traversalBlockIds.Count == 0)
            return false;
        if (traversalBlockIds.Count == 1)
            return true;

        var finalSegmentIndex = traversalBlockIds.Count - 2;
        return runtime.CurrentTraversalIndex >= finalSegmentIndex;
    }

    private static List<string>? NormalizeTraversalBlockIds(IEnumerable<string>? traversalBlockIds)
    {
        if (traversalBlockIds == null)
            return null;

        return traversalBlockIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
    }

    private static string? ResolveNextBlockIdInOrder(IReadOnlyList<string> drivingOrder, string currentBlockId)
    {
        if (drivingOrder.Count == 0 || string.IsNullOrWhiteSpace(currentBlockId))
            return null;

        for (int i = 0; i < drivingOrder.Count - 1; i++)
        {
            if (string.Equals(drivingOrder[i], currentBlockId, StringComparison.OrdinalIgnoreCase))
                return drivingOrder[i + 1];
        }

        return null;
    }
}



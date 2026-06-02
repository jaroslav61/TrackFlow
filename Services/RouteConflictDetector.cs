using System;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

public enum RouteConflictType
{
    TurnoutStateMismatch,
    SharedBlock,
    SharedPathElement
}

public sealed record RouteConflictItem(
    RouteConflictType Type,
    string CandidateRouteId,
    string ActiveRouteId,
    string ResourceId,
    string Reason);

public sealed class RouteConflictCheckResult
{
    public bool HasConflict => Conflicts.Count > 0;
    public List<RouteConflictItem> Conflicts { get; } = new();
}

/// <summary>
/// Checks whether a candidate route conflicts with already active routes.
/// </summary>
public sealed class RouteConflictDetector
{
    public RouteConflictCheckResult EvaluateActivation(
        RouteDefinition candidateRoute,
        IEnumerable<RouteDefinition> activeRoutes)
    {
        if (candidateRoute == null) throw new ArgumentNullException(nameof(candidateRoute));
        if (activeRoutes == null) throw new ArgumentNullException(nameof(activeRoutes));

        var result = new RouteConflictCheckResult();
        var active = activeRoutes
            .Where(r => r != null && !string.Equals(r.Id, candidateRoute.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var route in active)
        {
            DetectTurnoutStateConflicts(candidateRoute, route, result, seen);
            DetectSharedBlockConflicts(candidateRoute, route, result, seen);
            DetectSharedPathElementConflicts(candidateRoute, route, result, seen);
        }

        return result;
    }

    public RouteConflictCheckResult EvaluateActivation(
        string candidateRouteId,
        IEnumerable<string> activeRouteIds,
        IEnumerable<RouteDefinition> allRoutes)
    {
        if (string.IsNullOrWhiteSpace(candidateRouteId))
            throw new ArgumentException("Candidate route id is required.", nameof(candidateRouteId));
        if (activeRouteIds == null) throw new ArgumentNullException(nameof(activeRouteIds));
        if (allRoutes == null) throw new ArgumentNullException(nameof(allRoutes));

        var routeById = allRoutes
            .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Id))
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        if (!routeById.TryGetValue(candidateRouteId, out var candidate))
            throw new InvalidOperationException($"Route '{candidateRouteId}' not found.");

        var active = activeRouteIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(routeById.ContainsKey)
            .Select(id => routeById[id])
            .ToList();

        return EvaluateActivation(candidate, active);
    }

    private static void DetectTurnoutStateConflicts(
        RouteDefinition candidate,
        RouteDefinition active,
        RouteConflictCheckResult result,
        HashSet<string> seen)
    {
        foreach (var candidateSetting in candidate.TurnoutSettings)
        {
            if (string.IsNullOrWhiteSpace(candidateSetting.TurnoutId))
                continue;

            var activeSetting = active.TurnoutSettings.FirstOrDefault(ts =>
                string.Equals(ts.TurnoutId, candidateSetting.TurnoutId, StringComparison.OrdinalIgnoreCase));
            if (activeSetting == null)
                continue;

            if (candidateSetting.RequiredState == activeSetting.RequiredState)
                continue;

            AddConflict(
                result,
                seen,
                RouteConflictType.TurnoutStateMismatch,
                candidate.Id,
                active.Id,
                candidateSetting.TurnoutId,
                $"turnout-state-mismatch:{candidateSetting.RequiredState}!={activeSetting.RequiredState}");
        }
    }

    private static void DetectSharedBlockConflicts(
        RouteDefinition candidate,
        RouteDefinition active,
        RouteConflictCheckResult result,
        HashSet<string> seen)
    {
        var sharedBlocks = candidate.BlockIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Intersect(active.BlockIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);

        foreach (var blockId in sharedBlocks)
        {
            AddConflict(
                result,
                seen,
                RouteConflictType.SharedBlock,
                candidate.Id,
                active.Id,
                blockId,
                "shared-block");
        }
    }

    private static void DetectSharedPathElementConflicts(
        RouteDefinition candidate,
        RouteDefinition active,
        RouteConflictCheckResult result,
        HashSet<string> seen)
    {
        var sharedElements = candidate.PathElementIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Intersect(active.PathElementIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);

        foreach (var elementId in sharedElements)
        {
            AddConflict(
                result,
                seen,
                RouteConflictType.SharedPathElement,
                candidate.Id,
                active.Id,
                elementId,
                "shared-path-element");
        }
    }

    private static void AddConflict(
        RouteConflictCheckResult result,
        HashSet<string> seen,
        RouteConflictType type,
        string candidateRouteId,
        string activeRouteId,
        string resourceId,
        string reason)
    {
        var key = $"{type}|{candidateRouteId}|{activeRouteId}|{resourceId}";
        if (!seen.Add(key))
            return;

        result.Conflicts.Add(new RouteConflictItem(type, candidateRouteId, activeRouteId, resourceId, reason));
    }
}


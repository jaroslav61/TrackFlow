using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TrackFlow.Models.Layout;
using TrackFlow.Runtime;
namespace TrackFlow.Services.UI;

internal sealed class ActiveRouteVisualScopeResolver
{
    private readonly Func<TrackLayout, RouteDefinition, string, string, IEnumerable<string>> _resolveSegmentPathIds;
    private readonly Dictionary<string, string> _lastScopeSignatures = new(StringComparer.OrdinalIgnoreCase);

    private sealed record RouteVisualScope(
        string Source,
        string? FrontierBlockId,
        HashSet<string> BlockIds,
        HashSet<string> PathElementIds,
        string? RejectedTailClearedBlockId,
        bool UsedFullRouteFallback);

    public ActiveRouteVisualScopeResolver(
        Func<TrackLayout, RouteDefinition, string, string, IEnumerable<string>> resolveSegmentPathIds)
    {
        _resolveSegmentPathIds = resolveSegmentPathIds;
    }

    public bool IsElementInActiveRouteVisualScope(
        TrackLayout? layout,
        RuntimeStateRegistry runtimeRegistry,
        string elementId)
    {
        if (layout == null || string.IsNullOrWhiteSpace(elementId))
            return false;

        foreach (var routeId in runtimeRegistry.ActiveRouteIds)
        {
            var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
            if (route == null)
                continue;

            var scope = ResolveRouteScope(layout, runtimeRegistry, route);
            if (scope == null)
                continue;

            LogScopeIfChanged(route.Id, scope);

            if (scope.BlockIds.Contains(elementId) || scope.PathElementIds.Contains(elementId))
                return true;
        }

        return false;
    }

    public void ResetRoute(string routeId)
    {
        if (!string.IsNullOrWhiteSpace(routeId))
            _lastScopeSignatures.Remove(routeId);
    }

    public void Clear()
        => _lastScopeSignatures.Clear();

    private RouteVisualScope? ResolveRouteScope(
        TrackLayout layout,
        RuntimeStateRegistry runtimeRegistry,
        RouteDefinition route)
    {
        if (runtimeRegistry.TryGetReservationWindow(route.Id, out var window) && window != null)
        {
            return new RouteVisualScope(
                Source: "reservation-window",
                FrontierBlockId: ResolveFrontierBlockFromWindow(route, window),
                BlockIds: new HashSet<string>(window.BlockIds, StringComparer.OrdinalIgnoreCase),
                PathElementIds: new HashSet<string>(window.PathElementIds, StringComparer.OrdinalIgnoreCase),
                RejectedTailClearedBlockId: null,
                UsedFullRouteFallback: false);
        }

        var runtime = runtimeRegistry.GetRuntime(route.Id);
        if (runtime?.TraversalBlockIds.Count > 0)
            return BuildFrontierDerivedScope(layout, route, runtime);

        return new RouteVisualScope(
            Source: "full-route-fallback",
            FrontierBlockId: null,
            BlockIds: route.BlockIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase),
            PathElementIds: route.PathElementIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase),
            RejectedTailClearedBlockId: null,
            UsedFullRouteFallback: true);
    }

    private RouteVisualScope BuildFrontierDerivedScope(
        TrackLayout layout,
        RouteDefinition route,
        RouteRuntimeState runtime)
    {
        var traversalBlockIds = runtime.TraversalBlockIds;
        var currentIndex = ResolveCurrentBlockIndex(traversalBlockIds, runtime);
        var blockIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pathElementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? rejectedTailClearedBlockId = null;

        var frontierBlockId = traversalBlockIds[currentIndex];
        if (!string.IsNullOrWhiteSpace(frontierBlockId))
            blockIds.Add(frontierBlockId);

        if (currentIndex < traversalBlockIds.Count - 1)
        {
            var nextBlockId = traversalBlockIds[currentIndex + 1];
            if (!string.IsNullOrWhiteSpace(nextBlockId))
                blockIds.Add(nextBlockId);

            foreach (var pathId in _resolveSegmentPathIds(layout, route, frontierBlockId, nextBlockId))
            {
                if (!string.IsNullOrWhiteSpace(pathId))
                    pathElementIds.Add(pathId);
            }
        }

        var tailClearedSource = runtime.TailClearState.TailClearTriggered
            ? runtime.TailClearState.SourceBlockId
            : null;
        if (!string.IsNullOrWhiteSpace(tailClearedSource)
            && !blockIds.Contains(tailClearedSource)
            && traversalBlockIds.Any(id => string.Equals(id, tailClearedSource, StringComparison.OrdinalIgnoreCase)))
        {
            rejectedTailClearedBlockId = tailClearedSource;
        }

        return new RouteVisualScope(
            Source: "runtime-frontier-derived",
            FrontierBlockId: frontierBlockId,
            BlockIds: blockIds,
            PathElementIds: pathElementIds,
            RejectedTailClearedBlockId: rejectedTailClearedBlockId,
            UsedFullRouteFallback: false);
    }

    private static int ResolveCurrentBlockIndex(IReadOnlyList<string> traversalBlockIds, RouteRuntimeState runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime.CurrentBlockId))
        {
            var currentIndex = traversalBlockIds
                .Select((id, index) => new { id, index })
                .FirstOrDefault(x => string.Equals(x.id, runtime.CurrentBlockId, StringComparison.OrdinalIgnoreCase));
            if (currentIndex != null)
                return currentIndex.index;
        }

        return Math.Clamp(runtime.CurrentTraversalIndex, 0, traversalBlockIds.Count - 1);
    }

    private static string? ResolveFrontierBlockFromWindow(RouteDefinition route, RouteReservationWindowState window)
    {
        if (window.LeadSegmentIndex is not int leadIndex)
            return null;
        if (leadIndex < 0 || leadIndex >= route.BlockIds.Count)
            return null;
        return route.BlockIds[leadIndex];
    }

    private void LogScopeIfChanged(string routeId, RouteVisualScope scope)
    {
        var blocks = scope.BlockIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        var paths = scope.PathElementIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        var signature = new StringBuilder()
            .Append(scope.Source)
            .Append('|').Append(scope.FrontierBlockId ?? string.Empty)
            .Append('|').Append(string.Join(",", blocks))
            .Append('|').Append(string.Join(",", paths))
            .Append('|').Append(scope.RejectedTailClearedBlockId ?? string.Empty)
            .Append('|').Append(scope.UsedFullRouteFallback)
            .ToString();

        if (_lastScopeSignatures.TryGetValue(routeId, out var lastSignature)
            && string.Equals(lastSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastScopeSignatures[routeId] = signature;
    }
}


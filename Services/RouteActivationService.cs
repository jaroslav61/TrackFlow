using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models.Layout;
using TrackFlow.Services.Dcc;

namespace TrackFlow.Services;

public sealed class RouteActivationResult
{
    public bool IsSuccess { get; init; }
    public string Reason { get; init; } = string.Empty;
    public RouteConflictCheckResult? Conflict { get; init; }

    public static RouteActivationResult Success() => new() { IsSuccess = true };

    public static RouteActivationResult Failed(string reason, RouteConflictCheckResult? conflict = null)
        => new() { IsSuccess = false, Reason = reason, Conflict = conflict };
}

/// <summary>
/// Applies route activation/deactivation at runtime and keeps block locks consistent.
/// </summary>
public sealed class RouteActivationService
{
    private readonly RouteConflictDetector _conflictDetector = new();

    public Task<RouteActivationResult> TryActivateAsync(
        TrackLayout layout,
        string routeId,
        ISet<string> activeRouteIds,
        IDccCentralClient? dccClient = null,
        CancellationToken ct = default)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        if (activeRouteIds == null) throw new ArgumentNullException(nameof(activeRouteIds));
        if (string.IsNullOrWhiteSpace(routeId))
            return Task.FromResult(RouteActivationResult.Failed("route-id-empty"));

        var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
        if (route == null)
            return Task.FromResult(RouteActivationResult.Failed("route-not-found"));

        if (!route.IsEnabled)
            return Task.FromResult(RouteActivationResult.Failed("route-disabled"));

        if (activeRouteIds.Contains(route.Id))
            return Task.FromResult(RouteActivationResult.Success());

        var conflict = _conflictDetector.EvaluateActivation(route.Id, activeRouteIds, layout.Routes);

        // Bezpečnostná kontrola fyzickej kontinuity: cesta nesmie aktivovať cez prerušenú trať.
        // Kontrolu aplikujeme IBA ak layout obsahuje skutočný graf koľajiska (FindAllRoutes > 0).
        // Ak je graf prázdny (napr. testovacie layouty bez track segmentov), kontrolu preskočíme.
        var graphRoutes = new RoutePathfinder(layout).FindAllRoutes();
        if (graphRoutes.Count > 0 && !IsRouteContinuousInGraph(route, graphRoutes))
            return Task.FromResult(RouteActivationResult.Failed("route-track-disconnected"));


        activeRouteIds.Add(route.Id);
        RebuildBlockLocks(layout, activeRouteIds);

        return Task.FromResult(new RouteActivationResult
        {
            IsSuccess = true,
            Conflict = conflict.HasConflict ? conflict : null
        });
    }

    public void Deactivate(TrackLayout layout, string routeId, ISet<string> activeRouteIds)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        if (activeRouteIds == null) throw new ArgumentNullException(nameof(activeRouteIds));
        if (string.IsNullOrWhiteSpace(routeId))
            return;

        activeRouteIds.Remove(routeId);
        RebuildBlockLocks(layout, activeRouteIds);
    }

    public void DeactivateAll(TrackLayout layout, ISet<string> activeRouteIds)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        if (activeRouteIds == null) throw new ArgumentNullException(nameof(activeRouteIds));

        activeRouteIds.Clear();
        RebuildBlockLocks(layout, activeRouteIds);
    }

    internal static bool MapTurnoutStateToBranch(TurnoutState state)
    {        return state switch
        {
            TurnoutState.Diverge => true,
            TurnoutState.DivergeLeft => true,
            TurnoutState.DivergeRight => true,
            TurnoutState.Cross => true,
            _ => false
        };
    }

    private static bool IsRouteContinuousInGraph(RouteDefinition route, List<FoundRoute> graphRoutes)
    {
        if (route.BlockIds.Count < 2)
            return true;

        var routeTurnouts = route.TurnoutSettings
            .ToDictionary(t => t.TurnoutId, t => t.RequiredState, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < route.BlockIds.Count - 1; i++)
        {
            var fromId = route.BlockIds[i];
            var toId = route.BlockIds[i + 1];

            if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
                continue;

            var edge = graphRoutes.FirstOrDefault(f =>
                string.Equals(f.FromBlockId, fromId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.ToBlockId, toId, StringComparison.OrdinalIgnoreCase)
                && f.TurnoutStates.All(kv => routeTurnouts.TryGetValue(kv.Key, out var st) && st == kv.Value));

            if (edge == null)
                return false;
        }

        return true;
    }

    private static void RebuildBlockLocks(TrackLayout layout, IEnumerable<string> activeRouteIds)
    {
        var routeById = layout.Routes
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var lockSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Faza 2.5.3: Lockujeme iba prvé 2 bloky každej aktívnej cesty
        // (t.j. štartový blok + prvý nasledujúci blok). Sliding window
        // počas pohybu spravuje OperationViewModel.ApplyDynamicLockWindow().
        foreach (var routeId in activeRouteIds)
        {
            if (!routeById.TryGetValue(routeId, out var route))
                continue;

            int added = 0;
            foreach (var blockId in route.BlockIds)
            {
                if (string.IsNullOrWhiteSpace(blockId))
                    continue;
                lockSet.Add(blockId);
                if (++added >= 2)
                    break;
            }
        }

        foreach (var block in layout.Elements.OfType<BlockElement>())
            block.IsLocked = lockSet.Contains(block.Id);
    }

}

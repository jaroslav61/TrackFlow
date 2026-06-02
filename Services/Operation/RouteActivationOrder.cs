using System;
using System.Linq;
using TrackFlow.Models;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services.Operation;

/// <summary>
/// Stateless helpers pre určenie poradia blokov v ceste pri jej aktivácii.
/// Mechanická 1:1 extrakcia z OperationViewModel (behavior-preserving).
/// </summary>
internal static class RouteActivationOrder
{
    /// <summary>
    /// RouteManager: pre aktiváciu vytvorí pracovné poradie cesty podľa aktuálnej polohy lokomotívy.
    /// Ak lokomotíva stojí na koncovom bloku pôvodnej cesty, všetky aktivačné operácie pracujú s Reverse(BlockIds).
    /// </summary>
    public static RouteDefinition ResolveActivationRouteOrder(TrackLayout layout, RouteDefinition route, Locomotive? loco)
    {
        if (route.BlockIds.Count < 2 || loco == null || string.IsNullOrWhiteSpace(loco.Code))
            return route;

        var sourceBlockId = LocoStateResolver.ResolveLocoCurrentBlockId(layout.Elements, loco);
        if (string.IsNullOrWhiteSpace(sourceBlockId))
            return route;

        var sourceIndex = route.BlockIds.FindIndex(id => string.Equals(id, sourceBlockId, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0)
            return route;

        if (sourceIndex == route.BlockIds.Count - 1)
            return CreateReversedActivationRoute(route);

        return route;
    }

    public static RouteDefinition CreateReversedActivationRoute(RouteDefinition route)
    {
        var reversed = new RouteDefinition
        {
            Id = route.Id,
            Name = route.Name,
            FromBlockId = route.ToBlockId,
            ToBlockId = route.FromBlockId,
            FromBlockDirection = route.ToBlockDirection,
            ToBlockDirection = route.FromBlockDirection,
            StartNavigationDirection = route.ToBlockDirection,
            SafetyFallbackAspect = route.SafetyFallbackAspect,
            Kind = route.Kind,
            IsEnabled = route.IsEnabled,
            Color = route.Color,
            MaxSpeed = route.MaxSpeed
        };

        reversed.BlockIds.AddRange(route.BlockIds.AsEnumerable().Reverse());
        reversed.PathElementIds.AddRange(route.PathElementIds.AsEnumerable().Reverse());
        reversed.TurnoutSettings.AddRange(route.TurnoutSettings);
        reversed.RouteSignalIds.AddRange(route.RouteSignalIds);
        return reversed;
    }
}


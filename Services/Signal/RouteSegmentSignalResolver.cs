using System;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services.Signal;

/// <summary>
/// Stateless helper s čistou logikou rezolúcie navigačného smeru a "štartovacieho"
/// návestidla pre segment cesty. Extrahované 1:1 z
/// <see cref="TrackFlow.ViewModels.Operation.OperationViewModel"/> – mechanický presun
/// bez zmeny správania.
///
/// POZNÁMKA: Niektoré z týchto helperov majú priame ekvivalenty v
/// <see cref="TrackFlow.Services.RoutePathfinder"/> (<c>ResolveNavigationDirectionFromBlockPort</c>)
/// a <see cref="TrackFlow.Services.SignalController"/> (<c>MapRouteDirectionToNavigationDirection</c>).
/// Tieto duplicity sú v aktuálnej fáze refaktoru ZÁMERNE ponechané – konsolidácia bude
/// riešená až po stabilizácii rozbíjania OperationViewModel.
/// </summary>
internal static class RouteSegmentSignalResolver
{
    public static NavigationDirection ResolveSegmentTravelDirection(TrackLayout layout, RouteDefinition route, string fromBlockId, string toBlockId)
    {
        var blocksById = layout.Elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        if (!blocksById.TryGetValue(fromBlockId, out var fromBlock))
            return NavigationDirection.Right;

        var routeTurnouts = route.TurnoutSettings
            .ToDictionary(t => t.TurnoutId, t => t.RequiredState, StringComparer.OrdinalIgnoreCase);

        var edge = new RoutePathfinder(layout)
            .FindAllRoutes()
            .FirstOrDefault(f =>
                string.Equals(f.FromBlockId, fromBlockId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.ToBlockId, toBlockId, StringComparison.OrdinalIgnoreCase)
                && f.TurnoutStates.All(kv => routeTurnouts.TryGetValue(kv.Key, out var state) && state == kv.Value));

        if (edge != null)
            return ResolveNavigationDirectionFromBlockPort(fromBlock, edge.FromBlockExitPort);

        // Fallback 1: ak je to štartový segment, použime StartNavigationDirection.
        if (string.Equals(route.FromBlockId, fromBlockId, StringComparison.OrdinalIgnoreCase))
        {
            return RouteDirection.NormalizeOrDefault(route.StartNavigationDirection, RouteDirection.Right,
                    $"Route[{route.Id}].{nameof(RouteDefinition.StartNavigationDirection)}")
                switch
                {
                    var d when string.Equals(d, RouteDirection.Left, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Left,
                    var d when string.Equals(d, RouteDirection.Up, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Up,
                    var d when string.Equals(d, RouteDirection.Down, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Down,
                    _ => NavigationDirection.Right
                };
        }

        // Fallback 2: heuristika podľa pozície blokov.
        if (blocksById.TryGetValue(toBlockId, out var toBlock))
        {
            var dx = toBlock.X - fromBlock.X;
            var dy = toBlock.Y - fromBlock.Y;
            if (Math.Abs(dx) >= Math.Abs(dy))
                return dx >= 0 ? NavigationDirection.Right : NavigationDirection.Left;
            return dy >= 0 ? NavigationDirection.Down : NavigationDirection.Up;
        }

        return NavigationDirection.Right;
    }

    public static SignalElement? ResolveSegmentStartSignal(
        TrackLayout layout,
        string fromBlockId,
        NavigationDirection travelDirection,
        string? toBlockId = null)
    {
        var signalsById = layout.Elements.OfType<SignalElement>()
            .ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
        var blocksById = layout.Elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        if (!blocksById.TryGetValue(fromBlockId, out var fromBlock))
            return null;

        // KĽÚČOVÁ OPRAVA: Pýtame sa bloku na jeho priradené návestidlo pre tento smer
        var signalId = fromBlock.GetSignalForDirection(travelDirection);
    
        if (string.IsNullOrWhiteSpace(signalId) || !signalsById.TryGetValue(signalId, out var signal))
        {
            return null;
        }

        return signal;
    }

    public static NavigationDirection ResolveNavigationDirectionFromBlockPort(BlockElement block, string? portName)
    {
        bool isVertical = LayoutElementFootprintHelper.IsVertical(block.Rotation);
        if (string.Equals(portName, "A", StringComparison.OrdinalIgnoreCase))
            return isVertical ? NavigationDirection.Up : NavigationDirection.Left;
        if (string.Equals(portName, "B", StringComparison.OrdinalIgnoreCase))
            return isVertical ? NavigationDirection.Down : NavigationDirection.Right;
        return NavigationDirection.Right;
    }

    public static NavigationDirection MapRouteDirectionToNavigationDirection(string direction)
        => direction switch
        {
            var d when string.Equals(d, RouteDirection.Left, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Left,
            var d when string.Equals(d, RouteDirection.Right, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Right,
            var d when string.Equals(d, RouteDirection.Up, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Up,
            var d when string.Equals(d, RouteDirection.Down, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Down,
            _ => NavigationDirection.Right
        };

    /// <summary>
    /// Vráti SKUTOČNÉ ďalšie hlavné návestidlo nachádzajúce sa za cieľovým blokom cesty
    /// v smere jazdy (t.j. návestidlo, ktoré by bolo štartom nadväzujúcej cesty).
    /// Vracia null ak také návestidlo neexistuje.
    /// Používa sa pre výpočet "Caution" iba pri reálnom riziku ďalšieho STOP – NIKDY
    /// na základe samotného faktu, že cesta končí (vrátane terminálnych blokov / bumperov).
    /// </summary>
    public static SignalElement? ResolveNextMainSignalAfterRouteTarget(TrackLayout layout, RouteDefinition route)
    {
        if (string.IsNullOrWhiteSpace(route.ToBlockId))
            return null;

        var normalizedDirection = RouteDirection.NormalizeOrDefault(
            route.ToBlockDirection,
            RouteDirection.Right,
            $"Route[{route.Id}].{nameof(RouteDefinition.ToBlockDirection)}");
        var travelDirection = MapRouteDirectionToNavigationDirection(normalizedDirection);

        return ResolveSegmentStartSignal(layout, route.ToBlockId, travelDirection);
    }
}


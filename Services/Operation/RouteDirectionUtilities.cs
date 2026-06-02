using System;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services.Operation;

/// <summary>
/// Stateless helper s čistou logikou klasifikácie a invertovania <see cref="RouteDirection"/>
/// reťazcov. Extrahované 1:1 z
/// <see cref="TrackFlow.ViewModels.Operation.OperationViewModel"/> – mechanický presun
/// bez zmeny správania.
/// </summary>
internal static class RouteDirectionUtilities
{
    /// <summary>
    /// Vráti true ak smer reprezentuje "dopredný" pohyb v rámci bloku
    /// (Right alebo Down – v súlade s pôvodnou OVM logikou).
    /// </summary>
    public static bool IsForwardRouteDirection(string direction)
        => string.Equals(direction, RouteDirection.Right, StringComparison.OrdinalIgnoreCase)
           || string.Equals(direction, RouteDirection.Down, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Vráti opačný smer (Left↔Right, Up↔Down). Pri neznámom vstupe vracia
    /// <see cref="RouteDirection.Right"/> – zhodne s pôvodnou OVM implementáciou.
    /// </summary>
    public static string InvertRouteDirection(string direction)
        => direction switch
        {
            var d when string.Equals(d, RouteDirection.Left, StringComparison.OrdinalIgnoreCase) => RouteDirection.Right,
            var d when string.Equals(d, RouteDirection.Right, StringComparison.OrdinalIgnoreCase) => RouteDirection.Left,
            var d when string.Equals(d, RouteDirection.Up, StringComparison.OrdinalIgnoreCase) => RouteDirection.Down,
            var d when string.Equals(d, RouteDirection.Down, StringComparison.OrdinalIgnoreCase) => RouteDirection.Up,
            _ => RouteDirection.Right
        };
}


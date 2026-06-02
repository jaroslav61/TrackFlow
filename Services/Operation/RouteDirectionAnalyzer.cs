using System;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services.Operation
{
    public static class RouteDirectionAnalyzer
    {
        public static DirectionAnalysis AnalyzeDirectionForMove(RouteDefinition route, string sourceBlockId, string targetBlockId)
        {
            var from = !string.IsNullOrWhiteSpace(route.FromBlockId)
                ? route.FromBlockId
                : route.BlockIds.FirstOrDefault() ?? string.Empty;
            var to = !string.IsNullOrWhiteSpace(route.ToBlockId)
                ? route.ToBlockId
                : route.BlockIds.LastOrDefault() ?? string.Empty;

            if (string.Equals(sourceBlockId, from, StringComparison.OrdinalIgnoreCase)
                && string.Equals(targetBlockId, to, StringComparison.OrdinalIgnoreCase))
                return new DirectionAnalysis(true, DesiredForward: true);

            if (string.Equals(sourceBlockId, to, StringComparison.OrdinalIgnoreCase)
                && string.Equals(targetBlockId, from, StringComparison.OrdinalIgnoreCase))
                return new DirectionAnalysis(true, DesiredForward: false);

            return new DirectionAnalysis(false, DesiredForward: true);
        }

        public static void TryApplyAutomaticDirectionIfStopped(Locomotive loco, bool desiredForward)
        {
            if (loco.TargetSpeed != 0 || loco.CurrentDisplaySpeed != 0)
                return;

            if (desiredForward)
            {
                if (!loco.IsForward)
                    loco.IsForward = true;
            }
            else
            {
                if (!loco.IsReverse)
                    loco.IsReverse = true;
            }
        }

        public static OrientationSyncAnalysis AnalyzeOrientationSyncForRoute(
            RouteDefinition route,
            IEnumerable<LayoutElement> layoutElements,
            Locomotive loco,
            bool? travelDesiredForward = null)
        {
            var sourceBlockId = loco.AssignedBlockId;
            if (string.IsNullOrWhiteSpace(sourceBlockId))
            {
                sourceBlockId = layoutElements
                    .OfType<BlockElement>()
                    .FirstOrDefault(b => string.Equals(b.AssignedLocoId, loco.Code, StringComparison.OrdinalIgnoreCase))
                    ?.Id;
            }

            if (string.IsNullOrWhiteSpace(sourceBlockId))
                return new OrientationSyncAnalysis(false, false, true);

            var sourceBlock = layoutElements
                .OfType<BlockElement>()
                .FirstOrDefault(b => string.Equals(b.Id, sourceBlockId, StringComparison.OrdinalIgnoreCase));
            if (sourceBlock == null)
                return new OrientationSyncAnalysis(false, false, true);

            // ARCHITEKTÚRA:
            // routeWantsForwardInBlock = "ide loco v rámci tejto cesty smerom from→to?"
            // To je TRAVERSAL semantika, NIE geometrická (Left/Right/Up/Down).
            // RouteDirection (Left/Right/Up/Down) reprezentuje smer v layoute, NIE forward/reverse
            // travel direction lokomotívy. Pri ceste, ktorá má StartNavigationDirection v inej osi
            // než blok (napr. B1 → B5 s Up, kde B1 je horizontálny blok), geometrická klasifikácia
            // (Right/Down=forward, Left/Up=reverse) nesprávne prepínala DCC packet na reverse.
            //
            // Travel direction už bol správne určený traversal analýzou (AnalyzeSelectedLocoDirectionForRoute /
            // AnalyzeDirectionForMove) v call-site a sem sa odovzdáva ako travelDesiredForward.
            // Orientation-sync ho NESMIE prepísať geometrickou klasifikáciou.
            bool routeWantsForwardInBlock;
            if (travelDesiredForward.HasValue)
            {
                routeWantsForwardInBlock = travelDesiredForward.Value;
            }
            else
            {
                // Legacy fallback (žiadny call-site v produkčnom kóde dnes nepoužíva, ponechané
                // pre spätnú kompatibilitu prípadných externých volaní/testov).
                var startDirection = RouteDirection.NormalizeOrDefault(
                    route.StartNavigationDirection,
                    RouteDirection.Right,
                    $"Route[{route.Id}].{nameof(RouteDefinition.StartNavigationDirection)}");

                if (!string.IsNullOrWhiteSpace(route.ToBlockId)
                    && string.Equals(sourceBlock.Id, route.ToBlockId, StringComparison.OrdinalIgnoreCase))
                {
                    startDirection = RouteDirectionUtilities.InvertRouteDirection(startDirection);
                }

                routeWantsForwardInBlock = RouteDirectionUtilities.IsForwardRouteDirection(startDirection);
            }

            var orientationForwardInBlock = sourceBlock.AssignedLocoIsForward;
            var isReversedByOrientation = routeWantsForwardInBlock != orientationForwardInBlock;
            var desiredDccForward = !isReversedByOrientation;

            return new OrientationSyncAnalysis(true, isReversedByOrientation, desiredDccForward);
        }
    }

    public readonly record struct DirectionAnalysis(bool HasDirection, bool DesiredForward);

    public readonly record struct OrientationSyncAnalysis(bool HasData, bool IsReversedByOrientation, bool DesiredDccForward);
}


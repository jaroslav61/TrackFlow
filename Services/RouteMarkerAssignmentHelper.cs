using System;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

public static class RouteMarkerAssignmentHelper
{
    public static bool HasAssignedRoute(TrackLayout? layout, RouteElement? routeElement)
    {
        if (layout == null || routeElement == null || string.IsNullOrWhiteSpace(routeElement.SelectedRouteDefinitionId))
            return false;

        var selectedRouteId = routeElement.SelectedRouteDefinitionId.Trim();
        return layout.Routes.Any(route => string.Equals(route.Id, selectedRouteId, StringComparison.OrdinalIgnoreCase));
    }

    public static int ClearInvalidAssignments(TrackLayout? layout)
    {
        if (layout == null)
            return 0;

        var validRouteIds = new HashSet<string>(
            layout.Routes
                .Where(route => !string.IsNullOrWhiteSpace(route.Id))
                .Select(route => route.Id.Trim()),
            StringComparer.OrdinalIgnoreCase);

        int clearedCount = 0;
        foreach (var routeMarker in layout.Elements.OfType<RouteElement>())
        {
            if (string.IsNullOrWhiteSpace(routeMarker.SelectedRouteDefinitionId))
                continue;

            var selectedRouteId = routeMarker.SelectedRouteDefinitionId.Trim();
            if (validRouteIds.Contains(selectedRouteId))
                continue;

            routeMarker.SelectedRouteDefinitionId = null;
            clearedCount++;
        }

        return clearedCount;
    }
}

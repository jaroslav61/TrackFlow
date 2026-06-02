using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

public sealed record RouteAutoRepairAction(
    int InsertIndex,
    string InsertedElementId,
    string BetweenElementIdA,
    string BetweenElementIdB);

public sealed record RouteIntegrityIssue(
    string RouteId,
    string RouteName,
    IReadOnlyList<string> MissingPathElementIds,
    IReadOnlyList<RouteAutoRepairAction> Repairs);

public sealed class RouteIntegrityReport
{
    public List<RouteIntegrityIssue> Issues { get; } = new();

    public bool AnyMissingReferences => Issues.Any(i => i.MissingPathElementIds.Count > 0);
    public bool AnyRepairs => Issues.Any(i => i.Repairs.Count > 0);
}

/// <summary>
/// Best-effort validácia a oprava integrity uložených definícií ciest.
/// 
/// Ciele:
/// - Detekovať cesty, ktoré odkazujú na ID prvkov v <see cref="RouteDefinition.PathElementIds"/>,
///   ktoré už neexistujú v <see cref="TrackLayout.Elements"/>.
/// - Auto-opraviť manuálne cesty, keď chýba presne jeden koľajový prvok *medzi* dvoma po sebe
///   idúcimi prvkami cesty, ale daný prvok stále existuje v layoute.
/// </summary>
public static class RouteIntegrityService
{
    /// <summary>
    /// Skontroluje všetky cesty v <paramref name="layout"/> a (voliteľne) vykoná best-effort auto-opravu.
    /// 
    /// Pravidlo opravy:
    /// Ak dva po sebe idúce prvky cesty nie sú priamo prepojené v konektivitnom grafe,
    /// ale existuje presne jeden medzi-prvok (nie blok), ktorý spája A → C → B, tak vloží
    /// tento prvok do <see cref="RouteDefinition.PathElementIds"/>.
    /// 
    /// Metóda modifikuje <paramref name="layout"/>, ak aplikuje opravy.
    /// </summary>
    public static RouteIntegrityReport ValidateAndRepairOnLoad(TrackLayout layout, bool autoRepairManualRoutes = true)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));

        var report = new RouteIntegrityReport();

        var elementIdSet = layout.Elements
            .Select(e => e.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Graf zostavíme raz – použije sa na overenie konektivity aj auto-opravu.
        var graph = new TrackGraphBuilder().Build(layout);

        foreach (var route in layout.Routes)
        {
            var repairs = new List<RouteAutoRepairAction>();

            if (autoRepairManualRoutes
                && route.Kind == RouteDefinitionKind.UserDefinedRoute
            )
            {
                // 1) Najprv skúsime opraviť priamo chýbajúce odkazy v PathElementIds.
                repairs.AddRange(AutoRepairMissingPathReferences(route, graph, elementIdSet));

                // 2) Následne skúsime doplniť chýbajúci medzi-prvok medzi existujúcimi po sebe idúcimi prvkami.
                repairs.AddRange(AutoRepairSingleMissingBetweenElements(route, graph));
            }

            // Missing vyhodnocujeme až PO opravách.
            var missing = route.PathElementIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Where(id => !elementIdSet.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missing.Count > 0 || repairs.Count > 0)
            {
                report.Issues.Add(new RouteIntegrityIssue(
                    route.Id,
                    route.Name,
                    missing,
                    repairs));
            }
        }

        return report;
    }

    private static IReadOnlyList<RouteAutoRepairAction> AutoRepairSingleMissingBetweenElements(
        RouteDefinition route,
        TrackGraphBuilder.TrackGraph graph)
    {
        // Opravujeme len vtedy, keď vieme rozumne rozhodovať o konektivite.
        if (string.IsNullOrWhiteSpace(route.FromBlockId) || string.IsNullOrWhiteSpace(route.ToBlockId))
            return Array.Empty<RouteAutoRepairAction>();

        if (!graph.PortsByElement.ContainsKey(route.FromBlockId) || !graph.PortsByElement.ContainsKey(route.ToBlockId))
            return Array.Empty<RouteAutoRepairAction>();

        // Pracujeme s kombinovaným zoznamom: [FromBlock] + PathElementIds + [ToBlock]
        var sequence = new List<string>(route.PathElementIds.Count + 2)
        {
            route.FromBlockId
        };
        sequence.AddRange(route.PathElementIds);
        sequence.Add(route.ToBlockId);

        var actions = new List<RouteAutoRepairAction>();

        // Iterujeme nad meniteľným zoznamom (počas skenu môžeme vkladať prvky).
        // Premenná i indexuje dvojicu (sequence[i] -> sequence[i+1]).
        for (int i = 0; i < sequence.Count - 1; i++)
        {
            var aId = sequence[i];
            var bId = sequence[i + 1];

            if (string.IsNullOrWhiteSpace(aId) || string.IsNullOrWhiteSpace(bId))
                continue;

            if (!graph.PortsByElement.TryGetValue(aId, out var aPorts))
                continue;

            if (!graph.PortsByElement.TryGetValue(bId, out var bPorts))
                continue;

            // Už sú prepojené – nie je čo opravovať.
            if (AreElementsDirectlyConnected(graph, aPorts, bId))
                continue;

            // Skús nájsť presne JEDEN medzi-prvok C tak, aby platilo A -> C a C -> B.
            var candidates = FindSingleIntermediateCandidates(graph, aPorts, aId, bId);
            if (candidates.Count != 1)
                continue;

            var cId = candidates[0];

            // Nevkladať bloky.
            if (graph.ElementsById.TryGetValue(cId, out var cEl) && cEl is BlockElement)
                continue;

            // Nevytvárať duplikáty.
            if (route.PathElementIds.Contains(cId, StringComparer.OrdinalIgnoreCase))
                continue;

            // Vloženie do PathElementIds podľa indexu dvojice.
            // Mapovanie:
            //   sequence = [From] + path + [To]
            //   dvojica na indexe i zodpovedá vloženiu na index i v path.
            // Príklady:
            //   i=0 (From -> prvý prvok) => vlož na index 0
            //   i=1 (path[0] -> path[1]) => vlož na index 1
            //   i=path.Count (posledný prvok -> To) => vlož na koniec
            var insertIndex = Math.Clamp(i, 0, route.PathElementIds.Count);
            route.PathElementIds.Insert(insertIndex, cId);

            // Udrž skenovací zoznam synchronizovaný a pokračuj.
            sequence.Insert(i + 1, cId);
            actions.Add(new RouteAutoRepairAction(insertIndex, cId, aId, bId));

            // Ďalšia iterácia má validovať novú dvojicu (C -> B).
            i++;
        }

        if (actions.Count > 0)
        {
            Log.Information("Auto-oprava cesty '{RouteId}': doplnených {InsertCount} chýbajúcich prvkov.", route.Id, actions.Count);
        }

        return actions;
    }

    private static IReadOnlyList<RouteAutoRepairAction> AutoRepairMissingPathReferences(
        RouteDefinition route,
        TrackGraphBuilder.TrackGraph graph,
        HashSet<string> elementIdSet)
    {
        if (string.IsNullOrWhiteSpace(route.FromBlockId) || string.IsNullOrWhiteSpace(route.ToBlockId))
            return Array.Empty<RouteAutoRepairAction>();

        // sequence = [From] + path + [To]
        var sequence = new List<string>(route.PathElementIds.Count + 2)
        {
            route.FromBlockId
        };
        sequence.AddRange(route.PathElementIds);
        sequence.Add(route.ToBlockId);

        var actions = new List<RouteAutoRepairAction>();

        // Prechádzame len PathElementIds (t.j. sequence indexy 1..Count)
        for (int pathIndex = 0; pathIndex < route.PathElementIds.Count; pathIndex++)
        {
            var id = route.PathElementIds[pathIndex];
            if (string.IsNullOrWhiteSpace(id))
                continue;

            // Opravujeme len skutočne chýbajúce odkazy (prvok nie je v layoute).
            // Ak prvok existuje, ale nie je v grafe (napr. nejaký netrackový prvok), radšej do toho nezasahujeme.
            if (elementIdSet.Contains(id))
                continue;

            // Ak ID existuje v grafe, nie je čo opravovať.
            if (graph.PortsByElement.ContainsKey(id))
                continue;

            // Nájdeme najbližší existujúci prvok vľavo a vpravo.
            int seqIndex = pathIndex + 1;

            string? leftId = null;
            for (int i = seqIndex - 1; i >= 0; i--)
            {
                var cand = sequence[i];
                if (!string.IsNullOrWhiteSpace(cand) && graph.PortsByElement.ContainsKey(cand))
                {
                    leftId = cand;
                    break;
                }
            }

            string? rightId = null;
            for (int i = seqIndex + 1; i < sequence.Count; i++)
            {
                var cand = sequence[i];
                if (!string.IsNullOrWhiteSpace(cand) && graph.PortsByElement.ContainsKey(cand))
                {
                    rightId = cand;
                    break;
                }
            }

            if (leftId == null || rightId == null)
                continue;

            if (!graph.PortsByElement.TryGetValue(leftId, out var leftPorts))
                continue;

            // Ak sú ľavý a pravý prvok už priamo prepojené, chýbajúci odkaz je „len“ zvyšok – odstránime ho.
            if (AreElementsDirectlyConnected(graph, leftPorts, rightId))
            {
                route.PathElementIds.RemoveAt(pathIndex);
                sequence.RemoveAt(seqIndex);
                pathIndex--; // posun indexu po Remove
                continue;
            }

            // Inak skús nájsť jednoznačný medzi-prvok (A -> C -> B) a chýbajúci odkaz nahradiť.
            var candidates = FindSingleIntermediateCandidates(graph, leftPorts, leftId, rightId);
            if (candidates.Count != 1)
                continue;

            var cId = candidates[0];
            if (graph.ElementsById.TryGetValue(cId, out var cEl) && cEl is BlockElement)
                continue;

            // Nevytvárať duplikáty.
            if (route.PathElementIds.Contains(cId, StringComparer.OrdinalIgnoreCase))
            {
                // Ak už cesta tento prvok niekde má, radšej len odstránime chýbajúci odkaz.
                route.PathElementIds.RemoveAt(pathIndex);
                sequence.RemoveAt(seqIndex);
                pathIndex--;
                continue;
            }

            route.PathElementIds[pathIndex] = cId;
            sequence[seqIndex] = cId;
            actions.Add(new RouteAutoRepairAction(pathIndex, cId, leftId, rightId));
        }

        if (actions.Count > 0)
        {
            Log.Information("Auto-oprava cesty '{RouteId}': nahradených {ReplaceCount} chýbajúcich odkazov v PathElementIds.",
                route.Id, actions.Count);
        }

        return actions;
    }

    private static bool AreElementsDirectlyConnected(
        TrackGraphBuilder.TrackGraph graph,
        List<TrackGraphBuilder.ElementPort> aPorts,
        string bId)
    {
        foreach (var aPort in aPorts)
        {
            if (!graph.Adjacency.TryGetValue(aPort, out var neighbors))
                continue;

            if (neighbors.Any(n => string.Equals(n.ElementId, bId, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static List<string> FindSingleIntermediateCandidates(
        TrackGraphBuilder.TrackGraph graph,
        List<TrackGraphBuilder.ElementPort> aPorts,
        string aId,
        string bId)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var aPort in aPorts)
        {
            if (!graph.Adjacency.TryGetValue(aPort, out var neighbors))
                continue;

            foreach (var neighborPort in neighbors)
            {
                var cId = neighborPort.ElementId;
                if (string.Equals(cId, aId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(cId, bId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!graph.PortsByElement.TryGetValue(cId, out var cPorts))
                    continue;

                // C sa musí vedieť priamo prepojiť s B.
                if (AreElementsDirectlyConnected(graph, cPorts, bId))
                    candidates.Add(cId);
            }
        }

        // Stabilné poradie (užitočné pre testy a deterministické správanie).
        return candidates.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
    }
}






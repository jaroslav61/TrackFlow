using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using TrackFlow.Models.Layout;
using TrackFlow.Services.Operation;

namespace TrackFlow.Services;

public sealed record CollisionCheckResult(bool IsSafe, string Reason, string? BlockingBlockId = null)
{
    public static CollisionCheckResult Safe(string reason = "ok") => new(true, reason);
    public static CollisionCheckResult Blocked(string reason, string? blockingBlockId = null) => new(false, reason, blockingBlockId);
}

/// <summary>
/// Bezpecnostna kontrola pri vstupe lokomotivy do bloku.
/// Pravidla:
/// 1) Cielovy blok nesmie byt obsadeny inou lokomotivou.
/// 2) Cielovy blok nesmie byt locknuty.
/// 3) Volitelne: susedne bloky v danej bezpecnej vzdialenosti nesmu byt obsadene inou lokomotivou.
/// </summary>
public sealed class CollisionDetectionService
{
    private sealed class BlockAdjacencyGraph
    {
        public Dictionary<string, HashSet<string>> Adjacency { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> EdgeSources { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public CollisionCheckResult EvaluateEntry(
        IEnumerable<LayoutElement> layoutElements,
        string targetBlockId,
        string locoCode,
        int safetyDistanceBlocks = 1)
    {
        if (layoutElements == null) throw new ArgumentNullException(nameof(layoutElements));

        var elements = layoutElements.ToList();
        return EvaluateEntryCore(
            elements,
            targetBlockId,
            locoCode,
            safetyDistanceBlocks,
            safetySeedBlockIds: new[] { targetBlockId },
            adjacencyFactory: () => BuildBlockAdjacencyFromGraph(elements),
            topologySource: "track-graph");
    }

    public CollisionCheckResult EvaluateEntry(
        TrackLayout layout,
        string targetBlockId,
        string locoCode,
        RouteDefinition? candidateRoute = null,
        int safetyDistanceBlocks = 1)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));

        var elements = layout.Elements.ToList();
        var seedBlockIds = GetRouteSafetySeedBlockIds(candidateRoute, targetBlockId);
        return EvaluateEntryCore(
            elements,
            targetBlockId,
            locoCode,
            safetyDistanceBlocks,
            seedBlockIds,
            adjacencyFactory: () => BuildTopologicalBlockAdjacency(layout, elements, candidateRoute),
            topologySource: candidateRoute != null
                ? "candidate-route"
                : layout.Routes.Count > 0 ? "route-definitions" : "track-graph-fallback");
    }

    private static CollisionCheckResult EvaluateEntryCore(
        List<LayoutElement> elements,
        string targetBlockId,
        string locoCode,
        int safetyDistanceBlocks,
        IEnumerable<string> safetySeedBlockIds,
        Func<BlockAdjacencyGraph> adjacencyFactory,
        string topologySource)
    {
        if (string.IsNullOrWhiteSpace(targetBlockId)) return CollisionCheckResult.Blocked("target-block-missing");
        if (string.IsNullOrWhiteSpace(locoCode)) return CollisionCheckResult.Blocked("loco-code-missing");

        var blocks = elements.OfType<BlockElement>().ToList();
        var blockById = blocks.ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        if (!blockById.TryGetValue(targetBlockId, out var target))
            return CollisionCheckResult.Blocked("target-block-not-found");

        if (target.IsLocked)
            return CollisionCheckResult.Blocked("target-block-locked", target.Id);

        if (target.IsOccupied && !string.Equals(target.AssignedLocoId, locoCode, StringComparison.OrdinalIgnoreCase))
            return CollisionCheckResult.Blocked("target-block-occupied", target.Id);

        if (!string.IsNullOrWhiteSpace(target.ReservedLocoId)
            && !string.Equals(target.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase))
        {
            return CollisionCheckResult.Blocked("target-block-reserved", target.Id);
        }

        if (safetyDistanceBlocks <= 0)
            return CollisionCheckResult.Safe();

        var graph = adjacencyFactory();
        var adjacency = graph.Adjacency;
        if (adjacency.Count == 0)
            return CollisionCheckResult.Safe("no-topology");

        var seedIds = safetySeedBlockIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(id => blockById.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (seedIds.Count == 0)
            seedIds.Add(target.Id);

        Log.Debug("Collision safety-distance topology={TopologySource}, target={TargetBlock}, seeds=[{SeedBlocks}], depth={SafetyDistanceBlocks}",
            topologySource,
            BlockDisplayName(target),
            string.Join(",", seedIds.Select(id => blockById.TryGetValue(id, out var b) ? BlockDisplayName(b) : LayoutElementDisplayHelper.ShortId(id))),
            safetyDistanceBlocks);

        DiagnoseTargetAdjacency(target, blockById, graph);

        var visited = new HashSet<string>(seedIds, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string blockId, int distance)>();
        foreach (var seedId in seedIds)
            queue.Enqueue((seedId, 0));

        while (queue.Count > 0)
        {
            var (current, distance) = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var neighbors))
                continue;

            foreach (var neighborId in neighbors)
            {
                if (!visited.Add(neighborId))
                    continue;

                var nextDistance = distance + 1;
                if (!blockById.TryGetValue(neighborId, out var neighborBlock))
                    continue;

                if (nextDistance <= safetyDistanceBlocks && IsOccupiedByOtherLoco(neighborBlock, locoCode))
                {
                    return CollisionCheckResult.Blocked("neighbor-block-occupied", neighborBlock.Id);
                }

                if (nextDistance < safetyDistanceBlocks)
                    queue.Enqueue((neighborId, nextDistance));
            }
        }

        return CollisionCheckResult.Safe();
    }

    private static BlockAdjacencyGraph BuildTopologicalBlockAdjacency(TrackLayout layout, List<LayoutElement> elements, RouteDefinition? candidateRoute)
    {
        if (candidateRoute != null)
            return BuildBlockAdjacencyFromRouteDefinitions(layout, new[] { candidateRoute });

        var graph = BuildBlockAdjacencyFromRouteDefinitions(layout, layout.Routes);
        return graph.Adjacency.Count > 0 ? graph : BuildBlockAdjacencyFromGraph(elements);
    }

    private static BlockAdjacencyGraph BuildBlockAdjacencyFromRouteDefinitions(TrackLayout layout, IEnumerable<RouteDefinition> routes)
    {
        var graph = new BlockAdjacencyGraph();

        foreach (var route in routes.Where(r => r != null))
        {
            var blockIds = route.BlockIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            if (blockIds.Count < 2
                && !string.IsNullOrWhiteSpace(route.FromBlockId)
                && !string.IsNullOrWhiteSpace(route.ToBlockId))
            {
                blockIds = new List<string> { route.FromBlockId, route.ToBlockId };
            }

            var routeSource = RouteDisplayName(layout, route);
            for (int i = 0; i < blockIds.Count - 1; i++)
                AddUndirectedEdge(graph, blockIds[i], blockIds[i + 1], routeSource);
        }

        return graph;
    }

    private static BlockAdjacencyGraph BuildBlockAdjacencyFromGraph(List<LayoutElement> elements)
    {
        var graph = new BlockAdjacencyGraph();

        try
        {
            var layout = new TrackLayout
            {
                Elements = elements
            };

            var routes = new RoutePathfinder(layout).FindAllRoutes();
            foreach (var route in routes)
            {
                AddUndirectedEdge(graph, route.FromBlockId, route.ToBlockId, "track-graph");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CollisionDetectionService failed to build adjacency. Falling back to safe-by-default.");
        }

        return graph;
    }

    private static IEnumerable<string> GetRouteSafetySeedBlockIds(RouteDefinition? candidateRoute, string targetBlockId)
    {
        return new[] { targetBlockId };
    }

    private static void AddUndirectedEdge(BlockAdjacencyGraph graph, string fromBlockId, string toBlockId, string source)
    {
        if (string.IsNullOrWhiteSpace(fromBlockId) || string.IsNullOrWhiteSpace(toBlockId))
            return;
        if (string.Equals(fromBlockId, toBlockId, StringComparison.OrdinalIgnoreCase))
            return;

        if (!graph.Adjacency.TryGetValue(fromBlockId, out var fromSet))
        {
            fromSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            graph.Adjacency[fromBlockId] = fromSet;
        }

        if (!graph.Adjacency.TryGetValue(toBlockId, out var toSet))
        {
            toSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            graph.Adjacency[toBlockId] = toSet;
        }

        fromSet.Add(toBlockId);
        toSet.Add(fromBlockId);
        AddEdgeSource(graph, fromBlockId, toBlockId, source);
    }

    private static void AddEdgeSource(BlockAdjacencyGraph graph, string fromBlockId, string toBlockId, string source)
    {
        var key = EdgeKey(fromBlockId, toBlockId);
        if (!graph.EdgeSources.TryGetValue(key, out var sources))
        {
            sources = new List<string>();
            graph.EdgeSources[key] = sources;
        }

        if (!sources.Contains(source, StringComparer.OrdinalIgnoreCase))
            sources.Add(source);
    }

    private static string EdgeKey(string a, string b) =>
        string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0 ? $"{a}|{b}" : $"{b}|{a}";

    private static void DiagnoseTargetAdjacency(BlockElement target, IReadOnlyDictionary<string, BlockElement> blockById, BlockAdjacencyGraph graph)
    {
        var targetName = BlockDisplayName(target);
        var neighborsText = "žiadni susedia";

        if (graph.Adjacency.TryGetValue(target.Id, out var neighbors) && neighbors.Count > 0)
        {
            neighborsText = string.Join(" | ", neighbors.Select(neighborId =>
            {
                var neighborName = blockById.TryGetValue(neighborId, out var neighbor)
                    ? BlockDisplayName(neighbor)
                    : LayoutElementDisplayHelper.ShortId(neighborId);
                var sources = graph.EdgeSources.TryGetValue(EdgeKey(target.Id, neighborId), out var sourceList)
                    ? string.Join(", ", sourceList)
                    : "neznámy zdroj";
                return $"{neighborName} cez {sources}";
            }));
        }

        var message = $"Susedné bloky pre {targetName}: {neighborsText}";
        Log.Debug("{Message}", message);
        TrackFlowDoctorService.Instance.Diagnose("Safety", message, DiagnosticLevel.Info);
    }

    private static string RouteDisplayName(TrackLayout layout, RouteDefinition route)
    {
        if (!string.IsNullOrWhiteSpace(route.Name))
            return route.Name;

        var from = OperationDisplayHelpers.ResolveBlockDisplayName(layout, OperationDisplayHelpers.ResolveRouteStartBlockId(route));
        var to = OperationDisplayHelpers.ResolveBlockDisplayName(layout, OperationDisplayHelpers.ResolveRouteEndBlockId(route));
        return $"{from} → {to}";
    }

    private static bool IsOccupiedByOtherLoco(BlockElement block, string locoCode) =>
        block.IsOccupied
        && !string.IsNullOrWhiteSpace(block.AssignedLocoId)
        && !string.Equals(block.AssignedLocoId, locoCode, StringComparison.OrdinalIgnoreCase);

    private static string BlockDisplayName(BlockElement block) => OperationDisplayHelpers.BlockDisplayName(block);
}


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using static TrackFlow.Services.TrackGraphBuilder;
using Serilog;

namespace TrackFlow.Services;

/// <summary>
/// Finds all valid routes between neighboring blocks using DFS traversal
/// of the track connectivity graph built by TrackGraphBuilder.
/// 
/// Algorithm:
/// 1. Build connectivity graph from layout elements
/// 2. For each block, start DFS from both ports (A and B)
// 3. Traverse through track segments, curves, turnouts
/// 4. Stop when reaching another block (= found a route)
/// 5. Do NOT traverse through blocks (routes are between adjacent blocks only)
/// 6. Record turnout states required for each route
/// 7. Both directions (A→B and B→A) are kept as separate routes
/// 8. Multiple routes between same block pair are kept if turnout states differ
/// </summary>
public class RoutePathfinder
{
    private static readonly object CacheSync = new();
    private static readonly Dictionary<string, List<FoundRoute>> Cache = new(StringComparer.Ordinal);
    private static int _cacheHitCount;
    private static int _cacheMissCount;

    private readonly TrackLayout _layout;
    private readonly int _maxPathElements;
    private readonly int _maxTurnoutsInPath;

    public static int CacheHitCount
    {
        get { lock (CacheSync) return _cacheHitCount; }
    }

    public static int CacheMissCount
    {
        get { lock (CacheSync) return _cacheMissCount; }
    }

    public static void ClearCacheForTests()
    {
        lock (CacheSync)
        {
            Cache.Clear();
            _cacheHitCount = 0;
            _cacheMissCount = 0;
        }
    }

    /// <summary>
    /// Vytvára RoutePathfinder s konfigurovateľnými limitmi.
    /// </summary>
    /// <param name="layout">Layout na analýzu.</param>
    /// <param name="settings">Projektové nastavenia (voliteľné, použijú sa defaulty).</param>
    public RoutePathfinder(TrackLayout layout, ProjectSettingsData? settings = null)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _maxPathElements = settings?.MaxPathElements ?? 15;
        _maxTurnoutsInPath = settings?.MaxTurnoutsInPath ?? 5;
    }

    /// <summary>
    /// Finds all routes between neighboring blocks in the layout.
    /// Each route includes the turnout states needed to establish the path.
    /// Both directions and all parallel tracks (different turnout configs) are preserved.
    /// </summary>
    public List<FoundRoute> FindAllRoutes()
    {
        var cacheKey = BuildCacheKey(_layout, _maxPathElements, _maxTurnoutsInPath);
        lock (CacheSync)
        {
            if (Cache.TryGetValue(cacheKey, out var cached))
            {
                _cacheHitCount++;
                return CloneRoutes(cached);
            }
            _cacheMissCount++;
        }

        var graphBuilder = new TrackGraphBuilder();
        var graph = graphBuilder.Build(_layout);
        var allRoutes = new List<FoundRoute>();

        var blocks = _layout.Elements.OfType<BlockElement>().ToList();
        Log.Information("RoutePathfinder finding routes. Blocks: {BlockCount}, Elements: {ElementCount}", 
            blocks.Count, _layout.Elements.Count);

        foreach (var block in blocks)
        {
            if (!graph.PortsByElement.TryGetValue(block.Id, out var blockPorts))
            {
                Log.Warning("Block {BlockLabel} has no ports in graph", block.Label ?? block.Id);
                continue;
            }

            Log.Debug("Starting DFS from block {BlockLabel} ({PortCount} ports)", 
                block.Label ?? block.Id, blockPorts.Count);

            foreach (var startPort in blockPorts)
            {
                var visited = new HashSet<string> { block.Id };
                var pathElements = new List<string>();
                var turnoutStates = new Dictionary<string, TurnoutState>();

                DFS(graph, block.Id, startPort, visited, pathElements, turnoutStates, allRoutes,
                    startPort.PortName, lastTurnoutExitPort: "");
            }
        }

        Log.Information("Found {RawRouteCount} raw routes before filtering", allRoutes.Count);

        var deduped = DeduplicateRoutes(allRoutes);
        Log.Information("After dedup: {DedupedCount} unique routes", deduped.Count);

        var shortest = KeepShortestPerPair(deduped);
        Log.Information("After shortest-per-pair filter: {ShortestCount} routes", shortest.Count);

        var result = CloneRoutes(shortest);
        lock (CacheSync)
        {
            Cache[cacheKey] = CloneRoutes(shortest);
        }

        return result;
    }

    /// <summary>
    /// Nájde cestu medzi dvoma blokmi cez graf elementárnych susedných ciest.
    /// Výsledok obsahuje poradie blokov, potrebné výhybky so stavmi aj relevantné
    /// návestidlá orientované v smere jazdy.
    /// </summary>
    public RouteSearchResult? FindRouteBetweenBlocks(string fromBlockId, string toBlockId)
    {
        if (string.IsNullOrWhiteSpace(fromBlockId) || string.IsNullOrWhiteSpace(toBlockId))
            return null;

        if (string.Equals(fromBlockId, toBlockId, StringComparison.OrdinalIgnoreCase))
            return null;

        var blockById = _layout.Elements
            .OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        if (!blockById.ContainsKey(fromBlockId) || !blockById.ContainsKey(toBlockId))
            return null;

        var neighborRoutes = FindAllRoutes();
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previous = new Dictionary<string, FoundRoute>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue(fromBlockId);
        visited.Add(fromBlockId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (string.Equals(current, toBlockId, StringComparison.OrdinalIgnoreCase))
                break;

            foreach (var edge in neighborRoutes.Where(r => string.Equals(r.FromBlockId, current, StringComparison.OrdinalIgnoreCase)))
            {
                if (visited.Contains(edge.ToBlockId))
                    continue;

                visited.Add(edge.ToBlockId);
                previous[edge.ToBlockId] = edge;
                queue.Enqueue(edge.ToBlockId);
            }
        }

        if (!visited.Contains(toBlockId))
            return null;

        var edgePath = new List<FoundRoute>();
        var walk = toBlockId;
        while (!string.Equals(walk, fromBlockId, StringComparison.OrdinalIgnoreCase))
        {
            if (!previous.TryGetValue(walk, out var edge))
                return null;

            edgePath.Add(edge);
            walk = edge.FromBlockId;
        }
        edgePath.Reverse();

        var blockIds = new List<string> { fromBlockId };
        blockIds.AddRange(edgePath.Select(e => e.ToBlockId));

        var result = new RouteSearchResult
        {
            Blocks = blockIds
                .Where(id => blockById.ContainsKey(id))
                .Select(id => blockById[id])
                .ToList(),
            BlockIds = blockIds,
            PathElementIds = edgePath
                .SelectMany(e => e.PathElementIds)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        var switchStateById = new Dictionary<string, TurnoutState>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edgePath)
        {
            foreach (var (turnoutId, requiredState) in edge.TurnoutStates)
                switchStateById[turnoutId] = requiredState;
        }

        result.SwitchStates = switchStateById
            .Select(kv => new SwitchState { TurnoutId = kv.Key, RequiredState = kv.Value })
            .ToList();

        if (edgePath.Count > 0)
        {
            var fromDirection = ResolveDirectionFromBlockPort(blockById[fromBlockId], edgePath[0].FromBlockExitPort, RouteDirection.Right);
            var toDirection = ResolveDirectionFromBlockPort(blockById[toBlockId], edgePath[^1].ToBlockEntryPort, RouteDirection.Right);
            result.FromBlockDirection = fromDirection;
            result.ToBlockDirection = toDirection;
            result.StartNavigationDirection = fromDirection;
        }

        var signalById = _layout.Elements
            .OfType<SignalElement>()
            .ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edgePath)
        {
            if (!blockById.TryGetValue(edge.FromBlockId, out var block))
                continue;

            var travelDirection = ResolveNavigationDirectionFromBlockPort(block, edge.FromBlockExitPort);
            var signalId = block.GetSignalForDirection(travelDirection);
            if (string.IsNullOrWhiteSpace(signalId))
                continue;
            if (!signalById.TryGetValue(signalId, out var signal))
                continue;
            if (result.Signals.Any(s => string.Equals(s.Id, signal.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Signals.Add(signal);
        }

        return result;
    }

    private static List<FoundRoute> CloneRoutes(List<FoundRoute> routes)
    {
        return routes.Select(r => new FoundRoute
        {
            FromBlockId = r.FromBlockId,
            ToBlockId = r.ToBlockId,
            FromBlockExitPort = r.FromBlockExitPort,
            ToBlockEntryPort = r.ToBlockEntryPort,
            PathElementIds = new List<string>(r.PathElementIds),
            TurnoutStates = new Dictionary<string, TurnoutState>(r.TurnoutStates)
        }).ToList();
    }

    internal static string BuildCacheKey(TrackLayout layout, int maxPathElements, int maxTurnoutsInPath)
    {
        var sb = new StringBuilder(1024);
        sb.Append("limits:")
            .Append(maxPathElements).Append('|')
            .Append(maxTurnoutsInPath).Append(';');

        foreach (var element in layout.Elements.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            sb.Append(element.Id).Append('|')
              .Append((int)element.ElementType).Append('|')
              .Append(element.MarkerKey).Append('|')
              .Append(element.X).Append('|')
              .Append(element.Y).Append('|')
              .Append(element.Rotation);

            if (element is BlockElement block)
                sb.Append("|bl=").Append(block.BlockLengthCells);

            sb.Append(';');
        }

        return sb.ToString();
    }

    /// <summary>
    /// DFS traversal through the track graph.
    /// </summary>
    /// <param name="entryPortName">Port name through which the starting block was exited.</param>
    /// <param name="lastTurnoutExitPort">Port name through which the last traversed turnout was exited.
    /// Used to detect and block consecutive branch traversals (S-curves).</param>
    private void DFS(
        TrackGraph graph,
        string startBlockId,
        ElementPort currentPort,
        HashSet<string> visited,
        List<string> pathElements,
        Dictionary<string, TurnoutState> turnoutStates,
        List<FoundRoute> results,
        string entryPortName,
        string lastTurnoutExitPort)
    {
        // ─── Depth limit: terminate branch if it grew too long ──────────────
        if (pathElements.Count > _maxPathElements) return;
        if (turnoutStates.Count > _maxTurnoutsInPath) return;

        if (!graph.Adjacency.TryGetValue(currentPort, out var neighbors))
            return;

        foreach (var neighborPort in neighbors)
        {
            if (visited.Contains(neighborPort.ElementId))
                continue;

            if (!graph.ElementsById.TryGetValue(neighborPort.ElementId, out var neighborElement))
                continue;

            // ─── BINGO: Found a neighboring block! ───────────────────────────
            if (neighborElement is BlockElement targetBlock)
            {
                Log.Debug("FOUND route: {FromBlock} → {ToBlock} via {ElementCount} elements, " +
                          "{TurnoutCount} turnouts [{ExitPort}→{EntryPort}]",
                    GetLabel(startBlockId), GetLabel(targetBlock.Id), pathElements.Count, 
                    turnoutStates.Count, entryPortName, neighborPort.PortName);

                var route = new FoundRoute
                {
                    FromBlockId = startBlockId,
                    ToBlockId = targetBlock.Id,
                    FromBlockExitPort = entryPortName,
                    ToBlockEntryPort = neighborPort.PortName,
                    PathElementIds = new List<string>(pathElements),
                    TurnoutStates = new Dictionary<string, TurnoutState>(turnoutStates)
                };
                results.Add(route);

                // Stop-at-first-block pruning: do NOT search beyond this block.
                return;
            }

            // ─── Mark as visited and add to path ─────────────────────────────
            visited.Add(neighborPort.ElementId);
            pathElements.Add(neighborPort.ElementId);

            if (graph.PortsByElement.TryGetValue(neighborPort.ElementId, out var elementPorts))
            {
                var validExits = GetValidExitPorts(neighborElement, neighborPort.PortName, elementPorts);

                foreach (var exitPort in validExits)
                {
                    string nextLastTurnoutExitPort = lastTurnoutExitPort;

                    if (neighborElement is TurnoutElement)
                    {
                        // ─── Block consecutive branch traversals (S-curve / U-turn) ──
                        // If the previous turnout was also exited via a branch port,
                        // forbid exiting this turnout via another branch port.
                        // Pattern Root→Branch, Root→Branch through adjacent junctions = S-curve.
                        bool exitingViaBranch = exitPort.PortName is "Diverge"
                            or "DivergeLeft" or "DivergeRight";
                        bool lastWasBranch = lastTurnoutExitPort is "Diverge"
                            or "DivergeLeft" or "DivergeRight";

                        if (exitingViaBranch && lastWasBranch)
                            continue; // S-curve: skip this branch

                        var state = DetermineTurnoutState(neighborPort.PortName, exitPort.PortName);
                        turnoutStates[neighborPort.ElementId] = state;
                        nextLastTurnoutExitPort = exitPort.PortName;
                    }

                    DFS(graph, startBlockId, exitPort, visited, pathElements, turnoutStates, results,
                        entryPortName, nextLastTurnoutExitPort);

                    if (neighborElement is TurnoutElement)
                        turnoutStates.Remove(neighborPort.ElementId);
                }
            }

            // ─── Backtrack ───────────────────────────────────────────────────
            pathElements.RemoveAt(pathElements.Count - 1);
            visited.Remove(neighborPort.ElementId);
        }
    }

    /// <summary>
    /// Returns valid exit ports for a given entry port, respecting physical constraints.
    /// For turnouts: you can only go Root↔Branch, NOT Branch↔Branch (that requires reversal).
    /// For tracks/curves: the other port is always valid.
    /// </summary>
    private static List<ElementPort> GetValidExitPorts(
        LayoutElement element, string entryPortName, List<ElementPort> allPorts)
    {
        var result = new List<ElementPort>();

        if (element is TurnoutElement)
        {
            // Turnout traversal rules (no reversal at junction):
            // Root → Straight, Diverge, DivergeLeft, DivergeRight (any branch)
            // Straight → Root only
            // Diverge → Root only
            // DivergeLeft → Root only
            // DivergeRight → Root only
            // DoubleSlip: A↔C, B↔D, A↔D, B↔C (crossing patterns)

            bool isDoubleSlip = allPorts.Any(p => p.PortName == "A") &&
                                allPorts.Any(p => p.PortName == "D");

            if (isDoubleSlip)
            {
                // DoubleSlip: all exits except entry
                foreach (var p in allPorts)
                    if (p.PortName != entryPortName) result.Add(p);
            }
            else if (entryPortName == "Root")
            {
                // From Root: can exit through any branch port
                foreach (var p in allPorts)
                    if (p.PortName != "Root") result.Add(p);
            }
            else
            {
                // From any branch: can ONLY exit through Root
                foreach (var p in allPorts)
                    if (p.PortName == "Root") result.Add(p);
            }
        }
        else
        {
            // Track segments, curves, etc.: exit through any port except entry
            foreach (var p in allPorts)
                if (p.PortName != entryPortName) result.Add(p);
        }

        return result;
    }

    /// <summary>
    /// Determines the required turnout state based on which ports are used.
    /// </summary>
    private static TurnoutState DetermineTurnoutState(string entryPortName, string exitPortName)
    {
        var ports = new[] { entryPortName, exitPortName };
        if (ports.Contains("Straight")) return TurnoutState.Straight;
        if (ports.Contains("DivergeLeft")) return TurnoutState.DivergeLeft;
        if (ports.Contains("DivergeRight")) return TurnoutState.DivergeRight;
        if (ports.Contains("Diverge")) return TurnoutState.Diverge;
        return TurnoutState.Straight;
    }

    /// <summary>
    /// Remove exact duplicate routes. Key includes direction (FromId|ToId), port pair, and turnout states.
    /// A→B and B→A are different. Parallel tracks with same ports but different turnout states are both kept.
    /// </summary>
    /// <summary>
    /// Remove exact duplicate routes. Key: FromId|ToId|TurnoutStates.
    /// Ports are intentionally excluded because port names are inconsistent in the graph.
    /// A→B and B→A are different directions (different FromId/ToId).
    /// </summary>
    private static List<FoundRoute> DeduplicateRoutes(List<FoundRoute> routes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal); // Explicit comparer
        var result = new List<FoundRoute>();

        foreach (var route in routes)
        {
            // Use sorted OrderBy with explicit comparer for deterministic ordering
            var turnoutKey = string.Join(",",
                route.TurnoutStates
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal) // Explicit comparer
                    .Select(kv => $"{kv.Key}:{kv.Value}"));

            // Ports excluded from key – names are inconsistent in the graph.
            var key = $"{route.FromBlockId}|{route.ToBlockId}|{turnoutKey}";
            if (seen.Add(key))
                result.Add(route);
        }

        return result;
    }

    /// <summary>
    /// For each (FromBlockId → ToBlockId) pair, keep only routes with the absolute minimum
    /// number of path elements. Among those, keep all that have distinct turnout states
    /// (genuine parallel tracks / alternative platform roads).
    /// </summary>
    private static List<FoundRoute> KeepShortestPerPair(List<FoundRoute> routes)
    {
        var result = new List<FoundRoute>();

        // Group only by direction – ports are unreliable
        var grouped = routes.GroupBy(r => $"{r.FromBlockId}|{r.ToBlockId}");

        foreach (var group in grouped)
        {
            int minElements = group.Min(r => r.PathElementIds.Count);

            // Collect candidates at minimum length
            var candidates = group.Where(r => r.PathElementIds.Count == minElements).ToList();

            // Among candidates keep only those with unique turnout-state signatures
            var seenTurnouts = new HashSet<string>();
            foreach (var r in candidates)
            {
                var tKey = string.Join(",",
                    r.TurnoutStates.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
                if (seenTurnouts.Add(tKey))
                    result.Add(r);
            }
        }

        return result;
    }

    private string GetLabel(string elementId)
    {
        if (_layout.Elements.FirstOrDefault(e => e.Id == elementId) is var el && el != null)
            return string.IsNullOrWhiteSpace(el.Label) ? elementId[..Math.Min(8, elementId.Length)] : el.Label;
        return elementId[..Math.Min(8, elementId.Length)];
    }

    private static NavigationDirection ResolveNavigationDirectionFromBlockPort(BlockElement block, string? portName)
    {
        bool isVertical = LayoutElementFootprintHelper.IsVertical(block.Rotation);
        if (string.Equals(portName, "A", StringComparison.OrdinalIgnoreCase))
            return isVertical ? NavigationDirection.Up : NavigationDirection.Left;
        if (string.Equals(portName, "B", StringComparison.OrdinalIgnoreCase))
            return isVertical ? NavigationDirection.Down : NavigationDirection.Right;
        return NavigationDirection.Right;
    }

    private static string ResolveDirectionFromBlockPort(BlockElement block, string? portName, string defaultDirection)
    {
        return ResolveNavigationDirectionFromBlockPort(block, portName) switch
        {
            NavigationDirection.Left => RouteDirection.Left,
            NavigationDirection.Right => RouteDirection.Right,
            NavigationDirection.Up => RouteDirection.Up,
            NavigationDirection.Down => RouteDirection.Down,
            _ => defaultDirection
        };
    }
}

/// <summary>
/// A found route between two neighboring blocks, including the exact path
/// and required turnout states.
/// </summary>
public class FoundRoute
{
    public string FromBlockId { get; set; } = string.Empty;
    public string ToBlockId { get; set; } = string.Empty;

    /// <summary>Port name through which the source block was exited (e.g. "A" or "B").</summary>
    public string FromBlockExitPort { get; set; } = string.Empty;

    /// <summary>Port name through which the destination block was entered (e.g. "A" or "B").</summary>
    public string ToBlockEntryPort { get; set; } = string.Empty;

    /// <summary>IDs of all elements on the path (excluding start and end blocks).</summary>
    public List<string> PathElementIds { get; set; } = new();

    /// <summary>Required turnout states: TurnoutElementId → RequiredState.</summary>
    public Dictionary<string, TurnoutState> TurnoutStates { get; set; } = new();
}

/// <summary>
/// Výsledok cieleného hľadania cesty medzi dvoma blokmi.
/// </summary>
public sealed class RouteSearchResult
{
    public List<BlockElement> Blocks { get; set; } = new();
    public List<string> BlockIds { get; set; } = new();
    public List<string> PathElementIds { get; set; } = new();
    public List<SwitchState> SwitchStates { get; set; } = new();
    public List<SignalElement> Signals { get; set; } = new();
    public string FromBlockDirection { get; set; } = RouteDirection.Right;
    public string ToBlockDirection { get; set; } = RouteDirection.Right;
    public string StartNavigationDirection { get; set; } = RouteDirection.Right;
}

/// <summary>Požadovaný stav výhybky pre nájdenú trasu.</summary>
public sealed class SwitchState
{
    public string TurnoutId { get; set; } = string.Empty;
    public TurnoutState RequiredState { get; set; }
}


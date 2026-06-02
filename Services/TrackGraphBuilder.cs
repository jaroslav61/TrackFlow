using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

/// <summary>
/// Builds a connectivity graph from layout elements based on grid position matching.
/// Each element has ports (connection points) with directions.
/// Two elements are connected if their ports face each other on adjacent cells.
/// 
/// Direction convention (same as GetAdjacentCellForAngle):
///   0°=West, 45°=NW, 90°=North, 135°=NE, 180°=East, 225°=SE, 270°=South, 315°=SW
/// </summary>
public class TrackGraphBuilder
{
    private const double CellSize = 24.0;

    // ══════════════════════════════════════════════════════════════════════════
    // Public types
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Port on a layout element - a named connection point at a cell with a direction.</summary>
    public sealed record ElementPort(string ElementId, string PortName, int Direction, int CellX, int CellY);

    /// <summary>Connectivity graph of the track layout.</summary>
    public sealed class TrackGraph
    {
        /// <summary>All ports indexed by (cellX, cellY, direction). Multiple ports can exist at same key (overlapping elements).</summary>
        public Dictionary<(int cx, int cy, int dir), List<ElementPort>> PortsByCell { get; } = new();

        /// <summary>Adjacency list: port → connected ports on neighboring elements.</summary>
        public Dictionary<ElementPort, List<ElementPort>> Adjacency { get; } = new();

        /// <summary>All ports belonging to a given element.</summary>
        public Dictionary<string, List<ElementPort>> PortsByElement { get; } = new();

        /// <summary>Quick element lookup by ID.</summary>
        public Dictionary<string, LayoutElement> ElementsById { get; } = new();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Build
    // ══════════════════════════════════════════════════════════════════════════

    public TrackGraph Build(TrackLayout layout)
    {
        var graph = new TrackGraph();

        // Index elements by ID
        foreach (var el in layout.Elements)
            graph.ElementsById[el.Id] = el;

        // Step 1: Create ports for all elements
        foreach (var element in layout.Elements)
        {
            var ports = GetElementPorts(element);
            foreach (var port in ports)
            {
                var key = (port.CellX, port.CellY, port.Direction);
                if (!graph.PortsByCell.TryGetValue(key, out var list))
                {
                    list = new List<ElementPort>();
                    graph.PortsByCell[key] = list;
                }
                list.Add(port);

                if (!graph.PortsByElement.ContainsKey(port.ElementId))
                    graph.PortsByElement[port.ElementId] = new List<ElementPort>();
                graph.PortsByElement[port.ElementId].Add(port);

                if (!graph.Adjacency.ContainsKey(port))
                    graph.Adjacency[port] = new List<ElementPort>();
            }
        }

        Debug.WriteLine($"[TrackGraphBuilder] Built {graph.PortsByElement.Count} elements with ports, total port-cells: {graph.PortsByCell.Count}");

        // Step 2: Connect matching ports on adjacent cells
        int connectionCount = 0;
        foreach (var port in graph.PortsByElement.Values.SelectMany(l => l))
        {
            var (adjCx, adjCy) = GetAdjacentCell(port.CellX, port.CellY, port.Direction);
            int oppositeDir = (port.Direction + 180) % 360;

            var adjKey = (adjCx, adjCy, oppositeDir);
            if (graph.PortsByCell.TryGetValue(adjKey, out var adjPorts))
            {
                foreach (var adjPort in adjPorts)
                {
                    // Don't connect a port to another port on the same element
                    if (adjPort.ElementId == port.ElementId) continue;

                    if (!graph.Adjacency[port].Contains(adjPort))
                    {
                        graph.Adjacency[port].Add(adjPort);
                        connectionCount++;
                    }
                }
            }
        }

        Debug.WriteLine($"[TrackGraphBuilder] Created {connectionCount} port connections");

        // Diagnostic: log block ports and their connections
        foreach (var el in layout.Elements.OfType<BlockElement>())
        {
            if (graph.PortsByElement.TryGetValue(el.Id, out var bports))
            {
                foreach (var bp in bports)
                {
                    int connCount = graph.Adjacency.TryGetValue(bp, out var adj) ? adj.Count : 0;
                    Debug.WriteLine($"  Block '{el.Label}' port {bp.PortName} at cell ({bp.CellX},{bp.CellY}) dir={bp.Direction} → {connCount} connections");
                }
            }
        }

        return graph;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Port definitions per element type
    // ══════════════════════════════════════════════════════════════════════════

    private List<ElementPort> GetElementPorts(LayoutElement element)
    {
        var ports = new List<ElementPort>();
        int cellX = (int)Math.Round(element.X / CellSize);
        int cellY = (int)Math.Round(element.Y / CellSize);
        int rot = NormalizeAngle((int)element.Rotation);

        switch (element)
        {
            case BlockElement block:
                AddBlockPorts(ports, block, cellX, cellY, rot);
                break;

            case TurnoutElement:
                AddTurnoutPorts(ports, element.Id, element.MarkerKey, cellX, cellY, rot);
                break;

            case TrackSegmentElement:
                AddTrackSegmentPorts(ports, element, cellX, cellY, rot);
                break;

            case CurveElement:
                AddCurvePorts(ports, element, cellX, cellY, rot);
                break;

            case BumperElement:
                // Bumper has one port (the open end)
                ports.Add(new ElementPort(element.Id, "A", RotateDir(270, rot), cellX, cellY));
                break;
        }

        return ports;
    }

    private void AddBlockPorts(List<ElementPort> ports, BlockElement block, int cellX, int cellY, int rot)
    {
        int length = Math.Clamp(block.BlockLengthCells, 1, 20);

        if (rot == 0 || rot == 180)
        {
            // Horizontal block: Port A (left/west), Port B (right/east)
            ports.Add(new ElementPort(block.Id, "A", 0, cellX, cellY));                     // West
            ports.Add(new ElementPort(block.Id, "B", 180, cellX + length - 1, cellY));      // East
        }
        else // 90 or 270
        {
            // Vertical block: Port A (top/north), Port B (bottom/south)
            ports.Add(new ElementPort(block.Id, "A", 90, cellX, cellY));                    // North
            ports.Add(new ElementPort(block.Id, "B", 270, cellX, cellY + length - 1));      // South
        }
    }

    private void AddTurnoutPorts(List<ElementPort> ports, string id, string markerKey, int cx, int cy, int rot)
    {
        // Base ports at rotation=0, then rotate by element rotation
        // At rotation 0: Root=South(270°), Straight=North(90°)
        // Diverge direction depends on turnout type
        var basePorts = markerKey switch
        {
            "Turnout_L"      => new[] { ("Root", 270), ("Straight", 90), ("Diverge", 45) },
            "Turnout_R"      => new[] { ("Root", 270), ("Straight", 90), ("Diverge", 135) },
            "TurnoutL90"     => new[] { ("Root", 270), ("Straight", 90), ("Diverge", 0) },
            "TurnoutR90"     => new[] { ("Root", 270), ("Straight", 90), ("Diverge", 180) },
            "TurnoutCurve_L" => new[] { ("Root", 270), ("Straight", 90), ("Diverge", 45) },
            "TurnoutCurve_R" => new[] { ("Root", 270), ("Straight", 90), ("Diverge", 135) },
            "Turnout_Y"      => new[] { ("Root", 270), ("DivergeLeft", 45), ("DivergeRight", 135) },
            "Turnout_3W"     => new[] { ("Root", 270), ("Straight", 90), ("DivergeLeft", 45), ("DivergeRight", 135) },
            "DoubleSlip"     => new[] { ("A", 270), ("B", 90), ("C", 0), ("D", 180) },
            _                => new[] { ("Root", 270), ("Straight", 90), ("Diverge", 45) },
        };

        foreach (var (name, baseDir) in basePorts)
            ports.Add(new ElementPort(id, name, RotateDir(baseDir, rot), cx, cy));
    }

    private void AddTrackSegmentPorts(List<ElementPort> ports, LayoutElement el, int cx, int cy, int rot)
    {
        // TrackSegment at rotation=0 is HORIZONTAL: exits West(0°) and East(180°)
        // (matches MarkerTrackSegment.axaml: line from (0,12) to (24,12))
        ports.Add(new ElementPort(el.Id, "A", RotateDir(0, rot), cx, cy));
        ports.Add(new ElementPort(el.Id, "B", RotateDir(180, rot), cx, cy));
    }

    private void AddCurvePorts(List<ElementPort> ports, LayoutElement el, int cx, int cy, int rot)
    {
        // Distinguish curve types by MarkerKey
        // Curve_90 at rot=0: from (12,24) to (24,12) = South(270°) and East(180°)
        // Curve_45 at rot=0: from (0,24) to (24,12) = SouthWest(315°) and East(180°)
        switch (el.MarkerKey)
        {
            case "Curve_45":
                ports.Add(new ElementPort(el.Id, "A", RotateDir(315, rot), cx, cy));
                ports.Add(new ElementPort(el.Id, "B", RotateDir(180, rot), cx, cy));
                break;
            case "Curve_90":
            default:
                ports.Add(new ElementPort(el.Id, "A", RotateDir(270, rot), cx, cy));
                ports.Add(new ElementPort(el.Id, "B", RotateDir(180, rot), cx, cy));
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Rotate a base direction by given rotation (both in degrees, multiples of 45).</summary>
    private static int RotateDir(int baseDir, int rotation) => (baseDir + rotation) % 360;

    /// <summary>Normalize angle to 0-359 range.</summary>
    private static int NormalizeAngle(int angle) => ((angle % 360) + 360) % 360;

    /// <summary>Get adjacent cell coordinates for a given direction.</summary>
    public static (int cx, int cy) GetAdjacentCell(int cx, int cy, int direction) => direction switch
    {
        0   => (cx - 1, cy),      // West
        45  => (cx - 1, cy - 1),  // NorthWest
        90  => (cx, cy - 1),      // North
        135 => (cx + 1, cy - 1),  // NorthEast
        180 => (cx + 1, cy),      // East
        225 => (cx + 1, cy + 1),  // SouthEast
        270 => (cx, cy + 1),      // South
        315 => (cx - 1, cy + 1),  // SouthWest
        _   => (cx, cy)
    };
}







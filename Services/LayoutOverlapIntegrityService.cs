using System;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

public sealed record LayoutCellOverlapIssue(
    int CellX,
    int CellY,
    IReadOnlyList<LayoutElement> Elements);

/// <summary>
/// Detekcia nelegálnych prekrytí prvkov v layoute.
///
/// Poznámka: TrackFlow dnes vedome povoľuje výnimku „Block môže byť položený na rovnej koľaji",
/// aby sa blok viazal na koľajové segmenty.
/// </summary>
public static class LayoutOverlapIntegrityService
{
    public static IReadOnlyList<LayoutCellOverlapIssue> FindIllegalOverlaps(TrackLayout layout, double cellSize)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));

        var cellMap = new Dictionary<LayoutGridOccupancy.Cell, List<LayoutElement>>();

        foreach (var el in layout.Elements)
        {
            foreach (var cell in LayoutGridOccupancy.GetOccupiedCells(el, cellSize))
            {
                if (!cellMap.TryGetValue(cell, out var list))
                {
                    list = new List<LayoutElement>(2);
                    cellMap[cell] = list;
                }
                list.Add(el);
            }
        }

        var issues = new List<LayoutCellOverlapIssue>();

        foreach (var kv in cellMap)
        {
            var list = kv.Value;
            if (list.Count <= 1)
                continue;

            if (IsAllowedOverlap(list))
                continue;

            issues.Add(new LayoutCellOverlapIssue(kv.Key.X, kv.Key.Y, list.ToList()));
        }

        // Stabilné poradie pre deterministickú diagnostiku.
        return issues
            .OrderBy(i => i.CellY)
            .ThenBy(i => i.CellX)
            .ToList();
    }

    private static bool IsAllowedOverlap(List<LayoutElement> elementsInCell)
    {
        // Povolené je len: presne 2 prvky a to (Block + rovná koľaj TrackSegment).
        if (elementsInCell.Count != 2)
            return false;

        var a = elementsInCell[0];
        var b = elementsInCell[1];

        static bool IsBlock(LayoutElement e)
            => e is BlockElement || string.Equals(e.MarkerKey, "Block", StringComparison.Ordinal);

        static bool IsPlainTrack(LayoutElement e)
            => string.Equals(e.MarkerKey, "TrackSegment", StringComparison.Ordinal);

        return (IsBlock(a) && IsPlainTrack(b)) || (IsBlock(b) && IsPlainTrack(a));
    }

    public static string BuildIssueMessage(LayoutCellOverlapIssue issue)
    {
        var items = string.Join(", ", issue.Elements
            .OrderBy(e => e.ElementType)
            .Select(e => LayoutElementDisplayHelper.Describe(e)));

        var tip = BuildFixTip(issue);

        return string.IsNullOrWhiteSpace(tip)
            ? $"Nelegálne prekrytie prvkov v bunke ({issue.CellX + 1},{issue.CellY + 1}): {items}."
            : $"Nelegálne prekrytie prvkov v bunke ({issue.CellX + 1},{issue.CellY + 1}): {items}. Tip: {tip}";
    }

    private static string BuildFixTip(LayoutCellOverlapIssue issue)
    {
        var els = issue.Elements;
        if (els.Count == 0)
            return string.Empty;

        static bool IsBlock(LayoutElement e)
            => e is BlockElement || string.Equals(e.MarkerKey, "Block", StringComparison.Ordinal);

        static bool IsPlainTrack(LayoutElement e)
            => string.Equals(e.MarkerKey, "TrackSegment", StringComparison.Ordinal);

        static bool IsText(LayoutElement e)
            => e is TextElement || string.Equals(e.MarkerKey, "Text", StringComparison.Ordinal);

        static bool IsSignal(LayoutElement e)
            => e is SignalElement || string.Equals(e.MarkerKey, "Signal", StringComparison.Ordinal)
               || e.MarkerKey is "Signal2Main" or "Signal2Shunt" or "Signal2Route" or "Signal3Entry" or "Signal4" or "Signal5";

        static bool IsComplexTrack(LayoutElement e)
            => e is TurnoutElement
               || e.MarkerKey is "Turnout_L" or "Turnout_R" or "TurnoutL90" or "TurnoutR90"
                   or "TurnoutCurve_L" or "TurnoutCurve_R" or "Turnout_Y" or "Turnout_3W" or "DoubleSlip"
                   or "Cross90" or "Cross45" or "Bridge90" or "Bridge45L" or "Bridge45R";

        // 1) Najčastejší prípad: rovná koľaj na rovnej koľaji.
        if (els.Count >= 2 && els.All(IsPlainTrack))
            return "Vyzerá to na duplicitnú rovnú koľaj. Zmaž jednu z nich (ponechaj len jednu).";

        // 2) Zvyšný TrackSegment pod výhybkou/križovatkou/mostom – editor ho zvyčajne nahrádza,
        // ale historicky sa tam mohol ponechať (napr. po presune/paste).
        if (els.Any(IsPlainTrack) && els.Any(IsComplexTrack))
            return "Pravdepodobne je tam zvyšná rovná koľaj pod výhybkou/križovatkou/mostom. Zmaž rovnú koľaj (TrackSegment) a ponechaj zložitejší prvok.";

        // 3) Blok môže byť len na rovnej koľaji.
        if (els.Any(IsBlock) && !els.Any(IsPlainTrack))
            return "Blok môže byť položený len na rovnej koľaji (TrackSegment). Presuň alebo zmaž prvok pod blokom (alebo blok).";

        // 4) Text cez prvky.
        if (els.Any(IsText) && els.Count > 1)
            return "Text by nemal prekrývať koľaje ani iné prvky. Presuň text mimo prekrytia (alebo zmeň jeho veľkosť).";

        // 5) Návestidlo cez prvky.
        if (els.Any(IsSignal) && els.Count > 1)
            return "Návestidlo zaberá 1×1 alebo 1×2 bunky – presuň ho tak, aby neprekrývalo iné prvky.";

        // Fallback.
        return "Presuň alebo zmaž jeden z prvkov tak, aby v bunke zostal iba jeden prvok (výnimka: blok na rovnej koľaji).";
    }
}



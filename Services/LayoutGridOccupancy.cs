using System;
using System.Collections.Generic;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

/// <summary>
/// Pomocný prepočet „ktoré bunky mriežky prvok zaberá“.
/// Používa sa pre detekciu prekrytí a pre bezpečné operácie v editore.
/// </summary>
public static class LayoutGridOccupancy
{
    public readonly record struct Cell(int X, int Y)
    {
        public override string ToString() => $"({X},{Y})";
    }

    public static List<Cell> GetOccupiedCells(LayoutElement element, double cellSize)
    {
        if (element == null) throw new ArgumentNullException(nameof(element));
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));

        // Používame Round (nie Floor), aby sme tolerovali historické uložené hodnoty,
        // ktoré sú takmer na mriežke (napr. 48.00000000002).
        int startX = (int)Math.Round(element.X / cellSize, MidpointRounding.AwayFromZero);
        int startY = (int)Math.Round(element.Y / cellSize, MidpointRounding.AwayFromZero);

        // Blok = 1xN podľa rotácie.
        if (element is BlockElement || string.Equals(element.MarkerKey, "Block", StringComparison.Ordinal))
        {
            int length = LayoutElementFootprintHelper.GetBlockLength(element);
            bool isVertical = LayoutElementFootprintHelper.IsVertical(element.Rotation);

            var cells = new List<Cell>(length);
            for (int i = 0; i < length; i++)
                cells.Add(isVertical ? new Cell(startX, startY + i) : new Cell(startX + i, startY));
            return cells;
        }

        // Text = WxH (v bunkách).
        if (element is TextElement text)
        {
            int w = Math.Clamp(text.WidthInCells, 1, 200);
            int h = Math.Clamp(text.HeightInCells, 1, 200);
            var cells = new List<Cell>(w * h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                cells.Add(new Cell(startX + x, startY + y));
            return cells;
        }

        // Signál = 1x1 (kompaktný 2-znakový) alebo 1x2 / 2x1 podľa rotácie.
        if (element is SignalElement signal)
        {
            var (wPx, hPx) = SignalFootprintHelper.GetFootprint(signal, cellSize, compactTwoAspect: true);
            int w = Math.Max(1, (int)Math.Round(wPx / cellSize, MidpointRounding.AwayFromZero));
            int h = Math.Max(1, (int)Math.Round(hPx / cellSize, MidpointRounding.AwayFromZero));
            var cells = new List<Cell>(w * h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                cells.Add(new Cell(startX + x, startY + y));
            return cells;
        }

        // Všetko ostatné považujeme za 1 bunku.
        return new List<Cell>(1) { new(startX, startY) };
    }
}


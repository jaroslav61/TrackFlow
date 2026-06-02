using System;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

/// <summary>
/// Shared footprint utility for layout elements (editor + operation helpers).
/// </summary>
public static class LayoutElementFootprintHelper
{
    public static int NormalizeMarkerAngle(double angle)
    {
        int rounded = (int)Math.Round(angle, MidpointRounding.AwayFromZero);
        rounded %= 360;
        if (rounded < 0) rounded += 360;
        return rounded;
    }

    public static int NormalizeRightAngle(double angle)
    {
        int normalized = NormalizeMarkerAngle(angle);
        return ((normalized + 45) / 90) * 90 % 360;
    }

    public static bool IsVertical(double rotation)
        => rotation == 90 || rotation == 270;

    public static int GetBlockLength(LayoutElement element)
        => element is BlockElement b ? Math.Clamp(b.BlockLengthCells, 1, 20) : 4;

    public static (double Width, double Height) GetFootprint(LayoutElement element, double cellSize, bool compactTwoAspectSignals = true)
    {
        if (element is BlockElement || string.Equals(element.MarkerKey, "Block", StringComparison.Ordinal))
        {
            int length = GetBlockLength(element);
            bool isVertical = IsVertical(element.Rotation);
            return isVertical ? (cellSize, cellSize * length) : (cellSize * length, cellSize);
        }

        if (element is TextElement textEl)
            return (textEl.WidthInCells * cellSize, textEl.HeightInCells * cellSize);

        if (element is SignalElement signal)
            return SignalFootprintHelper.GetFootprint(signal, cellSize, compactTwoAspectSignals);

        return (cellSize, cellSize);
    }

    public static bool IsPointInside(
        LayoutElement element,
        double x,
        double y,
        double cellSize,
        bool compactTwoAspectSignals = true,
        double tolerance = 0)
    {
        var (w, h) = GetFootprint(element, cellSize, compactTwoAspectSignals);
        return x >= element.X - tolerance && x < element.X + w - tolerance &&
               y >= element.Y - tolerance && y < element.Y + h - tolerance;
    }
}


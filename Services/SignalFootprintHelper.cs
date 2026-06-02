using System;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

/// <summary>
/// Spolocny helper pre urcenie footprintu signalov v editore.
/// 2-znakove profily su kompaktne 1x1 bunka, ostatne 1x2 / 2x1 podla rotacie.
/// </summary>
public static class SignalFootprintHelper
{
    public static int ParseSignCount(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile)) return 3;

        var tokens = profile.Split(new[] { '-', '_', ':', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
            if (int.TryParse(token, out int n) && n >= 2 && n <= 5)
                return n;

        foreach (char ch in profile)
            if (ch is >= '2' and <= '5')
                return ch - '0';

        return 3;
    }

    public static (double Width, double Height) GetFootprint(SignalElement signal, double cellSize)
        => GetFootprint(signal, cellSize, compactTwoAspect: true);

    public static (double Width, double Height) GetFootprint(SignalElement signal, double cellSize, bool compactTwoAspect)
    {
        int signCount = ParseSignCount(signal.SignalProfile);
        if (compactTwoAspect && signCount == 2)
            return (cellSize, cellSize);

        bool isHorizontal = signal.Rotation == 90 || signal.Rotation == 270;
        return isHorizontal ? (cellSize * 2, cellSize) : (cellSize, cellSize * 2);
    }

    public static bool IsPointInside(SignalElement signal, double x, double y, double cellSize, double tolerance = 0)
    {
        var (w, h) = GetFootprint(signal, cellSize);
        return x >= signal.X - tolerance && x < signal.X + w - tolerance &&
               y >= signal.Y - tolerance && y < signal.Y + h - tolerance;
    }
}


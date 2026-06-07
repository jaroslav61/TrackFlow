using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TrackFlow.Converters;

public class BoolToOpacityConverter : IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();
    public static readonly BoolToOpacityConverter InverseInstance = new() { Invert = true };

    public bool Invert { get; set; }

    // Returns 1.0 when true, 0.35 when false (or inverted if Invert=true)
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value as bool? ?? false;

        if (Invert)
            b = !b;

        return b ? 1.0 : 0.35;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
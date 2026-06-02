using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace TrackFlow.Converters;

/// <summary>
/// Konvertuje IsBasicMode bool na názov režimu (text).
/// true → "Základný režim"
/// false → "Rozšírený režim"
/// </summary>
public class DecoderModeNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isBasic)
        {
            return isBasic ? "Základný režim" : "Rozšírený režim";
        }
        return "Neznámy režim";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}


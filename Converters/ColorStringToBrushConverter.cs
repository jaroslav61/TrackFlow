using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TrackFlow.Converters;

/// <summary>
///     Konvertuje color string (#RRGGBB alebo špeciálne hodnoty) na Brush
/// </summary>
public class ColorStringToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string colorString)
            return new SolidColorBrush(Colors.Transparent);

        // Špeciálne hodnoty
        if (colorString == "Automatický" || colorString == "Automatic")
            return new SolidColorBrush(Colors.Gray); // Neutrálna farba pre "auto"

        if (colorString == "Transparentné" || colorString == "Transparent")
            return new SolidColorBrush(Colors.Transparent);

        if (colorString == "Vlastná farba...")
            return new SolidColorBrush(Colors.White); // Prázdna vzorka pre vlastný výber

        // Hex hodnota
        if (colorString.StartsWith("#"))
            try
            {
                return new SolidColorBrush(Color.Parse(colorString));
            }
            catch
            {
                return new SolidColorBrush(Colors.Transparent);
            }

        return new SolidColorBrush(Colors.Transparent);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
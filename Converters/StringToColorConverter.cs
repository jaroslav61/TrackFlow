using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TrackFlow.Converters;

/// <summary>
/// Konvertuje string (hex alebo "Transparent") na Avalonia Color
/// </summary>
public class StringToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string colorString)
            return Colors.Black;

        if (colorString == "Transparent" || string.IsNullOrEmpty(colorString))
            return Colors.Transparent;

        try
        {
            return Color.Parse(colorString);
        }
        catch
        {
            return Colors.Black;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            if (color.A == 0)
                return "Transparent";
            
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        
        return "#000000";
    }
}


using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TrackFlow.Converters;

/// <summary>
/// Konvertuje IsBasicMode bool na farbu pozadia.
/// true (Základný režim) → svetlá modrá #E8F4FD
/// false (Rozšírený režim) → svetlá zelená #E8F5E9
/// </summary>
public class DecoderModeBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isBasic)
        {
            // Základný režim = modrá; Rozšírený režim = zelená
            return isBasic 
                ? Color.Parse("#E8F4FD") 
                : Color.Parse("#E8F5E9");
        }
        return Colors.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Konvertuje IsBasicMode bool na farbu ohraničenia.
/// true (Základný režim) → modrá #2196F3
/// false (Rozšírený režim) → zelená #4CAF50
/// </summary>
public class DecoderModeBorderConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isBasic)
        {
            return isBasic 
                ? Color.Parse("#2196F3") 
                : Color.Parse("#4CAF50");
        }
        return Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Konvertuje IsBasicMode bool na farbu textu.
/// true (Základný režim) → modrá #1976D2
/// false (Rozšírený režim) → zelená #2E7D32
/// </summary>
public class DecoderModeTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isBasic)
        {
            return isBasic 
                ? Color.Parse("#1976D2") 
                : Color.Parse("#2E7D32");
        }
        return Colors.Black;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}


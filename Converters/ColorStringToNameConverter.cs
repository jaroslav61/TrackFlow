using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace TrackFlow.Converters;

/// <summary>
/// Konvertuje color string na čitateľný názov pre užívateľa
/// </summary>
public class ColorStringToNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string colorString)
            return string.Empty;

        return colorString switch
        {
            "Automatický" or "Automatic" => "Automatický",
            "Transparentné" or "Transparent" => "Transparentné",
            "Vlastná farba..." => "Vlastná farba...",
            "#000000" => "Čierna",
            "#FFFFFF" => "Biela",
            "#FF0000" => "Červená",
            "#00FF00" => "Zelená",
            "#0000FF" => "Modrá",
            "#FFFF00" => "Žltá",
            "#FFA500" => "Oranžová",
            "#808080" => "Sivá",
            "#FF00FF" => "Fialová",
            "#00FFFF" => "Cyan",
            "#800000" => "Tmavočervená",
            "#008000" => "Tmavozelená",
            "#000080" => "Tmavomodrá",
            _ when colorString.StartsWith("#") => colorString, // Zobraz hex pre vlastné farby
            _ => colorString
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}


using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TrackFlow.Converters;

/// <summary>
/// Konvertuje bool na string.
/// ConverterParameter: "TrueValue|FalseValue"
/// </summary>
public class BoolToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool boolValue)
            return string.Empty;

        if (parameter is not string param)
            return boolValue.ToString();

        var parts = param.Split('|');
        if (parts.Length != 2)
            return boolValue.ToString();

        return boolValue ? parts[0] : parts[1];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}


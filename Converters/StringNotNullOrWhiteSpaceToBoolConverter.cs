using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace TrackFlow.Converters;

public class StringNotNullOrWhiteSpaceToBoolConverter : IValueConverter
{
    // If parameter equals "Invert" the boolean result is negated.
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value is string s && !string.IsNullOrWhiteSpace(s);
        if (parameter is string p && string.Equals(p, "Invert", StringComparison.OrdinalIgnoreCase))
            return !has;
        return has;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

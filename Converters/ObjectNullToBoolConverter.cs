using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace TrackFlow.Converters;

public class ObjectNullToBoolConverter : IValueConverter
{
    // Returns true when value is not null. If parameter equals "Invert" the boolean is negated.
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var notNull = value != null;
        if (parameter is string p && string.Equals(p, "Invert", StringComparison.OrdinalIgnoreCase))
            return !notNull;
        return notNull;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

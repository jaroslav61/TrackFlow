using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TrackFlow.Converters;

public sealed class EqualsStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var current = value as string ?? string.Empty;
        var expected = parameter as string ?? string.Empty;

        return string.Equals(current, expected, StringComparison.Ordinal);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TrackFlow.Converters;

public sealed class BoolToScaleXConverter : IValueConverter
{
    public double TrueValue { get; set; } = -1;
    public double FalseValue { get; set; } = 1;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool bb && bb;
        return b ? TrueValue : FalseValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

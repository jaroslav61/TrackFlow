using System;
using System.Globalization;
using Avalonia.Layout;
using Avalonia.Data.Converters;

namespace TrackFlow.Converters;

public sealed class BoolToHorizontalAlignmentConverter : IValueConverter
{
    public HorizontalAlignment TrueValue { get; set; } = HorizontalAlignment.Left;
    public HorizontalAlignment FalseValue { get; set; } = HorizontalAlignment.Right;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool bb && bb;
        return b ? TrueValue : FalseValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

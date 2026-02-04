using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TrackFlow.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public IBrush? TrueBrush { get; set; }
    public IBrush? FalseBrush { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value as bool? ?? false;
        return b ? (object?)TrueBrush : FalseBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

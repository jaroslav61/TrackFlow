using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace TrackFlow.Converters
{
    public class BoolToZeroOneConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var b = value as bool? ?? false;
            return b ? 1.0 : 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}

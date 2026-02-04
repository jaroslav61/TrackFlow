using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace TrackFlow.Converters
{
    public class ValueToLengthConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return 0.0;
            if (!double.TryParse(value.ToString(), out var v)) return 0.0;

            var fraction = v / 100.0;
            double trackHeight = 150.0;
            if (parameter != null)
                double.TryParse(parameter.ToString(), out trackHeight);

            return Math.Max(0.0, fraction * trackHeight);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}

using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace TrackFlow.Converters
{
    public class ValueToOffsetConverter : IValueConverter
    {
        // parameter: trackHeight as double
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return 0.0;
            if (!double.TryParse(value.ToString(), out var v)) return 0.0;
            double trackHeight = 200.0;
            if (parameter != null)
            {
                double.TryParse(parameter.ToString(), out trackHeight);
            }
            // slider value expected 0..100, map to Y offset where 0 -> bottom, 100 -> top
            var fraction = v / 100.0;
            // position relative to center: top is -trackHeight/2, bottom +trackHeight/2
            var pos = (0.5 - fraction) * trackHeight;
            return pos;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}

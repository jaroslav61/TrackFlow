using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace TrackFlow.Converters
{
    public class ValueToThumbTopConverter : IValueConverter
    {
        // parameter: trackHeight as double
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return 0.0;
            if (!double.TryParse(value.ToString(), out var v)) return 0.0;
            double trackHeight = 150.0;
            if (parameter != null)
            {
                double.TryParse(parameter.ToString(), out trackHeight);
            }
            double thumbHeight = 24.0;
            var fraction = Math.Clamp(v / 100.0, 0.0, 1.0);
            // top position: 0 = top, trackHeight-thumbHeight = bottom
            var top = (1.0 - fraction) * (trackHeight - thumbHeight);
            return top;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}

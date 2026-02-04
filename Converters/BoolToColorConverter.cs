using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TrackFlow.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        public Color TrueColor { get; set; } = Colors.Red;
        public Color FalseColor { get; set; } = Colors.Cyan;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var b = value as bool? ?? false;
            return b ? TrueColor : FalseColor;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}

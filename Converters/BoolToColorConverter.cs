using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TrackFlow.Converters
{
    /// <summary>
    /// Konvertuje bool na Color.
    /// Podporuje Properties (TrueColor/FalseColor) alebo ConverterParameter: "TrueColorHex|FalseColorHex"
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public Color TrueColor { get; set; } = Colors.Red;
        public Color FalseColor { get; set; } = Colors.Cyan;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var b = value as bool? ?? false;

            // Ak je zadaný ConverterParameter, použije sa namiesto Properties
            if (parameter is string param && param.Contains('|'))
            {
                var parts = param.Split('|');
                if (parts.Length == 2)
                {
                    try
                    {
                        var trueColor = Color.Parse(parts[0]);
                        var falseColor = Color.Parse(parts[1]);
                        return b ? trueColor : falseColor;
                    }
                    catch
                    {
                        // Fallback na Properties
                    }
                }
            }

            return b ? TrueColor : FalseColor;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}

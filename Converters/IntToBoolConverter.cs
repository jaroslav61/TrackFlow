using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TrackFlow.Converters
{
    public class IntToBoolConverter : IValueConverter
    {
        public static readonly IntToBoolConverter Instance = new IntToBoolConverter();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (value == null) return false;

                if (value is int i) return i > 0;
                if (value is long l) return l > 0;
                if (value is short s) return s > 0;
                if (value is uint ui) return ui > 0;
                if (value is ulong ul) return ul > 0;

                if (int.TryParse(value.ToString(), out var parsed))
                    return parsed > 0;
            }
            catch { }

            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

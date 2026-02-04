using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;
using TrackFlow.Models;

namespace TrackFlow.Converters
{
    public class DirectionStateToColorConverter : IValueConverter
    {
        // Expects DataContext (Locomotive) or a bool (IsForward). If given Locomotive, prefer IsForward/IsReverse.
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Locomotive loco)
            {
                if (loco.IsForward)
                    return Color.Parse("#32CD32"); // LimeGreen
                if (loco.IsReverse)
                    return Color.Parse("#00BFFF"); // DeepSkyBlue
                return Colors.Transparent;
            }

            // fallback when bound directly to bool - treat boolean as 'IsReverse' flag
            // so true => reverse (blue), false => forward (green)
            if (value is bool b)
            {
                return b ? Color.Parse("#00BFFF") : Color.Parse("#32CD32");
            }

            return Colors.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}

using Avalonia.Data.Converters;
using System;
using System.Globalization;
using TrackFlow.Services;

namespace TrackFlow.Converters;

public class IconNameToPathConverter : IValueConverter
{
    // Return a Bitmap so Avalonia Image can display it directly
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrWhiteSpace(name))
        {
            try
            {
                return VehicleIconLoader.TryLoadBitmap(name);
            }
            catch (Exception)
            {
                // Debug output removed
                return null;
            }
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

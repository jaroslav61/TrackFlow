using Avalonia.Data.Converters;
using System;
using System.Globalization;
using Avalonia.Controls;

namespace TrackFlow.Converters;

public class ModeNotAutoConverter : IValueConverter
{
    // Returns true when mode is not "Automatická". Value can be ComboBoxItem, string or other.
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return true;

        string? text = null;

        if (value is ComboBoxItem cbi)
            text = cbi.Content?.ToString();
        else
            text = value.ToString();

        if (text == null)
            return true;

        return !string.Equals(text.Trim(), "Automatická", StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

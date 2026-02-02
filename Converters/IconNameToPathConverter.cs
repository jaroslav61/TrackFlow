using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

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
                // If value is already a full path, try to load it directly
                if (Path.IsPathRooted(name) && File.Exists(name))
                {
                    Debug.WriteLine($"IconNameToPathConverter: loading bitmap from absolute path '{name}'");
                    return new Bitmap(name);
                }

                // Try registry first
                if (TrackFlow.Services.IconRegistry.TryGet(name, out var registered))
                {
                    if (File.Exists(registered))
                    {
                        Debug.WriteLine($"IconNameToPathConverter: loading bitmap from registry path '{registered}' for name '{name}'");
                        return new Bitmap(registered);
                    }
                }

                var baseDir = AppContext.BaseDirectory ?? string.Empty;
                var p = Path.Combine(baseDir, "Assets", "LocoIcons", name);
                if (!File.Exists(p))
                {
                    p = Path.Combine("Assets", "LocoIcons", name);
                    if (!File.Exists(p))
                    {
                        Debug.WriteLine($"IconNameToPathConverter: file not found for '{name}' (tried paths)");
                        return null;
                    }
                }

                Debug.WriteLine($"IconNameToPathConverter: loading bitmap from '{p}' for name '{name}'");
                return new Bitmap(p);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IconNameToPathConverter: exception loading '{name}': {ex}");
                return null;
            }
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

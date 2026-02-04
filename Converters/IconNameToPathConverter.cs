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

                // Try common runtime locations (bin output): <base>/Assets/LocoIcons/<name>
                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? AppContext.BaseDirectory ?? string.Empty;
                var candidate = Path.Combine(baseDir, "Assets", "LocoIcons", name);
                if (File.Exists(candidate))
                {
                    Debug.WriteLine($"IconNameToPathConverter: loading bitmap from '{candidate}' for name '{name}'");
                    return new Bitmap(candidate);
                }

                // Wagons: <base>/Assets/VagonIcons/<name>
                candidate = Path.Combine(baseDir, "Assets", "VagonIcons", name);
                if (File.Exists(candidate))
                {
                    Debug.WriteLine($"IconNameToPathConverter: loading bitmap from '{candidate}' for name '{name}' (wagon)");
                    return new Bitmap(candidate);
                }

                // Development-time fallback: walk up a few levels to find repo-root Assets/LocoIcons
                var dir = baseDir;
                for (var i = 0; i < 6; i++)
                {
                    var up = Path.GetDirectoryName(dir);
                    if (string.IsNullOrEmpty(up) || up == dir)
                        break;
                    dir = up;

                    candidate = Path.Combine(dir, "Assets", "LocoIcons", name);
                    if (File.Exists(candidate))
                    {
                        Debug.WriteLine($"IconNameToPathConverter: loading bitmap from '{candidate}' (upsearch) for name '{name}'");
                        return new Bitmap(candidate);
                    }

                    candidate = Path.Combine(dir, "Assets", "VagonIcons", name);
                    if (File.Exists(candidate))
                    {
                        Debug.WriteLine($"IconNameToPathConverter: loading bitmap from '{candidate}' (upsearch) for name '{name}' (wagon)");
                        return new Bitmap(candidate);
                    }
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

                Debug.WriteLine($"IconNameToPathConverter: file not found for '{name}'");
                return null;
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

using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System;
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
                    // Debug output removed
                    return new Bitmap(name);
                }

                // Try common runtime locations (bin output): <base>/Assets/LocoIcons/<name>
                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? AppContext.BaseDirectory ?? string.Empty;
                var candidate = Path.Combine(baseDir, "Assets", "LocoIcons", name);
                if (File.Exists(candidate))
                {
                    // Debug output removed
                    return new Bitmap(candidate);
                }

                // Wagons: <base>/Assets/VagonIcons/<name>
                candidate = Path.Combine(baseDir, "Assets", "VagonIcons", name);
                if (File.Exists(candidate))
                {
                    // Debug output removed
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
                        // Debug output removed
                        return new Bitmap(candidate);
                    }

                    candidate = Path.Combine(dir, "Assets", "VagonIcons", name);
                    if (File.Exists(candidate))
                    {
                        // Debug output removed
                        return new Bitmap(candidate);
                    }
                }

                // Try registry first
                if (TrackFlow.Services.IconRegistry.TryGet(name, out var registered))
                {
                    if (File.Exists(registered))
                    {
                        // Debug output removed
                        return new Bitmap(registered);
                    }
                }

                // Debug output removed
                return null;
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

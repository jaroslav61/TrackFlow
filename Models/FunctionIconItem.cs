using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TrackFlow.Models;

public class FunctionIconItem
{
    private static readonly Dictionary<string, IImage?> Cache = new(StringComparer.Ordinal);

    public string Name { get; set; } = "";
    public string IconPath { get; set; } = "";

    // Avalonia Image.Source potrebuje IImage; string z bindingu sa bežne neprekonvertuje.
    public IImage? Icon => LoadIcon(IconPath);

    private static IImage? LoadIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (Cache.TryGetValue(path, out var cached))
            return cached;

        try
        {
            var uri = new Uri(path, UriKind.Absolute);
            using var s = AssetLoader.Open(uri);
            var bmp = new Bitmap(s);
            Cache[path] = bmp;
            return bmp;
        }
        catch
        {
            Cache[path] = null;
            return null;
        }
    }
}
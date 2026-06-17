using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TrackFlow.Services;

/// <summary>
/// Resolves locomotive/wagon icons from absolute file paths, IconRegistry and embedded avares resources.
/// Designed to work in IDE runs and published single-file deployments.
/// </summary>
public static class VehicleIconLoader
{
    private static readonly string AssemblyName = typeof(App).Assembly.GetName().Name ?? "TrackFlow";
    private static readonly Lazy<Dictionary<string, Uri>> EmbeddedIconMap = new(BuildEmbeddedIconMap);

    public static IReadOnlyList<string> GetEmbeddedVehicleIconFileNames()
    {
        try
        {
            return EmbeddedIconMap.Value.Values
                // 1. FILTER: Pustíme ďalej len tie URI cesty, ktoré NEOBSAHUJÚ "WagonIcons"
                .Where(uri => !uri.AbsolutePath.Contains("WagonIcons", StringComparison.OrdinalIgnoreCase))
                // 2. Až potom vytiahneme čistý názov súboru
                .Select(uri => Path.GetFileName(uri.AbsolutePath))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static Bitmap? TryLoadBitmap(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
            return null;

        foreach (var candidate in BuildCandidates(iconName))
        {
            var fromPath = TryLoadFromAbsolutePath(candidate);
            if (fromPath != null)
                return fromPath;

            var fromRegistry = TryLoadFromRegistry(candidate);
            if (fromRegistry != null)
                return fromRegistry;

            var fromAvares = TryLoadFromAvares(candidate);
            if (fromAvares != null)
                return fromAvares;

            var fromAssets = TryLoadFromEmbeddedAssets(candidate);
            if (fromAssets != null)
                return fromAssets;
        }

        return null;
    }

    private static IEnumerable<string> BuildCandidates(string raw)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var normalized = raw.Trim().Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
            yield return normalized;

        var fileName = Path.GetFileName(normalized);
        if (!string.IsNullOrWhiteSpace(fileName) && seen.Add(fileName))
            yield return fileName;

        var snapshot = seen.ToList();
        foreach (var current in snapshot)
        {
            if (Path.HasExtension(current))
                continue;

            var withPng = current + ".png";
            if (seen.Add(withPng))
                yield return withPng;
        }
    }

    private static Bitmap? TryLoadFromAbsolutePath(string candidate)
    {
        try
        {
            if (!Path.IsPathRooted(candidate) || !File.Exists(candidate))
                return null;

            return new Bitmap(candidate);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryLoadFromRegistry(string candidate)
    {
        try
        {
            if (!IconRegistry.TryGet(candidate, out var registeredPath) || string.IsNullOrWhiteSpace(registeredPath))
                return null;

            if (!File.Exists(registeredPath))
                return null;

            return new Bitmap(registeredPath);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryLoadFromAvares(string candidate)
    {
        try
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                return null;

            if (!string.Equals(uri.Scheme, "avares", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!AssetLoader.Exists(uri))
                return null;

            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryLoadFromEmbeddedAssets(string candidate)
    {
        var fileName = Path.GetFileName(candidate.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        if (TryLoadFromEmbeddedIndex(fileName, out var fromIndex))
            return fromIndex;

        if (!Path.HasExtension(fileName) && TryLoadFromEmbeddedIndex(fileName + ".png", out fromIndex))
            return fromIndex;

        return null;
    }

    private static bool TryLoadFromEmbeddedIndex(string key, out Bitmap? bitmap)
    {
        bitmap = null;

        try
        {
            if (!EmbeddedIconMap.Value.TryGetValue(key, out var uri))
                return false;

            using var stream = AssetLoader.Open(uri);
            bitmap = new Bitmap(stream);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, Uri> BuildEmbeddedIconMap()
    {
        var map = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> assemblyCandidates = new[] { AssemblyName, "TrackFlow" }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblyCandidates)
        {
            foreach (var folder in new[] { "LocoIcons", "WagonIcons" })
            {
                try
                {
                    var dirUri = new Uri($"avares://{assembly}/Assets/{folder}/", UriKind.Absolute);
                    foreach (var assetUri in AssetLoader.GetAssets(dirUri, null))
                    {
                        var fileName = Path.GetFileName(assetUri.AbsolutePath);
                        if (string.IsNullOrWhiteSpace(fileName))
                            continue;

                        if (!map.ContainsKey(fileName))
                            map[fileName] = assetUri;

                        var baseName = Path.GetFileNameWithoutExtension(fileName);
                        if (!string.IsNullOrWhiteSpace(baseName) && !map.ContainsKey(baseName))
                            map[baseName] = assetUri;
                    }
                }
                catch
                {
                    // Continue with next folder/assembly candidate.
                }
            }
        }

        return map;
    }
}



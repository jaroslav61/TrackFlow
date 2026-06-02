using System;
using System.IO;
using System.Text;
using System.Text.Json;
using TrackFlow.Models;

namespace TrackFlow.Services;

/// <summary>
/// Ukladá globálny katalóg lokomotív a vozňov do catalog.json.
/// Tento katalóg je zdieľaný medzi všetkými projektami.
/// </summary>
public sealed class CatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private const string FileName = "catalog.json";

    public string FilePath { get; }

    public CatalogStore(string? filePath = null)
    {
        FilePath = filePath ?? GetFilePath();
    }

    public TrackFlowProject Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new TrackFlowProject();

            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<TrackFlowProject>(json, JsonOptions);
            return data ?? new TrackFlowProject();
        }
        catch
        {
            return new TrackFlowProject();
        }
    }

    public bool Save(TrackFlowProject catalog)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(catalog, JsonOptions);
            var tmp = FilePath + ".tmp";

            File.WriteAllText(tmp, json, Encoding.UTF8);

            if (File.Exists(FilePath))
                File.Replace(tmp, FilePath, null);
            else
                File.Move(tmp, FilePath);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string GetFilePath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
        return Path.Combine(baseDir, FileName);
    }
}


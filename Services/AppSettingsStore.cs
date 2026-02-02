using System;
using System.IO;
using System.Text;
using System.Text.Json;
using TrackFlow.Models;

namespace TrackFlow.Services;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string FilePath { get; }

    public AppSettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? GetDefaultPath();
    }

    public AppSettingsData Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettingsData();

            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<AppSettingsData>(json, JsonOptions);
            return data ?? new AppSettingsData();
        }
        catch
        {
            return new AppSettingsData();
        }
    }

    public bool Save(AppSettingsData data)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(data, JsonOptions);
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

    private static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "TrackFlow", "appsettings.json");
    }
}

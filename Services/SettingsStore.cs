using System;
using System.IO;
using System.Text;
using System.Text.Json;
using TrackFlow.Models;

namespace TrackFlow.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsFilePath { get; }

    public SettingsStore(string? settingsFilePath = null)
    {
        SettingsFilePath = settingsFilePath ?? GetDefaultSettingsPath();
    }

    public SettingsData Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return new SettingsData();

            var json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<SettingsData>(json, JsonOptions);
            return data ?? new SettingsData();
        }
        catch
        {
            return new SettingsData();
        }
    }

    public bool Save(SettingsData data)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(data, JsonOptions);
            var tmp = SettingsFilePath + ".tmp";

            File.WriteAllText(tmp, json, Encoding.UTF8);

            if (File.Exists(SettingsFilePath))
            {
                File.Replace(tmp, SettingsFilePath, null);
            }
            else
            {
                File.Move(tmp, SettingsFilePath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetDefaultSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "TrackFlow", "settings.json");
    }
}

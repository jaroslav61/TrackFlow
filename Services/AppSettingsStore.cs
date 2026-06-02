using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TrackFlow.Models;

namespace TrackFlow.Services;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private const string FileName = "settings.json";

    public string FilePath { get; }

    public AppSettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? GetFilePath();
    }

    public AppSettingsData Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettingsData();

            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            
            // ✅ MIGRÁCIA: Ak settings.json obsahuje "Locomotives" pole, je to starý formát (TrackFlowProject)
            // Presunúť ho do catalog.json a vrátiť default AppSettingsData
            if (json.Contains("\"Locomotives\""))
            {
                try
                {
                    var oldProject = JsonSerializer.Deserialize<TrackFlowProject>(json, JsonOptions);
                    if (oldProject != null && (oldProject.Locomotives.Any() || oldProject.Wagons.Any()))
                    {
                        // Presunúť do catalog.json
                        var catalogStore = new CatalogStore();
                        catalogStore.Save(oldProject);
                        
                        // Vymazať starý settings.json (bude vytvorený nový pri prvom Save)
                        try { File.Delete(FilePath); } catch { }
                    }
                }
                catch
                {
                    // Migrácia zlyhala, ale pokračujeme s default hodnotami
                }
                
                return new AppSettingsData();
            }
            
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

    public static string GetFilePath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
        return Path.Combine(baseDir, FileName);
    }
}

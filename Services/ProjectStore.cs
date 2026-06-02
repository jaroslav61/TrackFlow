using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using TrackFlow.Models;

namespace TrackFlow.Services;

public sealed class ProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public TrackFlowProject Load(string projectFilePath)
    {
        try
        {
            if (!File.Exists(projectFilePath))
                return new TrackFlowProject();
            var json = File.ReadAllText(projectFilePath, Encoding.UTF8);
            // Parse JSON document and always prefer root-level Locomotives/Wagons
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Try full deserialization to TrackFlowProject (will populate whatever fields are present)
            var result = JsonSerializer.Deserialize<TrackFlowProject>(json, JsonOptions) ?? new TrackFlowProject();

            // Ensure root-level Locomotives are loaded (force from root "Locomotives" property)
            if (root.TryGetProperty("Locomotives", out var locElem))
            {
                try
                {
                    var locos = JsonSerializer.Deserialize<List<LocoRecord>>(locElem.GetRawText(), JsonOptions);
                    result.Locomotives = locos ?? new List<LocoRecord>();
                }
                catch
                {
                    result.Locomotives = new List<LocoRecord>();
                }
            }
            else
            {
                result.Locomotives ??= new List<LocoRecord>();
            }

            // Ensure root-level Wagons are loaded
            if (root.TryGetProperty("Wagons", out var wagElem))
            {
                try
                {
                    var wagons = JsonSerializer.Deserialize<List<Wagon>>(wagElem.GetRawText(), JsonOptions);
                    result.Wagons = wagons ?? new List<Wagon>();
                }
                catch
                {
                    result.Wagons = new List<Wagon>();
                }
            }
            else
            {
                result.Wagons ??= new List<Wagon>();
            }

            // Normalize Settings object
            result.Settings ??= new ProjectSettingsData();

            return result;
        }
        catch
        {
            return new TrackFlowProject();
        }
    }

    public bool Save(string projectFilePath, TrackFlowProject project)
    {
        try
        {
            var dir = Path.GetDirectoryName(projectFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            // POZNÁMKA: dirty stav projektu spravuje výhradne ProjectDirtyTracker
            // (volaný zo SettingsManager.SaveProject/SaveProjectAs po úspechu).
            // Tento store sa o IsDirty nestará – inak by sme miešali zodpovednosti.

            // Serialize as root object containing Locomotives, Wagons, Layout and other root properties
            var json = JsonSerializer.Serialize(project, JsonOptions);

            // Atomický zápis: najprv .tmp, potom Replace/Move – chráni pred poškodením súboru
            // pri páde aplikácie počas zápisu.
            var tmp = projectFilePath + ".tmp";
            File.WriteAllText(tmp, json, Encoding.UTF8);

            if (File.Exists(projectFilePath))
                File.Replace(tmp, projectFilePath, null);
            else
                File.Move(tmp, projectFilePath);

            return true;
        }
        catch
        {
            // Pokus o úklid prípadného .tmp z neúspešného zápisu (best-effort).
            try
            {
                var tmp = projectFilePath + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch { /* ignore */ }
            return false;
        }
    }
}

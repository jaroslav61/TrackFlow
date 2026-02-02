using System.IO;
using System.Text;
using System.Text.Json;
using TrackFlow.Models;

namespace TrackFlow.Services;

public sealed class ProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public TrackFlowProject Load(string projectFilePath)
    {
        try
        {
            if (!File.Exists(projectFilePath))
                return new TrackFlowProject();

            var json = File.ReadAllText(projectFilePath, Encoding.UTF8);

            // 1) Nový formát (TrackFlowProject)
            var project = JsonSerializer.Deserialize<TrackFlowProject>(json, JsonOptions);
            if (project != null)
                return project;

            // 2) Fallback: starý formát (iba ProjectSettingsData) → zabaliť do TrackFlowProject
            var oldSettings = JsonSerializer.Deserialize<ProjectSettingsData>(json, JsonOptions) ?? new ProjectSettingsData();
            return new TrackFlowProject
            {
                SchemaVersion = 1,
                Settings = oldSettings
            };
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

            project.IsDirty = false;

            var json = JsonSerializer.Serialize(project, JsonOptions);
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
            return false;
        }
    }
}

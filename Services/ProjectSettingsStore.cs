using System.IO;
using System.Text;
using System.Text.Json;
using TrackFlow.Models;

namespace TrackFlow.Services;

public sealed class ProjectSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ProjectSettingsData Load(string projectFilePath)
    {
        try
        {
            if (!File.Exists(projectFilePath))
                return new ProjectSettingsData();

            var json = File.ReadAllText(projectFilePath, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<ProjectSettingsData>(json, JsonOptions);
            return data ?? new ProjectSettingsData();
        }
        catch
        {
            return new ProjectSettingsData();
        }
    }

    public bool Save(string projectFilePath, ProjectSettingsData data)
    {
        try
        {
            var dir = Path.GetDirectoryName(projectFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(data, JsonOptions);
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

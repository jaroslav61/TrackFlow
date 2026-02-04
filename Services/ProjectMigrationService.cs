using TrackFlow.Models;

namespace TrackFlow.Services;

public sealed class ProjectMigrationService
{
    public TrackFlowProject MigrateIfNeeded(TrackFlowProject project)
    {
        // Zatiaľ máme iba v1. Mechanizmus je pripravený na budúce zmeny formátu.
        if (project.SchemaVersion <= 0)
            project.SchemaVersion = 1;

        // Data fix: older/experimental builds may have written locomotives into TrackFlowProject.Locomotives
        // while newer code reads from ProjectSettingsData.Locomotives.
        if (project.Settings == null)
            project.Settings = new ProjectSettingsData();

        if ((project.Settings.Locomotives == null || project.Settings.Locomotives.Count == 0)
            && project.Locomotives is { Count: > 0 })
        {
            project.Settings.Locomotives = project.Locomotives;
        }

        // TODO: v2+ migrácie sem.
        // Example:
        // if (project.SchemaVersion == 1) { ...; project.SchemaVersion = 2; }

        return project;
    }
}

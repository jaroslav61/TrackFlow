using TrackFlow.Models;

namespace TrackFlow.Services;

public sealed class ProjectMigrationService
{
    public TrackFlowProject MigrateIfNeeded(TrackFlowProject project)
    {
        // Zatiaľ máme iba v1. Mechanizmus je pripravený na budúce zmeny formátu.
        if (project.SchemaVersion <= 0)
            project.SchemaVersion = 1;

        // TODO: v2+ migrácie sem.
        // Example:
        // if (project.SchemaVersion == 1) { ...; project.SchemaVersion = 2; }

        return project;
    }
}

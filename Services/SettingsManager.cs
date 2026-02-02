using TrackFlow;
using TrackFlow.Models;

using System.IO;
using TrackFlow.Models;

namespace TrackFlow.Services;

public sealed class SettingsManager
{
    private readonly AppSettingsStore _appStore;
    private readonly ProjectStore _projectStore;
    private readonly ProjectMigrationService _migration;

    public AppSettingsData App { get; private set; } = new();

    /// <summary>
    /// Aktuálne otvorený projekt (plný model + dáta).
    /// </summary>
    public TrackFlowProject? CurrentProject { get; private set; }

    /// <summary>
    /// Spätná kompatibilita pre existujúce UI: Settings v projekte (ak je projekt otvorený).
    /// </summary>
    public ProjectSettingsData? Project => CurrentProject?.Settings;

    /// <summary>
    /// Zabezpečí existenciu projektu aj jeho Settings a vráti Settings objekt.
    /// </summary>
    public ProjectSettingsData EnsureProjectSettings()
    {
        CurrentProject ??= new TrackFlowProject();
        CurrentProject.Settings ??= new ProjectSettingsData();
        return CurrentProject.Settings;
    }

    public string? CurrentProjectPath { get; private set; }

    public SettingsManager(
        AppSettingsStore? appStore = null,
        ProjectStore? projectStore = null,
        ProjectMigrationService? migration = null)
    {
        _appStore = appStore ?? new AppSettingsStore();
        _projectStore = projectStore ?? new ProjectStore();
        _migration = migration ?? new ProjectMigrationService();
    }

    public void LoadApp()
    {
        App = _appStore.Load();

        if (string.IsNullOrWhiteSpace(CurrentProjectPath)
            && !string.IsNullOrWhiteSpace(App.LastProjectPath)
            && File.Exists(App.LastProjectPath))
        {
            CurrentProjectPath = App.LastProjectPath;
            CurrentProject = _migration.MigrateIfNeeded(_projectStore.Load(CurrentProjectPath));
        }
    }

    public bool SaveApp()
    {
        return _appStore.Save(App);
    }

    public void OpenProject(string projectFilePath)
    {
        CurrentProjectPath = projectFilePath;
        CurrentProject = _migration.MigrateIfNeeded(_projectStore.Load(projectFilePath));

        App.LastProjectPath = projectFilePath;
        _appStore.Save(App);
    }

    public void CloseProject()
    {
        CurrentProjectPath = null;
        CurrentProject = null;
    }

    public bool SaveProject()
    {
        if (CurrentProjectPath == null || CurrentProject == null)
            return false;

        return _projectStore.Save(CurrentProjectPath, CurrentProject);
    }

    /// <summary>
    /// Uloží aktuálne otvorený projekt do novej cesty (Save As) bez toho,
    /// aby sa projekt znovu načítal zo súboru a bez predčasného prepísania LastProjectPath.
    /// </summary>
    public bool SaveProjectAs(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            return false;

        // Ak ešte neexistuje projekt v pamäti, vytvoríme prázdny
        CurrentProject ??= new TrackFlowProject();

        // Uložiť najprv do cieľa
        var ok = _projectStore.Save(projectFilePath, CurrentProject);
        if (!ok)
            return false;

        // Až po úspechu prepnúť "current" a uložiť last path
        CurrentProjectPath = projectFilePath;
        App.LastProjectPath = projectFilePath;
        _appStore.Save(App);

        return true;
    }

    public EffectiveSettings GetEffective()
    {
        return EffectiveSettings.Merge(App, CurrentProject?.Settings);
    }
}
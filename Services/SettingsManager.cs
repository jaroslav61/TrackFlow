using System;
using TrackFlow.Models;
using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

public sealed class SettingsManager
{
    public event Action? ProjectChanged;
    public event Action? AppSettingsChanged;

    private readonly AppSettingsStore _appStore;
    private readonly ProjectStore _projectStore;
    private readonly CatalogStore _catalogStore;
    private readonly ProjectMigrationService _migration;

    /// <summary>Centralizovaný tracker neuložených zmien (Step 10).</summary>
    public ProjectDirtyTracker Dirty { get; }

    public AppSettingsData App { get; private set; } = new();

    /// <summary>
    /// Aktuálne otvorený projekt (plný model + dáta).
    /// </summary>
    public TrackFlowProject? CurrentProject { get; private set; }

    // Observable kolekcie pre UI bindovanie na „in-memory" projektové zoznamy.
    public ObservableCollection<LocoRecord> ProjectLocomotives { get; } = new();
    public ObservableCollection<Wagon> ProjectWagons { get; } = new();

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
        return CurrentProject.Settings;
    }

    /// <summary>
    /// Vytvorí nový čistý projekt (bez otvoreného súboru).
    /// </summary>
    public void NewProject()
    {
        using (Dirty.SuspendTracking())
        {
            CurrentProject = new TrackFlowProject();
            CurrentProjectPath = null;
            ProjectLocomotives.Clear();
            ProjectWagons.Clear();
        }
        Dirty.MarkClean();
        ProjectChanged?.Invoke();
    }

    public string? CurrentProjectPath { get; private set; }

    public SettingsManager(
        AppSettingsStore? appStore = null,
        ProjectStore? projectStore = null,
        CatalogStore? catalogStore = null,
        ProjectMigrationService? migration = null)
    {
        _appStore = appStore ?? new AppSettingsStore();
        _projectStore = projectStore ?? new ProjectStore();
        _catalogStore = catalogStore ?? new CatalogStore();
        _migration = migration ?? new ProjectMigrationService(_catalogStore);

        Dirty = new ProjectDirtyTracker(this);

        // Auto-track top-level project collections (loco/wagon add/remove → dirty).
        // Mutácie počas Load sú obalené do Dirty.SuspendTracking(), takže load nehlási dirty.
        ProjectLocomotives.CollectionChanged += (_, _) => Dirty.MarkDirty("locomotives");
        ProjectWagons.CollectionChanged       += (_, _) => Dirty.MarkDirty("wagons");
    }

    public string AppSettingsPath => _appStore.FilePath;

    public void LoadApp()
    {
        App = _appStore.Load();

        using (Dirty.SuspendTracking())
        {
            CurrentProject = null;
            CurrentProjectPath = null;
            ProjectLocomotives.Clear();
            ProjectWagons.Clear();
        }
    }

    public bool SaveApp()
    {
        var result = _appStore.Save(App);
        AppSettingsChanged?.Invoke();
        return result;
    }

    public void OpenProject(string projectFilePath)
    {
        using (Dirty.SuspendTracking())
        {
            CurrentProjectPath = projectFilePath;
            CurrentProject = _migration.MigrateIfNeeded(_projectStore.Load(projectFilePath));
            RepairLegacyContactIndicatorBindingsFromEffectiveProfiles();

            if (!App.RecentProjectPaths.Contains(projectFilePath))
            {
                App.RecentProjectPaths.Insert(0, projectFilePath);
                if (App.RecentProjectPaths.Count > 10)
                    App.RecentProjectPaths.RemoveAt(10);
            }

            App.LastProjectPath = projectFilePath;
            _appStore.Save(App);

            ProjectLocomotives.Clear();
            foreach (var l in CurrentProject.Locomotives)
                ProjectLocomotives.Add(l);

            ProjectWagons.Clear();
            foreach (var w in CurrentProject.Wagons)
                ProjectWagons.Add(w);
        }

        Dirty.MarkClean();

        // Integritu ciest kontrolujeme pri načítaní projektu.
        // Ak niečo opravíme, zámerne označíme projekt ako „dirty" (používateľ má uložiť).
        if (CurrentProject?.Layout != null)
            ApplyRouteIntegrityChecksOnLoad(CurrentProject.Layout);

        ProjectChanged?.Invoke();
    }

    private void RepairLegacyContactIndicatorBindingsFromEffectiveProfiles()
    {
        var layout = CurrentProject?.Layout;
        if (layout == null)
            return;

        var enabledProfiles = GetEffectiveEnabledDccCentralProfiles();
        var selectedProfileId = GetEffectiveSelectedDccCentralProfileId();
        var selectedEnabledProfileId = selectedProfileId.HasValue && enabledProfiles.Any(p => p.Id == selectedProfileId.Value)
            ? selectedProfileId
            : enabledProfiles.Count == 1
                ? enabledProfiles[0].Id
                : null;

        if (!selectedEnabledProfileId.HasValue)
            return;

        foreach (var indicator in layout.Elements
                     .OfType<BlockElement>()
                     .SelectMany(block => block.Indicators)
                     .Where(static indicator => indicator.Type == BlockIndicatorType.Contact && !indicator.DccCentralProfileId.HasValue))
        {
            indicator.DccCentralProfileId = selectedEnabledProfileId.Value;
        }
    }

    private void ApplyRouteIntegrityChecksOnLoad(TrackLayout layout)
    {
        var report = RouteIntegrityService.ValidateAndRepairOnLoad(layout, autoRepairManualRoutes: true);
        if (report.Issues.Count == 0)
        {
            // Aj keď s cestami nie je problém, layout môže obsahovať nelegálne prekrytia.
            ApplyLayoutOverlapDiagnosticsOnLoad(layout);
            return;
        }

        string DescribeElementId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "(prázdne ID)";

            var el = layout.Elements.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
            return el != null
                ? LayoutElementDisplayHelper.Describe(el, includeId: true)
                : $"<chýba [{LayoutElementDisplayHelper.ShortId(id)}]>";
        }

        string DescribeRouteContext(string routeId)
        {
            var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
            var routeName = route?.Name;
            if (string.IsNullOrWhiteSpace(routeName))
                routeName = "(bez názvu)";

            if (route == null)
                return $"Cesta '{routeName}' [{LayoutElementDisplayHelper.ShortId(routeId)}]";

            var from = layout.Elements.FirstOrDefault(e => string.Equals(e.Id, route.FromBlockId, StringComparison.OrdinalIgnoreCase));
            var to = layout.Elements.FirstOrDefault(e => string.Equals(e.Id, route.ToBlockId, StringComparison.OrdinalIgnoreCase));

            var fromText = from != null ? LayoutElementDisplayHelper.Describe(from, includeId: false) : $"blok [{LayoutElementDisplayHelper.ShortId(route.FromBlockId)}]";
            var toText = to != null ? LayoutElementDisplayHelper.Describe(to, includeId: false) : $"blok [{LayoutElementDisplayHelper.ShortId(route.ToBlockId)}]";

            return $"Cesta '{routeName}' ({fromText} → {toText})";
        }

        foreach (var issue in report.Issues.Where(i => i.MissingPathElementIds.Count > 0))
        {
            var missingList = string.Join(", ", issue.MissingPathElementIds.Take(12).Select(id => LayoutElementDisplayHelper.ShortId(id)));
            if (issue.MissingPathElementIds.Count > 12)
                missingList += ", …";

            var routeCtx = DescribeRouteContext(issue.RouteId);
            var message = $"{routeCtx} odkazuje na {issue.MissingPathElementIds.Count} chýbajúcich prvkov v PathElementIds: {missingList}.";
            TrackFlowDoctorService.Instance.Diagnose("Routes", message, DiagnosticLevel.Warning);
            // Do logu dáme plné ID (pre troubleshooting / copy-paste).
            Log.Warning("[RouteIntegrity] {Message} FullMissingIds={MissingIds}", message, issue.MissingPathElementIds);
        }

        foreach (var issue in report.Issues.Where(i => i.Repairs.Count > 0))
        {
            foreach (var r in issue.Repairs)
            {
                var routeCtx = DescribeRouteContext(issue.RouteId);
                var inserted = DescribeElementId(r.InsertedElementId);
                var a = DescribeElementId(r.BetweenElementIdA);
                var b = DescribeElementId(r.BetweenElementIdB);
                var message = $"Auto-oprava: {routeCtx} doplnila prvok {inserted} medzi {a} a {b}.";
                TrackFlowDoctorService.Instance.Diagnose("Routes", message, DiagnosticLevel.Warning);
                Log.Warning("[RouteIntegrity] {Message} Inserted={Inserted} BetweenA={BetweenA} BetweenB={BetweenB}",
                    message, r.InsertedElementId, r.BetweenElementIdA, r.BetweenElementIdB);
            }
        }

        if (report.AnyRepairs)
            Dirty.MarkDirty("routes");

        ApplyLayoutOverlapDiagnosticsOnLoad(layout);
    }

    private void ApplyLayoutOverlapDiagnosticsOnLoad(TrackLayout layout)
    {
        // V editore (aj v TrackGraphBuilder) je mriežka fixne 24 px.
        const double cellSize = 24.0;

        var overlaps = LayoutOverlapIntegrityService.FindIllegalOverlaps(layout, cellSize);
        if (overlaps.Count == 0)
            return;

        foreach (var issue in overlaps.Take(25))
        {
            var message = LayoutOverlapIntegrityService.BuildIssueMessage(issue);
            TrackFlowDoctorService.Instance.Diagnose("Layout", message, DiagnosticLevel.Warning);

            // Do logu dáme celé objekty/ID (prípadne sa to bude hodiť pri debugovaní).
            Log.Warning("[LayoutIntegrity] {Message} ElementIds={Ids}",
                message,
                issue.Elements.Select(e => e.Id).ToList());
        }

        if (overlaps.Count > 25)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "Layout",
                $"Layout obsahuje ďalšie prekrytia prvkov (zobrazených 25 z {overlaps.Count}).",
                DiagnosticLevel.Warning);
        }
    }

    public void CloseProject()
    {
        using (Dirty.SuspendTracking())
        {
            CurrentProjectPath = null;
            CurrentProject = null;
            ProjectLocomotives.Clear();
            ProjectWagons.Clear();
        }
        Dirty.MarkClean();
        ProjectChanged?.Invoke();
    }

    public void NotifyProjectChanged()
    {
        foreach (var l in ProjectLocomotives)
            l.NotifyAllPropertiesChanged();

        try
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                ProjectChanged?.Invoke();
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ProjectChanged?.Invoke());
        }
        catch
        {
            ProjectChanged?.Invoke();
        }
    }

    public bool SaveProject()
    {
        if (CurrentProjectPath == null || CurrentProject == null)
            return false;

        CurrentProject.Locomotives = ProjectLocomotives.ToList();
        CurrentProject.Wagons = ProjectWagons.ToList();

        var ok = _projectStore.Save(CurrentProjectPath, CurrentProject);
        if (ok)
        {
            Dirty.MarkClean();
            ProjectChanged?.Invoke();
        }
        return ok;
    }

    public bool SaveProjectWithFallback()
    {
        if (CurrentProject == null)
            return false;

        if (string.IsNullOrWhiteSpace(CurrentProjectPath))
         {
             CurrentProject.Locomotives = ProjectLocomotives.ToList();
             CurrentProject.Wagons = ProjectWagons.ToList();
 
             var fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            var ok = _projectStore.Save(fallback, CurrentProject);
            if (ok)
            {
                CurrentProjectPath = fallback;
                App.LastProjectPath = fallback;
                _appStore.Save(App);
                Dirty.MarkClean();
                ProjectChanged?.Invoke();
            }
            return ok;
        }

        return SaveProject();
    }

    /// <summary>
    /// Uloží aktuálne Locomotives a Wagons do projektu.
    /// </summary>
    public bool SaveCatalog(TrackFlowProject? catalog)
    {
        if (catalog == null)
            return false;

        try
        {
            ProjectLocomotives.Clear();
            foreach (var l in catalog.Locomotives)
                ProjectLocomotives.Add(l);

            ProjectWagons.Clear();
            foreach (var w in catalog.Wagons)
                ProjectWagons.Add(w);

            if (CurrentProject != null)
            {
                CurrentProject.Locomotives = ProjectLocomotives.ToList();
                CurrentProject.Wagons = ProjectWagons.ToList();

                Dirty.MarkDirty("catalog");

                ProjectChanged?.Invoke();
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Uloží aktuálne otvorený projekt do novej cesty (Save As).
    /// </summary>
    public bool SaveProjectAs(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            return false;

        CurrentProject ??= new TrackFlowProject();

        CurrentProject.Locomotives = ProjectLocomotives.ToList();
        CurrentProject.Wagons = ProjectWagons.ToList();

        var ok = _projectStore.Save(projectFilePath, CurrentProject);
        if (!ok)
            return false;

        CurrentProjectPath = projectFilePath;
        App.LastProjectPath = projectFilePath;
        _appStore.Save(App);

        Dirty.MarkClean();
        ProjectChanged?.Invoke();

        return true;
    }

    public EffectiveSettings GetEffective()
    {
        return EffectiveSettings.Merge(App, CurrentProject?.Settings);
    }

    public IReadOnlyList<DccCentralProfile> GetEffectiveDccCentralProfiles()
        => Project?.DccCentralProfiles ?? App.DccCentralProfiles;

    /// <summary>
    /// Vráti iba aktívne/povolené DCC centrály z efektívneho scope nastavení.
    /// Táto metóda sa používa všade tam, kde si UI alebo logika majú vyberať len
    /// z reálne zapnutých centrálnych profilov.
    /// </summary>
    public IReadOnlyList<DccCentralProfile> GetEffectiveEnabledDccCentralProfiles()
        => GetEffectiveDccCentralProfiles()
            .Where(p => p.IsEnabled)
            .ToList();

    public Guid? GetEffectiveSelectedDccCentralProfileId()
        => Project?.DccCentralProfiles != null
            ? Project.SelectedDccCentralProfileId
            : App.SelectedDccCentralProfileId;

    public bool HasProjectDccOverride()
    {
        if (Project == null)
            return false;

        return Project.DccCentralType != null
               || Project.DccCentralHost != null
               || Project.DccCentralPort != null
               || Project.DccSerialPort != null
               || Project.DccBaudRate != null
               || Project.AutoConnect != null
               || Project.DccCentralProfiles != null;
    }
}


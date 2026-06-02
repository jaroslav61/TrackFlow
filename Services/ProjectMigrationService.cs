using System.Linq;
using TrackFlow.Models;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

public sealed class ProjectMigrationService
{
    private const int ProjectSchemaVersion2 = 2;
    private const int LayoutSchemaVersion2 = 2;
    private const int CurrentProjectSchemaVersion = 3;
    private const int CurrentLayoutSchemaVersion = 3;

    public ProjectMigrationService(CatalogStore? catalogStore = null)
    {
        _ = catalogStore;
    }

    public TrackFlowProject MigrateIfNeeded(TrackFlowProject project)
    {
        if (project.SchemaVersion <= 0)
            project.SchemaVersion = 1;

        // Layout je vždy inicializovaný (TrackLayout s prázdnym zoznamom prvkov).
        // Staré projekty so LayoutStub sa automaticky načítajú ako prázdny TrackLayout
        // (JSON deserializácia nenájde zodpovedajúce polia → použije default).

        // ✅ NOVÁ LOGIKA: Locomotives a Wagons sa NEMIGRUJÚ do catalog.json
        // Zostanú v projekte, kde boli uložené

        if (project.Layout.SchemaVersion <= 0)
            project.Layout.SchemaVersion = 1;

        if (project.SchemaVersion < 2 || project.Layout.SchemaVersion < 2)
            MigrateToV2(project);

        if (project.SchemaVersion < 3 || project.Layout.SchemaVersion < 3)
            MigrateToV3(project);

        if (project.Layout.SchemaVersion < CurrentLayoutSchemaVersion)
            project.Layout.SchemaVersion = CurrentLayoutSchemaVersion;

        if (project.SchemaVersion < CurrentProjectSchemaVersion)
            project.SchemaVersion = CurrentProjectSchemaVersion;

        RepairContactIndicatorBindings(project);

        return project;
    }

    private static void RepairContactIndicatorBindings(TrackFlowProject project)
    {
        var enabledProfiles = (project.Settings.DccCentralProfiles ?? new())
            .Where(p => p.IsEnabled)
            .ToList();

        var selectedProfileId = project.Settings.SelectedDccCentralProfileId;
        var selectedEnabledProfileId = selectedProfileId.HasValue && enabledProfiles.Any(p => p.Id == selectedProfileId.Value)
            ? selectedProfileId
            : enabledProfiles.Count == 1
                ? enabledProfiles[0].Id
                : null;

        foreach (var block in project.Layout.Elements.OfType<BlockElement>())
        {
            foreach (var indicator in block.Indicators.Where(i => i.Type == BlockIndicatorType.Contact))
            {
                if (!indicator.DccCentralProfileId.HasValue && selectedEnabledProfileId.HasValue)
                    indicator.DccCentralProfileId = selectedEnabledProfileId.Value;

                if (indicator.ModuleAddress >= 1 && indicator.PortNumber >= 1)
                    continue;

                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"Kontaktný indikátor má neplatnú konfiguráciu: blok={block.Id}, label={block.Label}, indikator={indicator.Id}, modul={indicator.ModuleAddress}, vstup={indicator.PortNumber}, profileId={(indicator.DccCentralProfileId?.ToString() ?? "<none>")}. Obsadenie sa nebude dať spárovať, kým sa nenastaví platný modul/vstup.",
                    DiagnosticLevel.Warning);
            }
        }

        var duplicateBindings = project.Layout.Elements.OfType<BlockElement>()
            .SelectMany(block => block.Indicators
                .Where(static indicator => indicator.Type == BlockIndicatorType.Contact)
                .Where(indicator => indicator.DccCentralProfileId.HasValue && indicator.ModuleAddress >= 1 && indicator.PortNumber >= 1)
                .Select(indicator => new
                {
                    BlockId = block.Id,
                    BlockLabel = string.IsNullOrWhiteSpace(block.Label) ? block.Id : block.Label,
                    IndicatorId = indicator.Id,
                    IndicatorName = string.IsNullOrWhiteSpace(indicator.Name) ? indicator.Id.ToString() : indicator.Name,
                    indicator.DccCentralProfileId,
                    indicator.ModuleAddress,
                    indicator.PortNumber
                }))
            .GroupBy(x => new { x.DccCentralProfileId, x.ModuleAddress, x.PortNumber })
            .Where(group => group.Select(x => x.BlockId).Distinct().Count() > 1)
            .ToList();

        foreach (var duplicate in duplicateBindings)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"Duplicitné priradenie kontaktu: profileId={duplicate.Key.DccCentralProfileId}, modul={duplicate.Key.ModuleAddress}, vstup={duplicate.Key.PortNumber}, použité v {string.Join(", ", duplicate.Select(x => $"{x.BlockLabel}/{x.IndicatorName}"))}. Jeden fyzický vstup má byť priradený iba jednému bloku.",
                DiagnosticLevel.Warning);
        }
    }

    private static void MigrateToV2(TrackFlowProject project)
    {
        var layout = project.Layout;

        var defaultSystem = layout.SignalSystems
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Id)
                && string.Equals(s.Id, SignalSystemDefinition.DefaultSystemId, System.StringComparison.OrdinalIgnoreCase));

        if (defaultSystem == null)
        {
            layout.SignalSystems.Add(new SignalSystemDefinition
            {
                Id = SignalSystemDefinition.DefaultSystemId,
                Name = "Slovenská základná sústava",
                Kind = SignalingSystemKind.Slovak,
                SupportedHeadCounts = new() { 2, 3, 4, 5 }
            });
        }

        foreach (var signal in layout.Elements.OfType<SignalElement>())
        {
            if (string.IsNullOrWhiteSpace(signal.SignalSystemId))
                signal.SignalSystemId = SignalSystemDefinition.DefaultSystemId;
        }

        layout.SchemaVersion = LayoutSchemaVersion2;
        project.SchemaVersion = ProjectSchemaVersion2;
    }

    private static void MigrateToV3(TrackFlowProject project)
    {
        var layout = project.Layout;

        foreach (var route in layout.Routes)
        {
            route.FromBlockDirection = RouteDirection.NormalizeOrDefault(
                route.FromBlockDirection,
                RouteDirection.Right,
                $"Route[{route.Id}].{nameof(RouteDefinition.FromBlockDirection)}");

            route.ToBlockDirection = RouteDirection.NormalizeOrDefault(
                route.ToBlockDirection,
                RouteDirection.Right,
                $"Route[{route.Id}].{nameof(RouteDefinition.ToBlockDirection)}");

            route.StartNavigationDirection = RouteDirection.NormalizeOrDefault(
                route.StartNavigationDirection,
                RouteDirection.Right,
                $"Route[{route.Id}].{nameof(RouteDefinition.StartNavigationDirection)}");

            route.RouteSignalIds ??= new();
            route.SafetyFallbackAspect = "Stop";
        }

        layout.SchemaVersion = CurrentLayoutSchemaVersion;
        project.SchemaVersion = CurrentProjectSchemaVersion;
    }
}

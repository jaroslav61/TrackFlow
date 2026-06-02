using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.ViewModels.Operation;

#pragma warning disable 300
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable CollectionNeverQueried.Local
// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace TrackFlow.ViewModels.Editor;

/// <summary>
/// ViewModel pre Správca ciest a plánov.
/// Napojený na reálne TrackLayout.Routes a TrackLayout.Plans.
/// </summary>
public partial class RoutesManagerViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private readonly LayoutEditorViewModel? _layoutEditor;
    private readonly OperationViewModel? _operation;

    public event EventHandler? RoutePreviewChanged;
    
    // Public accessor for layout (needed by RoutePreviewControl)
    public TrackLayout? Layout => _settings.CurrentProject?.Layout;

    // ── Cesty ────────────────────────────────────────────────────────────────
    public ObservableCollection<RouteItemVm> Routes { get; } = new();

    [ObservableProperty] private RouteItemVm? selectedRoute;

    // ── Plány ────────────────────────────────────────────────────────────────
    public ObservableCollection<PlanItemVm> Plans { get; } = new();

    [ObservableProperty] private PlanItemVm? selectedPlan;

    // ── Zoznamy blokov a výhybiek (pre ComboBox) ─────────────────────────────
    public ObservableCollection<BlockInfo> AvailableBlocks { get; } = new();
    public ObservableCollection<TurnoutInfo> AvailableTurnouts { get; } = new();
    public ObservableCollection<SignalInfo> AvailableSignals { get; } = new();

    // ── Dostupné cesty pre kroky plánu ───────────────────────────────────────
    public ObservableCollection<RouteItemVm> AvailableRoutes => Routes;

    // ── Nastavenie automatickej regenerácie ciest ────────────────────────────
    [ObservableProperty] private bool autoRegenerateRoutes;

    public readonly record struct RoutePreviewState(
        TrackLayout? Layout,
        RouteItemVm? SelectedRoute,
        string? ManualStartBlockId,
        string? ManualEndBlockId);

    private bool _isManualRouteSelectionActive;
    private string _manualRouteSelectionHint = string.Empty;
    private string? _manualStartBlockId;
    private string? _manualEndBlockId;
    private CancellationTokenSource? _manualHintAutoClearCts;

    public bool IsManualRouteSelectionActive
    {
        get => _isManualRouteSelectionActive;
        set
        {
            if (!SetProperty(ref _isManualRouteSelectionActive, value))
                return;
            NotifyRoutePreviewChanged();
        }
    }

    public string ManualRouteSelectionHint
    {
        get => _manualRouteSelectionHint;
        set
        {
            if (!SetProperty(ref _manualRouteSelectionHint, value))
                return;
            OnPropertyChanged(nameof(HasManualRouteSelectionHint));
        }
    }

    public string? ManualStartBlockId
    {
        get => _manualStartBlockId;
        set
        {
            if (!SetProperty(ref _manualStartBlockId, value))
                return;
            NotifyRoutePreviewChanged();
        }
    }

    public string? ManualEndBlockId
    {
        get => _manualEndBlockId;
        set
        {
            if (!SetProperty(ref _manualEndBlockId, value))
                return;
            NotifyRoutePreviewChanged();
        }
    }

    public bool HasManualRouteSelectionHint => ManualRouteSelectionHint.Length > 0;

    public static string[] RouteColors { get; } =
        new[] { "#00D4AA", "#FFD700", "#FF7043", "#42A5F5", "#AB47BC", "#66BB6A", "#EF5350", "#26C6DA" };

    /// <summary>Zoznam dostupných farieb pre ComboBox v UI.</summary>
    public static IReadOnlyList<ColorItem> AvailableColors { get; } = new[]
    {
        new ColorItem { Hex = "#00D4AA", Label = "Tyrkysová" },
        new ColorItem { Hex = "#FFD700", Label = "Zlatá" },
        new ColorItem { Hex = "#FF7043", Label = "Oranžová" },
        new ColorItem { Hex = "#42A5F5", Label = "Modrá" },
        new ColorItem { Hex = "#AB47BC", Label = "Fialová" },
        new ColorItem { Hex = "#66BB6A", Label = "Zelená" },
        new ColorItem { Hex = "#EF5350", Label = "Červená" },
        new ColorItem { Hex = "#26C6DA", Label = "Azúrová" },
    };

    public RoutesManagerViewModel(SettingsManager settings, LayoutEditorViewModel? layoutEditor = null, OperationViewModel? operation = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _layoutEditor = layoutEditor;
        _operation = operation;
        LoadFromLayout();
        
        // Načítať nastavenie AutoRegenerateRoutes z projektu
        AutoRegenerateRoutes = _settings.CurrentProject?.Settings?.AutoRegenerateRoutes ?? false;
    }
    
    partial void OnAutoRegenerateRoutesChanged(bool value)
    {
        // Uložiť do ProjectSettingsData
        var project = _settings.CurrentProject;
        if (project != null)
        {
            if (project.Settings == null)
                project.Settings = new Models.ProjectSettingsData();
                
            project.Settings.AutoRegenerateRoutes = value;
            project.IsDirty = true;
        }
    }

    partial void OnSelectedRouteChanged(RouteItemVm? value)
    {
        _ = value;
        NotifyRoutePreviewChanged();
    }

    public RoutePreviewState GetRoutePreviewState()
        => new(Layout, SelectedRoute, ManualStartBlockId, ManualEndBlockId);


    /// <summary>Načíta cesty, plány, bloky a výhybky z aktuálneho layoutu.</summary>
    public void LoadFromLayout()
    {
        Routes.Clear();
        Plans.Clear();
        AvailableBlocks.Clear();
        AvailableTurnouts.Clear();
        AvailableSignals.Clear();

        var layout = Layout;
        if (layout == null) return;

        // Načítať bloky a výhybky z elementov
        foreach (var el in layout.Elements)
        {
            if (el is BlockElement)
            {
                AvailableBlocks.Add(new BlockInfo
                {
                    Id = el.Id,
                    Name = string.IsNullOrWhiteSpace(el.Label) ? $"Blok {AvailableBlocks.Count + 1}" : el.Label
                });
            }
            else if (el is TurnoutElement turnout)
            {
                AvailableTurnouts.Add(new TurnoutInfo
                {
                    Id = el.Id,
                    Name = string.IsNullOrWhiteSpace(el.Label) ? $"Výhybka {AvailableTurnouts.Count + 1}" : el.Label,
                    DccAddress = turnout.DccAddress,
                    MarkerKey = el.MarkerKey // Zachytíme typ výhybky
                });
            }
            else if (el is SignalElement signal)
            {
                AvailableSignals.Add(new SignalInfo
                {
                    Id = signal.Id,
                    Name = ResolveElementDisplayName(signal, "Návestidlo", AvailableSignals.Count + 1)
                });
            }
        }

        // Načítať existujúce cesty
        foreach (var route in layout.Routes)
            Routes.Add(CreateRouteItemVm(route));

        // Pre cesty ktoré boli uložené pred zavedením PathElementIds doplníme
        // chýbajúce ID-čka prvkov dopočítaním cez pathfinder.
        BackfillMissingPathElementIds(layout);
        BackfillRouteDirectionalMetadata(layout);

        // Ak nie sú žiadne cesty ale sú bloky, automaticky pregeneruj
        if (Routes.Count == 0 && AvailableBlocks.Count >= 2)
            GenerateRoutes();

        // Načítať existujúce plány
        foreach (var plan in layout.Plans)
            Plans.Add(CreatePlanItemVm(plan));

        if (Routes.Count > 0) SelectedRoute = Routes[0];
        if (Plans.Count > 0) SelectedPlan = Plans[0];
        NotifyRoutePreviewChanged();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Commands: Cesty
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Automaticky vygeneruje cesty pre všetky dvojice blokov.
    /// Pre každú dvojicu blokov (A→B) vytvorí cestu a priradí výhybky,
    /// ktoré ležia medzi nimi na schéme.
    /// </summary>
    [RelayCommand]
    private void RegenerateRoutes()
    {
        var layout = Layout;
        if (layout == null) return;

        // Vymazať existujúce auto-generované cesty
        var autoRoutes = layout.Routes.Where(r => r.IsAutoGenerated).ToList();
        foreach (var r in autoRoutes)
            layout.Routes.Remove(r);

        var toRemove = Routes.Where(r => r.IsAutoGenerated).ToList();
        foreach (var r in toRemove)
            Routes.Remove(r);

        GenerateRoutes();

        if (Routes.Count > 0 && SelectedRoute == null)
            SelectedRoute = Routes[0];

        MarkDirty();
    }

    /// <summary>
    /// Generuje cesty na základe reálnej konektivity koľajiska.
    /// Používa TrackGraphBuilder pre vytvorenie grafu a DFS pre hľadanie ciest.
    /// </summary>
    private void GenerateRoutes()
    {
        var layout = Layout;
        if (layout == null) return;

        int routeNum = layout.Routes.Count;

        // Nový graf-based pathfinder: nájde všetky skutočné cesty medzi blokmi.
        var foundRoutes = GetFoundRoutes(layout);

        foreach (var rawFound in foundRoutes)
        {
            var found = OrientFoundRouteForCanonicalGeneration(rawFound);

            // Skontroluj či takáto cesta už neexistuje (rovnaká dvojica blokov + rovnaké výhybky)
            bool exists = layout.Routes.Any(r =>
                ((r.FromBlockId == found.FromBlockId && r.ToBlockId == found.ToBlockId) ||
                 (r.FromBlockId == found.ToBlockId && r.ToBlockId == found.FromBlockId)) &&
                r.TurnoutSettings.Count == found.TurnoutStates.Count &&
                r.TurnoutSettings.All(ts =>
                    found.TurnoutStates.TryGetValue(ts.TurnoutId, out var state) && ts.RequiredState == state));

            if (exists) continue;

            routeNum++;

            // Názvy blokov
            var fromBlockInfo = AvailableBlocks.FirstOrDefault(b => b.Id == found.FromBlockId);
            var toBlockInfo = AvailableBlocks.FirstOrDefault(b => b.Id == found.ToBlockId);
            string fromName = fromBlockInfo?.Name ?? "?";
            string toName = toBlockInfo?.Name ?? "?";

            // Konzistentné poradie (abecedne)
            var (firstName, secondName) = string.CompareOrdinal(fromName, toName) < 0
                ? (fromName, toName) : (toName, fromName);

            var route = new RouteDefinition
            {
                Name = $"{firstName} <--> {secondName}",
                FromBlockId = found.FromBlockId,
                ToBlockId = found.ToBlockId,
                FromBlockDirection = ResolveDirectionFromBlockPort(
                    layout.Elements.OfType<BlockElement>().FirstOrDefault(b => b.Id == found.FromBlockId),
                    found.FromBlockExitPort,
                    RouteDirection.Right),
                ToBlockDirection = ResolveDirectionFromBlockPort(
                    layout.Elements.OfType<BlockElement>().FirstOrDefault(b => b.Id == found.ToBlockId),
                    found.ToBlockEntryPort,
                    RouteDirection.Right),
                Kind = RouteDefinitionKind.AutoGeneratedPath,
                IsAutoGenerated = true,
                IsEnabled = true,
                Color = RouteColors[(routeNum - 1) % RouteColors.Length],
                MaxSpeed = 60,
                SafetyFallbackAspect = "Stop"
            };

            route.StartNavigationDirection = route.FromBlockDirection;

            var fromBlock = layout.Elements.OfType<BlockElement>().FirstOrDefault(b => b.Id == found.FromBlockId);
            var fromSignalId = ResolveSignalIdForDirection(fromBlock, route.StartNavigationDirection);
            if (!string.IsNullOrWhiteSpace(fromSignalId))
                route.RouteSignalIds.Add(fromSignalId);

            // Priradíme výhybky so správnymi stavmi z DFS
            foreach (var (turnoutId, requiredState) in found.TurnoutStates)
            {
                route.TurnoutSettings.Add(new RouteTurnoutSetting
                {
                    TurnoutId = turnoutId,
                    RequiredState = requiredState
                });
            }

            // Zoznam všetkých elementov na ceste
            route.BlockIds.Add(found.FromBlockId);
            route.BlockIds.Add(found.ToBlockId);
            route.PathElementIds = new List<string>(found.PathElementIds);

            layout.Routes.Add(route);
            Routes.Add(CreateRouteItemVm(route));
        }
    }

    private FoundRoute OrientFoundRouteForCanonicalGeneration(FoundRoute route)
    {
        var fromName = AvailableBlocks.FirstOrDefault(b => string.Equals(b.Id, route.FromBlockId, StringComparison.OrdinalIgnoreCase))?.Name
                       ?? route.FromBlockId;
        var toName = AvailableBlocks.FirstOrDefault(b => string.Equals(b.Id, route.ToBlockId, StringComparison.OrdinalIgnoreCase))?.Name
                     ?? route.ToBlockId;

        var comparison = string.Compare(fromName, toName, StringComparison.CurrentCultureIgnoreCase);
        if (comparison == 0)
            comparison = string.Compare(route.FromBlockId, route.ToBlockId, StringComparison.OrdinalIgnoreCase);

        if (comparison <= 0)
            return route;

        return new FoundRoute
        {
            FromBlockId = route.ToBlockId,
            ToBlockId = route.FromBlockId,
            FromBlockExitPort = route.ToBlockEntryPort,
            ToBlockEntryPort = route.FromBlockExitPort,
            PathElementIds = route.PathElementIds.AsEnumerable().Reverse().ToList(),
            TurnoutStates = new Dictionary<string, TurnoutState>(route.TurnoutStates, StringComparer.OrdinalIgnoreCase)
        };
    }


    [RelayCommand]
    private void AddRoute()
    {
        var layout = Layout;
        if (layout == null) return;

        CancelManualHintAutoClear();

        IsManualRouteSelectionActive = true;
        ManualStartBlockId = null;
        ManualEndBlockId = null;
        ManualRouteSelectionHint = "Vyber štartovací blok v náhľade.";
    }

    public void HandlePreviewBlockClicked(string blockId)
    {
        if (!IsManualRouteSelectionActive)
            return;
        if (string.IsNullOrWhiteSpace(blockId))
            return;

        if (string.IsNullOrWhiteSpace(ManualStartBlockId))
        {
            ManualStartBlockId = blockId;
            ManualEndBlockId = null;
            var startName = AvailableBlocks.FirstOrDefault(b => string.Equals(b.Id, blockId, StringComparison.OrdinalIgnoreCase))?.Name ?? blockId;
            ManualRouteSelectionHint = $"Štart: {startName}. Vyber cieľový blok.";
            return;
        }

        if (string.Equals(ManualStartBlockId, blockId, StringComparison.OrdinalIgnoreCase))
        {
            ManualRouteSelectionHint = "Štart a cieľ nemôžu byť rovnaký blok. Vyber iný cieľ.";
            return;
        }

        ManualEndBlockId = blockId;
        if (TryCreateUserDefinedRoute(ManualStartBlockId!, ManualEndBlockId))
        {
            IsManualRouteSelectionActive = false;
            ManualRouteSelectionHint = "Vlaková cesta bola vytvorená.";
            ManualStartBlockId = null;
            ManualEndBlockId = null;
            ScheduleManualHintAutoClear(TimeSpan.FromSeconds(2));
            return;
        }

        ManualEndBlockId = null;
        ManualRouteSelectionHint = "Medzi vybranými blokmi sa nenašla súvislá cesta. Vyber iný cieľ.";
    }

    [RelayCommand]
    private void DeleteRoute()
    {
        if (SelectedRoute == null) return;
        var layout = Layout;
        if (layout == null) return;

        var model = layout.Routes.FirstOrDefault(r => r.Id == SelectedRoute.Id);
        if (model != null)
            layout.Routes.Remove(model);

        int clearedAssignments = RouteMarkerAssignmentHelper.ClearInvalidAssignments(layout);

        Routes.Remove(SelectedRoute);
        SelectedRoute = Routes.FirstOrDefault();

        if (clearedAssignments > 0)
        {
            _layoutEditor?.RequestVisualRefresh();
            _operation?.RefreshLayoutFromProject();
            _settings.NotifyProjectChanged();
        }

        MarkDirty();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Commands: Plány
    // ══════════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void AddPlan()
    {
        var layout = Layout;
        if (layout == null) return;

        int num = layout.Plans.Count + 1;
        var plan = new TrainPlan { Name = $"Plán {num}" };

        layout.Plans.Add(plan);
        var vm = CreatePlanItemVm(plan);
        Plans.Add(vm);
        SelectedPlan = vm;
        MarkDirty();
    }

    [RelayCommand]
    private void DeletePlan()
    {
        if (SelectedPlan == null) return;
        var layout = Layout;
        if (layout == null) return;

        var model = layout.Plans.FirstOrDefault(p => p.Id == SelectedPlan.Id);
        if (model != null) layout.Plans.Remove(model);

        Plans.Remove(SelectedPlan);
        SelectedPlan = Plans.FirstOrDefault();
        MarkDirty();
    }

    [RelayCommand]
    private void AddPlanStep()
    {
        if (SelectedPlan == null) return;
        var layout = Layout;
        if (layout == null) return;

        var planModel = layout.Plans.FirstOrDefault(p => p.Id == SelectedPlan.Id);
        if (planModel == null) return;

        var step = new PlanStep { DwellTimeSeconds = 30 };
        planModel.Steps.Add(step);
        SelectedPlan.Steps.Add(CreateStepVm(step, planModel.Steps.Count));
        OnPropertyChanged(nameof(SelectedPlan));
        MarkDirty();
    }

    [RelayCommand]
    private void RemovePlanStep(PlanStepVm? step)
    {
        if (step == null || SelectedPlan == null) return;
        var layout = Layout;
        if (layout == null) return;

        var planModel = layout.Plans.FirstOrDefault(p => p.Id == SelectedPlan.Id);
        if (planModel == null) return;

        int idx = SelectedPlan.Steps.IndexOf(step);
        if (idx >= 0 && idx < planModel.Steps.Count)
        {
            planModel.Steps.RemoveAt(idx);
            SelectedPlan.Steps.RemoveAt(idx);
            RenumberSteps(SelectedPlan);
            MarkDirty();
        }
    }

    [RelayCommand]
    private void MoveStepUp(PlanStepVm? step)
    {
        if (step == null || SelectedPlan == null) return;
        int idx = SelectedPlan.Steps.IndexOf(step);
        if (idx <= 0) return;

        var planModel = Layout?.Plans.FirstOrDefault(p => p.Id == SelectedPlan.Id);
        if (planModel == null || idx >= planModel.Steps.Count) return;

        (planModel.Steps[idx - 1], planModel.Steps[idx]) = (planModel.Steps[idx], planModel.Steps[idx - 1]);
        SelectedPlan.Steps.Move(idx, idx - 1);
        RenumberSteps(SelectedPlan);
        MarkDirty();
    }

    [RelayCommand]
    private void MoveStepDown(PlanStepVm? step)
    {
        if (step == null || SelectedPlan == null) return;
        int idx = SelectedPlan.Steps.IndexOf(step);
        if (idx < 0 || idx >= SelectedPlan.Steps.Count - 1) return;

        var planModel = Layout?.Plans.FirstOrDefault(p => p.Id == SelectedPlan.Id);
        if (planModel == null || idx + 1 >= planModel.Steps.Count) return;

        (planModel.Steps[idx], planModel.Steps[idx + 1]) = (planModel.Steps[idx + 1], planModel.Steps[idx]);
        SelectedPlan.Steps.Move(idx, idx + 1);
        RenumberSteps(SelectedPlan);
        MarkDirty();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Save – synchronizuje VM → model a uloží projekt
    // ══════════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void Save()
    {
        var layout = Layout;
        if (layout == null) return;

        // Sync cesty
        foreach (var routeVm in Routes)
        {
            var model = layout.Routes.FirstOrDefault(r => r.Id == routeVm.Id);
            if (model == null) continue;

            model.Name = routeVm.Name;
            model.FromBlockId = routeVm.FromBlockId;
            model.ToBlockId = routeVm.ToBlockId;
            model.IsEnabled = routeVm.IsEnabled;
            model.Color = routeVm.Color;
            model.MaxSpeed = routeVm.MaxSpeed;
            model.Kind = routeVm.Kind;
            model.FromBlockDirection = NormalizeDirectionForSave(routeVm.FromBlockDirection, RouteDirection.Right, model.Id, nameof(RouteDefinition.FromBlockDirection));
            model.ToBlockDirection = NormalizeDirectionForSave(routeVm.ToBlockDirection, RouteDirection.Right, model.Id, nameof(RouteDefinition.ToBlockDirection));
            model.StartNavigationDirection = NormalizeDirectionForSave(routeVm.StartNavigationDirection, RouteDirection.Right, model.Id, nameof(RouteDefinition.StartNavigationDirection));
            model.SafetyFallbackAspect = "Stop";
            model.IsAutoGenerated = routeVm.Kind == RouteDefinitionKind.AutoGeneratedPath;

            model.TurnoutSettings.Clear();
            foreach (var ts in routeVm.TurnoutSettings)
                model.TurnoutSettings.Add(new RouteTurnoutSetting
                {
                    TurnoutId = ts.TurnoutId,
                    RequiredState = ts.RequiredState
                });

            model.PathElementIds = new List<string>(routeVm.PathElementIds);
            model.RouteSignalIds = routeVm.RouteSignals
                .Select(s => s.SignalId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Sync plány
        foreach (var planVm in Plans)
        {
            var model = layout.Plans.FirstOrDefault(p => p.Id == planVm.Id);
            if (model == null) continue;

            model.Name = planVm.Name;
            model.LocoName = planVm.LocoName;
            model.IsLoop = planVm.IsLoop;

            model.Steps.Clear();
            foreach (var stepVm in planVm.Steps)
                model.Steps.Add(new PlanStep
                {
                    RouteId = stepVm.RouteId,
                    DwellTimeSeconds = stepVm.DwellTimeSeconds,
                    SpeedKmh = stepVm.SpeedKmh
                });
        }

        _settings.SaveProject();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private RouteItemVm CreateRouteItemVm(RouteDefinition route)
    {
        var fromBlock = AvailableBlocks.FirstOrDefault(b => b.Id == route.FromBlockId);
        var toBlock = AvailableBlocks.FirstOrDefault(b => b.Id == route.ToBlockId);

        // Vytvoríme stabilné symboly smeru aj pre legacy/invalid vstupy.
        var normalizedFromDirection = NormalizeDirectionForDisplay(route.FromBlockDirection, RouteDirection.Right, "CreateRouteItemVm.FromBlockDirection");
        var normalizedToDirection = NormalizeDirectionForDisplay(route.ToBlockDirection, RouteDirection.Right, "CreateRouteItemVm.ToBlockDirection");
        var normalizedStartDirection = NormalizeDirectionForDisplay(route.StartNavigationDirection, normalizedFromDirection, "CreateRouteItemVm.StartNavigationDirection");

        string fromSymbol = DirectionToSymbol(normalizedFromDirection);
        string toSymbol = DirectionToSymbol(normalizedToDirection);

        var vm = new RouteItemVm
        {
            Id = route.Id,
            Name = route.Name,
            FromBlockId = route.FromBlockId,
            ToBlockId = route.ToBlockId,
            FromBlockName = $"{fromBlock?.Name ?? route.FromBlockId} {fromSymbol}",
            ToBlockName = $"{toSymbol} {toBlock?.Name ?? route.ToBlockId}",
            IsEnabled = route.IsEnabled,
            IsAutoGenerated = route.IsAutoGenerated,
            Kind = route.Kind,
            Color = route.Color,
            MaxSpeed = route.MaxSpeed,
            PathElementIds = new List<string>(route.PathElementIds),
            RouteSignalIds = route.RouteSignalIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FromBlockDirection = normalizedFromDirection,
            ToBlockDirection = normalizedToDirection,
            StartNavigationDirection = normalizedStartDirection,
            SafetyFallbackAspect = "Stop"
        };

        foreach (var signalItem in BuildRouteSignalItems(vm.RouteSignalIds))
            vm.RouteSignals.Add(signalItem);

        foreach (var ts in route.TurnoutSettings)
        {
            var turnout = AvailableTurnouts.FirstOrDefault(t => t.Id == ts.TurnoutId);
            vm.TurnoutSettings.Add(new TurnoutSettingVm
            {
                TurnoutId = ts.TurnoutId,
                TurnoutName = turnout?.Name ?? ts.TurnoutId,
                DccAddress = turnout?.DccAddress ?? 0,
                RequiredState = ts.RequiredState,
                TurnoutType = turnout?.MarkerKey ?? "Turnout_L"
            });
        }

        vm.TurnoutsSummary = vm.TurnoutSettings.Count == 0
            ? "—"
            : string.Join(", ", vm.TurnoutSettings.Select(t =>
                $"{t.TurnoutName}→{(t.RequiredState == TurnoutState.Straight ? "priamo" : "odbočka")}"));

        return vm;
    }

    private static string ResolveDirectionFromBlockPort(BlockElement? block, string? portName, string defaultDirection)
    {
        if (block == null)
            return defaultDirection;

        bool isVertical = LayoutElementFootprintHelper.IsVertical(block.Rotation);
        if (string.Equals(portName, "A", StringComparison.OrdinalIgnoreCase))
            return isVertical ? RouteDirection.Up : RouteDirection.Left;
        if (string.Equals(portName, "B", StringComparison.OrdinalIgnoreCase))
            return isVertical ? RouteDirection.Down : RouteDirection.Right;

        Log.Warning("Unknown block port '{PortName}' for block '{BlockId}'. Using default direction '{DefaultDirection}'.",
            portName ?? "<null>", block.Id, defaultDirection);
        return defaultDirection;
    }

    private static string? ResolveSignalIdForDirection(BlockElement? block, string direction)
    {
        if (block == null)
            return null;

        var dir = direction switch
        {
            var d when string.Equals(d, RouteDirection.Left, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Left,
            var d when string.Equals(d, RouteDirection.Right, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Right,
            var d when string.Equals(d, RouteDirection.Up, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Up,
            var d when string.Equals(d, RouteDirection.Down, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Down,
            _ => NavigationDirection.Right
        };

        return block.GetSignalForDirection(dir);
    }

    private static string DirectionToSymbol(string direction)
        => direction switch
        {
            var d when string.Equals(d, RouteDirection.Left, StringComparison.OrdinalIgnoreCase) => "←",
            var d when string.Equals(d, RouteDirection.Right, StringComparison.OrdinalIgnoreCase) => "→",
            var d when string.Equals(d, RouteDirection.Up, StringComparison.OrdinalIgnoreCase) => "↑",
            var d when string.Equals(d, RouteDirection.Down, StringComparison.OrdinalIgnoreCase) => "↓",
            _ => "→"
        };

    private static string NormalizeDirectionForDisplay(string? direction, string defaultDirection, string context)
        => RouteDirection.NormalizeOrDefault(direction, defaultDirection, context);

    private static string NormalizeDirectionForSave(string? direction, string defaultDirection, string routeId, string fieldName)
        => RouteDirection.NormalizeOrDefault(direction, defaultDirection, $"Route[{routeId}].{fieldName}");

    private List<RouteSignalVm> BuildRouteSignalItems(IEnumerable<string> signalIds)
    {
        var byId = AvailableSignals.ToDictionary(s => s.Id, s => s.Name, StringComparer.OrdinalIgnoreCase);

        return signalIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new RouteSignalVm
            {
                SignalId = id,
                Name = byId.TryGetValue(id, out var name) ? name : id
            })
            .ToList();
    }

    private bool TryCreateUserDefinedRoute(string fromBlockId, string toBlockId)
    {
        var layout = Layout;
        if (layout == null)
            return false;

        var pathfinder = new RoutePathfinder(layout, _settings.CurrentProject?.Settings);
        var path = pathfinder.FindRouteBetweenBlocks(fromBlockId, toBlockId);
        if (path == null || path.BlockIds.Count < 2)
            return false;

        int routeNum = layout.Routes.Count + 1;
        var fromName = AvailableBlocks.FirstOrDefault(b => string.Equals(b.Id, fromBlockId, StringComparison.OrdinalIgnoreCase))?.Name ?? fromBlockId;
        var toName = AvailableBlocks.FirstOrDefault(b => string.Equals(b.Id, toBlockId, StringComparison.OrdinalIgnoreCase))?.Name ?? toBlockId;
        var baseName = $"{fromName} -> {toName}";
        var routeName = BuildUniqueRouteName(baseName);

        var route = new RouteDefinition
        {
            Name = routeName,
            FromBlockId = fromBlockId,
            ToBlockId = toBlockId,
            FromBlockDirection = path.FromBlockDirection,
            ToBlockDirection = path.ToBlockDirection,
            StartNavigationDirection = path.StartNavigationDirection,
            Kind = RouteDefinitionKind.UserDefinedRoute,
            IsAutoGenerated = false,
            IsEnabled = true,
            Color = RouteColors[(routeNum - 1) % RouteColors.Length],
            MaxSpeed = 60,
            SafetyFallbackAspect = "Stop"
        };

        route.BlockIds = new List<string>(path.BlockIds);
        route.PathElementIds = new List<string>(path.PathElementIds);
        route.RouteSignalIds = path.Signals
            .Select(s => s.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var sw in path.SwitchStates)
        {
            route.TurnoutSettings.Add(new RouteTurnoutSetting
            {
                TurnoutId = sw.TurnoutId,
                RequiredState = sw.RequiredState
            });
        }

        layout.Routes.Add(route);
        var vm = CreateRouteItemVm(route);
        Routes.Add(vm);
        SelectedRoute = vm;
        MarkDirty();
        return true;
    }

    private string BuildUniqueRouteName(string baseName)
    {
        if (!Routes.Any(r => string.Equals(r.Name, baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;

        int i = 2;
        while (Routes.Any(r => string.Equals(r.Name, $"{baseName} ({i})", StringComparison.OrdinalIgnoreCase)))
            i++;
        return $"{baseName} ({i})";
    }

    private static string ResolveElementDisplayName(LayoutElement element, string fallbackPrefix, int ordinal)
    {
        if (!string.IsNullOrWhiteSpace(element.Label))
            return element.Label;
        return $"{fallbackPrefix} {ordinal}";
    }

    private PlanItemVm CreatePlanItemVm(TrainPlan plan)
    {
        var vm = new PlanItemVm
        {
            Id = plan.Id,
            Name = plan.Name,
            LocoName = plan.LocoName,
            IsLoop = plan.IsLoop
        };

        int stepNum = 1;
        foreach (var step in plan.Steps)
            vm.Steps.Add(CreateStepVm(step, stepNum++));

        return vm;
    }

    private PlanStepVm CreateStepVm(PlanStep step, int number)
    {
        var route = Layout?.Routes.FirstOrDefault(r => r.Id == step.RouteId);
        var fromBlock = route != null ? AvailableBlocks.FirstOrDefault(b => b.Id == route.FromBlockId) : null;
        var toBlock = route != null ? AvailableBlocks.FirstOrDefault(b => b.Id == route.ToBlockId) : null;

        return new PlanStepVm
        {
            Number = number,
            RouteId = step.RouteId,
            RouteName = route?.Name ?? "(vyberte cestu)",
            FromBlockName = fromBlock?.Name ?? "?",
            ToBlockName = toBlock?.Name ?? "?",
            DwellTimeSeconds = step.DwellTimeSeconds,
            SpeedKmh = step.SpeedKmh
        };
    }

    private static void RenumberSteps(PlanItemVm plan)
    {
        for (int i = 0; i < plan.Steps.Count; i++)
            plan.Steps[i].Number = i + 1;
    }

    /// <summary>
    /// Doplní PathElementIds pre cesty, ktoré ich nemajú (staré uložené projekty
    /// pred zavedením tohto poľa). Pre každú takú cestu spustí pathfinder a hľadá
    /// zhodu podľa blokov + množiny stavov výhybiek a prevezme jeho PathElementIds.
    /// </summary>
    private void BackfillMissingPathElementIds(TrackLayout layout)
    {
        var missing = layout.Routes.Where(r => r.PathElementIds.Count == 0).ToList();
        if (missing.Count == 0) return;

        List<FoundRoute> found;
        try
        {
            found = GetFoundRoutes(layout);
        }
        catch
        {
            return;
        }

        foreach (var route in missing)
        {
            var match = found.FirstOrDefault(f =>
                ((f.FromBlockId == route.FromBlockId && f.ToBlockId == route.ToBlockId) ||
                 (f.FromBlockId == route.ToBlockId && f.ToBlockId == route.FromBlockId)) &&
                f.TurnoutStates.Count == route.TurnoutSettings.Count &&
                route.TurnoutSettings.All(ts =>
                    f.TurnoutStates.TryGetValue(ts.TurnoutId, out var st) && st == ts.RequiredState));

            if (match != null)
            {
                route.PathElementIds = new List<string>(match.PathElementIds);
                var vm = Routes.FirstOrDefault(r => r.Id == route.Id);
                if (vm != null) vm.PathElementIds = new List<string>(match.PathElementIds);
            }
        }
    }

    private void BackfillRouteDirectionalMetadata(TrackLayout layout)
    {
        var missing = layout.Routes
            .Where(r => !RouteDirection.IsValid(r.FromBlockDirection)
                        || !RouteDirection.IsValid(r.ToBlockDirection)
                        || !RouteDirection.IsValid(r.StartNavigationDirection)
                        || r.RouteSignalIds.Count == 0)
            .ToList();

        if (missing.Count == 0)
            return;

        List<FoundRoute> found;
        try
        {
            found = GetFoundRoutes(layout);
        }
        catch
        {
            return;
        }

        foreach (var route in missing)
        {
            bool MatchesTurnouts(FoundRoute f) =>
                f.TurnoutStates.Count == route.TurnoutSettings.Count &&
                route.TurnoutSettings.All(ts =>
                    f.TurnoutStates.TryGetValue(ts.TurnoutId, out var st) && st == ts.RequiredState);

            var direct = found.FirstOrDefault(f =>
                f.FromBlockId == route.FromBlockId
                && f.ToBlockId == route.ToBlockId
                && MatchesTurnouts(f));

            var reversed = direct == null
                ? found.FirstOrDefault(f =>
                    f.FromBlockId == route.ToBlockId
                    && f.ToBlockId == route.FromBlockId
                    && MatchesTurnouts(f))
                : null;

            if (direct == null && reversed == null)
                continue;

            var fromExitPort = direct?.FromBlockExitPort ?? reversed!.ToBlockEntryPort;
            var toEntryPort = direct?.ToBlockEntryPort ?? reversed!.FromBlockExitPort;

            var fromBlock = layout.Elements.OfType<BlockElement>().FirstOrDefault(b => b.Id == route.FromBlockId);
            var toBlock = layout.Elements.OfType<BlockElement>().FirstOrDefault(b => b.Id == route.ToBlockId);

            route.FromBlockDirection = ResolveDirectionFromBlockPort(fromBlock, fromExitPort, RouteDirection.Right);
            route.ToBlockDirection = ResolveDirectionFromBlockPort(toBlock, toEntryPort, RouteDirection.Right);
            route.StartNavigationDirection = route.FromBlockDirection;

            if (route.RouteSignalIds.Count == 0)
            {
                var signalId = ResolveSignalIdForDirection(fromBlock, route.StartNavigationDirection);
                if (!string.IsNullOrWhiteSpace(signalId))
                    route.RouteSignalIds.Add(signalId);
            }

            route.SafetyFallbackAspect = "Stop";

            var vm = Routes.FirstOrDefault(r => r.Id == route.Id);
            if (vm != null)
            {
                vm.FromBlockDirection = route.FromBlockDirection;
                vm.ToBlockDirection = route.ToBlockDirection;
                vm.StartNavigationDirection = route.StartNavigationDirection;
                vm.SafetyFallbackAspect = route.SafetyFallbackAspect;
                vm.RouteSignalIds = route.RouteSignalIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                vm.RouteSignals.Clear();
                foreach (var signalItem in BuildRouteSignalItems(vm.RouteSignalIds))
                    vm.RouteSignals.Add(signalItem);

                var fromBlockInfo = AvailableBlocks.FirstOrDefault(b => b.Id == route.FromBlockId);
                var toBlockInfo = AvailableBlocks.FirstOrDefault(b => b.Id == route.ToBlockId);
                vm.FromBlockName = $"{fromBlockInfo?.Name ?? route.FromBlockId} {DirectionToSymbol(route.FromBlockDirection)}";
                vm.ToBlockName = $"{DirectionToSymbol(route.ToBlockDirection)} {toBlockInfo?.Name ?? route.ToBlockId}";
            }
        }
    }

    private void MarkDirty()
    {
        _settings.Dirty.MarkDirty("routes");
        _settings.NotifyProjectChanged();
    }

    private List<FoundRoute> GetFoundRoutes(TrackLayout layout)
    {
        var pf = new RoutePathfinder(layout, _settings.CurrentProject?.Settings);
        return pf.FindAllRoutes();
    }

    private void NotifyRoutePreviewChanged()
    {
        RoutePreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleManualHintAutoClear(TimeSpan delay)
    {
        CancelManualHintAutoClear();

        var cts = new CancellationTokenSource();
        _manualHintAutoClearCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                if (cts.IsCancellationRequested)
                    return;

                ManualRouteSelectionHint = string.Empty;
            }
            catch (TaskCanceledException)
            {
                // Nový výber/akcia zrušil pôvodný auto-clear.
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_manualHintAutoClearCts, cts))
                    _manualHintAutoClearCts = null;
            }
        });
    }

    private void CancelManualHintAutoClear()
    {
        var cts = _manualHintAutoClearCts;
        if (cts == null)
            return;

        _manualHintAutoClearCts = null;
        try
        {
            cts.Cancel();
        }
        finally
        {
            cts.Dispose();
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Sub-ViewModely
// ══════════════════════════════════════════════════════════════════════════════

public partial class RouteItemVm : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string fromBlockId = string.Empty;
    [ObservableProperty] private string toBlockId = string.Empty;
    [ObservableProperty] private string fromBlockName = string.Empty;
    [ObservableProperty] private string toBlockName = string.Empty;
    [ObservableProperty] private bool isEnabled = true;
    [ObservableProperty] private bool isAutoGenerated;
    [ObservableProperty] private string color = "#00D4AA";
    [ObservableProperty] private int maxSpeed = 60;
    [ObservableProperty] private string turnoutsSummary = "—";

    public ObservableCollection<TurnoutSettingVm> TurnoutSettings { get; } = new();
    public ObservableCollection<RouteSignalVm> RouteSignals { get; } = new();

    /// <summary>ID prvkov na ceste (track, curve, turnout) – bez štart/cieľ blokov.</summary>
    public List<string> PathElementIds { get; set; } = new();
    public List<string> RouteSignalIds { get; set; } = new();

    public string FromBlockDirection { get; set; } = RouteDirection.Right;
    public string ToBlockDirection { get; set; } = RouteDirection.Right;
    public string StartNavigationDirection { get; set; } = RouteDirection.Right;
    public string SafetyFallbackAspect { get; set; } = "Stop";
    public RouteDefinitionKind Kind { get; set; } = RouteDefinitionKind.UserDefinedRoute;

    public string KindGlyph => Kind == RouteDefinitionKind.AutoGeneratedPath ? "⚙" : "✍";
    public string KindDisplayName => Kind == RouteDefinitionKind.AutoGeneratedPath
        ? "Automaticky generovaná cesta"
        : "Manuálne definovaná vlaková cesta";
    public string KindBadgeText => Kind == RouteDefinitionKind.AutoGeneratedPath ? "AUTO" : "MANUÁL";

    /// <summary>Krátky text počtu výhybiek pre tabuľku.</summary>
    public string TurnoutCountText => TurnoutSettings.Count == 0
        ? "—"
        : $"{TurnoutSettings.Count} výh.";

    /// <summary>
    /// Vybraná farba ako <see cref="ColorItem"/> pre binding ComboBoxu.
    /// Zmena synchrónne aktualizuje vlastnosť <see cref="Color"/>.
    /// </summary>
    public ColorItem? SelectedColorItem
    {
        get => RoutesManagerViewModel.AvailableColors.FirstOrDefault(c => c.Hex == Color);
        set
        {
            if (value != null && value.Hex != Color)
                Color = value.Hex;
        }
    }

    partial void OnColorChanged(string value)
    {
        _ = value;
        OnPropertyChanged(nameof(SelectedColorItem));
    }
}

public partial class TurnoutSettingVm : ObservableObject
{
    public string TurnoutId { get; set; } = string.Empty;
    [ObservableProperty] private string turnoutName = string.Empty;
    [ObservableProperty] private int dccAddress;
    [ObservableProperty] private TurnoutState requiredState = TurnoutState.Straight;
    
    /// <summary>Typ výhybky (pre správne vykreslenie ikony)</summary>
    public string TurnoutType { get; set; } = "Turnout_L";

    /// <summary>Index pre ComboBox (0=Straight, 1=Diverge) - zachované pre kompatibilitu</summary>
    public int RequiredStateIndex
    {
        get => RequiredState == TurnoutState.Straight ? 0 : 1;
        set => RequiredState = value == 0 ? TurnoutState.Straight : TurnoutState.Diverge;
    }

    /// <summary>Textový popis stavu výhybky.</summary>
    public string RequiredStateText => RequiredState switch
    {
        TurnoutState.Straight => "Priamo",
        TurnoutState.Diverge => "Odbočka",
        TurnoutState.DivergeLeft => "Odbočka vľavo",
        TurnoutState.DivergeRight => "Odbočka vpravo",
        _ => "Priamo"
    };
}

public partial class PlanItemVm : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string locoName = string.Empty;
    [ObservableProperty] private bool isLoop;

    public ObservableCollection<PlanStepVm> Steps { get; } = new();
    public string StepCountText => $"{Steps.Count} krok{(Steps.Count == 1 ? "" : Steps.Count < 5 ? "y" : "ov")}";
}

public partial class PlanStepVm : ObservableObject
{
    [ObservableProperty] private int number;
    [ObservableProperty] private string routeId = string.Empty;
    [ObservableProperty] private string routeName = string.Empty;
    [ObservableProperty] private string fromBlockName = "?";
    [ObservableProperty] private string toBlockName = "?";
    [ObservableProperty] private int dwellTimeSeconds = 30;
    [ObservableProperty] private int speedKmh;
}

public class BlockInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public override string ToString() => Name;
}

public class TurnoutInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DccAddress { get; set; }
    public string MarkerKey { get; set; } = "Turnout_L"; // Typ výhybky pre rendering
    public override string ToString() => $"{Name} (DCC:{DccAddress})";
}

public class SignalInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public override string ToString() => Name;
}

public class RouteSignalVm
{
    public string SignalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Položka farebného ComboBoxu – hex kód + slovenský popis.</summary>
public class ColorItem
{
    public string Hex { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public override string ToString() => Label;
}

#pragma warning restore 300



using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrackFlow.Models.Layout;
using TrackFlow.Services;

namespace TrackFlow.ViewModels.Editor;

/// <summary>ViewModel pre okno editora ciest (Routes).</summary>
public partial class RouteEditorViewModel : ObservableObject
{
    public sealed class RouteDefinitionItem
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Display => string.IsNullOrWhiteSpace(Name) ? Id : $"{Name} ({Id})";
    }

    private readonly LayoutEditorViewModel? _layoutVm;
    private readonly SettingsManager? _settingsManager;
    private RouteElement? _editingRoute;

    public string WindowTitle => "Cesta";

    // ── Všeobecné ────────────────────────────────────────────────────────────

    /// <summary>Typ prvku (readonly).</summary>
    public string ElementType => "Cesta";

    [ObservableProperty] private string routeName = "";

    // ── Signál a rýchlostné obmedzenia ───────────────────────────────────────

    [ObservableProperty] private bool requestYellow;
    [ObservableProperty] private int maxSpeed = 60;
    [ObservableProperty] private int limitedSpeed = 40;

    // ── Cesta (väzba na existujúce RouteDefinition) ───────────────────────────
    public ObservableCollection<RouteDefinitionItem> AvailableRoutes { get; } = new();

    [ObservableProperty]
    private RouteDefinitionItem? selectedRouteDefinition;

    // ── Indikátory ───────────────────────────────────────────────────────────

    public ObservableCollection<SensorItem> AvailableSensors { get; } = new();

    // ── Príkazy ──────────────────────────────────────────────────────────────

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public event Action<bool>? CloseRequested;

    /// <summary>Design-time constructor.</summary>
    public RouteEditorViewModel() : this(null, null) { }

    public RouteEditorViewModel(
        LayoutEditorViewModel? layoutVm,
        SettingsManager? settingsManager)
    {
        _layoutVm = layoutVm;
        _settingsManager = settingsManager;

        // Načítať indikátory
        LoadSensors();
        LoadRoutes();

        // Príkazy
        SaveCommand = new RelayCommand(OnSave);
        CancelCommand = new RelayCommand(OnCancel);
    }

    private void LoadSensors()
    {
        AvailableSensors.Clear();

        if (_layoutVm == null) return;

        // Načítaj všetky indikátory zo všetkých blokov v layoute
        foreach (var blockElement in _layoutVm.Elements.OfType<BlockElement>())
        {
            foreach (var indicator in blockElement.Indicators)
            {
                // Ikona neaktívneho indikátora (s _d.png koncovkou)
                string iconPath = indicator.Type switch
                {
                    BlockIndicatorType.Contact => "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png",
                    BlockIndicatorType.Flagman => "avares://TrackFlow/Assets/Appicons/16/flag_d.png",
                    BlockIndicatorType.Virtual => "avares://TrackFlow/Assets/Appicons/16/virt_cont_d.png",
                    _ => "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png"
                };

                AvailableSensors.Add(new SensorItem
                {
                    Id = indicator.Id.ToString(),
                    Name = indicator.Name,
                    IconPath = iconPath,
                    IsSelected = false
                });
            }
        }
    }

    private void LoadRoutes()
    {
        AvailableRoutes.Clear();

        if (_settingsManager?.CurrentProject?.Layout == null)
            return;

        foreach (var route in _settingsManager.CurrentProject.Layout.Routes)
        {
            AvailableRoutes.Add(new RouteDefinitionItem
            {
                Id = route.Id,
                Name = route.Name
            });
        }
    }

    public void SelectRouteDefinitionById(string? routeDefinitionId)
    {
        if (string.IsNullOrWhiteSpace(routeDefinitionId))
        {
            SelectedRouteDefinition = null;
            return;
        }

        SelectedRouteDefinition = AvailableRoutes.FirstOrDefault(r =>
            string.Equals(r.Id, routeDefinitionId, StringComparison.OrdinalIgnoreCase));
    }

    public string? GetSelectedRouteDefinitionId() => SelectedRouteDefinition?.Id;

    public void LoadFromElement(RouteElement route)
    {
        if (route == null) throw new ArgumentNullException(nameof(route));

        _editingRoute = route;

        RouteName = route.RouteName;
        RequestYellow = route.RequestYellow;
        MaxSpeed = route.MaxSpeed;
        LimitedSpeed = route.LimitedSpeed;

        SelectRouteDefinitionById(route.SelectedRouteDefinitionId);
        SelectSensorsByIds(route.IndicatorIds);
    }

    public void SaveToElement(RouteElement route)
    {
        if (route == null) throw new ArgumentNullException(nameof(route));

        route.RouteName = RouteName;
        route.RequestYellow = RequestYellow;
        route.MaxSpeed = MaxSpeed;
        route.LimitedSpeed = LimitedSpeed;
        route.SelectedRouteDefinitionId = GetSelectedRouteDefinitionId();

        route.IndicatorIds = AvailableSensors
            .Where(s => s.IsSelected && !string.IsNullOrWhiteSpace(s.Id))
            .Select(s => s.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void SelectSensorsByIds(System.Collections.Generic.IEnumerable<string>? ids)
    {
        var idSet = ids == null
            ? new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new System.Collections.Generic.HashSet<string>(
                ids.Where(i => !string.IsNullOrWhiteSpace(i)),
                StringComparer.OrdinalIgnoreCase);

        foreach (var sensor in AvailableSensors)
            sensor.IsSelected = idSet.Contains(sensor.Id);
    }

    private void OnSave()
    {
        if (_editingRoute != null)
            SaveToElement(_editingRoute);

        CloseRequested?.Invoke(true);
    }

    private void OnCancel()
    {
        CloseRequested?.Invoke(false);
    }
}



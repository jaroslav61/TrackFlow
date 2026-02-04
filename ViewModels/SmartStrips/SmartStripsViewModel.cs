using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.Services;

namespace TrackFlow.ViewModels.SmartStrips;

public partial class SmartStripsViewModel : ObservableObject
{
    public ObservableCollection<LocoRecord> ProjectLocomotives { get; } = new();
    public ObservableCollection<Locomotive> Locomotives { get; } = new();
    public ObservableCollection<Locomotive> ActiveLocomotives { get; } = new();
    public ObservableCollection<Wagon> DepotWagons { get; } = new();

    public IEnumerable<Locomotive> TopVehiclesView => Locomotives
        .OrderByDescending(l => l.HasWagons)
        .ThenBy(l => l.Name);

    private readonly SettingsManager _settings;

    // Design-time constructor (Avalonia designer instantiates VM from XAML).
    // Keep it lightweight and side-effect free.
    public SmartStripsViewModel() : this(new SettingsManager())
    {
    }

    public SmartStripsViewModel(SettingsManager settings)
    {
        _settings = settings;
        Locomotives.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TopVehiclesView));
        DepotWagons.CollectionChanged += (_, _) => { };

        ActiveLocomotives.CollectionChanged += (_, _) => { };

        _settings.ProjectChanged += RefreshFromProject;
        RefreshFromProject();
        // Initialize commands
        ItemPressedCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(OnItemPressed);
        ItemDropCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(OnItemDrop);
    }

    public System.Windows.Input.ICommand? ItemPressedCommand { get; private set; }
    public System.Windows.Input.ICommand? ItemDropCommand { get; private set; }

    private bool _suppressSelectionSync;

    private Locomotive? _selectedLocomotive;
    public Locomotive? SelectedLocomotive
    {
        get => _selectedLocomotive;
        set
        {
            if (_selectedLocomotive == value) return;
            _selectedLocomotive = value;
            OnPropertyChanged(nameof(SelectedLocomotive));

            if (!_suppressSelectionSync)
                OnSelectedLocomotiveChanged(value);
        }
    }

    [ObservableProperty]
    private bool isLocoSelected;

    private void OnItemPressed(object? parameter)
    {
        // parameter is the DataContext of the clicked item (Locomotive or Wagon)
        if (parameter is not Locomotive loco)
            return;

        // Toggle activation state on click
        loco.IsActive = !loco.IsActive;

        // Maintain ActiveLocomotives collection: add on activate, remove on deactivate
        if (loco.IsActive)
        {
            if (!ActiveLocomotives.Contains(loco))
                ActiveLocomotives.Add(loco); // add to end

            // select for dashboard
            _suppressSelectionSync = true;
            try { SelectedLocomotive = loco; }
            finally { _suppressSelectionSync = false; }
        }
        else
        {
            if (ActiveLocomotives.Contains(loco))
                ActiveLocomotives.Remove(loco);

            // if deactivated and it was the selected one, clear selection
            if (ReferenceEquals(SelectedLocomotive, loco))
            {
                _suppressSelectionSync = true;
                try { SelectedLocomotive = null; }
                finally { _suppressSelectionSync = false; }
            }
        }

        OnPropertyChanged(nameof(TopVehiclesView));
    }

    private void OnSelectedLocomotiveChanged(Locomotive? loco)
    {
        if (_suppressSelectionSync)
            return;

        // Selection only controls dashboard visibility. Activation is handled by OnItemPressed.
        IsLocoSelected = loco != null;
    }

    private void OnItemDrop(object? parameter)
    {
        // Expect parameter as object[] { target, wagon }
        if (parameter is not object[] arr || arr.Length != 2)
            return;

        var target = arr[0];
        var wagon = arr[1] as Wagon;
        if (wagon == null)
            return;

        if (target is LocoRecord record)
        {
            AttachWagonToLocoRecord(record, wagon);
            return;
        }

        if (target is Locomotive loco)
        {
            AttachWagon(loco, wagon);
            return;
        }
    }

    private void RefreshFromProject()
    {
        ProjectLocomotives.Clear();
        DepotWagons.Clear();

        var list = _settings.Project?.Locomotives;
        if (list == null || list.Count == 0)
        {
            // Still try to show wagons even if locomotives are empty.
            LoadDepotWagonsFromProject();
            return;
        }

        Locomotives.Clear();
        foreach (var loco in list)
        {
            ProjectLocomotives.Add(loco);
            // create runtime Locomotive object for strip if not present
            var key = !string.IsNullOrWhiteSpace(loco.Id) ? loco.Id : loco.Address.ToString();
            var r = new Locomotive(key, loco.Name) { IconName = loco.IconName ?? string.Empty };
            Locomotives.Add(r);
        }

        LoadDepotWagonsFromProject();
    }

    private void LoadDepotWagonsFromProject()
    {
        var wagons = (_settings.CurrentProject?.Wagons ?? _settings.Project?.Wagons) ?? new List<Wagon>();
        foreach (var w in wagons)
            DepotWagons.Add(w);
    }

    public void AttachWagon(Locomotive loco, Wagon wagon)
    {
        if (loco == null || wagon == null)
            return;

        if (DepotWagons.Contains(wagon))
            DepotWagons.Remove(wagon);

        if (!loco.Wagons.Contains(wagon))
            loco.Wagons.Add(wagon);

        OnPropertyChanged(nameof(TopVehiclesView));
    }

    public void AttachWagonToLocoRecord(LocoRecord record, Wagon wagon)
    {
        if (record == null || wagon == null)
            return;

        // Wagon attachments live on runtime Locomotive instances. Bridge LocoRecord -> Locomotive by stable key.
        var key = !string.IsNullOrWhiteSpace(record.Id) ? record.Id : record.Address.ToString();
        var loco = Locomotives.FirstOrDefault(l =>
            string.Equals(l.Code, key, System.StringComparison.OrdinalIgnoreCase));

        if (loco == null)
        {
            loco = new Locomotive(key, record.Name) { IconName = record.IconName ?? string.Empty };
            Locomotives.Add(loco);
        }

        if (loco == null)
            return;

        AttachWagon(loco, wagon);
    }

    public void DetachLastWagon(Locomotive loco)
    {
        if (loco == null)
            return;

        if (loco.Wagons.Count == 0)
            return;

        var wagon = loco.Wagons[^1];
        DetachWagon(loco, wagon);
    }

    public void DetachAllWagons(Locomotive loco)
    {
        if (loco == null)
            return;

        if (loco.Wagons.Count == 0)
            return;

        // copy first, because DetachWagon mutates the collection
        var wagons = loco.Wagons.ToArray();
        foreach (var w in wagons)
            DetachWagon(loco, w);
    }

    [RelayCommand]
    private void DetachLastWagonFrom(object? parameter)
    {
        if (parameter is not Locomotive loco)
            return;

        DetachLastWagon(loco);
    }

    [RelayCommand]
    private void DetachAllWagonsFrom(object? parameter)
    {
        if (parameter is not Locomotive loco)
            return;

        DetachAllWagons(loco);
    }

    public void DetachWagon(Locomotive loco, Wagon wagon)
    {
        if (loco == null || wagon == null)
            return;

        if (loco.Wagons.Contains(wagon))
            loco.Wagons.Remove(wagon);

        if (!DepotWagons.Contains(wagon))
            DepotWagons.Add(wagon);

        OnPropertyChanged(nameof(TopVehiclesView));
    }

    [RelayCommand]
    private void ReturnWagonToDepot(object? parameter)
    {
        if (parameter is not Wagon wagon)
            return;

        foreach (var loco in Locomotives)
        {
            if (!loco.Wagons.Contains(wagon))
                continue;

            DetachWagon(loco, wagon);
            return;
        }
    }
}

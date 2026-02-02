using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.Services;

namespace TrackFlow.ViewModels.SmartStrips;

public partial class SmartStripsViewModel : ObservableObject
{
    public ObservableCollection<LocoRecord> ProjectLocomotives { get; } = new();
    public ObservableCollection<Locomotive> Locomotives { get; } = new();
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

        _settings.ProjectChanged += RefreshFromProject;
        RefreshFromProject();
    }

    private void RefreshFromProject()
    {
        ProjectLocomotives.Clear();

        var list = _settings.Project?.Locomotives;
        if (list == null || list.Count == 0)
            return;

        foreach (var loco in list)
            ProjectLocomotives.Add(loco);
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

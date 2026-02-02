using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models;

namespace TrackFlow.ViewModels.SmartStrips;

public partial class SmartStripsViewModel : ObservableObject
{
    public ObservableCollection<Locomotive> Locomotives { get; } = new();
    public ObservableCollection<Wagon> DepotWagons { get; } = new();

    public IEnumerable<Locomotive> TopVehiclesView => Locomotives
        .OrderByDescending(l => l.HasWagons)
        .ThenBy(l => l.Name);

    public SmartStripsViewModel()
    {
        // seed demo data (can be deleted once wired to project data)
        // Use actual icon files present in Assets/LocoIcons
        var l1 = new Locomotive("L001", "Brejlovec") { IconName = "zsr_350.png" };
        var l2 = new Locomotive("L002", "Okuliarnik") { IconName = "zsr_750z.png" };
        var l3 = new Locomotive("L003", "Zamraèená") { IconName = "zsr_751zc.png" };
        l1.Wagons.Add(new Wagon("W101", "Eaos"));
        l1.Wagons.Add(new Wagon("W102", "Raj"));

        Locomotives.Add(l1);
        Locomotives.Add(l2);
        Locomotives.Add(l3);

        DepotWagons.Add(new Wagon("W201", "Uacs"));
        DepotWagons.Add(new Wagon("W202", "Falls"));
        DepotWagons.Add(new Wagon("W203", "Zas"));

        Locomotives.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TopVehiclesView));
        DepotWagons.CollectionChanged += (_, _) => { };
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

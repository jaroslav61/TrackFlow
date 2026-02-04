using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TrackFlow.Models;

public partial class Locomotive : Vehicle
{
    public Locomotive(string code, string name) : base(code, name)
    {
        IsForward = true;
    }

    public ObservableCollection<Wagon> Wagons { get; } = new();

    public bool HasWagons => Wagons.Count > 0;

    // Icon file name (e.g. '754.png')
    public string IconName { get; set; } = string.Empty;

    public bool IsDirectionSelected => IsForward || IsReverse;

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private bool isForward;

    [ObservableProperty]
    private bool isReverse;

    [ObservableProperty]
    private int targetSpeed; // Toto vytvorÌ vlastnosù TargetSpeed

    
    partial void OnIsForwardChanged(bool value)
    {
        if (value && IsReverse)
        {
            // use property to ensure notifications and any side-effects
            IsReverse = false;
        }

        if (value)
        {
            // when direction changes to forward, immediately reset target speed
            TargetSpeed = 0;
        }
    }

    partial void OnIsReverseChanged(bool value)
    {
        if (value && IsForward)
        {
            // use property to ensure notifications and any side-effects
            IsForward = false;
        }

        if (value)
        {
            // when direction changes to reverse, immediately reset target speed
            TargetSpeed = 0;
        }
    }

    partial void OnIsActiveChanged(bool value)
    {
        // when locomotive activation state changes (e.g. start/stop), reset target speed
        // This ensures slider jumps to 0 on activation changes as required.
        TargetSpeed = 0;
    }

    // remove direction properties if not used
}

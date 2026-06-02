using CommunityToolkit.Mvvm.ComponentModel;

namespace TrackFlow.Models;

public sealed partial class Wagon : Vehicle
{
    public Wagon(string code, string name) : base(code, name)
    {
    }

    // Legacy weight property (kept for compatibility)
    public double Weight { get; set; }

    // Length over buffers in meters (or the unit used by the app)
    public double LengthOverBuffers { get; set; }

    // Type of wagon (e.g. "Boxcar", "Flat", "Tank" etc.)
    public string VagonType { get; set; } = string.Empty;

    // Icon file name (e.g. 'wagon_box.png') - used by the same icon registry/converter as locomotives
    public string IconName { get; set; } = string.Empty;

    // Optional description / notes about the wagon
    public string Description { get; set; } = string.Empty;

    // Tare (empty) weight
    [ObservableProperty]
    private double tareWeight;

    // Cargo weight
    [ObservableProperty]
    private double cargoWeight;

    // Active flag for UI binding (e.g., opacity)
    [ObservableProperty]
    private bool isActive;

    // Computed total weight
    public double TotalWeight => TareWeight + CargoWeight;

    // Notify TotalWeight change when components change
    partial void OnTareWeightChanged(double value)
    {
        OnPropertyChanged(nameof(TotalWeight));
    }

    partial void OnCargoWeightChanged(double value)
    {
        OnPropertyChanged(nameof(TotalWeight));
    }
}


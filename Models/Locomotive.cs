using System.Collections.ObjectModel;

namespace TrackFlow.Models;

public sealed class Locomotive : Vehicle
{
    public Locomotive(string code, string name) : base(code, name)
    {
    }

    public ObservableCollection<Wagon> Wagons { get; } = new();

    public bool HasWagons => Wagons.Count > 0;

    // Icon file name (e.g. '754.png')
    public string IconName { get; set; } = string.Empty;
}

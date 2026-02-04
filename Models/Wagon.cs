namespace TrackFlow.Models;

public sealed class Wagon : Vehicle
{
    public Wagon(string code, string name) : base(code, name)
    {
    }

    // Weight in tonnes (or the unit used by the app)
    public double Weight { get; set; }

    // Length over buffers in meters (or the unit used by the app)
    public double LengthOverBuffers { get; set; }

    // Type of wagon (e.g. "Boxcar", "Flat", "Tank" etc.)
    public string VagonType { get; set; } = string.Empty;

    // Icon file name (e.g. 'wagon_box.png') - used by the same icon registry/converter as locomotives
    public string IconName { get; set; } = string.Empty;

    // Optional description / notes about the wagon
    public string Description { get; set; } = string.Empty;
}

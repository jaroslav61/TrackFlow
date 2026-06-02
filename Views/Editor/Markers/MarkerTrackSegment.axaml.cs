using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerTrackSegment : UserControl, IMarkerAngle
{
    public static readonly StyledProperty<int> AngleProperty =
        AvaloniaProperty.Register<MarkerTrackSegment, int>(nameof(Angle), 0);

    // Registrovať class handler staticky – nie v konštruktore (inak sa pridáva pri každej inštancii)
    static MarkerTrackSegment()
    {
        AngleProperty.Changed.AddClassHandler<MarkerTrackSegment>((s, e) => 
        {
            if (e.NewValue is int a) s.UpdateGeometry(a);
        });
    }

    public int Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    public MarkerTrackSegment()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public void SetAngle(int angle)
    {
        Angle = ((angle % 360) + 360) % 360;
        UpdateGeometry(Angle);
    }

    private void UpdateGeometry(int angle)
    {
        // Normalize to 0..359
        angle = ((angle % 360) + 360) % 360;

        // Use the eight steps mapping as specified by the user
        // 0 -> (0,12)-(24,12)
        // 45 -> (0,0)-(24,24) - skrátené o 0.3px na oboch koncoch pre diagonálu
        // 90 -> (12,0)-(12,24)
        // 135 -> (24,0)-(0,24) - skrátené o 0.3px na oboch koncoch pre diagonálu
        // 180 -> same as 0
        // 225 -> same as 45
        // 270 -> same as 90
        // 315 -> same as 135

        var line = this.FindControl<Line>("TrackLine");
        if (line == null) return;

        switch (angle)
        {
            case 0:
            case 180:
            case 360:
                line.StartPoint = new Point(0,12);
                line.EndPoint   = new Point(24,12);
                break;
            case 45:
            case 225:
                // Diagonála: skrátené o 0.3px na oboch koncoch
                line.StartPoint = new Point(0.2, 0.2);
                line.EndPoint   = new Point(23.8, 23.8);
                break;
            case 90:
            case 270:
                line.StartPoint = new Point(12,0);
                line.EndPoint   = new Point(12,24);
                break;
            case 135:
            case 315:
                // Diagonála: skrátené o 0.3px na oboch koncoch
                line.StartPoint = new Point(23.8, 0.2);
                line.EndPoint   = new Point(0.2, 23.8);
                break;
            default:
                // fallback to horizontal
                line.StartPoint = new Point(0,12);
                line.EndPoint   = new Point(24,12);
                break;
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerBumper : UserControl, IMarkerAngle
{
    public static readonly StyledProperty<int> AngleProperty =
        AvaloniaProperty.Register<MarkerBumper, int>(nameof(Angle));

    // Registrovať class handler staticky
    static MarkerBumper()
    {
        AngleProperty.Changed.AddClassHandler<MarkerBumper>((s, e) =>
        {
            if (e.NewValue is int a) s.UpdateGeometry(a);
        });
    }

    public int Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    public MarkerBumper()
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

        var trackLine = this.FindControl<Line>("TrackLine");
        var barrierLine = this.FindControl<Line>("BarrierLine");
        if (trackLine == null || barrierLine == null) return;

        switch (angle)
        {
            case 0:
            case 360:
                // Line1: (12,12),(12,24); Line2:(6,12),(16,12)
                trackLine.StartPoint = new Point(12, 12);
                trackLine.EndPoint = new Point(12, 24);
                barrierLine.StartPoint = new Point(6, 12);
                barrierLine.EndPoint = new Point(18, 12);
                break;
            case 45:
                // Line1: (12,12),(0,24); Line2:(8,9),(15,15)
                trackLine.StartPoint = new Point(12, 12);
                trackLine.EndPoint = new Point(0, 24);
                barrierLine.StartPoint = new Point(7, 7);
                barrierLine.EndPoint = new Point(17, 17);
                break;
            case 90:
                // Line1: (0,12),(12,12); Line2: (12,9),(12,15)
                trackLine.StartPoint = new Point(0, 12);
                trackLine.EndPoint = new Point(12, 12);
                barrierLine.StartPoint = new Point(12, 6);
                barrierLine.EndPoint = new Point(12, 18);
                break;
            case 135:
                // Line1: (0,0),(12,12); Line2: (15,9),(8,12)
                trackLine.StartPoint = new Point(0, 0);
                trackLine.EndPoint = new Point(12, 12);
                barrierLine.StartPoint = new Point(17, 7);
                barrierLine.EndPoint = new Point(7, 17);
                break;
            case 180:
                // Line1: (12,0),(12,12); Line2: (8,12),(15,12)
                trackLine.StartPoint = new Point(12, 0);
                trackLine.EndPoint = new Point(12, 12);
                barrierLine.StartPoint = new Point(6, 12);
                barrierLine.EndPoint = new Point(18, 12);
                break;
            case 225:
                // Line1: (24,0),(12,12); Line2: (8,9),(15,15)
                trackLine.StartPoint = new Point(24, 0);
                trackLine.EndPoint = new Point(12, 12);
                barrierLine.StartPoint = new Point(7, 8);
                barrierLine.EndPoint = new Point(17, 17);
                break;
            case 270:
                // Line1: (12,12),(24,12); Line2: (12,9),(12,15)
                trackLine.StartPoint = new Point(12, 12);
                trackLine.EndPoint = new Point(24, 12);
                barrierLine.StartPoint = new Point(12, 6);
                barrierLine.EndPoint = new Point(12, 18);
                break;
            case 315:
                // Line1: (12,12),(24,24); Line2: (15,9),(8,15)
                trackLine.StartPoint = new Point(12, 12);
                trackLine.EndPoint = new Point(24, 24);
                barrierLine.StartPoint = new Point(17, 8);
                barrierLine.EndPoint = new Point(7, 17);
                break;
            default:
                // fallback to 0°
                trackLine.StartPoint = new Point(12, 12);
                trackLine.EndPoint = new Point(12, 24);
                barrierLine.StartPoint = new Point(6, 12);
                barrierLine.EndPoint = new Point(16, 12);
                break;
        }
    }
}

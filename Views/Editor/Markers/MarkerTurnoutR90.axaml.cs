using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerTurnoutR90 : UserControl, IMarkerAngle
{
    public MarkerTurnoutR90()
    {
        AvaloniaXamlLoader.Load(this);
        UpdateGeometry(0);
    }

    public void SetAngle(int angle)
    {
        angle = ((angle % 360) + 360) % 360;
        UpdateGeometry(angle);
    }

    private void UpdateGeometry(int angle)
    {
        var line = this.FindControl<Line>("StraightLine");
        var path = this.FindControl<Path>("CurvePath");
        var lineOutline = this.FindControl<Line>("StraightLineOutline");
        var pathOutline = this.FindControl<Path>("CurvePathOutline");
        if (line == null || path == null) return;

        // Globálne nastavenia pre ostrosť a čistotu
        this.UseLayoutRounding = false;
        line.UseLayoutRounding = false;
        path.UseLayoutRounding = false;

        line.StrokeThickness = 2;
        path.StrokeThickness = 2;
        line.StrokeLineCap = PenLineCap.Flat;
        path.StrokeLineCap = PenLineCap.Flat;
        
        // Nastavenie outline elementov
        if (lineOutline != null)
        {
            lineOutline.UseLayoutRounding = false;
            lineOutline.StrokeThickness = 3.5;
            lineOutline.StrokeLineCap = PenLineCap.Flat;
            lineOutline.ZIndex = 9;
        }
        if (pathOutline != null)
        {
            pathOutline.UseLayoutRounding = false;
            pathOutline.StrokeThickness = 3.5;
            pathOutline.StrokeLineCap = PenLineCap.Flat;
            RenderOptions.SetEdgeMode(pathOutline, EdgeMode.Antialias);
            pathOutline.ZIndex = 0;
        }
        
        // Nastavenie ZIndex pre farebné čiary
        line.ZIndex = 10;
        path.ZIndex = 1;

        RenderOptions.SetEdgeMode(path, EdgeMode.Antialias);

        // Súradnice (z=0.2, m=12.0, f=23.8)
        double z = 0.2;
        double m = 12.0;
        double f = 23.8;
        double radius = 12.0;  // Kratší oblúk pre 90° výhybku

        // Základná geometria: Priama (12,24)-(12,0), Oblúk (12,24)-(24,12)
        // Logika: (LineStart, LineEnd, CurveStart, CurveEnd, SweepDirection)
        (Point lS, Point lE, Point cS, Point cE, SweepDirection sweep) = angle switch
        {
            // 0°: Track (12,24)→(12,0), Arc (12,24)→(24,12)
            0   => (new Point(m, f), new Point(m, z), new Point(m, f), new Point(f, m), SweepDirection.Clockwise),
            
            // 90°: Track (0,12)→(12,24), Arc (0,12)→(12,24)
            90  => (new Point(z, m), new Point(f, m), new Point(z, m), new Point(m, f), SweepDirection.Clockwise),
            
            // 180°: Track (12,0)→(12,24), Arc (12,0)→(0,12)
            180 => (new Point(m, z), new Point(m, f), new Point(m, z), new Point(z, m), SweepDirection.Clockwise),
            
            // 270°: Track (24,12)→(0,12), Arc (24,12)→(12,24)
            270 => (new Point(f, m), new Point(z, m), new Point(f, m), new Point(m, z), SweepDirection.Clockwise),
            
            _   => (new Point(m, f), new Point(m, z), new Point(m, f), new Point(f, m), SweepDirection.Clockwise),
        };

        // 1. Nastavenie priamej koľaje
        line.StartPoint = lS;
        line.EndPoint = lE;
        if (lineOutline != null)
        {
            lineOutline.StartPoint = lS;
            lineOutline.EndPoint = lE;
        }

        // 2. Nastavenie oblúka
        path.Data = CreateArcGeometry(cS, cE, radius, sweep);
        if (pathOutline != null)
        {
            pathOutline.Data = CreateArcGeometry(cS, cE, radius, sweep);
        }
    }

    private PathGeometry CreateArcGeometry(Point start, Point end, double r, SweepDirection sweep)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments!.Add(new ArcSegment { Point = end, Size = new Size(r, r), SweepDirection = sweep, IsLargeArc = false });
        geometry.Figures!.Add(figure);
        return geometry;
    }
}


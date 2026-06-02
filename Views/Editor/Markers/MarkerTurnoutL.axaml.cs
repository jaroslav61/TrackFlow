using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerTurnoutL : UserControl, IMarkerAngle
{
    public MarkerTurnoutL()
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
            lineOutline.StrokeThickness = 3.1;
            lineOutline.StrokeLineCap = PenLineCap.Flat;
            // Aktívna vetva (priama) - outline je tesne pod farebnou čiarou
            lineOutline.ZIndex = 9;
        }
        if (pathOutline != null)
        {
            pathOutline.UseLayoutRounding = false;
            pathOutline.StrokeThickness = 3.1;
            pathOutline.StrokeLineCap = PenLineCap.Flat;
            RenderOptions.SetEdgeMode(pathOutline, EdgeMode.Antialias);
            // Neaktívna vetva (oblúk) - outline je úplne vzadu
            pathOutline.ZIndex = 0;
        }
        
        // Nastavenie ZIndex pre farebné čiary
        // Priama koľaj je štandardne aktívna (vpredu)
        line.ZIndex = 10;
        path.ZIndex = 1;
        
        RenderOptions.SetEdgeMode(path, EdgeMode.Antialias);

        // Súradnice (z=0.2, m=12.0, f=23.8)
        double z = 0.2;
        double m = 12.0;
        double f = 23.8;
        double radius = 36.0;

        // Logika: (LineStart, LineEnd, CurveStart, CurveEnd, SweepDirection)
        (Point lS, Point lE, Point cS, Point cE, SweepDirection sweep) = angle switch
        {
            // 0°: Vstup dole (stred) -> Priama: Hore (stred), Oblúk: Vľavo hore
            0   => (new Point(m, f), new Point(m, z), new Point(m, f), new Point(z, z), SweepDirection.CounterClockwise),
            
            // 45°: Vstup vľavo dole -> Priama: Vpravo hore, Oblúk: Hore (stred)
            45  => (new Point(z, f), new Point(f, z), new Point(z, f), new Point(m, z), SweepDirection.CounterClockwise),
            
            // 90°: Vstup vľavo (stred) -> Priama: Vpravo (stred), Oblúk: Vpravo hore
            90  => (new Point(z, m), new Point(f, m), new Point(z, m), new Point(f, z), SweepDirection.CounterClockwise),
            
            // 135°: Vstup vľavo hore -> Priama: Vpravo dole, Oblúk: Vpravo (stred)
            135 => (new Point(z, z), new Point(f, f), new Point(z, z), new Point(f, m), SweepDirection.CounterClockwise),
            
            // 180°: Vstup hore (stred) -> Priama: Dole (stred), Oblúk: Vpravo dole
            180 => (new Point(m, z), new Point(m, f), new Point(m, z), new Point(f, f), SweepDirection.CounterClockwise),
            
            // 225°: Vstup vpravo hore -> Priama: Vľavo dole, Oblúk: Dole (stred)
            225 => (new Point(f, z), new Point(z, f), new Point(f, z), new Point(m, f), SweepDirection.CounterClockwise),
            
            // 270°: Vstup vpravo (stred) -> Priama: Vľavo (stred), Oblúk: Vľavo dole
            270 => (new Point(f, m), new Point(z, m), new Point(f, m), new Point(z, f), SweepDirection.CounterClockwise),
            
            // 315°: Vstup vpravo dole -> Priama: Vľavo hore, Oblúk: Vľavo (stred)
            315 => (new Point(f, f), new Point(z, z), new Point(f, f), new Point(z, m), SweepDirection.CounterClockwise),
            
            _   => (new Point(m, f), new Point(m, z), new Point(m, f), new Point(z, z), SweepDirection.CounterClockwise),
        };

        // 1. Nastavenie priamej koľaje
        line.StartPoint = lS;
        line.EndPoint = lE;
        
        // Outline pre priamu koľaj
        if (lineOutline != null)
        {
            lineOutline.StartPoint = lS;
            lineOutline.EndPoint = lE;
        }

        // 2. Nastavenie oblúka
        var arcGeometry = CreateArcGeometry(cS, cE, radius, sweep);
        path.Data = arcGeometry;
        
        // Outline pre oblúk
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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerTurnout3W : UserControl, IMarkerAngle
{
    public MarkerTurnout3W()
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
        var arcR = this.FindControl<Path>("ArcRPath");
        var arcL = this.FindControl<Path>("ArcLPath");
        var line = this.FindControl<Line>("CenterLine");
        var arcROutline = this.FindControl<Path>("ArcRPathOutline");
        var arcLOutline = this.FindControl<Path>("ArcLPathOutline");
        var lineOutline = this.FindControl<Line>("CenterLineOutline");
        
        if (arcR == null || line == null || arcL == null) return;
        
        // Globálne nastavenia pre hladké vykresľovanie
        this.UseLayoutRounding = false;
        arcR.StrokeThickness = 2;
        arcL.StrokeThickness = 2;
        line.StrokeThickness = 2;
        
        // Nastavenie outline elementov
        if (arcROutline != null)
        {
            arcROutline.StrokeThickness = 3.5;
            RenderOptions.SetEdgeMode(arcROutline, EdgeMode.Antialias);
            arcROutline.ZIndex = 0;
        }
        if (arcLOutline != null)
        {
            arcLOutline.StrokeThickness = 3.5;
            RenderOptions.SetEdgeMode(arcLOutline, EdgeMode.Antialias);
            arcLOutline.ZIndex = 0;
        }
        if (lineOutline != null)
        {
            lineOutline.StrokeThickness = 3.5;
            lineOutline.ZIndex = 9;
        }
        
        // Nastavenie ZIndex pre farebné čiary - priama je aktívna
        arcR.ZIndex = 1;
        arcL.ZIndex = 1;
        line.ZIndex = 10;
        
        RenderOptions.SetEdgeMode(arcR, EdgeMode.Antialias);
        RenderOptions.SetEdgeMode(arcL, EdgeMode.Antialias);

        // Zjednotené súradnice (z = 0.2, m = 12.0, f = 23.8)
        double z = 0.2;
        double m = 12.0;
        double f = 23.8;
        double radius = 36.0;

        // Logika: (Vstupný bod, Koniec Vpravo, Smer R, Koniec Vľavo, Smer L, Koniec Stred)
        (Point s, Point eR, SweepDirection swR, Point eL, SweepDirection swL, Point eLine) = angle switch
        {
            // 0°: Vstup dole (stred) -> Vetvy: Hore-Vpravo, Hore-Vľavo, Hore-Stred
            0   => (new Point(m, f), new Point(f, z), SweepDirection.Clockwise, new Point(z, z), SweepDirection.CounterClockwise, new Point(m, z)),
            
            // 45°: Vstup vľavo dole -> Vetvy: Vpravo-Stred, Hore-Stred, Hore-Vpravo
            45  => (new Point(z, f), new Point(f, m), SweepDirection.Clockwise, new Point(m, z), SweepDirection.CounterClockwise, new Point(f, z)),
            
            // 90°: Vstup vľavo (stred) -> Vetvy: Vpravo-Dole, Vpravo-Hore, Vpravo-Stred
            90  => (new Point(z, m), new Point(f, f), SweepDirection.Clockwise, new Point(f, z), SweepDirection.CounterClockwise, new Point(f, m)),
            
            // 135°: Vstup vľavo hore -> Vetvy: Dole-Stred, Vpravo-Stred, Vpravo-Dole
            135 => (new Point(z, z), new Point(m, f), SweepDirection.Clockwise, new Point(f, m), SweepDirection.CounterClockwise, new Point(f, f)),
            
            // 180°: Vstup hore (stred) -> Vetvy: Dole-Vľavo, Dole-Vpravo, Dole-Stred
            180 => (new Point(m, z), new Point(z, f), SweepDirection.Clockwise, new Point(f, f), SweepDirection.CounterClockwise, new Point(m, f)),
            
            // 225°: Vstup vpravo hore -> Vetvy: Vľavo-Stred, Dole-Stred, Vľavo-Dole
            225 => (new Point(f, z), new Point(z, m), SweepDirection.Clockwise, new Point(m, f), SweepDirection.CounterClockwise, new Point(z, f)),
            
            // 270°: Vstup vpravo (stred) -> Vetvy: Vľavo-Hore, Vľavo-Dole, Vľavo-Stred
            270 => (new Point(f, m), new Point(z, z), SweepDirection.Clockwise, new Point(z, f), SweepDirection.CounterClockwise, new Point(z, m)),
            
            // 315°: Vstup vpravo dole -> Vetvy: Hore-Stred, Vľavo-Stred, Hore-Vľavo
            315 => (new Point(f, f), new Point(m, z), SweepDirection.Clockwise, new Point(z, m), SweepDirection.CounterClockwise, new Point(z, z)),
            
            _   => (new Point(m, f), new Point(f, z), SweepDirection.Clockwise, new Point(z, z), SweepDirection.CounterClockwise, new Point(m, z)),
        };

        // 1. Priamy smer (CenterLine)
        line.StartPoint = s;
        line.EndPoint = eLine;
        if (lineOutline != null)
        {
            lineOutline.StartPoint = s;
            lineOutline.EndPoint = eLine;
        }

        // 2. Pravý a Ľavý oblúk
        arcR.Data = CreateArcGeometry(s, eR, radius, swR);
        arcL.Data = CreateArcGeometry(s, eL, radius, swL);
        
        // Outline geometria
        if (arcROutline != null)
            arcROutline.Data = CreateArcGeometry(s, eR, radius, swR);
        if (arcLOutline != null)
            arcLOutline.Data = CreateArcGeometry(s, eL, radius, swL);
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
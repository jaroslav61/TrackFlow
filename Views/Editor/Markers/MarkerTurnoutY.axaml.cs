using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerTurnoutY : UserControl, IMarkerAngle
{
    public MarkerTurnoutY()
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
        var arc1 = this.FindControl<Path>("Arc1Path");
        var arc2 = this.FindControl<Path>("Arc2Path");
        var arc1Outline = this.FindControl<Path>("Arc1PathOutline");
        var arc2Outline = this.FindControl<Path>("Arc2PathOutline");
        if (arc1 == null || arc2 == null) return;
        
        // Globálne nastavenia pre ostrosť a rendering
        this.UseLayoutRounding = false;
        arc1.StrokeThickness = 2;
        arc2.StrokeThickness = 2;
        arc1.StrokeLineCap = PenLineCap.Flat;
        arc2.StrokeLineCap = PenLineCap.Flat;
        
        // Nastavenie outline elementov
        if (arc1Outline != null)
        {
            arc1Outline.StrokeThickness = 3.5;
            arc1Outline.StrokeLineCap = PenLineCap.Flat;
            RenderOptions.SetEdgeMode(arc1Outline, EdgeMode.Antialias);
            arc1Outline.ZIndex = 0;
        }
        if (arc2Outline != null)
        {
            arc2Outline.StrokeThickness = 3.5;
            arc2Outline.StrokeLineCap = PenLineCap.Flat;
            RenderOptions.SetEdgeMode(arc2Outline, EdgeMode.Antialias);
            arc2Outline.ZIndex = 9;
        }
        
        // Nastavenie ZIndex pre farebné čiary
        arc1.ZIndex = 1;
        arc2.ZIndex = 10;
        
        RenderOptions.SetEdgeMode(arc1, EdgeMode.Antialias);
        RenderOptions.SetEdgeMode(arc2, EdgeMode.Antialias);

        // Súradnice (z=0.2, m=12.0, f=23.8)
        double z = 0.2;
        double m = 12.0;
        double f = 23.8;
        double radius = 36.0;

        // Logika: (Spoločný ŠTART, Cieľ1, Smer1, Cieľ2, Smer2)
        (Point s, Point e1, SweepDirection sw1, Point e2, SweepDirection sw2) = angle switch
        {
            // 0°: Vstup dole (stred) -> Odbočky: Vpravo hore a Vľavo hore
            0   => (new Point(m, f), new Point(f, z), SweepDirection.Clockwise, new Point(z, z), SweepDirection.CounterClockwise),
            
            // 45°: Vstup vľavo dole -> Odbočky: Vpravo (stred) a Hore (stred)
            45  => (new Point(z, f), new Point(f, m), SweepDirection.Clockwise, new Point(m, z), SweepDirection.CounterClockwise),
            
            // 90°: Vstup vľavo (stred) -> Odbočky: Vpravo hore a Vpravo dole
            90  => (new Point(z, m), new Point(f, z), SweepDirection.CounterClockwise, new Point(f, f), SweepDirection.Clockwise),
            
            // 135°: Vstup vľavo hore -> Odbočky: Dole (stred) a Vpravo (stred)
            135 => (new Point(z, z), new Point(m, f), SweepDirection.Clockwise, new Point(f, m), SweepDirection.CounterClockwise),
            
            // 180°: Vstup hore (stred) -> Odbočky: Vľavo dole a Vpravo dole
            180 => (new Point(m, z), new Point(z, f), SweepDirection.Clockwise, new Point(f, f), SweepDirection.CounterClockwise),
            
            // 225°: Vstup vpravo hore -> Odbočky: Vľavo (stred) a Dole (stred)
            225 => (new Point(f, z), new Point(z, m), SweepDirection.Clockwise, new Point(m, f), SweepDirection.CounterClockwise),
            
            // 270°: Vstup vpravo (stred) -> Odbočky: Vľavo hore a Vľavo dole
            270 => (new Point(f, m), new Point(z, z), SweepDirection.Clockwise, new Point(z, f), SweepDirection.CounterClockwise),
            
            // 315°: Vstup vpravo dole -> Odbočky: Vľavo (stred) a Hore (stred)
            315 => (new Point(f, f), new Point(z, m), SweepDirection.CounterClockwise, new Point(m, z), SweepDirection.Clockwise),
            
            _   => (new Point(m, f), new Point(f, z), SweepDirection.Clockwise, new Point(z, z), SweepDirection.CounterClockwise),
        };

        // Priradenie geometrie oblúkov
        arc1.Data = CreateArcGeometry(s, e1, radius, sw1);
        arc2.Data = CreateArcGeometry(s, e2, radius, sw2);
        
        // Outline geometria
        if (arc1Outline != null)
            arc1Outline.Data = CreateArcGeometry(s, e1, radius, sw1);
        if (arc2Outline != null)
            arc2Outline.Data = CreateArcGeometry(s, e2, radius, sw2);
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
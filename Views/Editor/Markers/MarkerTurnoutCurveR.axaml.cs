using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerTurnoutCurveR : UserControl, IMarkerAngle
{
    public MarkerTurnoutCurveR()
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
        
        // Globálne nastavenia
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
            arc1Outline.ZIndex = 9;
        }
        if (arc2Outline != null)
        {
            arc2Outline.StrokeThickness = 3.5;
            arc2Outline.StrokeLineCap = PenLineCap.Flat;
            RenderOptions.SetEdgeMode(arc2Outline, EdgeMode.Antialias);
            arc2Outline.ZIndex = 0;
        }
        
        // Nastavenie ZIndex pre farebné čiary
        arc1.ZIndex = 10;
        arc2.ZIndex = 1;
        
        RenderOptions.SetEdgeMode(arc1, EdgeMode.Antialias);
        RenderOptions.SetEdgeMode(arc2, EdgeMode.Antialias);

        // Zjednotené súradnice (z = 0.2, m = 12.0, f = 23.8)
        double z = 0.2;
        double m = 12.0;
        double f = 23.8;
        
        // Polomery (zhodné s TurnoutCurveL)
        double r1 = 36.0; // Plochší oblúk
        double r2 = 18.0; // Strmší oblúk

        // Logika: Spoločný ŠTART (s) a dva rôzne CIELE (e1, e2) vpravo (v smere jazdy)
        (Point s, Point e1, Point e2, SweepDirection sw) = angle switch
        {
            // 0°: Vstup dole (stred) -> Ciele: Hore (vpravo) a Vpravo (stred)
            0 or 360 => (new Point(m, f), new Point(f, z), new Point(f, m), SweepDirection.Clockwise),
            
            // 45°: Vstup vľavo dole -> Ciele: Vpravo (stred) a Vpravo (dole)
            45 => (new Point(z, f), new Point(f, m), new Point(f, f), SweepDirection.Clockwise),
            
            // 90°: Vstup vľavo (stred) -> Ciele: Vpravo (dole) a Dole (stred)
            90 => (new Point(z, m), new Point(f, f), new Point(m, f), SweepDirection.Clockwise),
            
            // 135°: Vstup vľavo hore -> Ciele: Dole (stred) a Dole (vľavo)
            135 => (new Point(z, z), new Point(m, f), new Point(z, f), SweepDirection.Clockwise),
            
            // 180°: Vstup hore (stred) -> Ciele: Dole (vľavo) a Vľavo (stred)
            180 => (new Point(m, z), new Point(z, f), new Point(z, m), SweepDirection.Clockwise),
            
            // 225°: Vstup vpravo hore -> Ciele: Vľavo (stred) a Vľavo (hore)
            225 => (new Point(f, z), new Point(z, m), new Point(z, z), SweepDirection.Clockwise),
            
            // 270°: Vstup vpravo (stred) -> Ciele: Vľavo (hore) a Hore (stred)
            270 => (new Point(f, m), new Point(z, z), new Point(m, z), SweepDirection.Clockwise),
            
            // 315°: Vstup vpravo dole -> Ciele: Hore (stred) a Hore (vpravo)
            315 => (new Point(f, f), new Point(m, z), new Point(f, z), SweepDirection.Clockwise),
            
            _ => (new Point(m, f), new Point(f, z), new Point(f, m), SweepDirection.Clockwise),
        };

        // Arc 1 (Plochší) a Arc 2 (Strmší)
        arc1.Data = CreateArcGeometry(s, e1, r1, sw);
        arc2.Data = CreateArcGeometry(s, e2, r2, sw);
        
        // Outline geometria
        if (arc1Outline != null)
            arc1Outline.Data = CreateArcGeometry(s, e1, r1, sw);
        if (arc2Outline != null)
            arc2Outline.Data = CreateArcGeometry(s, e2, r2, sw);
    }

    private PathGeometry CreateArcGeometry(Point start, Point end, double r, SweepDirection sweep)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments!.Add(new ArcSegment 
        { 
            Point = end, 
            Size = new Size(r, r), 
            SweepDirection = sweep, 
            IsLargeArc = false 
        });
        geometry.Figures!.Add(figure);
        return geometry;
    }
}
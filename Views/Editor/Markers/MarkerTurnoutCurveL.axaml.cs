using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerTurnoutCurveL : UserControl, IMarkerAngle
{
    public MarkerTurnoutCurveL()
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
        
        // Globálne nastavenia pre čisté hrany
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

        // Definícia bodov (z = 0.2, m = 12.0, f = 23.8)
        double z = 0.2;
        double m = 12.0;
        double f = 23.8;
        
        // Dva polomery pre oblúkovú výhybku
        double r1 = 36.0; // Vonkajší (plochší) oblúk
        double r2 = 18.0; // Vnútorný (ostrejší) oblúk

        // Logika: Dva rôzne štarty (s1, s2) končiace v spoločnom bode (e)
        (Point s1, Point s2, Point e, SweepDirection sw) = angle switch
        {
            // 0°: Vstup vľavo hore/stred -> Výstup dole (stred)
            0 or 360 => (new Point(z, z), new Point(z, m), new Point(m, f), SweepDirection.Clockwise),
            
            // 45°: Vstup hore stred/vľavo hore -> Výstup vľavo (stred)
            45 => (new Point(m, z), new Point(z, z), new Point(z, f), SweepDirection.Clockwise),
            
            // 90°: Vstup vpravo hore/stred hore -> Výstup vľavo (stred)
            90 => (new Point(f, z), new Point(m, z), new Point(z, m), SweepDirection.Clockwise),
            
            // 135°: Vstup vpravo stred/vpravo hore -> Výstup hore (stred)
            135 => (new Point(f, m), new Point(f, z), new Point(z, z), SweepDirection.Clockwise),
            
            // 180°: Vstup vpravo dole/vpravo stred -> Výstup hore (stred)
            180 => (new Point(f, f), new Point(f, m), new Point(m, z), SweepDirection.Clockwise),
            
            // 225°: Vstup dole stred/vpravo dole -> Výstup vpravo (stred)
            225 => (new Point(m, f), new Point(f, f), new Point(f, z), SweepDirection.Clockwise),
            
            // 270°: Vstup vľavo dole/dole stred -> Výstup vpravo (stred)
            270 => (new Point(z, f), new Point(m, f), new Point(f, m), SweepDirection.Clockwise),
            
            // 315°: Vstup vľavo stred/vľavo dole -> Výstup dole (stred)
            315 => (new Point(z, m), new Point(z, f), new Point(f, f), SweepDirection.Clockwise),
            
            _ => (new Point(z, z), new Point(z, m), new Point(m, f), SweepDirection.Clockwise),
        };

        // Priradenie geometrie s príslušným polomerom
        arc1.Data = CreateArcGeometry(s1, e, r1, sw);
        arc2.Data = CreateArcGeometry(s2, e, r2, sw);
        
        // Outline geometria
        if (arc1Outline != null)
            arc1Outline.Data = CreateArcGeometry(s1, e, r1, sw);
        if (arc2Outline != null)
            arc2Outline.Data = CreateArcGeometry(s2, e, r2, sw);
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
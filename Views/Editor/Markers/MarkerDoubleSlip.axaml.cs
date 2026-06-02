using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerDoubleSlip : UserControl, IMarkerAngle
{
    public MarkerDoubleSlip()
    {
        AvaloniaXamlLoader.Load(this);
        UpdateGeometry(0);
    }

    public void SetAngle(int angle)
    {
        // Normalizácia uhla na rozsah 0-359
        angle = ((angle % 360) + 360) % 360;
        UpdateGeometry(angle);
    }

    private void UpdateGeometry(int angle)
    {
        var arcR = this.FindControl<Path>("ArcRPath");
        var arcL = this.FindControl<Path>("ArcLPath");
        var line1 = this.FindControl<Line>("Line1");
        var line2 = this.FindControl<Line>("Line2");

        if (arcR == null || arcL == null || line1 == null || line2 == null) return;

        // Globálne nastavenia pre hladké vykresľovanie
        this.UseLayoutRounding = false;
        arcR.StrokeThickness = 2;
        arcL.StrokeThickness = 2;
        line1.StrokeThickness = 2;
        line2.StrokeThickness = 2;
        
        RenderOptions.SetEdgeMode(arcR, EdgeMode.Antialias);
        RenderOptions.SetEdgeMode(arcL, EdgeMode.Antialias);

        // Definícia bodov (z = 0.2, m = 12.0, f = 23.8)
        double z = 0.2;
        double m = 12.0;
        double f = 23.8;
        double radius = 36.0;

        // Logika: (ArcR_Start, ArcR_End, SweepR, ArcL_Start, ArcL_End, SweepL, Line1_Start, Line1_End, Line2_Start, Line2_End)
        (Point sR, Point eR, SweepDirection swR, Point sL, Point eL, SweepDirection swL, Point s1, Point e1, Point s2, Point e2) = angle switch
        {
            // 0°: Zvislá + Šikmá (vpravo dole -> vľavo hore)
            0 or 360 => (new Point(f, f), new Point(m, z), SweepDirection.Clockwise, 
                         new Point(m, f), new Point(z, z), SweepDirection.CounterClockwise,
                         new Point(m, f), new Point(m, z), 
                         new Point(f, f), new Point(z, z)),

            // 45°: Šikmá (vľavo dole -> vpravo hore) + Zvislá
            45  => (new Point(m, f), new Point(f, z), SweepDirection.Clockwise, 
                    new Point(z, f), new Point(m, z), SweepDirection.CounterClockwise,
                    new Point(z, f), new Point(f, z), 
                    new Point(m, f), new Point(m, z)),

            // 90°: Vodorovná + Šikmá (vľavo dole -> vpravo hore)
            90  => (new Point(z, f), new Point(f, m), SweepDirection.Clockwise, 
                    new Point(z, m), new Point(f, z), SweepDirection.CounterClockwise,
                    new Point(z, m), new Point(f, m), 
                    new Point(z, f), new Point(f, z)),

            // 135°: Šikmá (vľavo hore -> vpravo dole) + Vodorovná
            135 => (new Point(z, m), new Point(f, f), SweepDirection.Clockwise, 
                    new Point(z, z), new Point(f, m), SweepDirection.CounterClockwise,
                    new Point(z, z), new Point(f, f), 
                    new Point(z, m), new Point(f, m)),

            // 180°: Zrkadlo k 0°
            180 => (new Point(z, z), new Point(m, f), SweepDirection.Clockwise, 
                    new Point(m, z), new Point(f, f), SweepDirection.CounterClockwise,
                    new Point(m, z), new Point(m, f), 
                    new Point(z, z), new Point(f, f)),

            // 225°: Zrkadlo k 45°
            225 => (new Point(m, z), new Point(z, f), SweepDirection.Clockwise, 
                    new Point(f, z), new Point(m, f), SweepDirection.CounterClockwise,
                    new Point(f, z), new Point(z, f), 
                    new Point(m, z), new Point(m, f)),

            // 270°: Zrkadlo k 90°
            270 => (new Point(f, z), new Point(z, m), SweepDirection.Clockwise, 
                    new Point(f, m), new Point(z, f), SweepDirection.CounterClockwise,
                    new Point(f, m), new Point(z, m), 
                    new Point(f, z), new Point(z, f)),

            // 315°: Zrkadlo k 135°
            315 => (new Point(f, m), new Point(z, z), SweepDirection.Clockwise, 
                    new Point(f, f), new Point(z, m), SweepDirection.CounterClockwise,
                    new Point(f, f), new Point(z, z), 
                    new Point(f, m), new Point(z, m)),

            _   => (new Point(f, f), new Point(m, z), SweepDirection.Clockwise, 
                    new Point(m, f), new Point(z, z), SweepDirection.CounterClockwise,
                    new Point(m, f), new Point(m, z), 
                    new Point(f, f), new Point(z, z)),
        };

        // Nastavenie priamych čiar
        line1.StartPoint = s1; line1.EndPoint = e1;
        line2.StartPoint = s2; line2.EndPoint = e2;

        // Nastavenie oblúkových ciest
        arcR.Data = CreateArcGeometry(sR, eR, radius, swR);
        arcL.Data = CreateArcGeometry(sL, eL, radius, swL);
    }

    /// <summary>
    /// Pomocná metóda na vytvorenie PathGeometry pre oblúk
    /// </summary>
    private PathGeometry CreateArcGeometry(Point start, Point end, double r, SweepDirection sweep)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure 
        { 
            StartPoint = start, 
            IsClosed = false 
        };
        
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
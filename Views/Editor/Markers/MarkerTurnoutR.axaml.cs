using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerTurnoutR : UserControl, IMarkerAngle
{
    public MarkerTurnoutR()
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

        // Nastavenia pre ostrosť a rendering
        line.UseLayoutRounding = false;
        path.UseLayoutRounding = false;
        this.UseLayoutRounding = false;
        
        line.StrokeThickness = 2;
        path.StrokeThickness = 2;
        line.StrokeLineCap = PenLineCap.Flat;
        path.StrokeLineCap = PenLineCap.Flat;
        path.StrokeJoin = PenLineJoin.Round;
        RenderOptions.SetEdgeMode(path, EdgeMode.Antialias);
        
        // Nastavenie outline elementov
        if (lineOutline != null)
        {
            lineOutline.UseLayoutRounding = false;
            lineOutline.StrokeThickness = 3.5;
            lineOutline.StrokeLineCap = PenLineCap.Flat;
            // Aktívna vetva (priama) - outline je tesne pod farebnou čiarou
            lineOutline.ZIndex = 9;
        }
        if (pathOutline != null)
        {
            pathOutline.UseLayoutRounding = false;
            pathOutline.StrokeThickness = 3.5;
            pathOutline.StrokeLineCap = PenLineCap.Flat;
            pathOutline.StrokeJoin = PenLineJoin.Round;
            RenderOptions.SetEdgeMode(pathOutline, EdgeMode.Antialias);
            // Neaktívna vetva (oblúk) - outline je úplne vzadu
            pathOutline.ZIndex = 0;
        }
        
        // Nastavenie ZIndex pre farebné čiary
        // Priama koľaj je štandardne aktívna (vpredu)
        line.ZIndex = 10;
        path.ZIndex = 1;

        // Súradnice (z=0.2, m=12.0, f=23.8)
        double z = 0.2;
        double m = 12.0;
        double f = 23.8;
        double radius = 36.0;

        // Logika pre pravú výhybku (odbočuje doprava v smere jazdy)
        (Point lS, Point lE, Point cS, Point cE, SweepDirection sweep) = angle switch
        {
            0   => (new Point(m, f), new Point(m, z), new Point(m, f), new Point(f, z), SweepDirection.Clockwise),
            45  => (new Point(z, f), new Point(f, z), new Point(z, f), new Point(f, m), SweepDirection.Clockwise),
            90  => (new Point(z, m), new Point(f, m), new Point(z, m), new Point(f, f), SweepDirection.Clockwise),
            135 => (new Point(z, z), new Point(f, f), new Point(z, z), new Point(m, f), SweepDirection.Clockwise),
            180 => (new Point(m, z), new Point(m, f), new Point(m, z), new Point(z, f), SweepDirection.Clockwise),
            225 => (new Point(f, z), new Point(z, f), new Point(f, z), new Point(z, m), SweepDirection.Clockwise),
            // OPRAVENÉ 270: Vstup vpravo (f,m), priama vľavo (z,m), oblúk vľavo hore (z,z)
            270 => (new Point(f, m), new Point(z, m), new Point(f, m), new Point(z, z), SweepDirection.Clockwise),
            // OPRAVENÉ 315: Vstup vpravo dole (f,f), priama vľavo hore (z,z), oblúk stred hore (m,z)
            315 => (new Point(f, f), new Point(z, z), new Point(f, f), new Point(m, z), SweepDirection.Clockwise),
            _   => (new Point(m, f), new Point(m, z), new Point(m, f), new Point(f, z), SweepDirection.Clockwise),
        };

        // 1. Nastavenie priamej koľaje
        line.StartPoint = lS;
        line.EndPoint = lE;
        if (lineOutline != null)
        {
            lineOutline.StartPoint = lS;
            lineOutline.EndPoint = lE;
        }

        // 2. Kružnicový oblúk cez StreamGeometry
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(cS, false);
            context.ArcTo(cE, new Size(radius, radius), 0, false, sweep);
            context.EndFigure(false);
        }
        path.Data = geometry;
        
        // Outline pre oblúk
        if (pathOutline != null)
        {
            var outlineGeometry = new StreamGeometry();
            using (var context = outlineGeometry.Open())
            {
                context.BeginFigure(cS, false);
                context.ArcTo(cE, new Size(radius, radius), 0, false, sweep);
                context.EndFigure(false);
            }
            pathOutline.Data = outlineGeometry;
        }
    }
}
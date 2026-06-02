using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerCurve45 : UserControl, IMarkerAngle
{
    public MarkerCurve45()
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
        var path = this.FindControl<Path>("TrackPath");
        if (path == null) return;
        
        // 1. Vynútené nastavenia priamo v kóde (keďže AXAML sa ignoruje)
        path.UseLayoutRounding = false;
        this.UseLayoutRounding = false;
        path.StrokeThickness = 2; 
        path.StrokeLineCap = PenLineCap.Flat; // KRITICKÉ: Presne nadväzujúce hrany
        path.StrokeJoin = PenLineJoin.Round;
        RenderOptions.SetEdgeMode(path, EdgeMode.Antialias); // Vynútené vyhladzovanie
       

        // 2. Súradnice s jemným presahom (overlapping)
        double z = 0; // Presne hranica
        double m = 12;  
        double f = 24; // Presne hranica

        (Point start, Point control, Point end) = angle switch
        {
            0   => (new Point(z, f), new Point(m, m), new Point(f, m)),
            45  => (new Point(z, m), new Point(m, m), new Point(f, f)),
            90  => (new Point(z, z), new Point(m, m), new Point(m, f)),
            135 => (new Point(z, f), new Point(m, m), new Point(m, z)),
            180 => (new Point(z, m), new Point(m, m), new Point(f, z)),
            225 => (new Point(z, z), new Point(m, m), new Point(f, m)),
            270 => (new Point(m, z), new Point(m, m), new Point(f, f)),
            315 => (new Point(f, z), new Point(m, m), new Point(m, f)),
            _   => (new Point(z, f), new Point(m, m), new Point(f, m)),
        };

        // 3. Vytvorenie geometrie pomocou StreamGeometry (najstabilnejšie v Avalonii)
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false);
            context.QuadraticBezierTo(control, end);
            context.EndFigure(false);
        }

        path.Data = geometry;
    }
}
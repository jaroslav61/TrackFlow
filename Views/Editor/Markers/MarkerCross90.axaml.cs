using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerCross90 : UserControl, IMarkerAngle
{
    public MarkerCross90()
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
        var line1 = this.FindControl<Line>("Line1");
        var line2 = this.FindControl<Line>("Line2");
        if (line1 == null || line2 == null) return;

        // Nastavenia pre ostrosť
        line1.UseLayoutRounding = false;
        line2.UseLayoutRounding = false;
        line1.StrokeThickness = 2;
        line2.StrokeThickness = 2;
        line1.StrokeLineCap = PenLineCap.Flat;
        line2.StrokeLineCap = PenLineCap.Flat;

        // Zjednotené súradnice (z = 0.2, m = 12.0, f = 23.8)
        double z = 0.2;
        double m = 12.0;
        double f = 23.8;

        // Logika smeru jazdy: Start -> End
        (Point s1, Point e1, Point s2, Point e2) = angle switch
        {
            // 0°: Zvislá (zdola nahor) + Vodorovná (zľava doprava)
            0   => (new Point(m, f), new Point(m, z), new Point(z, m), new Point(f, m)),
            
            // 45°: Šikmá (vľavo dole -> vpravo hore) + Šikmá (vľavo hore -> vpravo dole)
            45  => (new Point(z, f), new Point(f, z), new Point(z, z), new Point(f, f)),
            
            // 90°: Vodorovná (zľava doprava) + Zvislá (zhora nadol)
            90  => (new Point(z, m), new Point(f, m), new Point(m, z), new Point(m, f)),
            
            // 135°: Šikmá (vľavo hore -> vpravo dole) + Šikmá (vpravo hore -> vľavo dole)
            135 => (new Point(z, z), new Point(f, f), new Point(f, z), new Point(z, f)),
            
            // 180°: Zvislá (zhora nadol) + Vodorovná (sprava doľava)
            180 => (new Point(m, z), new Point(m, f), new Point(f, m), new Point(z, m)),
            
            // 225°: Šikmá (vpravo hore -> vľavo dole) + Šikmá (vpravo dole -> vľavo hore)
            225 => (new Point(f, z), new Point(z, f), new Point(f, f), new Point(z, z)),
            
            // 270°: Vodorovná (sprava doľava) + Zvislá (zdola nahor)
            270 => (new Point(f, m), new Point(z, m), new Point(m, f), new Point(m, z)),
            
            // 315°: Šikmá (vpravo dole -> vľavo hore) + Šikmá (vľavo dole -> vpravo hore)
            315 => (new Point(f, f), new Point(z, z), new Point(z, f), new Point(f, z)),
            
            _   => (new Point(m, f), new Point(m, z), new Point(z, m), new Point(f, m)),
        };

        line1.StartPoint = s1;
        line1.EndPoint = e1;
        line2.StartPoint = s2;
        line2.EndPoint = e2;
    }
}
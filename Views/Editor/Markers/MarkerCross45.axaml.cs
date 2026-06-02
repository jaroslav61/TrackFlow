using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerCross45 : UserControl, IMarkerAngle
{
    public MarkerCross45()
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

        // Zjednotené súradnice (z = zero/štart, m = middle, f = full/koniec)
        double z = 0.2;
        double m = 12.0;
        double f = 23.8;

        // Logika smeru jazdy (Line1 je primárna, Line2 sekundárna)
        (Point s1, Point e1, Point s2, Point e2) = angle switch
        {
            // 0°: Zvislá (zdola nahor) + Šikmá (vpravo dole -> vľavo hore)
            0   => (new Point(m, f), new Point(m, z), new Point(f, f), new Point(z, z)),
            
            // 45°: Šikmá (vľavo dole -> vpravo hore) + Zvislá (zdola nahor)
            45  => (new Point(z, f), new Point(f, z), new Point(m, f), new Point(m, z)),
            
            // 90°: Vodorovná (zľava doprava) + Šikmá (vľavo dole -> vpravo hore)
            90  => (new Point(z, m), new Point(f, m), new Point(z, f), new Point(f, z)),
            
            // 135°: Šikmá (vľavo hore -> vpravo dole) + Vodorovná (zľava doprava)
            135 => (new Point(z, z), new Point(f, f), new Point(z, m), new Point(f, m)),
            
            // 180°: Zvislá (zhora nadol) + Šikmá (vľavo hore -> vpravo dole)
            180 => (new Point(m, z), new Point(m, f), new Point(z, z), new Point(f, f)),
            
            // 225°: Šikmá (vpravo hore -> vľavo dole) + Zvislá (zhora nadol)
            225 => (new Point(f, z), new Point(z, f), new Point(m, z), new Point(m, f)),
            
            // 270°: Vodorovná (sprava doľava) + Šikmá (vpravo hore -> vľavo dole)
            270 => (new Point(f, m), new Point(z, m), new Point(f, z), new Point(z, f)),
            
            // 315°: Šikmá (vpravo dole -> vľavo hore) + Vodorovná (sprava doľava)
            315 => (new Point(f, f), new Point(z, z), new Point(f, m), new Point(z, m)),
            
            _   => (new Point(m, f), new Point(m, z), new Point(f, f), new Point(z, z)),
        };

        line1.StartPoint = s1;
        line1.EndPoint = e1;
        line2.StartPoint = s2;
        line2.EndPoint = e2;
    }
}
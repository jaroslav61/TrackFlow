using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerBridge90 : UserControl, IMarkerAngle
{
    public MarkerBridge90()
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
        var CrossTrackTop = this.FindControl<Line>("CrossTrackTop");
        var CrossTrackMiddle = this.FindControl<Line>("CrossTrackMiddle");
        var CrossTrackBottom = this.FindControl<Line>("CrossTrackBottom");
        var TrackLine = this.FindControl<Line>("TrackLine");
        var Bridge1Line = this.FindControl<Line>("Bridge1Line");
        var Bridge2Line = this.FindControl<Line>("Bridge2Line");
       
        if (CrossTrackTop == null || CrossTrackMiddle == null || CrossTrackBottom == null ||
            TrackLine == null || Bridge1Line == null || Bridge2Line == null ) return;

        switch (angle)
        {
            case 0:
            case 360:
                // POPOD: Zvislá koľaj (zhora nadol)
                CrossTrackTop.StartPoint = new Point(12, 0.2);
                CrossTrackTop.EndPoint = new Point(12, 8);
                CrossTrackMiddle.StartPoint = new Point(12, 8);
                CrossTrackMiddle.EndPoint = new Point(12, 16);
                CrossTrackBottom.StartPoint = new Point(12, 16);
                CrossTrackBottom.EndPoint = new Point(12, 23.8);
                // CEZ: Vodorovná koľaj (zľava doprava)
                TrackLine.StartPoint = new Point(0.2, 12);
                TrackLine.EndPoint = new Point(23.8, 12);
                // Nosníky (vodorovné)
                Bridge1Line.StartPoint = new Point(2, 8.5);
                Bridge1Line.EndPoint = new Point(22, 8.5);
                Bridge2Line.StartPoint = new Point(2, 15.5);
                Bridge2Line.EndPoint = new Point(22, 15.5);
                break;
            
            case 45:
                // POPOD: Šikmá koľaj (vpravo hore -> vľavo dole)
                CrossTrackTop.StartPoint = new Point(23.8, 0.2);
                CrossTrackTop.EndPoint = new Point(15, 9);
                CrossTrackMiddle.StartPoint = new Point(15, 9);
                CrossTrackMiddle.EndPoint = new Point(9, 15);
                CrossTrackBottom.StartPoint = new Point(9, 15);
                CrossTrackBottom.EndPoint = new Point(0.2, 23.8);
                // CEZ: Šikmá koľaj (vľavo hore -> vpravo dole)
                TrackLine.StartPoint = new Point(0.2, 0.2);
                TrackLine.EndPoint = new Point(23.8, 23.8);
                // Nosníky (šikmo rovnobežne s TrackLine)
                Bridge1Line.StartPoint = new Point(7, 2);
                Bridge1Line.EndPoint = new Point(22, 17);
                Bridge2Line.StartPoint = new Point(2, 7);
                Bridge2Line.EndPoint = new Point(17, 22);
                break;
       
            case 90:
                // POPOD: Vodorovná koľaj (zľava doprava)
                CrossTrackTop.StartPoint = new Point(0.2, 12);
                CrossTrackTop.EndPoint = new Point(8, 12);
                CrossTrackMiddle.StartPoint = new Point(8, 12);
                CrossTrackMiddle.EndPoint = new Point(16, 12);
                CrossTrackBottom.StartPoint = new Point(16, 12);
                CrossTrackBottom.EndPoint = new Point(23.8, 12);
                // CEZ: Zvislá koľaj (zhora nadol)
                TrackLine.StartPoint = new Point(12, 0.2);
                TrackLine.EndPoint = new Point(12, 23.8);
                // Nosníky (zvislé)
                Bridge1Line.StartPoint = new Point(8.5, 2);
                Bridge1Line.EndPoint = new Point(8.5, 22);
                Bridge2Line.StartPoint = new Point(15.5, 2);
                Bridge2Line.EndPoint = new Point(15.5, 22);
                break;
         
            case 135:
                // POPOD: Šikmá koľaj (vľavo hore -> vpravo dole)
                CrossTrackTop.StartPoint = new Point(0.2, 0.2);
                CrossTrackTop.EndPoint = new Point(9, 9);
                CrossTrackMiddle.StartPoint = new Point(9, 9);
                CrossTrackMiddle.EndPoint = new Point(15, 15);
                CrossTrackBottom.StartPoint = new Point(15, 15);
                CrossTrackBottom.EndPoint = new Point(23.8, 23.8);
                // CEZ: Šikmá koľaj (vpravo hore -> vľavo dole)
                TrackLine.StartPoint = new Point(23.8, 0.2);
                TrackLine.EndPoint = new Point(0.2, 23.8);
                // Nosníky (šikmo rovnobežne s TrackLine)
                Bridge1Line.StartPoint = new Point(22, 7);
                Bridge1Line.EndPoint = new Point(7, 22);
                Bridge2Line.StartPoint = new Point(17, 2);
                Bridge2Line.EndPoint = new Point(2, 17);
                break;
           
            case 180:
                // POPOD: Zvislá koľaj (zdola nahor)
                CrossTrackTop.StartPoint = new Point(12, 23.8);
                CrossTrackTop.EndPoint = new Point(12, 16);
                CrossTrackMiddle.StartPoint = new Point(12, 16);
                CrossTrackMiddle.EndPoint = new Point(12, 8);
                CrossTrackBottom.StartPoint = new Point(12, 8);
                CrossTrackBottom.EndPoint = new Point(12, 0.2);
                // CEZ: Vodorovná koľaj (sprava doľava)
                TrackLine.StartPoint = new Point(23.8, 12);
                TrackLine.EndPoint = new Point(0.2, 12);
                // Nosníky (vodorovné, prehodené)
                Bridge1Line.StartPoint = new Point(22, 15.5);
                Bridge1Line.EndPoint = new Point(2, 15.5);
                Bridge2Line.StartPoint = new Point(22, 8.5);
                Bridge2Line.EndPoint = new Point(2, 8.5);
                break;
         
            case 225:
                // POPOD: Šikmá koľaj (vľavo hore -> vpravo dole)
                CrossTrackTop.StartPoint = new Point(0.2, 23.8); // Oprava: malo by byť Start vľavo hore pri 225? Ponechané podľa tvojho kódu.
                CrossTrackTop.EndPoint = new Point(9, 15);
                CrossTrackMiddle.StartPoint = new Point(9, 15);
                CrossTrackMiddle.EndPoint = new Point(15, 9);
                CrossTrackBottom.StartPoint = new Point(15, 9);
                CrossTrackBottom.EndPoint = new Point(23.8, 0.2);
                // CEZ: Šikmá koľaj (vpravo dole -> vľavo hore)
                TrackLine.StartPoint = new Point(23.8, 23.8);
                TrackLine.EndPoint = new Point(0.2, 0.2);
                // Nosníky (šikmo rovnobežne s TrackLine)
                Bridge1Line.StartPoint = new Point(17, 22);
                Bridge1Line.EndPoint = new Point(2, 7);
                Bridge2Line.StartPoint = new Point(22, 17);
                Bridge2Line.EndPoint = new Point(7, 2);
                break;
           
            case 270:
                // POPOD: Vodorovná koľaj (sprava doľava)
                CrossTrackTop.StartPoint = new Point(23.8, 12);
                CrossTrackTop.EndPoint = new Point(16, 12);
                CrossTrackMiddle.StartPoint = new Point(16, 12);
                CrossTrackMiddle.EndPoint = new Point(8, 12);
                CrossTrackBottom.StartPoint = new Point(8, 12);
                CrossTrackBottom.EndPoint = new Point(0.2, 12);
                // CEZ: Zvislá koľaj (zdola nahor)
                TrackLine.StartPoint = new Point(12, 23.8);
                TrackLine.EndPoint = new Point(12, 0.2);
                // Nosníky (zvislé, prehodené)
                Bridge1Line.StartPoint = new Point(15.5, 22);
                Bridge1Line.EndPoint = new Point(15.5, 2);
                Bridge2Line.StartPoint = new Point(8.5, 22);
                Bridge2Line.EndPoint = new Point(8.5, 2);
                break;
         
            case 315:
                // POPOD: Šikmá koľaj (vpravo dole -> vľavo hore)
                CrossTrackTop.StartPoint = new Point(23.8, 23.8);
                CrossTrackTop.EndPoint = new Point(15, 15);
                CrossTrackMiddle.StartPoint = new Point(15, 15);
                CrossTrackMiddle.EndPoint = new Point(9, 9);
                CrossTrackBottom.StartPoint = new Point(9, 9);
                CrossTrackBottom.EndPoint = new Point(0.2, 0.2);
                // CEZ: Šikmá koľaj (vľavo dole -> vpravo hore)
                TrackLine.StartPoint = new Point(0.2, 23.8);
                TrackLine.EndPoint = new Point(23.8, 0.2);
                // Nosníky (šikmo rovnobežne s TrackLine)
                Bridge1Line.StartPoint = new Point(2, 17);
                Bridge1Line.EndPoint = new Point(17, 2);
                Bridge2Line.StartPoint = new Point(7, 22); 
                Bridge2Line.EndPoint = new Point(22, 7); 
                break;
        }
    }
}
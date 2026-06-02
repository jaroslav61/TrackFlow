using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerCurve90 : UserControl, IMarkerAngle
{
    public MarkerCurve90()
    {
        AvaloniaXamlLoader.Load(this);
        UpdateGeometry(0);
    }

    public void SetAngle(int angle)
    {
        // Normalizácia na 0-359
        angle = ((angle % 360) + 360) % 360;
        // Zaokrúhľovanie na najbližších 45° (0, 45, 90, 135, 180, 225, 270, 315)
        angle = (int)(Math.Round(angle / 45.0) * 45) % 360;
        UpdateGeometry(angle);
    }

private void UpdateGeometry(int angle)
{
    var path = this.FindControl<Path>("TrackPath");
    if (path == null) return;
    
    path.UseLayoutRounding = false;
    this.UseLayoutRounding = false;
    path.StrokeThickness = 2;
    path.StrokeLineCap = PenLineCap.Flat;
    path.StrokeJoin = PenLineJoin.Round;
    RenderOptions.SetEdgeMode(path, EdgeMode.Antialias);

    // Bez presahov - presné hranice
    double z = 0.2;
    double m = 12;  
    double f = 23.8;

    // Polomer oblúka: 24 pre uhlopriečne uhly (45°, 135°, 225°, 315°), 12 pre ostatné
    double radius = (angle == 45 || angle == 135 || angle == 225 || angle == 315) ? 24 : 12;

    // pozície: 0°, 45°, 90°, 135°, 180°, 225°, 270, 315°
    (Point start, Point end, SweepDirection sweep) = angle switch
    {
        0   => (new Point(m, f), new Point(f, m), SweepDirection.Clockwise),          // (12,24) → (24,12)
        45  => (new Point(z, f), new Point(f, f), SweepDirection.Clockwise),          // (0,24) → (24,24)
        90  => (new Point(z, m), new Point(m, f), SweepDirection.Clockwise),          // (0,12) → (12,24)
        135 => (new Point(z, z), new Point(z, f), SweepDirection.Clockwise),          // (0,0) → (0,24)
        180 => (new Point(m, z), new Point(z, m), SweepDirection.Clockwise),          // (12,0) → (0,12)
        225 => (new Point(f, z), new Point(z, z), SweepDirection.Clockwise),          // (24,0) → (0,0)
        270 => (new Point(m, z), new Point(f, m), SweepDirection.CounterClockwise),   // (12,0) → (24,12)
        315 => (new Point(f, f), new Point(f, z), SweepDirection.Clockwise),          // (24,24) → (24,0)
        _   => (new Point(m, f), new Point(f, m), SweepDirection.Clockwise),          // Default = 0°
    };

    // Vytvorenie geometrie pomocou PathGeometry s ArcSegment
    var geometry = new PathGeometry();
    var figure = new PathFigure 
    { 
        StartPoint = start,
        IsClosed = false  // PRIDANÉ: zabráni vykresleniu čiary späť k počiatočnému bodu
    };
    figure.Segments!.Add(new ArcSegment
    {
        Point = end,
        Size = new Size(radius, radius),
        RotationAngle = 0,
        SweepDirection = sweep,
        IsLargeArc = false
    });
    geometry.Figures!.Add(figure);

    path.Data = geometry;
}

}
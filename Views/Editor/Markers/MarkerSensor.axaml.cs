using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
namespace TrackFlow.Views.Editor.Markers;
public partial class MarkerSensor : UserControl, IMarkerAngle
{
    public MarkerSensor() => AvaloniaXamlLoader.Load(this);
    public void SetAngle(int angle)
    {
        angle = ((angle % 360) + 360) % 360;
        // TODO: nahradiť explicitnou geometriou podľa zadania
        RenderTransform = angle == 0 ? null : new RotateTransform(angle, 12, 12);
    }
}

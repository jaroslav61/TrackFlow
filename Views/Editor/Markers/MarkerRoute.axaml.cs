using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor.Markers;

public partial class MarkerRoute : UserControl, IMarkerAngle
{
    private Border? _buttonBase;
    private Path? _arrowOutlinePath;
    private Path? _arrowInnerPath;

    public MarkerRoute() 
    { 
        AvaloniaXamlLoader.Load(this);
        _buttonBase = this.FindControl<Border>("ButtonBase");
        _arrowOutlinePath = this.FindControl<Path>("ArrowOutlinePath");
        _arrowInnerPath = this.FindControl<Path>("ArrowInnerPath");
    }

    public void SetAngle(int angle)
    {
        angle = ((angle % 360) + 360) % 360;
        RenderTransform = angle == 0 ? null : new RotateTransform(angle, 12, 12);
    }

    /// <summary>
    /// Nastaví farbu šípky na základe toho, či má marker priradenú cestu.
    /// Outline zostáva vždy čierny, len vnútro sa mení (žltá/sivá).
    /// </summary>
    public void SetRouteAssigned(bool hasRoute)
    {
        if (_arrowOutlinePath != null)
        {
            // Outline musí byť stále čierny.
            _arrowOutlinePath.Stroke = new SolidColorBrush(Color.Parse("#333333"));
        }

        if (_arrowInnerPath != null)
        {
            // Vnútro: žltá ak má cestu, ináč svetlá sivá
            _arrowInnerPath.Stroke = hasRoute 
                ? new SolidColorBrush(Color.Parse("#D6ED17")) 
                : new SolidColorBrush(Color.Parse("#FF0000"));
        }
    }

    /// <summary>
    /// Jemný interakčný stav tlačidla pre operation režim (hover/pressed).
    /// </summary>
    public void SetInteractionState(bool isHovered, bool isPressed)
    {
        if (_buttonBase != null)
        {
            _buttonBase.Background = isPressed
                ? new SolidColorBrush(Color.Parse("#8E8C96"))
                : isHovered
                    ? new SolidColorBrush(Color.Parse("#B0AEB9"))
                    : new SolidColorBrush(Color.Parse("#A09EA9"));
        }

        Opacity = isPressed ? 0.9 : 1.0;
    }
}


using Avalonia;
using Avalonia.Media;
using TrackFlow.Models;

namespace TrackFlow.ViewModels.Settings;

public sealed class DccCentralListItem
{
    public string Name { get; }
    public bool IsHeader { get; }
    public DccCentralType? Type { get; }
    public bool IsImplemented { get; }
    public bool IsSelectable { get; }
    public string? ToolTipText { get; }
    public Thickness IndentThickness { get; }
    public FontWeight FontWeight { get; }
    public double Opacity { get; }

    private DccCentralListItem(string name, bool isHeader, DccCentralType? type, int indentLevel, bool isImplemented)
    {
        Name = name;
        IsHeader = isHeader;
        Type = type;
        IsImplemented = isImplemented;

        IndentThickness = new Thickness(indentLevel * 16, 0, 0, 0);

        if (isHeader)
        {
            FontWeight = FontWeight.SemiBold;
            Opacity = 0.85;
            IsSelectable = false;
            ToolTipText = null;
        }
        else
        {
            FontWeight = FontWeight.Normal;
            Opacity = isImplemented ? 1.0 : 0.55; // neimplementované viditeľne „zosivené“
            IsSelectable = isImplemented;
            ToolTipText = isImplemented ? null : "Zatiaľ neimplementované";
        }
    }

    public static DccCentralListItem Header(string name)
        => new(name, isHeader: true, type: null, indentLevel: 0, isImplemented: false);

    public static DccCentralListItem Item(string name, DccCentralType type, int indentLevel, bool isImplemented)
        => new(name, isHeader: false, type: type, indentLevel: indentLevel, isImplemented: isImplemented);

    public override string ToString() => Name;
}

using System.Collections.ObjectModel;
using Avalonia.Media;
using TrackFlow.Models;

namespace TrackFlow.ViewModels.Settings;

/// <summary>
/// Uzol v hierarchickom strome výberu DCC centrály.
/// Skupiny (výrobcovia): Type = null, Children naplnené.
/// Listy (modely centrál): Type != null, Children prázdne.
/// </summary>
public sealed class DccCentralTreeNode
{
    public string Name { get; }
    public DccCentralType? Type { get; }
    public bool IsImplemented { get; }
    public ObservableCollection<DccCentralTreeNode> Children { get; } = new();

    /// <summary>True = skupinový uzol (výrobca), False = listový uzol (model centrály).</summary>
    public bool IsGroup => !Type.HasValue;

    /// <summary>Len implementované listy sú selektovateľné.</summary>
    public bool IsSelectable => !IsGroup && IsImplemented;

    /// <summary>Neimplementované modely sú vizuálne zosivené.</summary>
    public double Opacity => (!IsGroup && !IsImplemented) ? 0.5 : 1.0;

    /// <summary>Skupiny = SemiBold, modely = Normal.</summary>
    public FontWeight FontWeight => IsGroup ? FontWeight.SemiBold : FontWeight.Normal;

    public DccCentralTreeNode(string name, DccCentralType? type = null, bool isImplemented = false)
    {
        Name = name;
        Type = type;
        IsImplemented = isImplemented;
    }

    public override string ToString() => Name;
}

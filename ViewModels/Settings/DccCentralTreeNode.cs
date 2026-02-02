using System.Collections.ObjectModel;
using TrackFlow.Models;

namespace TrackFlow.ViewModels.Settings;

public sealed class DccCentralTreeNode
{
    public string Name { get; }
    public DccCentralType? Type { get; }
    public ObservableCollection<DccCentralTreeNode> Children { get; } = new();

    public bool IsGroup => !Type.HasValue;

    public DccCentralTreeNode(string name, DccCentralType? type = null)
    {
        Name = name;
        Type = type;
    }

    public override string ToString() => Name;
}

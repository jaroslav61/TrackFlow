using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TrackFlow.ViewModels.Settings;

/// <summary>
/// Reprezentuje skupinu (výrobcu) v hierarchickom výbere DCC centrály.
/// </summary>
public sealed partial class DccCentralGroupViewModel : ObservableObject
{
    public string Name { get; }
    public IReadOnlyList<DccCentralListItem> Items { get; }

    /// <summary>
    /// Či je skupina rozbalená v stromovom zobrazení.
    /// Skupiny s implementovanými centrálami štartujú rozbalené.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    public DccCentralGroupViewModel(string name, IReadOnlyList<DccCentralListItem> items)
    {
        Name = name;
        Items = items;
        // Skupiny s aspoň jednou implementovanou centrálou budú predvolene rozbalené
        _isExpanded = items.Any(i => i.IsSelectable);
    }
}


using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TrackFlow.ViewModels.Cab;

/// <summary>
/// ViewModel pre floating okno pásu Multi-Cab.
/// Zatiaľ rieši iba titulok a notifikáciu o zatvorení okna.
/// </summary>
public partial class CabStripWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string title = "Multi-Cab";

    /// <summary>
    /// Callback – UI vrstva ho nastaví, keď sa okno zavrie (X).
    /// </summary>
    public Action? Closed { get; set; }

    public void NotifyClosed()
    {
        Closed?.Invoke();
    }
}

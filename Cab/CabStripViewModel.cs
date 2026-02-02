using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TrackFlow.ViewModels.Cab;

/// <summary>
/// ViewModel pre jednu kabínu (jednu kartu/dashboard) v Multi-Cab páse.
/// Zatiaľ rieši len identitu a zavretie karty.
/// </summary>
public partial class CabStripViewModel : ObservableObject
{
    /// <summary>
    /// Stabilný identifikátor kabíny (napr. adresa lokomotívy, alebo GUID).
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Názov, ktorý sa zobrazí na karte (napr. "750.123" alebo "Loco A (3)").
    /// </summary>
    [ObservableProperty]
    private string title;

    /// <summary>
    /// Callback, ktorý host (CabStripHostViewModel / MainWindowVM) nastaví,
    /// aby sa kabína dala korektne odstrániť zo zoznamu.
    /// </summary>
    public Action<CabStripViewModel>? RequestClose { get; set; }

    public CabStripViewModel(int id, string title)
    {
        Id = id;
        this.title = title;
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke(this);
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
namespace TrackFlow.ViewModels.Cab;

/// <summary>
/// Host ViewModel pre pás ovládačov (Multi-Cab).
/// Rieši len "dock/undock" (t. j. presun pásu do samostatného okna a späť).
/// UI okno samotné vytvára View/Service – VM iba signalizuje zámer.
/// </summary>
/// 


public partial class CabStripHostViewModel : ObservableObject
{
    public ObservableCollection<object> Dashboards { get; } = new();
    /// <summary>
    /// True = pás je v samostatnom okne (floating).
    /// False = pás je docknutý v hlavnom okne.
    /// </summary>
    [ObservableProperty]
    private bool isFloating;

    /// <summary>
    /// Voliteľný callback – UI vrstva si ho napojí (napr. MainWindowViewModel alebo DockingService).
    /// Keď VM požiada o undock, UI vytvorí CabStripWindow a zobrazí ho.
    /// </summary>
    public Action? RequestUndock { get; set; }

    /// <summary>
    /// Voliteľný callback – UI vrstva ho napojí.
    /// Keď VM požiada o dock, UI zavrie CabStripWindow a vráti pás do MainWindow.
    /// </summary>
    public Action? RequestDock { get; set; }

    public CabStripHostViewModel()
    {
        // default: docknuté v main okne
        IsFloating = false;
    }

    [RelayCommand(CanExecute = nameof(CanUndock))]
    private void Undock()
    {
        if (IsFloating) return;

        // Najprv nastav stav, potom signalizuj UI
        IsFloating = true;
        RequestUndock?.Invoke();
        
                // refresh CanExecute
        UndockCommand.NotifyCanExecuteChanged();
        DockCommand.NotifyCanExecuteChanged();

    }

    private bool CanUndock() => !IsFloating;

    [RelayCommand(CanExecute = nameof(CanDock))]
    private void Dock()
    {
        if (!IsFloating) return;

        // Najprv nastav stav, potom signalizuj UI
        IsFloating = false;
        RequestDock?.Invoke();

        // refresh CanExecute
        UndockCommand.NotifyCanExecuteChanged();
        DockCommand.NotifyCanExecuteChanged();
    }

    private bool CanDock() => IsFloating;

    /// <summary>
    /// Keď sa floating okno zavrie "X" tlačidlom (nie cez príkaz),
    /// UI vrstva má zavolať toto, aby sa VM zosynchronizoval.
    /// </summary>
    public void NotifyFloatingWindowClosed()
    {
        if (!IsFloating) return;

        IsFloating = false;
        UndockCommand.NotifyCanExecuteChanged();
        DockCommand.NotifyCanExecuteChanged();
    }
}

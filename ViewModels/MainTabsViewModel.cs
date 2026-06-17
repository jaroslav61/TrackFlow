using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.ViewModels.Editor;
using TrackFlow.ViewModels.Operation;

namespace TrackFlow.ViewModels;

public partial class MainTabsViewModel : ObservableObject
{
    public OperationViewModel Operation { get; }
    public LayoutEditorViewModel LayoutEditor { get; }

    /// <summary>
    /// Keď je True, záložka Editor rozloženia je disabled.
    /// Nastavuje MainWindowViewModel pri každej zmene OperationMode.
    /// </summary>
    [ObservableProperty] private bool isSimulationActive;

    public MainTabsViewModel(SettingsManager settingsManager, ObservableCollection<Locomotive> sharedLocomotives)
    {
        Operation    = new OperationViewModel(settingsManager, sharedLocomotives);
        LayoutEditor = new LayoutEditorViewModel(settingsManager);
    }
}
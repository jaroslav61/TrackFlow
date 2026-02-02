using CommunityToolkit.Mvvm.ComponentModel;
using TrackFlow.Services;
using TrackFlow.ViewModels.Editor;
using TrackFlow.ViewModels.Operation;

namespace TrackFlow.ViewModels;

public partial class MainTabsViewModel : ObservableObject
{
    public OperationViewModel Operation { get; }
    public LayoutEditorViewModel LayoutEditor { get; }

    public MainTabsViewModel(SettingsManager settingsManager)
    {
        Operation = new OperationViewModel(settingsManager);
        LayoutEditor = new LayoutEditorViewModel();
    }
}

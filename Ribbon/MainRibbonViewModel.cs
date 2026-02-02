using CommunityToolkit.Mvvm.ComponentModel;

namespace TrackFlow.Ribbon;

public partial class MainRibbonViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isConnected;

    public string ConnectButtonText => IsConnected ? "Odpojiť" : "Pripojiť";

    [ObservableProperty]
    private bool hasOpenProject;

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ConnectButtonText));
    }
}

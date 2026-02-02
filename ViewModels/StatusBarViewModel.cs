using CommunityToolkit.Mvvm.ComponentModel;

namespace TrackFlow.ViewModels;

public partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty]
    private string message = "Systém je pripravený";

    [ObservableProperty]
    private string rightHint = "";

    [ObservableProperty]
    private bool isDccConnected;

    // Jednoduché riešenie bez triggerov/converterov – XAML si zoberie farbu priamo zo stringu.
    public string DccLedColor => IsDccConnected ? "#00C853" : "#D50000";

    partial void OnIsDccConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(DccLedColor));
    }
 
}

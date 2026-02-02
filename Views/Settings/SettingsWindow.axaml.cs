using Avalonia.Controls;
using TrackFlow.ViewModels.Settings;

namespace TrackFlow.Views.Settings;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? _vm;

    public SettingsWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            AttachToVm(DataContext as SettingsViewModel);

            // Pri otvorení okna vždy refresh, aby sa HasProject/UseProject... nastavili podľa aktuálneho projektu
            _vm?.Load();
        };

        AttachToVm(DataContext as SettingsViewModel);
    }

    private void AttachToVm(SettingsViewModel? vm)
    {
        if (_vm != null)
            _vm.CloseRequested -= OnCloseRequested;

        _vm = vm;

        if (_vm != null)
            _vm.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(bool saved) => Close(saved);
}

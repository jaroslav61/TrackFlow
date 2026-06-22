using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.ViewModels.Settings;
using TrackFlow.Views.Settings.SettingsPages;

namespace TrackFlow.Views.Settings;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? _vm;
    private const int DccTabIndex = 1;
    private bool _dccTabInitialSelectionApplied;
    private GeneralSettingsView? _generalPage;
    private DccSettingsView? _dccPage;
    private ModelClockSettingsView? _clockPage;
    private ScaleSettingsView? _scalePage;
    private ColorsSettingsView? _colorsPage;

    public SettingsWindow()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) =>
        {
            AttachToVm(DataContext as SettingsViewModel);

            // ViewModel is freshly created for each open and already calls Load() in its ctor.
            // Running Load() again here causes redundant heavy refresh cycles.
            _dccTabInitialSelectionApplied = false;
            UpdateCurrentSettingsPage();
            FocusFirstCentralWhenDccTabShown();
        };

        Closing += (_, _) =>
        {
            try
            {
                (DataContext as SettingsViewModel)?.ResetCommunicationTestPanels();
            }
            catch
            {
                // best-effort
            }

            AttachToVm(null);
        };

        AttachToVm(DataContext as SettingsViewModel);
    }

    private void AttachToVm(SettingsViewModel? vm)
    {
        if (_vm != null)
        {
            _vm.CloseRequested -= OnCloseRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = vm;

        if (_vm != null)
        {
            _vm.CloseRequested += OnCloseRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.SetCentralEditDialogFactory(ShowCentralEditDialog);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedSettingsTabIndex))
        {
            UpdateCurrentSettingsPage();
            FocusFirstCentralWhenDccTabShown();
        }
    }

    private void UpdateCurrentSettingsPage()
    {
        var host = this.FindControl<ContentControl>("SettingsPageHost");
        if (host == null || _vm == null)
            return;

        host.Content = _vm.SelectedSettingsTabIndex switch
        {
            1 => _dccPage ??= new DccSettingsView(),
            2 => _clockPage ??= new ModelClockSettingsView(),
            3 => _scalePage ??= new ScaleSettingsView(),
            4 => _colorsPage ??= new ColorsSettingsView(),
            _ => _generalPage ??= new GeneralSettingsView()
        };
    }

    private async Task<DccCentralProfile?> ShowCentralEditDialog(DccCentralProfile? existing)
    {
        var editVm = new DccCentralEditViewModel(existing);
        var dialog = new DccCentralEditWindow { DataContext = editVm };
        TooltipPreferenceService.Attach(dialog);
        var accepted = await dialog.ShowDialog<bool>(this);
        return accepted ? editVm.Result : null;
    }


    private void FocusFirstCentralWhenDccTabShown()
    {
        if (_vm == null)
            return;

        // Check if DCC tab (index 1) is selected
        if (_vm.SelectedSettingsTabIndex != DccTabIndex)
            return;

        if (_vm.ConfiguredCentrals.Count == 0)
            return;

        if (_dccTabInitialSelectionApplied)
            return;

        _dccTabInitialSelectionApplied = true;

        Dispatcher.UIThread.Post(() =>
        {
            if (_vm == null)
                return;

            var first = _vm.ConfiguredCentrals[0];
            if (!ReferenceEquals(_vm.SelectedConfiguredCentral, first))
                _vm.SelectedConfiguredCentral = first;

            if (this.FindControl<ListBox>("CentralsListBox") is { } list)
            {
                list.SelectedIndex = 0;
                list.ScrollIntoView(first);
                list.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnCloseRequested(bool saved) => Close(saved);
}

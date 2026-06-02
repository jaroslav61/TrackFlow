using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using TrackFlow.Models;
using TrackFlow.ViewModels.Settings;

namespace TrackFlow.Views.Settings;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? _vm;
    private const int DccTabIndex = 1;
    private bool _dccTabInitialSelectionApplied;
    private IDisposable? _tabIndexSubscription;

    public SettingsWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // IMPORTANT:
        // TabControl.SelectionChanged is a routed event and SelectionChanged from nested controls
        // (e.g. the centrals ListBox) may bubble up and look like a tab selection change.
        // That used to re-select the first central, effectively "freezing" list selection.
        // We only react to changes of TabControl.SelectedIndex.
        if (this.FindControl<TabControl>("SettingsTabs") is { } tabs)
        {
            _tabIndexSubscription?.Dispose();
            _tabIndexSubscription = tabs.GetObservable(TabControl.SelectedIndexProperty)
                .Subscribe(_ => FocusFirstCentralWhenDccTabShown());
        }

        DataContextChanged += (_, _) =>
        {
            AttachToVm(DataContext as SettingsViewModel);

            // Pri otvorení okna vždy refresh, aby sa HasProject/UseProject... nastavili podľa aktuálneho projektu
            _vm?.Load();
            _dccTabInitialSelectionApplied = false;
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

            // Subscription drží lambdu so zachyteným `this` – bez explicitného Dispose
            // by každé znovu-otvorenie Settings nechalo starú inštanciu zaháknutú na
            // observable, čo postupne zaberá pamäť (memory leak rastúci s počtom otvorení).
            try { _tabIndexSubscription?.Dispose(); }
            catch { /* best-effort */ }
            _tabIndexSubscription = null;

            AttachToVm(null);
        };

        AttachToVm(DataContext as SettingsViewModel);
    }

    private void AttachToVm(SettingsViewModel? vm)
    {
        if (_vm != null)
            _vm.CloseRequested -= OnCloseRequested;

        _vm = vm;

        if (_vm != null)
        {
            _vm.CloseRequested += OnCloseRequested;
            _vm.SetCentralEditDialogFactory(ShowCentralEditDialog);
        }
    }

    private async Task<DccCentralProfile?> ShowCentralEditDialog(DccCentralProfile? existing)
    {
        var editVm = new DccCentralEditViewModel(existing);
        var dialog = new DccCentralEditWindow { DataContext = editVm };
        var accepted = await dialog.ShowDialog<bool>(this);
        return accepted ? editVm.Result : null;
    }


    private void FocusFirstCentralWhenDccTabShown()
    {
        if (_vm == null)
            return;

        if (this.FindControl<TabControl>("SettingsTabs") is not { SelectedIndex: DccTabIndex })
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

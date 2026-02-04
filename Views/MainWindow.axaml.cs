using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using Avalonia.Threading;
using System.Threading.Tasks;
using TrackFlow.ViewModels;
using TrackFlow.Views.Library;
using TrackFlow.Views.Settings;
using TrackFlow.ViewModels.Library;

namespace TrackFlow.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => AttachVm(DataContext as MainWindowViewModel);

        // prvotné pripojenie (ak je DataContext nastavený už v XAML)
        AttachVm(DataContext as MainWindowViewModel);
    }

    private void AttachVm(MainWindowViewModel? vm)
    {
        if (_vm != null)
        {
            // odpojiť delegáty dialógov
            _vm.ShowSettingsDialogAsync = null;
            _vm.ShowOpenProjectPickerAsync = null;
            _vm.ShowSaveProjectPickerAsync = null;
            _vm.ShowLocomotivesDialogAsync = null;

            _vm.ShowVehiclesDialogAsync = null;
            _vm.ShowTrainsDialogAsync = null;

            _vm.RequestProjectHintUpdate = null;
        }

        _vm = vm;
        if (_vm == null)
            return;

        // Bind dashboard DataContext/visibility to SmartStrips selection
        var dashboard = this.FindControl<UserControl>("Dashboard");
        if (dashboard != null)
        {
            // initial state
            dashboard.DataContext = _vm.SmartStrips.SelectedLocomotive;
            dashboard.IsVisible = _vm.SmartStrips.IsLocoSelected;

            // subscribe to selection changes on SmartStripsViewModel
            _vm.SmartStrips.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_vm.SmartStrips.SelectedLocomotive))
                {
                    // update DataContext and visibility when selection changes so dashboard stays visible for the selected loco
                    Dispatcher.UIThread.Post(() =>
                    {
                        dashboard.DataContext = _vm.SmartStrips.SelectedLocomotive;
                        dashboard.IsVisible = _vm.SmartStrips.SelectedLocomotive != null;
                    });
                }
            };

            // SmartStrips selection change handled via SettingsManager event in SmartStripsViewModel.
            _vm.SettingsManager.ProjectChanged += () =>
            {
                // ensure dashboard shows current selection
                Dispatcher.UIThread.Post(() =>
                {
                    dashboard.DataContext = _vm.SmartStrips.SelectedLocomotive;
                    dashboard.IsVisible = _vm.SmartStrips.IsLocoSelected;
                });
            };
        }

        // VM -> View: dialógy
        _vm.ShowSettingsDialogAsync = () => ShowSettingsDialogAsync(_vm);
        _vm.ShowOpenProjectPickerAsync = () => PickOpenProjectPathAsync(_vm);
        _vm.ShowSaveProjectPickerAsync = (suggestedName) => PickSaveProjectPathAsync(_vm, suggestedName);

        // Evidenčné okná
        _vm.ShowLocomotivesDialogAsync = () => ShowLocomotivesDialogAsync();
        _vm.ShowVehiclesDialogAsync = () => ShowVehiclesDialogAsync();
        _vm.ShowTrainsDialogAsync = () => ShowTrainsDialogAsync();

        // VM -> View: refresh hintu po akciách (Open/Save/Settings/…)
        _vm.RequestProjectHintUpdate = UpdateProjectHint;
        _vm.Ribbon.HasOpenProject = !string.IsNullOrWhiteSpace(_vm.SettingsManager.CurrentProjectPath);
        UpdateProjectHint();
    }

    // =====================================================================================
    // Delegáty pre VM: evidenčné dialógy (zatim placeholder)
    // =====================================================================================

    private async Task ShowLocomotivesDialogAsync()
    {
        try
        {
            // vždy na UI threade
            await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            var dlg = new LocomotivesWindow
                            {
                                DataContext = _vm == null ? null : new LocomotivesWindowViewModel(_vm.SettingsManager)
                            }
                            ;

                            await dlg.ShowDialog(this);
                        });
        }
        catch (Exception ex)
        {
            if (_vm != null)
                _vm.StatusBar.Message = "Chyba pri otvorení Lokomotív: " + ex.Message;
            else
                System.Diagnostics.Debug.WriteLine("Chyba pri otvorení Lokomotív: " + ex);
        }
    }

    private async Task ShowVehiclesDialogAsync()
    {
        try
        {
            // Open the real vehicles/wagons editor (VagonsWindow)
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new TrackFlow.Views.Library.VagonsWindow
                {
                    DataContext = _vm == null ? null : new TrackFlow.ViewModels.Library.VagonsWindowViewModel(_vm.SettingsManager)
                };

                await dlg.ShowDialog(this);
            });
        }
        catch (Exception ex)
        {
            if (_vm != null)
                _vm.StatusBar.Message = "Chyba pri otvorení Vozidiel: " + ex.Message;
            else
                System.Diagnostics.Debug.WriteLine("Chyba pri otvorení Vozidiel: " + ex);
        }
    }

    private async Task ShowTrainsDialogAsync()
    {
        var dlg = new TrainsWindow();
        await dlg.ShowDialog(this);
    }

    // =====================================================================================
    // Delegáty pre VM: Settings dialog
    // =====================================================================================

    private async Task<bool> ShowSettingsDialogAsync(MainWindowViewModel vm)
    {
        var owner = this;
        var dlg = new SettingsWindow
        {
            DataContext = vm.Settings
        };

        var result = await dlg.ShowDialog<bool>(owner);
        return result;
    }

    // =====================================================================================
    // Delegáty pre VM: Project file pickery
    // =====================================================================================

    private async Task<string?> PickOpenProjectPathAsync(MainWindowViewModel vm)
    {
        var sp = StorageProvider;
        if (sp == null)
            return null;

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Otvoriť projekt",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("TrackFlow projekt") { Patterns = new[] { "*.json" } },
                FilePickerFileTypes.All
            }
        });

        if (files == null || files.Count == 0)
            return null;

        return files[0].TryGetLocalPath();
    }

    private async Task<string?> PickSaveProjectPathAsync(MainWindowViewModel vm, string suggestedName)
    {
        var sp = StorageProvider;
        if (sp == null)
            return null;

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Uložiť projekt ako…",
            SuggestedFileName = suggestedName,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("TrackFlow projekt") { Patterns = new[] { "*.json" } },
                FilePickerFileTypes.All
            }
        });

        return file?.TryGetLocalPath();
    }

    private void UpdateProjectHint()
    {
        // tu si riešiš vlastný hint v UI (už máš)
    }
}

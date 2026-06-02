using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Avalonia.Interactivity;
using System.Threading.Tasks;
using System.ComponentModel;
using TrackFlow.Services;
using TrackFlow.ViewModels;
using TrackFlow.Views.Library;
using TrackFlow.Views.Settings;
using TrackFlow.ViewModels.Library;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    private DoctorWindow? _doctorWindow;
    private ClockView? _clockView;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Tunnel pointer presses for global debugging of hit tests
        // Pointer pressed handler (debugging removed)
        AddHandler(PointerPressedEvent, (_, _) => { /* debug handler removed */ }, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) => AttachVm(DataContext as MainWindowViewModel);

        // prvotné pripojenie (ak je DataContext nastavený už v XAML)
        AttachVm(DataContext as MainWindowViewModel);
        
        // Ochrana pred stratou neuložených zmien pri zatváraní
        Closing += OnWindowClosing;
        
        // Dispose resources pri zatvorení okna
        Closed += OnWindowClosed;
        
    }

    private void OpenOrFocusDoctorWindow()
    {
        if (_doctorWindow == null || !_doctorWindow.IsVisible)
        {
            _doctorWindow = new DoctorWindow
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            _doctorWindow.Closed += (_, _) =>
            {
                // Avoid holding a reference to a closed/disposed window.
                _doctorWindow = null;
            };

            _doctorWindow.Show(this);
        }
        else
        {
            _doctorWindow.Activate();
        }
    }

    private void OpenOrFocusClockView()
    {
        if (_clockView == null || !_clockView.IsVisible)
        {
            _clockView = new ClockView
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            _clockView.Closed += (_, _) =>
            {
                _clockView = null;
            };

            _clockView.Show(this);
        }
        else
        {
            _clockView.Activate();
        }
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
            _vm.ShowDoctorWindow = null;
            _vm.ShowClockWindow = null;
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
            _vm.SmartStrips.PropertyChanged += (_, e) =>
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
        _vm.ShowConfirmDialogAsync = (title, message) => ShowConfirmDialogAsync(title, message);

        // Evidenčné okná
        _vm.ShowLocomotivesDialogAsync = () => ShowLocomotivesDialogAsync();
        _vm.ShowVehiclesDialogAsync = () => ShowVehiclesDialogAsync();
        _vm.ShowTrainsDialogAsync = () => ShowTrainsDialogAsync();
        _vm.ShowRoutesManagerDialogAsync = () => ShowRoutesManagerDialogAsync();

        // VM -> View: refresh hintu po akciách (Open/Save/Settings/…)
        _vm.RequestProjectHintUpdate = UpdateProjectHint;
        _vm.ShowDoctorWindow = OpenOrFocusDoctorWindow;
        _vm.ShowClockWindow = OpenOrFocusClockView;
        _vm.Ribbon.HasOpenProject = !string.IsNullOrWhiteSpace(_vm.SettingsManager.CurrentProjectPath);
        
        // Sledovanie zmien projektu pre aktualizáciu titulku (dirty flag)
        _vm.SettingsManager.ProjectChanged += OnProjectChanged;
        
        UpdateProjectHint();
    }
    
    private void OnProjectChanged()
    {
        if (_vm != null)
        {
            Dispatcher.UIThread.Post(() => _vm.UpdateWindowTitle());
        }
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
                        DataContext = _vm == null ? null : new LocomotivesWindowViewModel(_vm.SettingsManager, _vm.LayoutEditor.Elements, _vm.Dcc)
                    }
                    ;

                await dlg.ShowDialog(this);
            });
        }
        catch (Exception)
        {
            // Chyby pri otváraní okien sa tu nepoužívajú na oznamovanie do status baru.
            // (Status riadku slúži výhradne pre stav DCC centrály.)
        }
    }

    private async Task ShowVehiclesDialogAsync()
    {
        try
        {
            // Open the real vehicles/wagons editor (VagonsWindow)
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new VagonsWindow
                {
                    DataContext = _vm == null ? null : new VagonsWindowViewModel(_vm.SettingsManager)
                };

                await dlg.ShowDialog(this);
            });
        }
        catch (Exception)
        {
            // Chyby pri otváraní okien sa tu nepoužívajú na oznamovanie do status baru.
            // (Status riadku slúži výhradne pre stav DCC centrály.)
        }
    }

    private async Task ShowTrainsDialogAsync()
    {
        var dlg = new TrainsWindow();
        await dlg.ShowDialog(this);
    }

    private async Task ShowRoutesManagerDialogAsync()
    {
        var vm = new ViewModels.Editor.RoutesManagerViewModel(_vm!.SettingsManager, _vm.Tabs.LayoutEditor, _vm.Tabs.Operation);
        var dlg = new Editor.RoutesManagerWindow { DataContext = vm };
        await dlg.ShowDialog(this);
    }

    // =====================================================================================
    // Delegáty pre VM: Confirm dialog
    // =====================================================================================
    
    private async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var dialog = new Views.Dialogs.ConfirmDialog(title, message);
        await dialog.ShowDialog(this);
        return dialog.Result == Views.Dialogs.ConfirmDialog.DialogResult.Yes;
    }

    // =====================================================================================
    // Delegáty pre VM: Settings dialog
    // =====================================================================================

    private async Task<bool> ShowSettingsDialogAsync(MainWindowViewModel vm)
    {
        var owner = this;
        var settingsVm = vm.CreateSettingsDialogViewModel();
        var dlg = new SettingsWindow
        {
            DataContext = settingsVm
        };

        try
        {
            var result = await dlg.ShowDialog<bool>(owner);
            return result;
        }
        finally
        {
            settingsVm.Dispose();
        }
    }

    // =====================================================================================
    // Delegáty pre VM: Project file pickery
    // =====================================================================================

    private async Task<string?> PickOpenProjectPathAsync(MainWindowViewModel _)
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

        if (files.Count == 0)
            return null;

        return files[0].TryGetLocalPath();
    }

    private async Task<string?> PickSaveProjectPathAsync(MainWindowViewModel _, string suggestedName)
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
    
    // =====================================================================================
    // Ochrana pred stratou neuložených zmien pri zatváraní aplikácie
    // =====================================================================================
    
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            // Ak je VM null alebo nie je otvorený projekt, povoliť zatvorenie
            if (_vm == null || _vm.SettingsManager.CurrentProject == null)
                return;

            // Skontrolovať či má projekt neuložené zmeny
            if (_vm.SettingsManager.CurrentProject.IsDirty != true)
                return; // Žiadne zmeny, povoliť zatvorenie

            // KRITICKÉ: Zrušiť zatvorenie a počkať na užívateľa
            e.Cancel = true;

            // Zobraziť confirm dialog s otázkou
            var message = "Projekt obsahuje neuložené zmeny." + Environment.NewLine + Environment.NewLine + "Chcete zmeny uložiť pred zatvorením?";
            var dialog = new Views.Dialogs.ConfirmDialog(
                "Máte neuložené zmeny",
                message);

            await dialog.ShowDialog(this);

            switch (dialog.Result)
            {
                case Views.Dialogs.ConfirmDialog.DialogResult.Yes:
                    // Uložiť projekt
                    bool saved;
                    if (string.IsNullOrWhiteSpace(_vm.SettingsManager.CurrentProjectPath))
                    {
                        // Ak nie je nastavená cesta, použiť Save As
                        var path = await PickSaveProjectPathAsync(_vm, "projekt.json");
                        if (path != null)
                        {
                            saved = _vm.SettingsManager.SaveProjectAs(path);
                        }
                        else
                        {
                            // Užívateľ zrušil Save As - zostať otvorený
                            return;
                        }
                    }
                    else
                    {
                        // Normálne uloženie
                        saved = _vm.SettingsManager.SaveProject();
                    }

                    if (saved)
                    {
                        // Uloženie úspešné, zatvoriť aplikáciu
                        // SaveProject už nastavil IsDirty = false, takže zatvorenie prejde
                        Close();
                    }
                    // Ak uloženie zlyhalo, e.Cancel zostáva true a okno zostane otvorené
                    break;

                case Views.Dialogs.ConfirmDialog.DialogResult.No:
                    // Zatvoriť bez uloženia: označ projekt ako čistý cez tracker (správna cesta)
                    _vm.SettingsManager.Dirty.MarkClean();
                    Close();
                    break;

                case Views.Dialogs.ConfirmDialog.DialogResult.Cancel:
                    // Zostať otvorený (e.Cancel už je true)
                    break;
            }
        }
        catch (Exception ex)
        {
            e.Cancel = true;
            Program.ReportUnhandledException("MainWindow.OnWindowClosing", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Aplikácia",
                $"⚠️ Zatváranie okna zlyhalo: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);
        }
    }
    
    // =====================================================================================
    // Bezpečné uvoľnenie COM portov (DCC centrály)
    // =====================================================================================

    /// <summary>
    /// Synchrónne a bezpečne zatvorí všetky otvorené sériové porty (COM porty).
    /// Idempotentná – bezpečná pre viacnásobné volanie.
    /// </summary>
    private void TryCleanUpDcc()
    {
        try
        {
            var dcc = _vm?.Dcc;
            if (dcc == null) return;

            System.Diagnostics.Debug.WriteLine("MainWindow.TryCleanUpDcc: uvoľňujem COM porty pred zatvorením okna.");

            try
            {
                dcc.DisconnectAll("window-closing");
                System.Diagnostics.Debug.WriteLine("MainWindow.TryCleanUpDcc: DisconnectAll dokončené.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow.TryCleanUpDcc: chyba DisconnectAll: {ex.Message}");
            }

            try
            {
                (dcc as IDisposable)?.Dispose();
                System.Diagnostics.Debug.WriteLine("MainWindow.TryCleanUpDcc: DccConnectionService.Dispose dokončené.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow.TryCleanUpDcc: chyba Dispose DCC: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow.TryCleanUpDcc: kritická chyba: {ex.Message}");
        }
    }
    
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // KROK 1: Okamžite a bezpečne zatvoriť všetky otvorené COM porty (sériové porty DCC centrál)
        // Toto sa volá ako PRVÉ, ešte pred ostatným čistením, aby OS dostal port čo najskôr späť.
        TryCleanUpDcc();

        // DoctorWindow can keep the app process alive (and remain visible) if left open.
        // Close it explicitly on main window close.
        try
        {
            _doctorWindow?.Close();
        }
        catch
        {
            // best-effort
        }
        finally
        {
            _doctorWindow = null;
        }

        try
        {
            _clockView?.Close();
        }
        catch
        {
            // best-effort
        }
        finally
        {
            _clockView = null;
        }

        // Dispose ViewModel resources to prevent memory leaks
        (_vm as IDisposable)?.Dispose();

        // Extra safety: ensure the desktop lifetime is shut down when the MainWindow closes.
        // This helps when auxiliary windows / background infrastructure would otherwise
        // keep the process alive (IDE still shows the debug session running).
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in new List<Window>(desktop.Windows))
                {
                    if (!ReferenceEquals(window, this))
                    {
                        try { window.Close(); } catch { /* best-effort */ }
                    }
                }

                desktop.Shutdown();
            }
        }
        catch
        {
            // best-effort
        }
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TrackFlow.ViewModels.Library;
using Avalonia.Threading;
using System.ComponentModel;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia;
using Avalonia.Markup.Xaml;
using System;
using LibVLCSharp.Shared;
using TrackFlow.ViewModels;
using TrackFlow.Views.Settings;
using TrackFlow.Views.Dialogs;
using TrackFlow.Services.Dcc;
using TrackFlow.Services;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models.Layout;


namespace TrackFlow.Views.Library;

public partial class LocomotivesWindow : Window
{
    private LocomotivesWindowViewModel? _vm;
    private bool _addressSanitizeGuard;
    private bool _weightSanitizeGuard;
    private bool _powerSanitizeGuard;
    private bool _minRadiusSanitizeGuard;
    private bool _maxSpeedSanitizeGuard;
    private bool _lengthSanitizeGuard;
    private bool _contactPointForwardSanitizeGuard;
    private bool _contactPointBackwardSanitizeGuard;
    private bool _ticksBuilt;
    private bool _ticksHooked;
    private bool _speedChartHooked;
    // Drž delegát referencie aby sme ich mohli korektne odhlasovať pri Closed –
    // inline lambda by zachytávala `this` a zabránila GC okna keď ostane visieť
    // ako handler na livovom Slider.PropertyChanged subjekte.
    private EventHandler<AvaloniaPropertyChangedEventArgs>? _posSliderPropChanged;
    private EventHandler<AvaloniaPropertyChangedEventArgs>? _volSliderPropChanged;
    private Slider? _hookedPosSlider;
    private Slider? _hookedVolSlider;
    private LibVLC? _vlc;
    private MediaPlayer? _player;
    private Media? _media;
    private DispatcherTimer? _posTimer;
    private bool _updatingFromPlayer;
    private bool _userSeeking;

    public LocomotivesWindow()

    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) => { AttachVm(DataContext as LocomotivesWindowViewModel); };

        this.Opened += (_, _) =>
        {
            if (DataContext == null)
                Title = "Editor lokomotív  [DataContext = NULL]";
            // Ticky generuj až po otvorení (layout už prebehol, Canvas má rozmery).
            if (!_ticksBuilt)
            {
                _ticksBuilt = true;
                Dispatcher.UIThread.Post(() =>
                {
                    HookTickRebuild();
                    RequestTickRebuild();
                    HookSpeedChartInteractions();
                });
            }
        };

        Closed += (_, _) =>
        {
            UnhookTickRebuild();
            DisposeSoundPlayer();
        };

        // digits-only pre decoder address
        var box = this.FindControl<TextBox>("AddressBox");
        if (box != null)
        {
            box.AddHandler(TextInputEvent, OnAddressTextInput, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            // Zachytí aj paste / drag-drop / IME – všetko sa prefiltruje na číslice.
            box.TextChanging += OnAddressTextChanging;
        }

        // digits-only + max 3 pre hmotnosť
        var weightBox = this.FindControl<TextBox>("WeightBox");
        if (weightBox != null)
        {
            weightBox.AddHandler(TextInputEvent, OnWeightTextInput, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            // Zachytí aj paste / drag-drop / IME – všetko sa prefiltruje na číslice a skráti na 3 znaky.
            weightBox.TextChanging += OnWeightTextChanging;
        }

        // digits-only + max 3 pre výkon
        var powerBox = this.FindControl<TextBox>("PowerBox");
        if (powerBox != null)
        {
            powerBox.AddHandler(TextInputEvent, OnPowerTextInput, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            powerBox.TextChanging += OnPowerTextChanging;
        }

        // digits-only + max 3 pre min. polomer
        var minRadiusBox = this.FindControl<TextBox>("MinRadiusBox");
        if (minRadiusBox != null)
        {
            minRadiusBox.AddHandler(TextInputEvent, OnMinRadiusTextInput, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            minRadiusBox.TextChanging += OnMinRadiusTextChanging;
        }

        // Max. rýchlosť a dĺžka sú v novom layoute NumericUpDown;
        // fallback zachováva staré správanie pre TextBox pri legacy layoute.
        var maxSpeedBoxText = this.FindControl<Control>("MaxSpeedBox") as TextBox;
        if (maxSpeedBoxText != null)
        {
            maxSpeedBoxText.AddHandler(TextInputEvent, OnMaxSpeedTextInput, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            maxSpeedBoxText.TextChanging += OnMaxSpeedTextChanging;
        }

        var lengthBoxText = this.FindControl<Control>("LengthBox") as TextBox;
        if (lengthBoxText != null)
        {
            lengthBoxText.AddHandler(TextInputEvent, OnLengthTextInput, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            lengthBoxText.TextChanging += OnLengthTextChanging;
        }

        // digits-only + max 2 pre kontaktný bod vpredu
        var contactPointForwardBox = this.FindControl<TextBox>("ContactPointForwardBox");
        if (contactPointForwardBox != null)
        {
            contactPointForwardBox.AddHandler(TextInputEvent, OnContactPointForwardTextInput, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            contactPointForwardBox.TextChanging += OnContactPointForwardTextChanging;
        }

        // digits-only + max 2 pre kontaktný bod vzadu
        var contactPointBackwardBox = this.FindControl<TextBox>("ContactPointBackwardBox");
        if (contactPointBackwardBox != null)
        {
            contactPointBackwardBox.AddHandler(TextInputEvent, OnContactPointBackwardTextInput, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            contactPointBackwardBox.TextChanging += OnContactPointBackwardTextChanging;
        }

        // CV programming buttons (DCC tab)
        // CV programming buttons (DCC tab)
        var readCvButton = this.FindControl<Button>("ReadCvButton");
        if (readCvButton != null)
            readCvButton.Click += ReadCvButton_Click;

        var writeCvButton = this.FindControl<Button>("WriteCvButton");
        if (writeCvButton != null)
            writeCvButton.Click += WriteCvButton_Click;

        var cvTestSlider = this.FindControl<Slider>("CvTestSlider");
        if (cvTestSlider != null)
            cvTestSlider.AddHandler(RangeBase.ValueChangedEvent, OnCvTestSliderChanged, RoutingStrategies.Bubble);
            cvTestSlider.AddHandler(KeyDownEvent, OnCvTestSliderKeyDown, RoutingStrategies.Bubble);

        var minSpeedCvBox = this.FindControl<NumericUpDown>("MinSpeedCvBox");
        if (minSpeedCvBox != null)
        {
            minSpeedCvBox.GotFocus     += (_, _) => OnSpeedCvBoxGotFocus(minSpeedCvBox, 1, 10);
            minSpeedCvBox.LostFocus    += (_, _) => OnSpeedCvBoxLostFocus();
            minSpeedCvBox.ValueChanged += (_, _) => OnSpeedCvBoxValueChanged(minSpeedCvBox);
        }

        var midSpeedCvBox = this.FindControl<NumericUpDown>("MidSpeedCvBox");
        if (midSpeedCvBox != null)
        {
            midSpeedCvBox.GotFocus     += (_, _) => OnSpeedCvBoxGotFocus(midSpeedCvBox, 32, 128);
            midSpeedCvBox.LostFocus    += (_, _) => OnSpeedCvBoxLostFocus();
            midSpeedCvBox.ValueChanged += (_, _) => OnSpeedCvBoxValueChanged(midSpeedCvBox);
        }

        var maxSpeedCvBox = this.FindControl<NumericUpDown>("MaxSpeedCvBox");
        if (maxSpeedCvBox != null)
        {
            maxSpeedCvBox.GotFocus     += (_, _) => OnSpeedCvBoxGotFocus(maxSpeedCvBox, 1, 255);
            maxSpeedCvBox.LostFocus    += (_, _) => OnSpeedCvBoxLostFocus();
            maxSpeedCvBox.ValueChanged += (_, _) => OnSpeedCvBoxValueChanged(maxSpeedCvBox);
        }

        var cv57Box = this.FindControl<NumericUpDown>("Cv57Box");
        if (cv57Box != null)
        {
            cv57Box.GotFocus     += (_, _) => OnSpeedCvBoxGotFocus(cv57Box, 50, 255);
            cv57Box.LostFocus    += (_, _) => OnSpeedCvBoxLostFocus();
            cv57Box.ValueChanged += (_, _) => OnSpeedCvBoxValueChanged(cv57Box);
        }
        
        AttachVm(DataContext as LocomotivesWindowViewModel);
        HookSpeedChartInteractions();
    }

    /// <summary>
    /// Sekvenčne načíta CV2, CV6, CV5, CV3, CV4, CV29 z dekodéra cez práve
    /// pripojenú DCC centrálu a zapíše ich do polí v DCC tabe.
    /// Priebeh sa zobrazuje v <see cref="ReadDecoderValuesWindow"/>.
    /// </summary>
    private void ReadCvButton_Click(object? _, RoutedEventArgs __)
    {
        _ = ReadCvButton_ClickAsync();
    }

    private async Task ReadCvButton_ClickAsync()
    {
        try
        {
            // 1) Sprístupni si DCC centrálu cez MainWindowViewModel.
            if (Owner?.DataContext is not MainWindowViewModel mainVm)
            {
                await ShowReadErrorAsync("Nepodarilo sa získať referenciu na hlavné okno aplikácie.");
                return;
            }

            IDccConnectionService connection = mainVm.Dcc;

            if (!connection.IsConnected || connection.Client is not IDccProgrammingClient programmingClient)
            {
                await ShowReadErrorAsync("DCC centrála nie je pripojená alebo nepodporuje čítanie CV registrov.");
                return;
            }

            // 2) Otvor progres-dialóg.
            var dialog = new ReadDecoderValuesWindow();
            TooltipPreferenceService.Attach(dialog);

            const int timeoutMsPerCv = 5000;
            const int interCvDelayMs = 1500;

            void OnDialogOpened(object? sender, EventArgs args)
            {
                dialog.Opened -= OnDialogOpened;
                _ = StartReadDecoderValuesDialogAsync(dialog, programmingClient, timeoutMsPerCv, interCvDelayMs);
            }

            dialog.Opened += OnDialogOpened;
            await dialog.ShowDialog(this);

            if (dialog.WasCancelled || dialog.Error != null)
                return;

            // 3) Defenzívne doreaplikuj finálny stav po zatvorení dialógu.
            ApplyReadCvValues(dialog.ReadValues);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotivesWindow.ReadCvButton_Click", ex, isTerminating: false);
            await ShowReadErrorAsync(ex.Message);
        }
    }

    // ── CV testovací slider ───────────────────────────────────────────────────

    private string _activeCvTarget = "";
    private NumericUpDown? _activeSpeedCvBox;
    private bool _cvSliderUpdating;

    private void OnSpeedCvBoxGotFocus(NumericUpDown box, double min, double max)
    {
        bool alreadyActive = _activeSpeedCvBox == box;

        _activeSpeedCvBox = box;
        _activeCvTarget = box.Name switch
        {
            "MinSpeedCvBox" => "CV2",
            "MidSpeedCvBox" => "CV6",
            "MaxSpeedCvBox" => "CV5",
            "Cv57Box"       => "CV57",
            _ => ""
        };

        var slider = this.FindControl<Slider>("CvTestSlider");
        if (slider != null)
            slider.IsEnabled = true;

        _cvSliderUpdating = true;

        if (!alreadyActive)
        {
            box.Value = (decimal)min;
            if (slider != null)
            {
                slider.Minimum = min;
                slider.Maximum = max;
                slider.Value = min;
            }
        }
        else
        {
            if (slider != null)
            {
                slider.Minimum = min;
                slider.Maximum = max;
                slider.Value = Math.Clamp(box.Value.HasValue ? (double)box.Value.Value : min, min, max);
            }
        }

        _cvSliderUpdating = false;
    }

    private void OnSpeedCvBoxLostFocus()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var minBox = this.FindControl<NumericUpDown>("MinSpeedCvBox");
            var midBox = this.FindControl<NumericUpDown>("MidSpeedCvBox");
            var maxBox = this.FindControl<NumericUpDown>("MaxSpeedCvBox");
            var cv57Box = this.FindControl<NumericUpDown>("Cv57Box");
            var slider = this.FindControl<Slider>("CvTestSlider");

            bool anyFocused = minBox?.IsKeyboardFocusWithin == true ||
                              midBox?.IsKeyboardFocusWithin == true ||
                              maxBox?.IsKeyboardFocusWithin == true ||
                              cv57Box?.IsKeyboardFocusWithin == true ||
                              slider?.IsKeyboardFocusWithin == true ||
                              slider?.IsFocused == true;

            if (!anyFocused)
            {
                _activeCvTarget = "";
                _activeSpeedCvBox = null;
                if (slider != null)
                {
                    // Slider zakážeme len keď je DCC sekcia enabled (checkbox zaškrtnutý).
                    // Keď nie je, parent kontajner riadi disabled stav — slider.IsEnabled nesmie pridávať vlastnú opacity navyše.
                    slider.IsEnabled = _vm?.IsDccProgrammingEnabled != true;
                    slider.Minimum = 0;
                    slider.Maximum = 255;
                    slider.Value = 0;
                }
            }
        });
    }

    private void OnSpeedCvBoxValueChanged(NumericUpDown box)
    {
        if (_cvSliderUpdating) return;
        if (box != _activeSpeedCvBox) return;

        var slider = this.FindControl<Slider>("CvTestSlider");
        if (slider == null) return;

        var val = Math.Clamp(box.Value.HasValue ? (double)box.Value.Value : slider.Minimum, slider.Minimum, slider.Maximum);
        _cvSliderUpdating = true;
        slider.Value = val;
        _cvSliderUpdating = false;
    }

    private void OnCvTestSliderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && sender is Slider slider)
        {
            slider.Value = 0;
            e.Handled = true;
        }
    }

    private void OnCvTestSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider) return;
        if (!slider.IsEffectivelyEnabled) return;
        if (_activeCvTarget == "") return;
        if (_cvSliderUpdating) return;

        int rawValue = (int)Math.Round(e.NewValue);
        int dccSpeed = Math.Abs(rawValue);
        bool forward = rawValue >= 0;

        _cvSliderUpdating = true;
        if (_activeSpeedCvBox != null)
            _activeSpeedCvBox.Value = (decimal)dccSpeed;
        var loco = _vm?.SelectedLocomotive;
        if (loco != null)
        {
            switch (_activeCvTarget)
            {
                case "CV2":  loco.MinSpeedCv = Math.Clamp(dccSpeed, 1, 10);    break;
                case "CV6":  loco.MidSpeedCv = Math.Clamp(dccSpeed, 32, 128);  break;
                case "CV5":  loco.MaxSpeedCv = Math.Clamp(dccSpeed, 1, 255);   break;
                case "CV57": loco.Cv57       = Math.Clamp(dccSpeed, 50, 255);  break;
            }
        }
        _cvSliderUpdating = false;

        _ = SendCvTestSpeedAsync(dccSpeed, forward);
    }

    private async Task SendCvTestSpeedAsync(int speed, bool forward)
    {
        try
        {
            if (Owner?.DataContext is not MainWindowViewModel mainVm) return;
            if (!mainVm.Dcc.IsConnected || mainVm.Dcc.Client is not IDccCentralClient client) return;
            var loco = _vm?.SelectedLocomotive;
            if (loco == null) return;

            await client.SetLocomotiveSpeedAsync(loco.DccAddress, speed, forward);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotivesWindow.SendCvTestSpeedAsync", ex, isTerminating: false);
        }
    }

    // ── WriteCvButton ─────────────────────────────────────────────────────────

    private void WriteCvButton_Click(object? _, RoutedEventArgs __)
        => _ = WriteCvButton_ClickAsync();

    private async Task WriteCvButton_ClickAsync()
    {
        try
        {
            if (_vm?.SelectedLocomotive == null) return;
            var loco = _vm.SelectedLocomotive;

            if (Owner?.DataContext is not MainWindowViewModel mainVm
                || !mainVm.Dcc.IsConnected
                || mainVm.Dcc.Client is not IDccProgrammingClient)
            {
                await ShowReadErrorAsync("DCC centrála nie je pripojená alebo nepodporuje programovanie CV registrov.");
                return;
            }

            var cvs = new List<(int, int)>
            {
                (2,  loco.MinSpeedCv),
                (6,  loco.MidSpeedCv),
                (5,  loco.MaxSpeedCv),
                (3,  loco.AccelerationCv),
                (4,  loco.BrakingCv),
                (57, loco.Cv57),
            };

            var dialog = new ReadDecoderValuesWindow();
            TooltipPreferenceService.Attach(dialog);

            void OnDialogOpened(object? sender, EventArgs args)
            {
                dialog.Opened -= OnDialogOpened;
                _ = dialog.StartWritingAsync(cvs, (cv, value, ct) => _vm.WriteProgrammingCvAsync(cv, value, ct));
            }

            dialog.Opened += OnDialogOpened;
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            TrackFlowDoctorService.Instance.Diagnose("DCC", $"❌ Zápis CV zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
            Program.ReportUnhandledException("LocomotivesWindow.WriteCvButton_Click", ex, isTerminating: false);
        }
    }

    
    // ── CV57 kalibrácia ───────────────────────────────────────────────────────

    private void Cv57TestButton_Click(object? _, RoutedEventArgs __)
        => _ = Cv57TestButton_ClickAsync();

    private async Task Cv57TestButton_ClickAsync()
    {
        try
        {
            if (Owner?.DataContext is not MainWindowViewModel mainVm) return;
            var loco = _vm?.SelectedLocomotive;
            if (loco == null) return;

            var layout = mainVm.SettingsManager.CurrentProject?.Layout;
            var indicators = BuildCv57Indicators(layout);

            var dialog = new Cv57CalibrationWindow();
            dialog.Initialize(mainVm.Dcc, loco, indicators);
            TooltipPreferenceService.Attach(dialog);
            await dialog.ShowDialog(this);

            if (dialog.FinalCv57Value.HasValue)
                loco.Cv57 = dialog.FinalCv57Value.Value;
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotivesWindow.Cv57TestButton_Click", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Lokomotívy",
                $"⚠️ Otvorenie CV57 kalibračného okna zlyhalo: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);
        }
    }

    private static List<Cv57IndicatorItem> BuildCv57Indicators(TrackLayout? layout)
    {
        var list = new List<Cv57IndicatorItem>();
        if (layout == null) return list;

        foreach (var block in layout.Elements.OfType<BlockElement>())
        {
            foreach (var ind in block.Indicators.Where(i => i.Type == BlockIndicatorType.Contact))
            {
                var blockLabel = string.IsNullOrWhiteSpace(block.Label)
                    ? $"Blok {block.Id[..Math.Min(6, block.Id.Length)]}"
                    : block.Label.Trim();
                var indLabel = string.IsNullOrWhiteSpace(ind.Name)
                    ? $"{blockLabel} [{ind.ModuleAddress}:{ind.PortNumber}]"
                    : $"{ind.Name.Trim()} ({blockLabel})";
                list.Add(new Cv57IndicatorItem(indLabel, ind.ModuleAddress, ind.PortNumber, ind.DccCentralProfileId));
            }
        }
        return list;
    }

    private async Task StartReadDecoderValuesDialogAsync(
        ReadDecoderValuesWindow dialog,
        IDccProgrammingClient programmingClient,
        int timeoutMsPerCv,
        int interCvDelayMs)
    {
        try
        {
            await dialog.StartReadingAsync(programmingClient, timeoutMsPerCv, interCvDelayMs, (cv, resultValue) =>
            {
                Dispatcher.UIThread.Post(() => HandleCvReadSuccess(cv, resultValue));
            });
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotivesWindow.StartReadDecoderValuesDialogAsync", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Lokomotívy",
                $"⚠️ Štart čítania CV v progres-dialógu zlyhal: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);

            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    private void ApplyReadCvValues(IReadOnlyDictionary<int, int> values)
    {
        foreach (var pair in values)
            HandleCvReadSuccess(pair.Key, pair.Value);
    }

    private void HandleCvReadSuccess(int cv, int value)
    {
        var locomotive = _vm?.SelectedLocomotive;
        if (locomotive == null)
            return;

        switch (cv)
        {
            case 2:
                locomotive.MinSpeedCv = value;
                break;
            case 6:
                locomotive.MidSpeedCv = value;
                break;
            case 5:
                locomotive.MaxSpeedCv = value;
                break;
            case 3:
                locomotive.AccelerationCv = value;
                break;
            case 4:
                locomotive.BrakingCv = value;
                break;
            case 57:
                if (_vm != null) _vm.Cv57 = value;
                break;
            case 29:
                locomotive.Cv29Value = value;
                locomotive.IsInvertDirectionEnabled = (value & 0x01) != 0;
                locomotive.IsAnalogOperationEnabled = (value & 0x04) != 0;
                locomotive.IsBemfEnabled = (value & 0x10) != 0;
                break;
        }
    }



    /// <summary>
    /// Pre prípady kedy ani progres-dialóg sa otvoriť nedá (napr. fatal v rámci wiring-u)
    /// – zobrazí svetlé info okno bez tmavej témy ConfirmDialog.
    /// </summary>
    private Task ShowReadErrorAsync(string message)
    {
        var win = new Window
        {
            Title = "Čítanie CV registrov",
            Width = 460,
            Height = 170,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        var ok = new Button { Content = "OK", MinWidth = 90, Height = 28, IsDefault = true, IsCancel = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        ok.Click += (_, _) => win.Close();
        win.Content = new Border
        {
            Padding = new Thickness(20),
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xFA)),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Chyba: " + message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C)),
                        FontSize = 13,
                        Margin = new Thickness(0, 0, 0, 16),
                        [DockPanel.DockProperty] = Dock.Top
                    },
                    ok
                }
            }
        };
        return win.ShowDialog(this);
    }

    private void OpenCalibrationWindow_Click(object? _, Avalonia.Interactivity.RoutedEventArgs __)
    {
        _ = OpenCalibrationWindow_ClickAsync();
    }

    private async Task OpenCalibrationWindow_ClickAsync()
    {
        try
        {
            if (_vm?.SpeedEditor == null)
                return;

            var window = new LocomotiveCalibrationWindow
            {
                DataContext = _vm.SpeedEditor
            };

            await window.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotivesWindow.OpenCalibrationWindow_Click", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Lokomotívy",
                $"⚠️ Otvorenie kalibračného okna zlyhalo: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);
        }
    }

    public void OpenProgrammingTrackSettings_Click(object? _, Avalonia.Interactivity.RoutedEventArgs __)
    {
        _ = OpenProgrammingTrackSettings_ClickAsync();
    }

    private async Task OpenProgrammingTrackSettings_ClickAsync()
    {
        try
        {
            if (Owner?.DataContext is not MainWindowViewModel mainVm)
                return;

            var locomotiveOrAddress = GetSelectedLocomotiveAddressForPom() ?? (object?)_vm?.Selected;
            var settingsVm = mainVm.CreateSettingsDialogViewModel(locomotiveOrAddress);

            var window = new SettingsWindow
            {
                DataContext = settingsVm
            };

            try
            {
                await window.ShowDialog<bool>(this);
            }
            finally
            {
                settingsVm.Dispose();
            }
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotivesWindow.OpenProgrammingTrackSettings_Click", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Lokomotívy",
                $"⚠️ Otvorenie nastavení programovacej koľaje zlyhalo: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);
        }
    }

    private int? GetSelectedLocomotiveAddressForPom()
    {
        if (_vm == null)
            return null;

        if (int.TryParse(_vm.AddressText, out var address) && address is >= 1 and <= 9999)
            return address;

        if (_vm.Selected?.Address is >= 1 and <= 9999)
            return _vm.Selected.Address;

        return null;
    }


    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not LocomotivesWindowViewModel vm)
            return;

        switch (e.PropertyName)
        {
            case nameof(LocomotivesWindowViewModel.FunctionSoundDurationSeconds):
                Dispatcher.UIThread.Post(RequestTickRebuild);
                break;

            case nameof(LocomotivesWindowViewModel.Mode):
                if (vm.Mode == LocomotivesWindowViewModel.EditorMode.Adding)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        var nameBox = this.FindControl<TextBox>("NameBox");
                        nameBox?.Focus();
                        nameBox?.SelectAll();
                    });
                }
                break;

            case nameof(LocomotivesWindowViewModel.FunctionSoundFilePath):
                Dispatcher.UIThread.Post(async () => await LoadSoundAsync(vm.FunctionSoundFilePath));
                break;

            case nameof(LocomotivesWindowViewModel.FunctionSoundIsPlaying):
                Dispatcher.UIThread.Post(() => ApplyPlayState(vm.FunctionSoundIsPlaying));
                break;

            case nameof(LocomotivesWindowViewModel.FunctionSoundVolume):
                Dispatcher.UIThread.Post(() => ApplyVolume(vm.FunctionSoundVolume));
                break;

            case nameof(LocomotivesWindowViewModel.FunctionSoundPosition):
                if (!_updatingFromPlayer && !_userSeeking)
                    Dispatcher.UIThread.Post(() => SeekToSeconds(vm.FunctionSoundPosition));
                break;

            case nameof(LocomotivesWindowViewModel.IsDccProgrammingEnabled):
                Dispatcher.UIThread.Post(() =>
                {
                    var slider = this.FindControl<Slider>("CvTestSlider");
                    if (slider == null) return;
                    if (vm.IsDccProgrammingEnabled)
                        // DCC sekcia sa zapla — slider zakáž, kým niektorý NUD nezíska fokus
                        slider.IsEnabled = _activeSpeedCvBox?.IsKeyboardFocusWithin == true;
                    else
                        // DCC sekcia sa vypla — parent riadi disabled stav, slider nesmie mať vlastný IsEnabled=false
                        slider.IsEnabled = true;
                });
                break;
        }

        UpdateSoundUiEnabled();
    }

    private void OnAddressTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        foreach (var ch in e.Text)
        {
            if (!char.IsDigit(ch))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void OnAddressTextChanging(object? sender, TextChangingEventArgs e)
    {
        if (_addressSanitizeGuard)
            return;

        if (sender is not TextBox box)
            return;

        var text = box.Text ?? string.Empty;

        // Ak sú tam len číslice, nerob nič.
        var allDigits = true;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]))
            {
                allDigits = false;
                break;
            }
        }

        if (allDigits)
            return;

        // Prefiltruj na číslice (funguje aj po paste).
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (char.IsDigit(ch))
                sb.Append(ch);

        var caret = box.CaretIndex;
        _addressSanitizeGuard = true;
        box.Text = sb.ToString();
        box.CaretIndex = caret > box.Text.Length ? box.Text.Length : caret;
        _addressSanitizeGuard = false;
    }

    private void OnWeightTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        // len číslice
        foreach (var ch in e.Text)
        {
            if (!char.IsDigit(ch))
            {
                e.Handled = true;
                return;
            }
        }

        // max 3 znaky (zohľadní aj označený text)
        if (sender is TextBox tb)
        {
            var current = tb.Text ?? string.Empty;
            var selectionLen = tb.SelectionEnd - tb.SelectionStart;
            if (selectionLen < 0) selectionLen = 0;
            var nextLen = (current.Length - selectionLen) + e.Text.Length;
            if (nextLen > 3)
                e.Handled = true;
        }
    }

    private void OnWeightTextChanging(object? sender, TextChangingEventArgs e)
    {
        if (_weightSanitizeGuard)
            return;

        if (sender is not TextBox box)
            return;
        var text = box.Text ?? string.Empty;

        // Prefiltruj na číslice (funguje aj po paste).
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (char.IsDigit(ch))
                sb.Append(ch);

        var filtered = sb.ToString();
        if (filtered.Length > 3)
            filtered = filtered.Substring(0, 3);
        // Ak sa nič nemení, nerob nič.
        if (filtered == text)
            return;

        var caret = box.CaretIndex;
        _weightSanitizeGuard = true;
        box.Text = filtered;
        box.CaretIndex = caret > box.Text.Length ? box.Text.Length : caret;
        _weightSanitizeGuard = false;
    }

    private void OnPowerTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        foreach (var ch in e.Text)
        {
            if (!char.IsDigit(ch))
            {
                e.Handled = true;
                return;
            }
        }

        if (sender is TextBox tb)
        {
            var current = tb.Text ?? string.Empty;
            var selectionLen = tb.SelectionEnd - tb.SelectionStart;
            if (selectionLen < 0) selectionLen = 0;
            var nextLen = (current.Length - selectionLen) + e.Text.Length;
            if (nextLen > 3)
                e.Handled = true;
        }
    }

    private void OnPowerTextChanging(object? sender, TextChangingEventArgs e)
    {
        if (_powerSanitizeGuard)
            return;

        if (sender is not TextBox box)
            return;
        var text = box.Text ?? string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (char.IsDigit(ch))
                sb.Append(ch);

        var filtered = sb.ToString();
        if (filtered.Length > 3)
            filtered = filtered.Substring(0, 3);

        if (filtered == text)
            return;

        var caret = box.CaretIndex;
        _powerSanitizeGuard = true;
        box.Text = filtered;
        box.CaretIndex = caret > box.Text.Length ? box.Text.Length : caret;
        _powerSanitizeGuard = false;
    }

    private void OnMinRadiusTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        foreach (var ch in e.Text)
        {
            if (!char.IsDigit(ch))
            {
                e.Handled = true;
                return;
            }
        }

        if (sender is TextBox tb)
        {
            var current = tb.Text ?? string.Empty;
            var selectionLen = tb.SelectionEnd - tb.SelectionStart;
            if (selectionLen < 0) selectionLen = 0;
            var nextLen = (current.Length - selectionLen) + e.Text.Length;
            if (nextLen > 3)
                e.Handled = true;
        }
    }

    private void OnMinRadiusTextChanging(object? sender, TextChangingEventArgs e)
    {
        if (_minRadiusSanitizeGuard)
            return;

        if (sender is not TextBox box)
            return;
        var text = box.Text ?? string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (char.IsDigit(ch))
                sb.Append(ch);

        var filtered = sb.ToString();
        if (filtered.Length > 3)
            filtered = filtered.Substring(0, 3);

        if (filtered == text)
            return;

        var caret = box.CaretIndex;
        _minRadiusSanitizeGuard = true;
        box.Text = filtered;
        box.CaretIndex = caret > box.Text.Length ? box.Text.Length : caret;
        _minRadiusSanitizeGuard = false;
    }

    private void OnMaxSpeedTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        foreach (var ch in e.Text)
        {
            if (!char.IsDigit(ch))
            {
                e.Handled = true;
                return;
            }
        }

        if (sender is TextBox tb)
        {
            var current = tb.Text ?? string.Empty;
            var selectionLen = tb.SelectionEnd - tb.SelectionStart;
            if (selectionLen < 0) selectionLen = 0;
            var nextLen = (current.Length - selectionLen) + e.Text.Length;
            if (nextLen > 3)
                e.Handled = true;
        }
    }

    private void OnMaxSpeedTextChanging(object? sender, TextChangingEventArgs e)
    {
        if (_maxSpeedSanitizeGuard)
            return;

        if (sender is not TextBox box)
            return;
        var text = box.Text ?? string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (char.IsDigit(ch))
                sb.Append(ch);

        var filtered = sb.ToString();
        if (filtered.Length > 3)
            filtered = filtered.Substring(0, 3);

        if (filtered == text)
            return;

        var caret = box.CaretIndex;
        _maxSpeedSanitizeGuard = true;
        box.Text = filtered;
        box.CaretIndex = caret > box.Text.Length ? box.Text.Length : caret;
        _maxSpeedSanitizeGuard = false;
    }

    private void OnLengthTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        foreach (var ch in e.Text)
        {
            if (!char.IsDigit(ch))
            {
                e.Handled = true;
                return;
            }
        }

        if (sender is TextBox tb)
        {
            var current = tb.Text ?? string.Empty;
            var selectionLen = tb.SelectionEnd - tb.SelectionStart;
            if (selectionLen < 0) selectionLen = 0;
            var nextLen = (current.Length - selectionLen) + e.Text.Length;
            if (nextLen > 3)
                e.Handled = true;
        }
    }

     private void OnLengthTextChanging(object? sender, TextChangingEventArgs e)
     {
         if (_lengthSanitizeGuard)
             return;

         if (sender is not TextBox box)
             return;
         var text = box.Text ?? string.Empty;

         var sb = new StringBuilder(text.Length);
         foreach (var ch in text)
             if (char.IsDigit(ch))
                 sb.Append(ch);

         var filtered = sb.ToString();
         if (filtered.Length > 3)
             filtered = filtered.Substring(0, 3);

         if (filtered == text)
             return;

         var caret = box.CaretIndex;
         _lengthSanitizeGuard = true;
         box.Text = filtered;
         box.CaretIndex = caret > box.Text.Length ? box.Text.Length : caret;
         _lengthSanitizeGuard = false;
     }

     private void OnContactPointForwardTextInput(object? sender, TextInputEventArgs e)
     {
         if (string.IsNullOrEmpty(e.Text))
             return;

         foreach (var ch in e.Text)
         {
             if (!char.IsDigit(ch))
             {
                 e.Handled = true;
                 return;
             }
         }

         if (sender is TextBox tb)
         {
             var current = tb.Text ?? string.Empty;
             var selectionLen = tb.SelectionEnd - tb.SelectionStart;
             if (selectionLen < 0) selectionLen = 0;
             var nextLen = (current.Length - selectionLen) + e.Text.Length;
             if (nextLen > 2)
                 e.Handled = true;
         }
     }

     private void OnContactPointForwardTextChanging(object? sender, TextChangingEventArgs e)
     {
         if (_contactPointForwardSanitizeGuard)
             return;

         if (sender is not TextBox box)
             return;
         var text = box.Text ?? string.Empty;

         var sb = new StringBuilder(text.Length);
         foreach (var ch in text)
             if (char.IsDigit(ch))
                 sb.Append(ch);

         var filtered = sb.ToString();
         if (filtered.Length > 2)
             filtered = filtered.Substring(0, 2);

         if (filtered == text)
             return;

         var caret = box.CaretIndex;
         _contactPointForwardSanitizeGuard = true;
         box.Text = filtered;
         box.CaretIndex = caret > box.Text.Length ? box.Text.Length : caret;
         _contactPointForwardSanitizeGuard = false;
     }

     private void OnContactPointBackwardTextInput(object? sender, TextInputEventArgs e)
     {
         if (string.IsNullOrEmpty(e.Text))
             return;

         foreach (var ch in e.Text)
         {
             if (!char.IsDigit(ch))
             {
                 e.Handled = true;
                 return;
             }
         }

         if (sender is TextBox tb)
         {
             var current = tb.Text ?? string.Empty;
             var selectionLen = tb.SelectionEnd - tb.SelectionStart;
             if (selectionLen < 0) selectionLen = 0;
             var nextLen = (current.Length - selectionLen) + e.Text.Length;
             if (nextLen > 2)
                 e.Handled = true;
         }
     }

     private void OnContactPointBackwardTextChanging(object? sender, TextChangingEventArgs e)
     {
         if (_contactPointBackwardSanitizeGuard)
             return;

         if (sender is not TextBox box)
             return;
         var text = box.Text ?? string.Empty;

         var sb = new StringBuilder(text.Length);
         foreach (var ch in text)
             if (char.IsDigit(ch))
                 sb.Append(ch);

         var filtered = sb.ToString();
         if (filtered.Length > 2)
             filtered = filtered.Substring(0, 2);

         if (filtered == text)
             return;

         var caret = box.CaretIndex;
         _contactPointBackwardSanitizeGuard = true;
         box.Text = filtered;
         box.CaretIndex = caret > box.Text.Length ? box.Text.Length : caret;
         _contactPointBackwardSanitizeGuard = false;
     }


     private void FocusLocoGrid(LocomotivesWindowViewModel vm)
    {
        var grid = this.FindControl<DataGrid>("LocoGrid");
        if (grid == null || vm.Selected == null) return;
        
        Dispatcher.UIThread.Post(() =>
        {
            var target = vm.Selected;
            if (target == null) return;
            grid.UpdateLayout();
            grid.ScrollIntoView(target, null);
            grid.Focus();
        }, DispatcherPriority.Background);
    }
    
    private void AttachVm(LocomotivesWindowViewModel? vm)
    {
        if (_vm == vm)
            return;

        if (_vm != null)
        {
            _vm.PropertyChanged -= VmOnPropertyChanged;
            _vm.RequestClose = null;
            _vm.RequestFocusOnLoco = null;
        }

        _vm = vm;
        if (_vm == null)
            return;

        _vm.PropertyChanged += VmOnPropertyChanged;
        _vm.RequestClose = Close;
        _vm.RequestFocusOnLoco = () =>
            Dispatcher.UIThread.Post(() => FocusLocoGrid(_vm));
        HookSoundSlidersOnce();
        HookSpeedChartInteractions();
        UpdatePlayStopIcons();
    }

    private void HookSpeedChartInteractions()
    {
        if (_speedChartHooked)
            return;

        var chartCanvas = this.FindControl<Canvas>("SpeedProfileChartInteractionCanvas")
            ?? this.FindControl<Canvas>("SpeedProfileChartCanvas");
        if (chartCanvas == null)
            return;

        chartCanvas.PointerPressed += OnSpeedChartPointerPressed;
        chartCanvas.PointerMoved += OnSpeedChartPointerMoved;
        chartCanvas.PointerReleased += OnSpeedChartPointerReleased;
        chartCanvas.PointerCaptureLost += OnSpeedChartPointerCaptureLost;
        _speedChartHooked = true;
    }

    private void OnSpeedChartPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            if (sender is not Canvas chartCanvas || _vm?.SpeedEditor == null)
                return;

            var handled = _vm.SpeedEditor.HandleChartPointerPressed(e.GetPosition(chartCanvas));
            if (!handled)
                return;

            if (_vm.SpeedEditor.IsDraggingChartPoint)
                e.Pointer.Capture(chartCanvas);

            e.Handled = true;
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotivesWindow.OnSpeedChartPointerPressed", ex, isTerminating: false);
            _vm?.SpeedEditor.ReportChartInteractionFailure("Klik do grafu", ex);
            e.Handled = true;
        }
    }

    private void OnSpeedChartPointerMoved(object? sender, PointerEventArgs e)
    {
        try
        {
            if (sender is not Canvas chartCanvas || _vm?.SpeedEditor == null)
                return;

            if (_vm.SpeedEditor.HandleChartPointerMoved(e.GetPosition(chartCanvas)))
                e.Handled = true;
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotivesWindow.OnSpeedChartPointerMoved", ex, isTerminating: false);
            _vm?.SpeedEditor.ReportChartInteractionFailure("Presun bodu v grafe", ex);
            e.Handled = true;
        }
    }

    private void OnSpeedChartPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (_vm?.SpeedEditor == null)
                return;

            _vm.SpeedEditor.HandleChartPointerReleased();

            if (e.Pointer.Captured == sender)
                e.Pointer.Capture(null);

            e.Handled = true;
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotivesWindow.OnSpeedChartPointerReleased", ex, isTerminating: false);
            _vm?.SpeedEditor.ReportChartInteractionFailure("Ukončenie úpravy grafu", ex);
            e.Handled = true;
        }
    }

    private void OnSpeedChartPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        try
        {
            _vm?.SpeedEditor.HandleChartPointerReleased();
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotivesWindow.OnSpeedChartPointerCaptureLost", ex, isTerminating: false);
            _vm?.SpeedEditor.ReportChartInteractionFailure("Strata zachytenia kurzora v grafe", ex);
        }
    }

    // Image picker removed

    private void HookTickRebuild()
    {
        if (_ticksHooked) return;
        _ticksHooked = true;

        var posSlider = this.FindControl<Slider>("PosSlider");
        if (posSlider != null)
        {
            _hookedPosSlider = posSlider;
            // prekresli pri zmene maximum (po načítaní súboru) aj pri zmene veľkosti
            _posSliderPropChanged = (_, e) =>
            {
                if (e.Property == RangeBase.MaximumProperty || e.Property == Visual.BoundsProperty)
                    Dispatcher.UIThread.Post(RequestTickRebuild);
            };
            posSlider.PropertyChanged += _posSliderPropChanged;
        }

        var volSlider = this.FindControl<Slider>("VolSlider");
        if (volSlider != null)
        {
            _hookedVolSlider = volSlider;
            _volSliderPropChanged = (_, e) =>
            {
                if (e.Property == Visual.BoundsProperty)
                    Dispatcher.UIThread.Post(RequestTickRebuild);
            };
            volSlider.PropertyChanged += _volSliderPropChanged;
        }
    }

    private void UnhookTickRebuild()
    {
        if (_hookedPosSlider != null && _posSliderPropChanged != null)
            _hookedPosSlider.PropertyChanged -= _posSliderPropChanged;
        if (_hookedVolSlider != null && _volSliderPropChanged != null)
            _hookedVolSlider.PropertyChanged -= _volSliderPropChanged;

        _hookedPosSlider = null;
        _hookedVolSlider = null;
        _posSliderPropChanged = null;
        _volSliderPropChanged = null;
        _ticksHooked = false;
    }

    private void BuildSoundSliderTicks()
    {
        var posCanvas = this.FindControl<Canvas>("PosTicksCanvas");
        var posSlider = this.FindControl<Slider>("PosSlider");
        if (posCanvas != null && posSlider != null)
        {
            // Pozícia: 0..DurationSeconds (fallback 50), major každých 10s, minor každé 2s
            double dur = _vm?.FunctionSoundDurationSeconds ?? 0.0;
            int max = (int)Math.Ceiling(dur);
            if (max <= 0) max = 50; // kým ešte nepoznáme dĺžku, použijeme placeholder
            
            posSlider.Minimum = 0; 
            posSlider.Maximum = max; 
            posSlider.Value = 0;
            

            BuildTicks(posCanvas, max: max, majorStep: 10, minorStep: 2, majorLen: 12, minorLen: 6);
        }

        var volCanvas = this.FindControl<Canvas>("VolTicksCanvas");
        if (volCanvas != null)
        {
            // Hlasitosť: 0..10, major každých 10
            BuildTicks(volCanvas, max: 10, majorStep: 10, minorStep: 1, majorLen: 12, minorLen: 6);
        }
    }
    
    private bool _pendingTickRebuild;

    private void RequestTickRebuild()
    {
        if (_pendingTickRebuild)
            return;

        _pendingTickRebuild = true;

        Dispatcher.UIThread.Post(() =>
        {
            _pendingTickRebuild = false;
            BuildSoundSliderTicks();
        });
    }

    

    private static void BuildTicks(Canvas canvas, int max, int majorStep, int minorStep, double majorLen,
        double minorLen)
    {
        canvas.Children.Clear();

        if (max <= 0 || minorStep <= 0)
            return;

        // Uprednostni explicitnú Width/Height z XAML, fallback na Bounds.
        var width = canvas.Width > 0 ? canvas.Width : canvas.Bounds.Width;
        var height = canvas.Height > 0 ? canvas.Height : canvas.Bounds.Height;

        if (width <= 0 || height <= 0)
            return;

        var stepPx = width / max;
        var stroke = new SolidColorBrush(Color.Parse("#666666"));

        for (int v = 0; v <= max; v += minorStep)
        {
            var isMajor = (majorStep > 0) && (v % majorStep == 0);
            var len = isMajor ? majorLen : minorLen;

            var x = v * stepPx;
            // ticky smerom DOLE (z hora nadol)
            var y1 = 0.0;
            var y2 = len;

            var line = new Line
            {
                StartPoint = new Point(x, y1),
                EndPoint = new Point(x, y2),
                Stroke = stroke,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };

            canvas.Children.Add(line);

            // Čísla pod dlhými tickmi: 0, 10, 20, ...
            // (0 chceš mať pri oboch slideroch)
            if (isMajor)
            {
                var tb = new TextBlock
                {
                    Text = v.ToString(),
                    FontSize = 11,
                    Foreground = stroke,
                    Width = 26,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(tb, x - (tb.Width / 2));
                Canvas.SetTop(tb, len + 2);
                canvas.Children.Add(tb);
            }
        }
    }

    private void HookSoundSlidersOnce()
    {
        var pos = this.FindControl<Slider>("PosSlider");
        if (pos != null)
        {
            pos.PointerPressed += (_, _) => _userSeeking = true;
            pos.PointerReleased += (_, _) =>
            {
                _userSeeking = false;
                if (_vm != null) SeekToSeconds(_vm.FunctionSoundPosition);
            };
        }
    }

    private void EnsureSoundPlayer()
    {
        if (_vlc != null && _player != null)
            return;

        Core.Initialize();
        _vlc = new LibVLC();
        _player = new MediaPlayer(_vlc);
        _player.EndReached += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_vm == null) return;
                _vm.FunctionSoundIsPlaying = false;
                _vm.FunctionSoundPosition  = 0;
                UpdatePlayStopIcons();
            });
        };

        _posTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _posTimer.Tick += (_, _) => SyncPositionFromPlayer();
    }

    private async System.Threading.Tasks.Task LoadSoundAsync(string? path)
    {
        StopInternal(resetPosition: true);
        UpdatePlayStopIcons();

        if (_vm == null)
            return;

        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            _vm.FunctionSoundDurationSeconds = 0;
            _vm.FunctionSoundIsLoaded = false;
            RequestTickRebuild();
            return;
        }

        EnsureSoundPlayer();
        if (_vlc == null || _player == null)
            return;

        _media?.Dispose();
        _media = new Media(_vlc, path, FromType.FromPath);

        try
        {
            await _media.Parse(MediaParseOptions.ParseLocal);
        }
        catch
        {
            // parse môže zlyhať na niektorých formátoch; necháme duration=0
        }

        var durMs = _media.Duration;
        var durSec = durMs > 0 ? (int)Math.Max(1, Math.Round(durMs / 1000.0)) : 0;

        _player.Media = _media;
        try { _player.Time = 0; } catch { }
        ApplyVolume(_vm.FunctionSoundVolume);

        _vm.FunctionSoundDurationSeconds = durSec;
        _vm.FunctionSoundIsLoaded = durSec > 0;
        _vm.FunctionSoundPosition = 0;
        UpdateSoundUiEnabled();
        RequestTickRebuild();
    }

    private void ApplyPlayState(bool playing)
    {
        if (_vm == null)
            return;

        if (playing)
        {
            if (string.IsNullOrWhiteSpace(_vm.FunctionSoundFilePath))
            {
                _vm.FunctionSoundIsPlaying = false;
                UpdatePlayStopIcons();
                return;
            }

            EnsureSoundPlayer();
            if (_player == null)
                return;

            if (_player.Media == null)
            {
                // fallback: načítaj (sync) – keď property change nestihol
                Dispatcher.UIThread.Post(async () => await LoadSoundAsync(_vm.FunctionSoundFilePath));
                return;
            }

            ApplyVolume(_vm.FunctionSoundVolume);
            _player.Play();
            // ▶️ Spusti timer len pri prehrávaní
            _posTimer?.Start();
        }
        else
        {
            // ⏹️ Pri zastavení prehrávania timer vypnúť
            if (!playing) _posTimer?.Stop();
            StopInternal(resetPosition: false);
        }

        UpdatePlayStopIcons();
        UpdateSoundUiEnabled();
    }

    private void StopInternal(bool resetPosition)
    {
        if (_player == null)
            return;

        if (_player.IsPlaying)
            _player.Stop();
        // ⏹️ Stop timer aj pri hard-stop
        _posTimer?.Stop();

        if (resetPosition)
        {
            try
            {
                _player.Time = 0;
            }
            catch
            {
            }
        }
    }

    private void SeekToSeconds(double seconds)
    {
        if (_player == null || _player.Media == null)
            return;

        var ms = (long)Math.Max(0, seconds * 1000.0);
        try
        {
            _player.Time = ms;
        }
        catch
        {
        }
    }

    private void ApplyVolume(double uiVol0to10)
    {
        if (_player == null)
            return;

        var v = (int)Math.Round(Math.Clamp(uiVol0to10, 0, 10) * 10.0); // 0..100
        try
        {
            _player.Volume = v;
        }
        catch
        {
        }
    }

    private void SyncPositionFromPlayer()
    {
        if (_vm == null || _player == null || _player.Media == null)
            return;

        // Kľúčové: keď sa neprehráva, NEPREPISUJ VM pozíciu.
        if (!_player.IsPlaying || !_vm.FunctionSoundIsPlaying)
            return;

        var ms = _player.Time;
        if (ms < 0) ms = 0;

        _updatingFromPlayer = true;
        try
        {
            _vm.FunctionSoundPosition = ms / 1000.0;
        }
        finally
        {
            _updatingFromPlayer = false;
        }
    }

    private void UpdatePlayStopIcons()
    {
        var play = this.FindControl<Path>("PlayIcon");
        var stop = this.FindControl<Rectangle>("StopIcon");
        if (play == null || stop == null || _vm == null)
            return;

        var isPlaying = _vm.FunctionSoundIsPlaying;
        play.IsVisible = !isPlaying;
        stop.IsVisible = isPlaying;
    }

    private void DisposeSoundPlayer()
    {
        try
        {
            _posTimer?.Stop();
        }
        catch
        {
        }

        _posTimer = null;

        try
        {
            _player?.Dispose();
        }
        catch
        {
        }

        _player = null;

        try
        {
            _media?.Dispose();
        }
        catch
        {
        }

        _media = null;

        try
        {
            _vlc?.Dispose();
        }
        catch
        {
        }

        _vlc = null;
    }

    private void UpdateSoundUiEnabled()
    {
        bool enabled = _vm?.FunctionSoundIsLoaded == true;

        var posSlider = this.FindControl<Slider>("PosSlider");
        var volSlider = this.FindControl<Slider>("VolSlider");
        var playStopButton = this.FindControl<ToggleButton>("PlayStopButton");

        if (posSlider != null)
            posSlider.IsEnabled = enabled;

        if (volSlider != null)
            volSlider.IsEnabled = enabled;

        if (playStopButton != null)
            playStopButton.IsEnabled = enabled;
    }

    private async void OnBrowseSoundFileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_vm == null)
                return;

            var sp = this.StorageProvider;
            if (sp == null)
                return;

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Vyberte zvukový súbor",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Zvukové súbory")
                    {
                        Patterns = new List<string>
                        {
                            "*.wav", "*.mp3", "*.ogg", "*.flac", "*.aac", "*.m4a", "*.wma"
                        }
                    },
                    FilePickerFileTypes.All
                }
            });

            var f = files?.FirstOrDefault();
            if (f == null)
                return;

            var path = f.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(path))
                path = f.Path.ToString();

            _vm.FunctionSoundIsPlaying = false;
            _vm.FunctionSoundPosition  = 0;
            _vm.FunctionSoundFilePath  = path;
            UpdateSoundUiEnabled();
            Dispatcher.UIThread.Post(RequestTickRebuild);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotivesWindow.OnBrowseSoundFileClick", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Lokomotívy",
                $"⚠️ Výber zvukového súboru zlyhal: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);
        }

    }
    
}
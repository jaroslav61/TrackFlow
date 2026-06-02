using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using IOPath = System.IO.Path;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Ribbon;
using TrackFlow.Services;
using TrackFlow.Services.Dcc;
using TrackFlow.ViewModels.Backstage;
using TrackFlow.ViewModels.Cab;
using TrackFlow.ViewModels.Dcc;
using TrackFlow.ViewModels.Editor;
using TrackFlow.ViewModels.SmartStrips;
using TrackFlow.ViewModels.Settings;
using TrackFlow.Views;
using TrackFlow.Views.Dcc;

namespace TrackFlow.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    
    private static string CN(DccCentralType t) => DccCentralDisplayName.Get(t);

    // =====================================================================================
    // Režim aplikácie (Editor / Prevádzka)
    // =====================================================================================
    
    [ObservableProperty]
    private AppMode currentMode = AppMode.Editor;
    
    partial void OnCurrentModeChanged(AppMode value)
    {
        // Aktualizovať status bar
        StatusBar.SetOperationMode(value == AppMode.Operation);
        
        // Aktualizovať Ribbon (pre budúce dynamické tlačidlá)
        OnPropertyChanged(nameof(IsEditorMode));
        OnPropertyChanged(nameof(IsOperationMode));

        // Synchronizovať aktívne lokomotívy podľa režimu:
        // - Operation: automaticky aktivovať lokomotívy v blokoch (zobrazí sa Dashboard, opacity 100%)
        // - Editor: deaktivovať všetky (Dashboard sa skryje, opacity v smart páse sa zníži)
        SmartStrips?.SyncActiveLocomotivesWithMode(value);

        // Pri prepnutí do prevádzky vždy načítaj aktuálne elementy z projektu,
        // aby sa zobrazili aj novo vložené markery bez nutnosti explicitného SaveToProject.
        if (value == AppMode.Operation)
        {
            Tabs.Operation.RefreshLayoutFromProject();
            _ = RefreshSignalsOnOperationEnterAsync();
        }

        // Undo/Redo je aktuálne implementované pre layout editor, dostupné iba v Editor režime.
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }
    
    public bool IsEditorMode => CurrentMode == AppMode.Editor;
    public bool IsOperationMode => CurrentMode == AppMode.Operation;

    // =====================================================================================
    // StatusBar message policy (priorita + sticky + TTL) – aby sa správy neprepisovali chaoticky
    // =====================================================================================

    private enum StatusTopic
    {
        None = 0,
        Disconnected = 10,
        InfoToast = 20,
        UserDisconnected = 25,
        Connecting = 30,
        ConnectFailedToast = 40,
        ConnectionLost = 50,
        Reconnecting = 80,
        Connected = 90,
        ReconnectTimeout = 95
    }

    private sealed class StatusMessagePolicy
    {
        private int _currentPriority;
        private bool _currentSticky;
        private CancellationTokenSource? _ttlCts;

        public void ResetAll()
        {
            ClearTtl();
            _currentPriority = (int)StatusTopic.None;
            _currentSticky = false;
        }

        public void ClearTtl()
        {
            try { _ttlCts?.Cancel(); } catch { }
            try { _ttlCts?.Dispose(); } catch { }
            _ttlCts = null;
        }

        public bool TrySet(
            StatusTopic topic,
            string message,
            Action<string> apply,
            Action? onTtlExpired = null,
            int? ttlMs = null,
            bool sticky = false)
        {
            var pr = (int)topic;

            // Sticky správa (napr. Reconnecting) – nižšia priorita ju nesmie prepísať.
            if (_currentSticky && pr < _currentPriority)
                return false;

            // Bežná priorita
            if (pr < _currentPriority)
                return false;

            _currentPriority = pr;
            _currentSticky = sticky;

            ClearTtl();
            apply(message);

            if (ttlMs.HasValue && ttlMs.Value > 0)
            {
                _ttlCts = new CancellationTokenSource();
                var ct = _ttlCts.Token;

                _ = Task.Run(async () =>
                {
                    try { await Task.Delay(ttlMs.Value, ct); }
                    catch { return; }

                    // Po TTL uvoľníme prioritu – ďalší event nastaví "stavovú" hlášku.
                    _currentPriority = (int)StatusTopic.None;
                    _currentSticky = false;
                    onTtlExpired?.Invoke();
                }, ct);
            }

            return true;
        }
    }

    // Jedna inštancia pre celú VM
    private readonly StatusMessagePolicy _statusPolicy = new();

    private EffectiveSettings? _settingsBeforeEdit;
    private DccTelemetryWidget? _telemetryWidget;
    private DccTelemetryWidgetViewModel? _telemetryWidgetViewModel;

    // View sem pripojí dialógy (VM nepozná Avalonia UI / StorageProvider)
    public Func<string, string, Task<bool>>? ShowConfirmDialogAsync { get; set; }
    public Func<Task<bool>>? ShowSettingsDialogAsync { get; set; }
    public Func<Task<string?>>? ShowOpenProjectPickerAsync { get; set; }
    public Func<string, Task<string?>>? ShowSaveProjectPickerAsync { get; set; }

    // View sem pripojí dialógy pre evidenciu (Lokomotívy / Vozidlá / Vlaky)
    public Func<Task>? ShowLocomotivesDialogAsync { get; set; }
    public Func<Task>? ShowVehiclesDialogAsync { get; set; }
    public Func<Task>? ShowTrainsDialogAsync { get; set; }
    public Func<Task>? ShowRoutesManagerDialogAsync { get; set; }

    // View sem pripojí aktualizáciu hintu (VM nevie nič o View)
    public Action? RequestProjectHintUpdate { get; set; }

    // View sem pripojí otvorenie okna diagnostiky
    public Action? ShowDoctorWindow { get; set; }

    // View sem pripojí otvorenie okna modelových hodín
    public Action? ShowClockWindow { get; set; }

    [RelayCommand]
    private void OpenDoctorWindow()
    {
        ShowDoctorWindow?.Invoke();
    }

    [RelayCommand]
    private void OpenClockWindow()
    {
        SyncTimeServiceFromSettings();
        ShowClockWindow?.Invoke();
    }

    [RelayCommand]
    private void OpenTelemetryWidget(StatusBarCentralItem? item)
    {
        if (item == null || !item.IsTelemetrySupported || item.TelemetrySource == null)
            return;

        // Widget je jednorazové okno: pred otvorením ďalšieho zavrieme predošlé.
        CloseTelemetryWidget();

        // Otvorenie odložíme o jednu iteráciu UI loopu, aby dobehol pôvodný click chain.
        var capturedName = item.Name;
        var capturedSource = item.TelemetrySource;
        var capturedMainTrackCurrentLimit = item.MainTrackCurrentLimitAmperes;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_disposed)
                    return;

                var widget = new DccTelemetryWidget();
                _telemetryWidget = widget;
                widget.Closed += OnTelemetryWidgetClosed;

                SetTelemetryWidgetDataContext(capturedName, capturedSource, capturedMainTrackCurrentLimit);
                widget.Show();
                widget.Activate();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open DCC telemetry widget.");
            }
        }, DispatcherPriority.Background);
    }

    public SettingsManager SettingsManager { get; }
    public DccConnectionService Dcc { get; }
    public LocoDccBridge LocoDccBridge { get; }

    public MainRibbonViewModel Ribbon { get; }
    public CabStripHostViewModel CabHost { get; }
    public MainTabsViewModel Tabs { get; }
    public StatusBarViewModel StatusBar { get; }
    public SettingsViewModel Settings { get; }
    public SmartStripsViewModel SmartStrips { get; }
    public FileBackstageViewModel FileBackstage { get; }

    /// <summary>Skratka pre Ribbon – priamy prístup k LayoutEditorViewModel.</summary>
    public LayoutEditorViewModel LayoutEditor => Tabs.LayoutEditor;

    [ObservableProperty]
    private bool isFileBackstageOpen;
    
    private string _windowTitle = "TrackFlow";
    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    public MainWindowViewModel()
    {
        SettingsManager = new SettingsManager();
        // Load settings/project at startup
        SettingsManager.LoadApp();

        Ribbon = new MainRibbonViewModel();
        CabHost = new CabStripHostViewModel();
        StatusBar = new StatusBarViewModel();
        Dcc = new DccConnectionService(SettingsManager);
        Settings = new SettingsViewModel(SettingsManager, dccConnectionService: Dcc);
        SyncTimeServiceFromSettings();
        SmartStrips = new SmartStripsViewModel(SettingsManager);
        Settings.SetDccTestLocomotiveProvider(() => SmartStrips.SelectedLocomotive, SmartStrips);
        
        // Create Tabs with shared Locomotives collection from SmartStrips
        Tabs = new MainTabsViewModel(SettingsManager, SmartStrips.Locomotives);

        Tabs.LayoutEditor.UndoRedoStateChanged += () =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };
        
        FileBackstage = new FileBackstageViewModel(this);

        // Prepojenie SmartStrips ↔ LayoutEditor: after loco drop → badge "on-track"
        SmartStrips.LinkLayoutEditor(Tabs.LayoutEditor);

        Dcc.ConnectionStateChanged += OnDccConnectionStateChanged;
        Dcc.FeedbackStateChanged += OnDccFeedbackStateChanged;

        // Inicializácia stavového riadku so zoznamom centrál
        RefreshStatusBarCentrals();

        LocoDccBridge = new LocoDccBridge(Dcc);
        LocoDccBridge.Attach(SmartStrips.Locomotives);
        // init stavu ribbonu podľa toho, či je otvorený projekt
        Ribbon.HasOpenProject = SettingsManager.CurrentProjectPath != null;
        
        // ✅ Automatické otvorenie posledného projektu alebo vytvorenie nového
        if (SettingsManager.App.OpenLastProjectOnStartup && 
            !string.IsNullOrWhiteSpace(SettingsManager.App.LastProjectPath) &&
            System.IO.File.Exists(SettingsManager.App.LastProjectPath))
        {
            // Otvorí posledný projekt
            try
            {
                SettingsManager.OpenProject(SettingsManager.App.LastProjectPath);
                Settings.Load();
                Tabs.Operation.RefreshFromProject();
                Tabs.LayoutEditor.LoadFromProject();
                Ribbon.HasOpenProject = true;
                UpdateWindowTitle();
            }
            catch
            {
                // Ak zlyhá otvorenie, vytvor nový projekt
                CreateNewProject();
            }
        }
        else
        {
            // Vytvorí nový čistý projekt
            CreateNewProject();
        }

        // príkazy
        Ribbon.HasOpenProject = !string.IsNullOrWhiteSpace(SettingsManager.CurrentProjectPath);

        // Pripojenie eventu: Po uložení schémy v Editore aktualizuj Prevádzku
        Tabs.LayoutEditor.LayoutSaved += () =>
        {
            Tabs.Operation.RefreshLayoutFromProject();
        };

        // Step 10: Centralizovaný dirty tracker → titul + status hint sa obnovia z jedného miesta
        SettingsManager.Dirty.DirtyChanged += OnProjectDirtyChanged;
        SettingsManager.ProjectChanged += SyncTimeServiceFromSettings;
    }

    private void SyncTimeServiceFromSettings()    {
        TimeService.Instance.SimulationSpeedFactor =
            SettingsManager.CurrentProject?.Settings.SimulationSpeedFactor
            ?? ProjectSettingsData.DefaultSimulationSpeedFactor;
    }

    private void OnProjectDirtyChanged()
    {
        try
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                UpdateWindowTitle();
                StatusBar.IsProjectDirty = SettingsManager.CurrentProject?.IsDirty == true;
                RequestProjectHintUpdate?.Invoke();
            }
            else
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    UpdateWindowTitle();
                    StatusBar.IsProjectDirty = SettingsManager.CurrentProject?.IsDirty == true;
                    RequestProjectHintUpdate?.Invoke();
                });
            }
        }
        catch { /* best-effort UI update */ }
    }

    [ObservableProperty]
    private bool isDashboardVisible;

    [RelayCommand]
    private void ToggleDashboard()
    {
        IsDashboardVisible = !IsDashboardVisible;
    }

    // =====================================================================================
    // Súbor (Backstage)
    // =====================================================================================

    [RelayCommand]
    private void OpenFileBackstage()
    {
        FileBackstage.ReloadRecent();
        IsFileBackstageOpen = true;
    }

    [RelayCommand]
    private void CloseFileBackstage()
    {
        IsFileBackstageOpen = false;
    }

    /// <summary>Aktualizuje titul okna podľa otvoreného projektu.</summary>
    public void UpdateWindowTitle()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "1.0.0";
        
        var dirtyMarker = (SettingsManager.CurrentProject?.IsDirty == true) ? "*" : "";
        
        if (string.IsNullOrWhiteSpace(SettingsManager.CurrentProjectPath))
        {
            // Nový projekt bez uloženého súboru - zobraz default názov
            WindowTitle = $"TrackFlow v{version} - MyLayout{dirtyMarker}";
        }
        else
        {
            var projectName = IOPath.GetFileNameWithoutExtension(SettingsManager.CurrentProjectPath);
            WindowTitle = $"TrackFlow v{version} - {projectName}{dirtyMarker}";
        }
    }

    private void SetTelemetryWidgetDataContext(string name, IDccTelemetry telemetrySource, double? configuredMainTrackCurrentLimitAmperes)
    {
        var nextVm = new DccTelemetryWidgetViewModel(name, telemetrySource, configuredMainTrackCurrentLimitAmperes);
        var previousVm = _telemetryWidgetViewModel;

        _telemetryWidgetViewModel = nextVm;

        if (_telemetryWidget != null)
            _telemetryWidget.DataContext = nextVm;

        if (previousVm != null && !ReferenceEquals(previousVm, nextVm))
            previousVm.Dispose();
    }

    private void OnTelemetryWidgetClosed(object? sender, EventArgs e)
    {
        if (sender is DccTelemetryWidget widget)
            widget.Closed -= OnTelemetryWidgetClosed;

        _telemetryWidgetViewModel?.Dispose();
        _telemetryWidgetViewModel = null;
        _telemetryWidget = null;
    }


    private void CloseTelemetryWidget()
    {
        var widget = _telemetryWidget;
        _telemetryWidget = null;

        if (widget != null)
        {
            widget.Closed -= OnTelemetryWidgetClosed;
            try { widget.Close(); } catch { }
        }

        _telemetryWidgetViewModel?.Dispose();
        _telemetryWidgetViewModel = null;
    }
    
    /// <summary>
    /// Skontroluje, či má aktuálny projekt neuložené zmeny a zobrazí dialóg.
    /// Vráti true, ak môže pokračovať (projekt uložený alebo užívateľ zvolil Nie).
    /// Vráti false, ak užívateľ zrušil operáciu.
    /// </summary>
    private async Task<bool> CheckUnsavedChangesAsync()
    {
        if (SettingsManager.CurrentProject?.IsDirty != true)
            return true; // Žiadne neuložené zmeny
            
        if (ShowConfirmDialogAsync == null)
            return true; // Dialóg nie je k dispozícii, pokračuj
            
        var result = await ShowConfirmDialogAsync(
            "Neuložené zmeny",
            "Projekt má neuložené zmeny. Chcete ich uložiť?"
        );
        
        if (result)
        {
            // Užívateľ zvolil Áno - uložiť
            if (string.IsNullOrWhiteSpace(SettingsManager.CurrentProjectPath))
            {
                // Nový projekt - zavolať Save As
                await SaveProjectAsAsync();
            }
            else
            {
                // Existujúci projekt - zavolať Save
                await SaveProjectAsync();
            }
        }
        
        // Užívateľ zvolil Nie - pokračuj bez uloženia
        // (alebo projekt bol uložený)
        return true;
    }

    /// <summary>Vytvorí nový čistý projekt (bez otvoreného súboru).</summary>
    public void CreateNewProject()
    {
        SettingsManager.NewProject();
        
        Settings.Load();
        SyncTimeServiceFromSettings();
        Tabs.Operation.RefreshFromProject();
        Tabs.LayoutEditor.LoadFromProject();
        
        Ribbon.HasOpenProject = false;
        UpdateWindowTitle();
        RequestProjectHintUpdate?.Invoke();
    }

    /// <summary>Vytvorí nový čistý projekt s kontrolou neuložených zmien.</summary>
    public async Task CreateNewProjectAsync()
    {
        // Kontrola neuložených zmien
        if (!await CheckUnsavedChangesAsync())
            return;
            
        CreateNewProject();
    }

    // Backstage potrebuje otvoriť "Recent" bez file pickeru.
    public void OpenProjectByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            SettingsManager.OpenProject(path);
            Settings.Load();
            SyncTimeServiceFromSettings();
            Tabs.Operation.RefreshFromProject();
            Tabs.LayoutEditor.LoadFromProject(); // DÔLEŽITÉ: Načítať layout elementy!

            Ribbon.HasOpenProject = true;
            UpdateWindowTitle();
            // Status riadku je vyhradené pre stav DCC centrály. Nezapisujeme sem projektové hlášky.
            RequestProjectHintUpdate?.Invoke();
        }
        catch (Exception)
        {
            Ribbon.HasOpenProject = !string.IsNullOrWhiteSpace(SettingsManager.CurrentProjectPath);
            UpdateWindowTitle();
            // Chyba pri otvorení projektu - logovanie do status baru je zakázané. Použiť iné UI kanály.
            RequestProjectHintUpdate?.Invoke();
        }
    }

    /// <summary>Prestaví zoznam centrál v StatusBare podľa aktuálneho stavu nastavení + pripojenia.</summary>
    private void RefreshStatusBarCentrals()
    {
        // Snapshot hodnôt tu (môžeme byť na background threade)
        var profiles        = SettingsManager.GetEffectiveDccCentralProfiles();
        var connectedIds    = Dcc.ConnectedProfileIds;
        var reconnectingIds = Dcc.ReconnectingProfileIds;

        // Resolver: pre daný profil vráti živý IDccTelemetry bez znalosti konkrétneho protokolu.
        //   1) multi-central mode → cez DccConnectionService.TryGetConnectedClient,
        //   2) legacy single-central mode (_multiConnections je prázdne) → fallback
        //      na Dcc.Client, pretože tam je v každom okamihu len jedna centrála.
        TrackFlow.Services.Dcc.IDccTelemetry? TelemetryResolver(Guid profileId)
        {
            if (Dcc.TryGetConnectedTelemetry(profileId, out var multiTelemetry))
            {
                return multiTelemetry;
            }

            // Single-central fallback: ak nie sme v multi režime a primárny Dcc.Client beží,
            // vrátime ho. (Pre viac profilov v jednoduchom režime to môže byť nepresné,
            // ale lepšie ako "navždy null".)
            if (!Dcc.IsMultiCentralModeActive
                && Dcc.IsConnected
                && Dcc.Client is TrackFlow.Services.Dcc.IDccTelemetry singleTelemetry)
            {
                return singleTelemetry;
            }

            return null;
        }

        bool IsTelemetryVisible() => SettingsManager.App.ShowTelemetryInStatusBar;

        // ObservableCollection MUSÍ byť modifikovaná na UI threade
        void Update() => StatusBar.UpdateCentrals(profiles, connectedIds, reconnectingIds, Dcc, TelemetryResolver, IsTelemetryVisible);

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            Update();
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(Update);
    }

    private void RefreshDccStatusMessage()
    {
        // DÔLEŽITÉ:
        // Ak bola naposledy aktívna hláška "Connected" (priorita 90), nižšie priority (Disconnected atď.)
        // by sa bez resetu nemuseli vôbec zobraziť -> ostane "pripojená S/N..." aj keď LED už zčervená.
        _statusPolicy.ResetAll();

        var eff = SettingsManager.GetEffective();
        var typeName = CN(eff.DccCentralType);

        if (Dcc.IsAnyConnected)
        {
            StatusBar.IsDccConnected = true;
            Ribbon.IsConnected = ShouldRibbonShowDisconnectState();

            var sn = Dcc.Client.SerialNumber;
            _statusPolicy.TrySet(
                StatusTopic.Connected,
                sn.HasValue ? $"{typeName} pripojená, S/N: {sn.Value}" : $"{typeName} pripojená.",
                m => StatusBar.Message = m);
        }
        else
        {
            StatusBar.IsDccConnected = false;
            Ribbon.IsConnected = false;

            _statusPolicy.TrySet(
                StatusTopic.Disconnected,
                $"{typeName} odpojená.",
                m => StatusBar.Message = m);
        }
    }

    /// <summary>
    /// Vráti true iba vtedy, keď sú VŠETKY nakonfigurované profily práve pripojené.
    /// Pre legacy cestu (bez profilov) vracia Dcc.IsConnected.
    /// </summary>
    private bool AreAllProfilesConnected()
    {
        var profiles = SettingsManager.GetEffectiveDccCentralProfiles().Where(p => p.IsEnabled).ToList();
        if (profiles.Count == 0) return Dcc.IsConnected;
        var connectedIds = Dcc.ConnectedProfileIds;
        return profiles.All(p => connectedIds.Any(id => id == p.Id));
    }

    /// <summary>
    /// Určuje, či má Ribbon tlačidlo prejsť do režimu „Odpojiť“.
    /// Nová politika:
    ///  - legacy/single-central: ak je klient pripojený, zobraz „Odpojiť"
    ///  - multi-central: ak je pripojená ASPOŇ jedna centrála alebo práve beží
    ///    auto-reconnect aspoň jednej centrály, používateľ musí mať možnosť
    ///    všetko manuálne zastaviť / odpojiť.
    /// Do režimu „Pripojiť“ sa tlačidlo vracia až vtedy, keď nie je pripojená ani
    /// reconnectujúca žiadna konfigurovaná centrála.
    /// </summary>
    private bool ShouldRibbonShowDisconnectState()
    {
        var profiles = SettingsManager.GetEffectiveDccCentralProfiles().Where(p => p.IsEnabled).ToList();
        if (profiles.Count == 0)
            return Dcc.IsConnected;

        return Dcc.ConnectedProfileIds.Count > 0 || Dcc.ReconnectingProfileIds.Count > 0;
    }

    // =====================================================================================
    // Settings (MVVM)
    // =====================================================================================

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenSettingsAsync()
    {
        PrepareSettingsEdit();

        if (ShowSettingsDialogAsync == null)
        {
            // Dialog pre nastavenia nie je pripojený. Stavová lišta sa nepoužíva na tieto hlásky.
            RequestProjectHintUpdate?.Invoke();
            return;
        }

        var saved = await ShowSettingsDialogAsync();
        await HandleSettingsDialogClosedAsync(saved);
    }

    public void PrepareSettingsEdit()
    {
        _settingsBeforeEdit = SettingsManager.GetEffective();
        Tabs.Operation.RefreshFromProject();
    }

    public SettingsViewModel CreateSettingsDialogViewModel(object? locomotiveOrAddress = null)
    {
        var vm = new SettingsViewModel(SettingsManager, dccConnectionService: Dcc);
        vm.SetDccTestLocomotiveProvider(() => SmartStrips.SelectedLocomotive, SmartStrips);

        if (locomotiveOrAddress != null)
            vm.OpenDccCentralTabForLocomotive(locomotiveOrAddress);

        return vm;
    }

    public async Task HandleSettingsDialogClosedAsync(bool saved)
    {
        if (!saved)
        {
            // Nastavenia nezmenené; neukladáme hlášku do status baru (vyhradené pre DCC). Updatni hinty.
            RequestProjectHintUpdate?.Invoke();
            return;
        }

        var before = _settingsBeforeEdit ?? SettingsManager.GetEffective();
        SyncTimeServiceFromSettings();
        var res = await Dcc.ApplyDccAfterSettingsSavedAsync(before);

        // Po uložení nastavení vždy obnov zoznam centrál v StatusBare
        RefreshStatusBarCentrals();

        // Ak sa DCC signatúra nezmenila, stačí informácia o uložení.
        if (!res.DccChanged)
        {
            // Nastavenia uložené; neukladáme hlášku do status baru (vyhradené pre DCC).
            RequestProjectHintUpdate?.Invoke();
            return;
        }

        // Stav pripojenia + hlášky rieši jednotne handler OnDccConnectionStateChanged
        RequestProjectHintUpdate?.Invoke();
    }

    // =====================================================================================
    // DCC Connect/Disconnect (MVVM príkazy)
    // =====================================================================================

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ToggleConnectAsync()
    {
        if (Ribbon.IsConnected)
        {
            DisconnectCore();
            return;
        }

        await ConnectCoreAsync();
    }

    [RelayCommand]
    private void Stop()
    {
        // Fire-and-forget: tlačidlo musí reagovať okamžite.
        _ = EmergencyStopCoreAsync();
    }

    private async Task EmergencyStopCoreAsync(CancellationToken ct = default)
    {
        try
        {
            // Okamžite zhoď rýchlosť aj v modeli (UI)
            foreach (var loco in SmartStrips.Locomotives)
            {
                loco.TargetSpeed = 0;
                loco.CurrentDisplaySpeed = 0;
                loco.IsForward = false;
                loco.IsReverse = false;
            }

            // Operation VM rieši aj simulácie + safety fallback.
            var sendDcc = !Tabs.Operation.IsSimulationMode;
            var dccClient = (sendDcc && Dcc.Client is { IsConnected: true }) ? Dcc.Client : null;
            await Tabs.Operation.EmergencyStopAsync(dccClient, sendDcc: sendDcc, ct: ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Emergency stop failed");
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ConnectAsync()
    {
        await ConnectCoreAsync();
        RequestProjectHintUpdate?.Invoke();
    }

    [RelayCommand]
    private void Disconnect()
    {
        DisconnectCore();
        RequestProjectHintUpdate?.Invoke();
    }

    public async Task<bool> ConnectCoreAsync()
    {
        var profiles = SettingsManager.GetEffectiveDccCentralProfiles();

        // ── Multi-central path: connect only missing/disconnected profiles ────
        if (profiles.Count > 0)
        {
            // Zobraziť "Pripájam..." hneď – eventy nahrádia text podľa výsledku.
            _statusPolicy.TrySet(
                StatusTopic.Connecting,
                "Pripájam centrály…",
                m => StatusBar.Message = m,
                sticky: true);

            try
            {
                // ConnectMissingAsync: pripojí iba profily ktoré nie sú aktívne pripojené.
                // Už pripojené centrály zostanú nedotknuté.
                await Dcc.ConnectMissingAsync(profiles);
                // Stav Ribbon + StatusBar + hlášky nastaví OnDccConnectionStateChanged.
                RefreshStatusBarCentrals();
                return Dcc.ConnectedProfileIds.Count > 0;
            }
            catch (Exception ex)
            {
                // Chyba na úrovni ConnectMissingAsync (programátorská chyba, nie sieťová).
                var msg = "Chyba pripojenia: " + ex.Message;
                void ApplyError()
                {
                    Ribbon.IsConnected       = ShouldRibbonShowDisconnectState();
                    StatusBar.IsDccConnected = false;
                    StatusBar.Message        = msg;
                }
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                    ApplyError();
                else
                    Avalonia.Threading.Dispatcher.UIThread.Post(ApplyError);
                RefreshStatusBarCentrals();
                return false;
            }
        }

        // ── Legacy single-central path (no profiles defined) ──────────────────
        var eff = SettingsManager.GetEffective();
        var typeName = CN(eff.DccCentralType);

        _statusPolicy.TrySet(
            StatusTopic.Connecting,
            $"Pripájam… ({typeName})",
            m => StatusBar.Message = m,
            sticky: true);

        try
        {
            var (ok, _, _) = await Dcc.ConnectAsync();
            // úspech/neúspech aj text hlášky nastaví OnDccConnectionStateChanged
            return ok;
        }
        catch (Exception ex)
        {
            Ribbon.IsConnected = false;
            StatusBar.Message  = "Chyba pripojenia: " + ex.Message;
            return false;
        }
    }

    public void DisconnectCore()
    {
        var profiles = SettingsManager.GetEffectiveDccCentralProfiles().Where(p => p.IsEnabled).ToList();
        if (profiles.Count > 0)
        {
            // DisconnectAll nepropagovuje eventy cez ConnectionStateChanged (handler je odobraný pred disconnect).
            // Nastavíme stav a hlášku priamo.
            _statusPolicy.ResetAll();
            Dcc.DisconnectAll("user");

            // UI aktualizácia musí ísť na UI thread (DisconnectCore sa môže volať z UI threadu cez RelayCommand).
            void ApplyDisconnected()
            {
                Ribbon.IsConnected       = false;
                StatusBar.IsDccConnected = false;
                // Nastav hlášku cez policy – rovnaká logika ako v OnDccConnectionStateChanged.
                var centralNames = string.Join(", ",
                    profiles.Select(p => DccCentralDisplayName.Get(p.Type)));
                _statusPolicy.TrySet(
                    StatusTopic.UserDisconnected,
                    $"Centrály odpojené (používateľ): {centralNames}.",
                    m => StatusBar.Message = m);
            }
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                ApplyDisconnected();
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(ApplyDisconnected);

            RefreshStatusBarCentrals();
        }
        else
        {
            // Stav + hlášku nastaví OnDccConnectionStateChanged (Disconnected, "user")
            Dcc.Disconnect("user");
        }
    }

    private void OnDccConnectionStateChanged(DccConnectionStateChange change)
    {
        // Handler môže byť volaný z background threadu (keepalive monitor, reconnect loop).
        // Všetky UI zmeny MUSIA ísť cez UI thread.
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnDccConnectionStateChanged(change));
            return;
        }

        var typeName = CN(change.Type);

        switch (change.Kind)
        {
            case DccConnectionChangeKind.Connected:
                // Aspoň jedna centrála je pripojená → Ribbon musí umožniť odpojenie.
                StatusBar.IsDccConnected = true;
                Ribbon.IsConnected       = ShouldRibbonShowDisconnectState();

                _statusPolicy.ResetAll();
                _statusPolicy.TrySet(
                    StatusTopic.Connected,
                    change.Serial.HasValue
                        ? $"{typeName} pripojená, S/N: {change.Serial.Value}"
                        : $"{typeName} pripojená.",
                    m => StatusBar.Message = m);

                // Po úspešnom pripojení centrály synchronizuj fyzické návestidlá s aktuálnym aspektom v TrackFlow.
                _ = SyncSignalsToCentralAfterConnectAsync();
                break;

            case DccConnectionChangeKind.Disconnected:
                var anyAfterDisconnect = Dcc.IsAnyConnected;
                StatusBar.IsDccConnected = anyAfterDisconnect;
                Ribbon.IsConnected       = ShouldRibbonShowDisconnectState();

                ClearRuntimeFeedbackOccupancyAfterDisconnect(change);

                // Pri odpojení vždy reset – inak môže ostať "Connected" text.
                _statusPolicy.ResetAll();

                if (string.Equals(change.Reason, "user", StringComparison.OrdinalIgnoreCase))
                {
                    _statusPolicy.TrySet(
                        StatusTopic.UserDisconnected,
                        $"{typeName} odpojená (používateľ).",
                        m => StatusBar.Message = m);
                }
                else if (string.Equals(change.Reason, "connection-lost", StringComparison.OrdinalIgnoreCase))
                {
                    _statusPolicy.TrySet(
                        StatusTopic.ConnectionLost,
                        $"{typeName} výpadok spojenia.",
                        m => StatusBar.Message = m);
                }
                else if (string.Equals(change.Reason, "settings-changed", StringComparison.OrdinalIgnoreCase))
                {
                    _statusPolicy.TrySet(
                        StatusTopic.Disconnected,
                        $"{typeName}: zmenené nastavenia – odpojené.",
                        m => StatusBar.Message = m);
                }
                else
                {
                    _statusPolicy.TrySet(
                        StatusTopic.Disconnected,
                        $"{typeName} odpojená.",
                        m => StatusBar.Message = m);
                }
                break;

            case DccConnectionChangeKind.Reconnecting:
                var anyDuringReconnect = Dcc.IsAnyConnected;
                StatusBar.IsDccConnected = anyDuringReconnect;
                Ribbon.IsConnected       = ShouldRibbonShowDisconnectState();

                // Reconnecting musí prepísať aj poslednú "Connected" správu.
                _statusPolicy.ResetAll();
                _statusPolicy.TrySet(
                    StatusTopic.Reconnecting,
                    $"{typeName} neodpovedá – automaticky pripájam…",
                    m => StatusBar.Message = m,
                    ttlMs: null,
                    sticky: true);
                break;

            case DccConnectionChangeKind.ConnectFailed:
                var anyOnFail = Dcc.IsAnyConnected;
                StatusBar.IsDccConnected = anyOnFail;
                Ribbon.IsConnected       = ShouldRibbonShowDisconnectState();

                // počas auto-reconnectu nechceme šumové hlásky
                if (string.Equals(change.Reason, "auto-reconnect-failed", StringComparison.OrdinalIgnoreCase))
                    break;

                if (string.Equals(change.Reason, "auto-reconnect-timeout", StringComparison.OrdinalIgnoreCase))
                {
                    _statusPolicy.ResetAll();
                    _statusPolicy.TrySet(
                        StatusTopic.ReconnectTimeout,
                        $"{typeName} neodpovedá – automatické pripájanie bolo ukončené po 1 minúte.",
                        m => StatusBar.Message = m);
                }
                else if (string.Equals(change.Reason, "timeout/no-response", StringComparison.OrdinalIgnoreCase))
                {
                    _statusPolicy.TrySet(
                        StatusTopic.ConnectFailedToast,
                        $"{typeName} neodpovedá (timeout).",
                        m => StatusBar.Message = m,
                        onTtlExpired: RefreshDccStatusMessage,
                        ttlMs: 2500);
                }
                else
                {
                    _statusPolicy.TrySet(
                        StatusTopic.ConnectFailedToast,
                        $"{typeName} neodpovedá.",
                        m => StatusBar.Message = m,
                        onTtlExpired: RefreshDccStatusMessage,
                        ttlMs: 2500);
                }
                break;

            case DccConnectionChangeKind.ClientChanged:
            default:
                break;
        }

        // Obnov zobrazenie centrál v StatusBare po každej zmene stavu pripojenia
        RefreshStatusBarCentrals();
    }

    private void ClearRuntimeFeedbackOccupancyAfterDisconnect(DccConnectionStateChange change)
    {
        var layout = SettingsManager.CurrentProject?.Layout;
        if (layout == null)
            return;

        // Po odpojení DCC centrály už nemáme autoritatívny zdroj živej feedback obsadenosti.
        // Runtime-only R-BUS/S88 stav preto vynulujeme, inak môže blok zostať falošne červený
        // aj keď ide len o "poslednú známu" telemetriu zo stratenej centrály.
        // Ak po tomto evente nie je pripojená ŽIADNA centrála, musíme vynulovať CELÝ runtime
        // feedback stav. Inak by vedel prežiť starý blok z predošlej jazdy (napr. YY) a po ďalšom
        // connecte sa k nemu pridá nový blok (XX), takže vizuálne ostanú obsadené oba.
        //
        // Ak ešte aspoň jedna iná centrála ostáva online, čistíme iba profil ktorý sa práve odpojil.
        var clearAll = !Dcc.IsAnyConnected;
        var changedBlocks = DccFeedbackLayoutApplier.ClearFeedbackState(layout, change.ProfileId, clearAll);
        if (changedBlocks.Count == 0)
            return;

        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"RBUS disconnect-clear: clearAll={clearAll}, profileId={(change.ProfileId?.ToString() ?? "<legacy>")}, changedBlocks={changedBlocks.Count}, ids={string.Join(", ", changedBlocks.Select(b => b.Id))}");

        foreach (var block in changedBlocks)
            Tabs.LayoutEditor.RequestBlockRepaint?.Invoke(block);

        _ = ReconcileExternalOccupancyFromFeedbackAsync();
    }

    // =====================================================================================
    // Project (Open / Save / Save As / Close) – MVVM príkazy
    // =====================================================================================

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenProjectAsync()
    {
        if (ShowOpenProjectPickerAsync == null)
        {
            RequestProjectHintUpdate?.Invoke();
            return;
        }

        var path = await ShowOpenProjectPickerAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            RequestProjectHintUpdate?.Invoke();
            return;
        }

        var progressWindow = new ProgressWindow();
        progressWindow.Show();

        try
        {
            SettingsManager.OpenProject(path);
            Settings.Load();
            Tabs.Operation.RefreshFromProject();
            Tabs.LayoutEditor.LoadFromProject(); // DÔLEŽITÉ: Načítať layout elementy!

            Ribbon.HasOpenProject = true;
            UpdateWindowTitle();
            // Projekt otvorený - status bar reserved for DCC; vyžiadaj update project hint.
            RequestProjectHintUpdate?.Invoke();
        }
        catch (Exception)
        {
            UpdateWindowTitle();
            // Chyba pri otváraní projektu. Nepoužívame status bar na chybové hlásenia.
            RequestProjectHintUpdate?.Invoke();
        }
        finally
        {
            progressWindow.Close();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SaveProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(SettingsManager.CurrentProjectPath))
        {
            await SaveProjectAsAsync();
            RequestProjectHintUpdate?.Invoke();
            return;
        }

        try
        {
            var ok = SettingsManager.SaveProject();
            Ribbon.HasOpenProject = !string.IsNullOrWhiteSpace(SettingsManager.CurrentProjectPath);

            // Nepoužívame status riadok pre projektové hlásenia; len požiadame View o update hintu.
            RequestProjectHintUpdate?.Invoke();
        }
        catch (Exception)
        {
            // Chyba pri ukladaní projektu; status riadok nie je pre tieto hlásenia.
            RequestProjectHintUpdate?.Invoke();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SaveProjectAsAsync()
    {
        if (ShowSaveProjectPickerAsync == null)
        {
            // Picker pre uloženie projektu nie je pripojený; nepoužívame status bar.
            RequestProjectHintUpdate?.Invoke();
            return;
        }

        try
        {
            var suggestedName = MakeSuggestedProjectFileName();
            var path = await ShowSaveProjectPickerAsync(suggestedName);

            if (string.IsNullOrWhiteSpace(path))
            {
                // Uloženie projektu zrušené; nepoužívame status bar.
                RequestProjectHintUpdate?.Invoke();
                return;
            }

            var ok = SettingsManager.SaveProjectAs(path);

            // Projekt uložený / chyba pri ukladaní: nepoužívame status bar pre tieto hlášky.
            RequestProjectHintUpdate?.Invoke();
        }
        catch (Exception)
        {
            // Chyba pri ukladaní projektu; nepoužívame status bar.
            RequestProjectHintUpdate?.Invoke();
        }
    }

    [RelayCommand]
    private void CloseProject()
    {
        try
        {
            SettingsManager.CloseProject();
            Settings.Load();

            Ribbon.HasOpenProject = false;
            UpdateWindowTitle();
            // Projekt zatvorený; nepoužívame status bar.
            RequestProjectHintUpdate?.Invoke();
        }
        catch (Exception)
        {
            UpdateWindowTitle();
            // Chyba pri zatvorení projektu; nepoužívame status bar.
            RequestProjectHintUpdate?.Invoke();
        }
    }

    private string MakeSuggestedProjectFileName()
    {
        var current = SettingsManager.CurrentProjectPath;
        if (!string.IsNullOrWhiteSpace(current))
        {
            var name = IOPath.GetFileName(current);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return "MyLayout.trackflow.json";
    }

    // =====================================================================================
    // Upraviť (Undo/Redo) + Evidencia (Lokomotívy/Vozidlá/Vlaky)
    // =====================================================================================

    private bool CanUndo() => CurrentMode == AppMode.Editor && Tabs.LayoutEditor.CanUndo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        Tabs.LayoutEditor.Undo();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private bool CanRedo() => CurrentMode == AppMode.Editor && Tabs.LayoutEditor.CanRedo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        Tabs.LayoutEditor.Redo();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenLocomotivesAsync()
    {
        if (ShowLocomotivesDialogAsync == null)
        {
            // Dialog Lokomotívy nie je pripojený; nepoužívame status bar.
            return;
        }

        await ShowLocomotivesDialogAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenVehiclesAsync()
    {
        if (ShowVehiclesDialogAsync == null)
        {
            // Dialog Vozidlá nie je pripojený; nepoužívame status bar.
            return;
        }

        await ShowVehiclesDialogAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenTrainsAsync()
    {
        if (ShowTrainsDialogAsync == null)
        {
            // Dialog Vlaky nie je pripojený; nepoužívame status bar.
            return;
        }

        await ShowTrainsDialogAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenRoutesManagerAsync()
    {
        if (ShowRoutesManagerDialogAsync == null) return;
        await ShowRoutesManagerDialogAsync();
    }

    // =====================================================================================
    // IDisposable implementation - prevent memory leaks
    // =====================================================================================

    public void Dispose()
    {
        if (_disposed) return;

        CloseTelemetryWidget();

        // Odpoj event handlery
        LocoDccBridge?.Dispose();
        if (Dcc != null)
        {
            Dcc.ConnectionStateChanged -= OnDccConnectionStateChanged;
            Dcc.FeedbackStateChanged -= OnDccFeedbackStateChanged;
            try { Dcc.Dispose(); } catch { /* best-effort */ }
        }
        if (SettingsManager?.Dirty != null)
            SettingsManager.Dirty.DirtyChanged -= OnProjectDirtyChanged;
        if (SettingsManager != null)
            SettingsManager.ProjectChanged -= SyncTimeServiceFromSettings;

        // Clear statusPolicy timers
        _statusPolicy.ClearTtl();

        _disposed = true;
    }

    private async Task RefreshSignalsOnOperationEnterAsync()
    {
        try
        {
            await Tabs.Operation.SetAllSignalsRedAndPushAsync(Dcc.Client);
        }
        catch
        {
            // Prevádzka môže bežať aj bez pripojenej centrály; chybu nepropagujeme do UI vlákna.
        }
    }

    private void OnDccFeedbackStateChanged(DccFeedbackStateChange change)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnDccFeedbackStateChanged(change));
            return;
        }

        var layout = SettingsManager.CurrentProject?.Layout;
        if (layout == null)
            return;

        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"RBUS received: profileId={(change.ProfileId?.ToString() ?? "<legacy>")}, modul={change.ModuleAddress}, vstup={change.PortNumber}, active={change.IsActive}");

        var changedBlocks = DccFeedbackLayoutApplier.ApplyFeedback(layout, change);
        if (changedBlocks.Count == 0)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"RBUS no-layout-change: modul={change.ModuleAddress}, vstup={change.PortNumber}, active={change.IsActive}");
            return;
        }

        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"RBUS changed-blocks: count={changedBlocks.Count}, ids={string.Join(", ", changedBlocks.Select(b => b.Id))}");

        foreach (var block in changedBlocks.OfType<BlockElement>())
            Tabs.LayoutEditor.RequestBlockRepaint?.Invoke(block);

        _ = ReconcileExternalOccupancyFromFeedbackAsync();
    }

    private async Task ReconcileExternalOccupancyFromFeedbackAsync()
    {
        try
        {
            await Tabs.Operation.HandleExternalOccupancyUpdateAsync(Dcc.Client);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Applying DCC feedback occupancy update failed.");
        }
    }

    /// <summary>
    /// Po úspešnom pripojení centrály nastaví bezpečný štartovací stav návestidiel.
    /// Safety pravidlo: po connect vždy pošli STOJ (Stop) na všetky návestidlá
    /// a následne vykonaj oneskorený force update aktuálnych aspektov.
    /// </summary>
    private async Task SyncSignalsToCentralAfterConnectAsync()
    {
        var syncId = Guid.NewGuid().ToString("N")[..8];
        try
        {
            Log.Information("DCC connect sync {SyncId}: start all-red push", syncId);
            await Tabs.Operation.SetAllSignalsRedAndPushAsync(Dcc.Client, syncId: syncId);

            // Niektoré dekodéry po connecte krátko ignorujú prvé pakety.
            // Preto po krátkej pauze zopakujeme push aktuálneho snapshotu.
            await Task.Delay(500);
            Log.Information("DCC connect sync {SyncId}: delayed force snapshot push", syncId);
            await Tabs.Operation.ForceSendCurrentSignalStatesAsync(Dcc.Client, syncId: syncId);
            Log.Information("DCC connect sync {SyncId}: completed", syncId);
        }
        catch (Exception ex)
        {
            // Synchronizácia po connect prebieha na pozadí; chybu nepropagujeme do UI vlákna.
            Log.Warning(ex, "DCC connect sync {SyncId} failed", syncId);
        }
    }


}

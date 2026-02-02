using Avalonia.Controls.Shapes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using System;
using IOPath = System.IO.Path;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models;
using TrackFlow.Ribbon;
using TrackFlow.Services;
using TrackFlow.Services.Dcc;
using TrackFlow.ViewModels.Backstage;
using TrackFlow.ViewModels.Cab;
using TrackFlow.ViewModels.SmartStrips;
using TrackFlow.ViewModels.Settings;

namespace TrackFlow.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private static string CN(DccCentralType t) => DccCentralDisplayName.Get(t);

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

    // View sem pripojí dialógy (VM nepozná Avalonia UI / StorageProvider)
    public Func<Task<bool>>? ShowSettingsDialogAsync { get; set; }
    public Func<Task<string?>>? ShowOpenProjectPickerAsync { get; set; }
    public Func<string, Task<string?>>? ShowSaveProjectPickerAsync { get; set; }

    // View sem pripojí dialógy pre evidenciu (Lokomotívy / Vozidlá / Vlaky)
    public Func<Task>? ShowLocomotivesDialogAsync { get; set; }
    public Func<Task>? ShowVehiclesDialogAsync { get; set; }
    public Func<Task>? ShowTrainsDialogAsync { get; set; }

    // View sem pripojí aktualizáciu hintu (VM nevie nič o View)
    public Action? RequestProjectHintUpdate { get; set; }

    public SettingsManager SettingsManager { get; }
    public DccConnectionService Dcc { get; }

    public MainRibbonViewModel Ribbon { get; }
    public CabStripHostViewModel CabHost { get; }
    public MainTabsViewModel Tabs { get; }
    public StatusBarViewModel StatusBar { get; }
    public SettingsViewModel Settings { get; }

    public SmartStripsViewModel SmartStrips { get; }

    public FileBackstageViewModel FileBackstage { get; }

    [ObservableProperty]
    private bool isFileBackstageOpen;

    public MainWindowViewModel()
    {
        SettingsManager = new SettingsManager();
        SettingsManager.LoadApp();

        Ribbon = new MainRibbonViewModel();
        CabHost = new CabStripHostViewModel();
        Tabs = new MainTabsViewModel(SettingsManager);
        StatusBar = new StatusBarViewModel();
        Settings = new SettingsViewModel(SettingsManager);
        SmartStrips = new SmartStripsViewModel(SettingsManager);
        FileBackstage = new FileBackstageViewModel(this);

        Dcc = new DccConnectionService(SettingsManager);
        Dcc.ConnectionStateChanged += OnDccConnectionStateChanged;

        // init stavu ribbonu podľa toho, či je otvorený projekt
        Ribbon.HasOpenProject = !string.IsNullOrWhiteSpace(SettingsManager.CurrentProjectPath);
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

    // Backstage potrebuje otvoriť "Recent" bez file pickeru.
    public void OpenProjectByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            SettingsManager.OpenProject(path);
            Settings.Load();
            Tabs.Operation.RefreshFromProject();

            Ribbon.HasOpenProject = true;
            StatusBar.Message = "Projekt otvorený: " + System.IO.Path.GetFileName(path);
            RequestProjectHintUpdate?.Invoke();
        }
        catch (Exception ex)
        {
            Ribbon.HasOpenProject = !string.IsNullOrWhiteSpace(SettingsManager.CurrentProjectPath);
            StatusBar.Message = "Chyba pri otvorení projektu: " + ex.Message;
            RequestProjectHintUpdate?.Invoke();
        }
    }

    private void RefreshDccStatusMessage()
    {
        // DÔLEŽITÉ:
        // Ak bola naposledy aktívna hláška "Connected" (priorita 90), nižšie priority (Disconnected atď.)
        // by sa bez resetu nemuseli vôbec zobraziť -> ostane "pripojená S/N..." aj keď LED už zčervená.
        _statusPolicy.ResetAll();

        var eff = SettingsManager.GetEffective();
        var typeName = CN(eff.DccCentralType);

        if (Dcc.Client.IsConnected)
        {
            StatusBar.IsDccConnected = true;
            Ribbon.IsConnected = true;

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

    // =====================================================================================
    // Settings (MVVM)
    // =====================================================================================

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenSettingsAsync()
    {
        PrepareSettingsEdit();

        if (ShowSettingsDialogAsync == null)
        {
            StatusBar.Message = "Chyba: dialóg Nastavenia nie je pripojený.";
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

    public async Task HandleSettingsDialogClosedAsync(bool saved)
    {
        if (!saved)
        {
            _statusPolicy.TrySet(
                StatusTopic.InfoToast,
                "Nastavenia nezmenené",
                m => StatusBar.Message = m,
                onTtlExpired: RefreshDccStatusMessage,
                ttlMs: 900);

            RequestProjectHintUpdate?.Invoke();
            return;
        }

        var before = _settingsBeforeEdit ?? SettingsManager.GetEffective();
        var res = await Dcc.ApplyDccAfterSettingsSavedAsync(before);

        // Ak sa DCC signatúra nezmenila, stačí informácia o uložení.
        if (!res.DccChanged)
        {
            _statusPolicy.TrySet(
                StatusTopic.InfoToast,
                "Nastavenia uložené",
                m => StatusBar.Message = m,
                onTtlExpired: RefreshDccStatusMessage,
                ttlMs: 900);

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
        // TODO: E-STOP / STOP pre aktuálnu kabínu / všetky
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
            StatusBar.Message = "Chyba pripojenia: " + ex.Message;
            return false;
        }
    }

    public void DisconnectCore()
    {
        // Disconnected stav + hlášku nastaví OnDccConnectionStateChanged
        Dcc.Disconnect("user");
    }

    private void OnDccConnectionStateChanged(DccConnectionStateChange change)
    {
        var typeName = CN(change.Type);

        switch (change.Kind)
        {
            case DccConnectionChangeKind.Connected:
                StatusBar.IsDccConnected = true;
                Ribbon.IsConnected = true;

                _statusPolicy.ResetAll();
                _statusPolicy.TrySet(
                    StatusTopic.Connected,
                    change.Serial.HasValue
                        ? $"{typeName} pripojená, S/N: {change.Serial.Value}"
                        : $"{typeName} pripojená.",
                    m => StatusBar.Message = m);
                break;

            case DccConnectionChangeKind.Disconnected:
                StatusBar.IsDccConnected = false;
                Ribbon.IsConnected = false;

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
                StatusBar.IsDccConnected = false;
                Ribbon.IsConnected = false;

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
                // počas auto-reconnectu nechceme šumové hlášky
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
    }

    // =====================================================================================
    // Project (Open / Save / Save As / Close) – MVVM príkazy
    // =====================================================================================

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenProjectAsync()
    {
        if (ShowOpenProjectPickerAsync == null)
        {
            StatusBar.Message = "Chyba: dialóg pre otvorenie projektu nie je pripojený.";
            RequestProjectHintUpdate?.Invoke();
            return;
        }

        StatusBar.Message = "Otváram projekt…";

        var path = await ShowOpenProjectPickerAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusBar.Message = "Otváranie projektu zrušené.";
            RequestProjectHintUpdate?.Invoke();
            return;
        }

        try
        {
            SettingsManager.OpenProject(path);
            Settings.Load();
            Tabs.Operation.RefreshFromProject();

            Ribbon.HasOpenProject = true;
            StatusBar.Message = "Projekt otvorený: " + System.IO.Path.GetFileName(path);
            RequestProjectHintUpdate?.Invoke();
        }
        catch (Exception ex)
        {
            Ribbon.HasOpenProject = !string.IsNullOrWhiteSpace(SettingsManager.CurrentProjectPath);
            StatusBar.Message = "Chyba pri otvorení projektu: " + ex.Message;
            RequestProjectHintUpdate?.Invoke();
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

            StatusBar.Message = ok ? "Projekt uložený." : "Projekt sa nepodarilo uložiť.";
            RequestProjectHintUpdate?.Invoke();
        }
        catch (Exception ex)
        {
            StatusBar.Message = "Chyba pri ukladaní projektu: " + ex.Message;
            RequestProjectHintUpdate?.Invoke();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SaveProjectAsAsync()
    {
        if (ShowSaveProjectPickerAsync == null)
        {
            StatusBar.Message = "Chyba: dialóg pre uloženie projektu nie je pripojený.";
            RequestProjectHintUpdate?.Invoke();
            return;
        }

        try
        {
            var suggestedName = MakeSuggestedProjectFileName();
            var path = await ShowSaveProjectPickerAsync(suggestedName);

            if (string.IsNullOrWhiteSpace(path))
            {
                StatusBar.Message = "Uloženie projektu zrušené.";
                RequestProjectHintUpdate?.Invoke();
                return;
            }

            var ok = SettingsManager.SaveProjectAs(path);

            Ribbon.HasOpenProject = ok && !string.IsNullOrWhiteSpace(SettingsManager.CurrentProjectPath);
            StatusBar.Message = ok
                ? "Projekt uložený: " + System.IO.Path.GetFileName(path)
                : "Projekt sa nepodarilo uložiť.";

            RequestProjectHintUpdate?.Invoke();
        }
        catch (Exception ex)
        {
            StatusBar.Message = "Chyba pri ukladaní projektu: " + ex.Message;
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
            StatusBar.Message = "Projekt zatvorený.";
            RequestProjectHintUpdate?.Invoke();
        }
        catch (Exception ex)
        {
            StatusBar.Message = "Chyba pri zatvorení projektu: " + ex.Message;
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

    [RelayCommand]
    private void Undo()
    {
        // TODO: napojiť na CommandStack (bude riešené spolu s Layout editorom)
        _statusPolicy.TrySet(
            StatusTopic.InfoToast,
            "Undo: zatiaľ nie je implementované.",
            m => StatusBar.Message = m,
            onTtlExpired: RefreshDccStatusMessage,
            ttlMs: 1200);
    }

    [RelayCommand]
    private void Redo()
    {
        // TODO: napojiť na CommandStack (bude riešené spolu s Layout editorom)
        _statusPolicy.TrySet(
            StatusTopic.InfoToast,
            "Redo: zatiaľ nie je implementované.",
            m => StatusBar.Message = m,
            onTtlExpired: RefreshDccStatusMessage,
            ttlMs: 1200);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenLocomotivesAsync()
    {
        if (ShowLocomotivesDialogAsync == null)
        {
            StatusBar.Message = "Chyba: dialóg Lokomotívy nie je pripojený.";
            return;
        }

        await ShowLocomotivesDialogAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenVehiclesAsync()
    {
        if (ShowVehiclesDialogAsync == null)
        {
            StatusBar.Message = "Chyba: dialóg Vozidlá nie je pripojený.";
            return;
        }

        await ShowVehiclesDialogAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenTrainsAsync()
    {
        if (ShowTrainsDialogAsync == null)
        {
            StatusBar.Message = "Chyba: dialóg Vlaky nie je pripojený.";
            return;
        }

        await ShowTrainsDialogAsync();
    }

}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.Services.Dcc;

namespace TrackFlow.ViewModels.Settings;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private const int GeneralTabIndex = 0;
    private const int DccCentralTabIndex = 1;

    public sealed record ScaleListItem(string Code, string Display);

    private readonly SettingsManager _mgr;
    private readonly IDccConnectionService _dccConnectionService;
    private readonly DccConnectionService? _dccConnectionServiceConcrete;
    private readonly SynchronizationContext? _uiContext;
    private Func<object?>? _dccTestLocomotiveProvider;
    private INotifyPropertyChanged? _dccTestLocomotiveNotifier;
    private object? _dccTestLocomotiveOverride;
    private bool _disposed;

    // ─── nové: zoznam skonfigurovaných centrál ──────────────────────────────
    /// <summary>
    /// Faktory otvárania dialógu Add / Edit – nastavuje SettingsWindow.axaml.cs.
    /// Ak je null (testy), príkaz nič nerobí.
    /// </summary>
    private Func<DccCentralProfile?, Task<DccCentralProfile?>>? _centralEditDialogFactory;

    public void SetCentralEditDialogFactory(Func<DccCentralProfile?, Task<DccCentralProfile?>> factory)
        => _centralEditDialogFactory = factory;

    public ObservableCollection<ConfiguredDccCentralItem> ConfiguredCentrals { get; } = new();

    private ConfiguredDccCentralItem? _selectedConfiguredCentral;
    public ConfiguredDccCentralItem? SelectedConfiguredCentral
    {
        get => _selectedConfiguredCentral;
        set
        {
            if (!SetProperty(ref _selectedConfiguredCentral, value))
                return;
            OnPropertyChanged(nameof(HasSelectedCentral));
            OnPropertyChanged(nameof(IsZ21Selected));
            EditCentralCommand.NotifyCanExecuteChanged();
            DeleteCentralCommand.NotifyCanExecuteChanged();
            ApplySelectedProfile();

            // Ensure the communication-test UI reacts immediately to the newly selected profile,
            // even when centrals are connected and the global/primary client is a different device.
            TestHandler.SelectedCentralProfileId = _selectedConfiguredCentral?.Profile.Id;
        }
    }

    public bool HasSelectedCentral => SelectedConfiguredCentral != null;

    /// <summary>
    /// Controls visibility of the yellow z21 warning.
    /// The warning must be shown ONLY when the user has a real z21 profile selected in the list
    /// (not merely because some other connected central happens to be z21).
    /// </summary>
    public bool IsZ21Selected
        => SelectedConfiguredCentral?.Profile.Type is DccCentralType.Z21 or DccCentralType.Z21Legacy;

    public IAsyncRelayCommand AddCentralCommand { get; }
    public IAsyncRelayCommand EditCentralCommand { get; }
    public IRelayCommand DeleteCentralCommand { get; }
    // ───────────────────────────────────────────────────────────────────────

    public DccCommunicationTestHandler TestHandler { get; }

    private bool _suppressAutoDefaults;
    private bool _portTouchedByUser;

    public string? CurrentProjectPath => _mgr.CurrentProjectPath;

    public string CurrentProjectName =>
        string.IsNullOrWhiteSpace(_mgr.CurrentProjectPath) ? "—" : Path.GetFileName(_mgr.CurrentProjectPath);

    public bool HasOpenProject => _mgr.Project != null;

    public ObservableCollection<DccCentralListItem> DccCentralItems { get; } = new();
    public ObservableCollection<string> AvailablePorts { get; } = new();
    public ObservableCollection<int> AvailableBaudRates { get; } = new(new[] { 9600, 19200, 38400, 57600, 115200 });

    // UI shows display text (incl. numeric ratio), but persistence MUST store only the canonical code.
    public ObservableCollection<ScaleListItem> ScaleItems { get; } = new(new[]
    {
        new ScaleListItem("H0", "H0 - 1/87"),
        new ScaleListItem("TT", "TT - 1/120"),
        new ScaleListItem("N",  "N - 1/160"),
    });

    public ScaleListItem? SelectedScaleItem
    {
        get
        {
            var normalized = NormalizeScale(Scale);
            return ScaleItems.FirstOrDefault(x => string.Equals(x.Code, normalized, StringComparison.OrdinalIgnoreCase))
                   ?? ScaleItems.FirstOrDefault(x => string.Equals(x.Code, "H0", StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            if (value == null)
                return;

            var code = NormalizeScale(value.Code);
            if (!string.Equals(Scale, code, StringComparison.OrdinalIgnoreCase))
                Scale = code;

            OnPropertyChanged(nameof(SelectedScaleItem));
        }
    }

    [ObservableProperty]
    private DccCentralListItem? selectedDccCentralItem;

    [ObservableProperty]
    private bool hasProject;

    [ObservableProperty]
    private bool useProjectForDcc;

    [ObservableProperty]
    private bool useProjectForScale;

    [ObservableProperty]
    private DccCentralType dccCentralType = DccCentralType.Z21;

    [ObservableProperty]
    private string dccCentralHost = "192.168.0.111";

    [ObservableProperty]
    private int? dccCentralPort = 21105;

    private string _dccCentralSerialPort = string.Empty;
    public string DccCentralSerialPort
    {
        get => _dccCentralSerialPort;
        set
        {
            if (!SetProperty(ref _dccCentralSerialPort, value))
                return;

            if (!_suppressAutoDefaults && HasProject && _mgr.Project != null && UseProjectForDcc)
                _mgr.Dirty.MarkDirty("dcc-serial-port");

            TestConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    private int _dccCentralBaudRate = 19200;
    public int DccCentralBaudRate
    {
        get => _dccCentralBaudRate;
        set
        {
            if (!SetProperty(ref _dccCentralBaudRate, value))
                return;

            if (!_suppressAutoDefaults && HasProject && _mgr.Project != null && UseProjectForDcc)
                _mgr.Dirty.MarkDirty("dcc-baud-rate");

            TestConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    [ObservableProperty]
    private bool autoConnect;

    [ObservableProperty]
    private string language = "sk-SK";

    [ObservableProperty]
    private string scale = "H0";

    [ObservableProperty]
    private string accentColor = "#1E88E5";

    [ObservableProperty]
    private bool openLastProjectOnStartup = false;

    [ObservableProperty]
    private bool autoSaveEnabled;

    [ObservableProperty]
    private int autoSaveIntervalMinutes;

    [ObservableProperty]
    private bool showTooltipsInApp = true;

    [ObservableProperty]
    private bool showClockOnStartup;

    [ObservableProperty]
    private bool showClockStartPauseButton = true;

    [ObservableProperty]
    private bool setModelClockTimeOnStartup;

    [ObservableProperty]
    private int startupModelClockHour = 8;

    [ObservableProperty]
    private int startupModelClockMinute;

    [ObservableProperty]
    private int visibleWagonsInTrain = 0; // 0 = všetky, 1-4 = konkrétny počet

    [ObservableProperty]
    private string defaultProjectsDirectory = string.Empty;

    private bool _enableTransientRouteMessages = true;
    public bool EnableTransientRouteMessages
    {
        get => _enableTransientRouteMessages;
        set => SetProperty(ref _enableTransientRouteMessages, value);
    }

    private bool _showTelemetryInStatusBar = true;
    public bool ShowTelemetryInStatusBar
    {
        get => _showTelemetryInStatusBar;
        set => SetProperty(ref _showTelemetryInStatusBar, value);
    }

    private int _routeMessageTtlSuccessMs = 1800;
    public int RouteMessageTtlSuccessMs
    {
        get => _routeMessageTtlSuccessMs;
        set => SetProperty(ref _routeMessageTtlSuccessMs, value);
    }

    private int _routeMessageTtlInfoMs = 2500;
    public int RouteMessageTtlInfoMs
    {
        get => _routeMessageTtlInfoMs;
        set => SetProperty(ref _routeMessageTtlInfoMs, value);
    }

    private int _routeMessageTtlWarningMs = 3500;
    public int RouteMessageTtlWarningMs
    {
        get => _routeMessageTtlWarningMs;
        set => SetProperty(ref _routeMessageTtlWarningMs, value);
    }

    [ObservableProperty]
    private string connectionTestResult = "";

    // POZN.: Zámerne NIE [ObservableProperty] – Rider analyzer občas necachuje
    // generovanú vlastnosť pre toto pole a hlási falošné chyby v XAML aj v C#.
    // Ručná implementácia funguje rovnako a je úplne odolná voči stavu generátora.
    private int _selectedSettingsTabIndex = GeneralTabIndex;
    public int SelectedSettingsTabIndex
    {
        get => _selectedSettingsTabIndex;
        set => SetProperty(ref _selectedSettingsTabIndex, value);
    }

    [ObservableProperty]
    private bool isTestingConnection;

    [ObservableProperty]
    private int maxPathElements = 15;

    [ObservableProperty]
    private int maxTurnoutsInPath = 5;

    private double _simulationSpeedFactor = ProjectSettingsData.DefaultSimulationSpeedFactor;
    public double SimulationSpeedFactor
    {
        get => _simulationSpeedFactor;
        set
        {
            var normalized = ProjectSettingsData.NormalizeSimulationSpeedFactor(value);
            if (!SetProperty(ref _simulationSpeedFactor, normalized))
                return;

            TimeService.Instance.SimulationSpeedFactor = normalized;

            if (!_suppressAutoDefaults && _mgr.Project != null)
            {
                _mgr.Project.SimulationSpeedFactor = normalized;
                _mgr.Dirty.MarkDirty("simulation-speed-factor");
            }
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }

    public bool UsesSerialConnectionSettings => DccCentralType == DccCentralType.NanoX_S88;
    public bool UsesNetworkConnectionSettings => !UsesSerialConnectionSettings;

    public event Action<bool>? CloseRequested;

    /// <summary>
    /// Lokomotíva použitá ako CommandParameter pre POM test komunikácie.
    /// Typicky ide o <c>SmartStrips.SelectedLocomotive</c>, ale handler vie prijať aj
    /// samotnú číselnú adresu, <c>LocoRecord</c> alebo objekt s Address/DccAddress.
    /// </summary>
    public object? SelectedLocomotiveForDccTest => _dccTestLocomotiveOverride ?? _dccTestLocomotiveProvider?.Invoke();

    public SettingsViewModel(
        SettingsManager mgr,
        IDccCommunicationTestService? dccCommunicationTestService = null,
        IDccConnectionService? dccConnectionService = null)
    {
        _mgr = mgr;
        var connectionService = dccConnectionService ?? new DccConnectionService(mgr);
        _dccConnectionService = connectionService;
        _dccConnectionServiceConcrete = connectionService as DccConnectionService;
        _uiContext = SynchronizationContext.Current;

        // Real-time connection state updates while Settings is open.
        // IMPORTANT: event callbacks can come from background threads (keepalive/auto-reconnect).
        // We must never block them; we only post UI updates.
        _dccConnectionService.IsConnectedChanged += OnDccIsConnectedChanged;
        if (_dccConnectionServiceConcrete != null)
            _dccConnectionServiceConcrete.ConnectionStateChanged += OnDccConnectionStateChanged;

        TestHandler = new DccCommunicationTestHandler(
            dccCommunicationTestService ?? new DccCommunicationTestService(mgr, connectionService),
            connectionService);

        SaveCommand = new RelayCommand(OnSave);
        CancelCommand = new RelayCommand(OnCancel);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanTestConnection);

        AddCentralCommand    = new AsyncRelayCommand(AddCentralAsync);
        EditCentralCommand   = new AsyncRelayCommand(EditCentralAsync, () => SelectedConfiguredCentral != null);
        DeleteCentralCommand = new RelayCommand(DeleteCentral,         () => SelectedConfiguredCentral != null);

        _suppressAutoDefaults = true;
        Load();
        _suppressAutoDefaults = false;
    }

    private void DispatchToUi(Action action)
    {
        if (_uiContext == null || SynchronizationContext.Current == _uiContext)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }

    private void OnDccIsConnectedChanged(bool _)
    {
        // In multi-central mode the aggregate "any connected" can stay true while a specific
        // selected central toggles, but we still want the UI to refresh.
        DispatchToUi(RefreshLiveDccStateInUi);
    }

    private void OnDccConnectionStateChanged(DccConnectionStateChange change)
    {
        _ = change;
        DispatchToUi(RefreshLiveDccStateInUi);
    }

    private void RefreshLiveDccStateInUi()
    {
        RefreshConfiguredCentralConnectionStates();

        // Ensure the communication-test panel immediately reflects the new state
        // (button enabled/disabled, hint visibility, etc.).
        TestHandler.RefreshConnectionDependentUiSafe();
    }

    private void RefreshConfiguredCentralConnectionStates()
    {
        if (_dccConnectionServiceConcrete != null && _dccConnectionServiceConcrete.IsMultiCentralModeActive)
        {
            var connected = _dccConnectionServiceConcrete.ConnectedProfileIds;
            var reconnecting = _dccConnectionServiceConcrete.ReconnectingProfileIds;

            foreach (var item in ConfiguredCentrals)
            {
                var id = item.Profile.Id;
                item.UpdateConnectionState(connected.Contains(id), reconnecting.Contains(id));

                // Pripoj / odpoj telemetrický zdroj podľa aktuálneho stavu klienta.
                if (_dccConnectionServiceConcrete.TryGetConnectedClient(id, out var client) &&
                    client is TrackFlow.Services.Dcc.IDccTelemetry telemetry)
                {
                    item.AttachTelemetry(telemetry);
                }
                else
                {
                    item.AttachTelemetry(null);
                }
            }

            return;
        }

        // Legacy/single-central: show the global state only on the currently selected row.
        var selectedId = SelectedConfiguredCentral?.Profile.Id;
        foreach (var item in ConfiguredCentrals)
        {
            var isSelected = selectedId.HasValue && item.Profile.Id == selectedId.Value;
            var isConn = isSelected && _dccConnectionService.IsConnected;
            item.UpdateConnectionState(isConn, isReconnecting: false);

            if (isConn && _dccConnectionService.Client is TrackFlow.Services.Dcc.IDccTelemetry telemetry)
                item.AttachTelemetry(telemetry);
            else
                item.AttachTelemetry(null);
        }
    }

    public void RefreshAvailablePorts()
    {
        var previousSelection = DccCentralSerialPort;

        // SerialPort.GetPortNames() can block on some systems/drivers; never run it on UI thread.
        _ = Task.Run(() =>
        {
            string[] ports;
            try
            {
                ports = SerialPort.GetPortNames()
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                ports = Array.Empty<string>();
            }

            DispatchToUi(() => ApplyAvailablePorts(ports, previousSelection));
        });
    }

    private void ApplyAvailablePorts(IReadOnlyCollection<string> ports, string previousSelection)
    {
        if (_disposed)
            return;

        AvailablePorts.Clear();

        foreach (var port in ports)
            AvailablePorts.Add(port);

        if (!string.IsNullOrWhiteSpace(previousSelection) && !AvailablePorts.Contains(previousSelection))
            AvailablePorts.Insert(0, previousSelection);

        if (string.IsNullOrWhiteSpace(DccCentralSerialPort) && AvailablePorts.Count > 0)
            DccCentralSerialPort = AvailablePorts[0];
    }

    // ─── CRUD – skonfigurované centrály ─────────────────────────────────────
    private async Task AddCentralAsync()
    {
        if (_centralEditDialogFactory == null) return;
        var result = await _centralEditDialogFactory(null);
        if (result == null) return;

        var item = new ConfiguredDccCentralItem(result, ConfiguredCentrals.Count + 1);
        ConfiguredCentrals.Add(item);
        SelectedConfiguredCentral = item;
    }

    private async Task EditCentralAsync()
    {
        if (_centralEditDialogFactory == null || SelectedConfiguredCentral == null) return;
        var result = await _centralEditDialogFactory(SelectedConfiguredCentral.Profile);
        if (result == null) return;

        var profile = SelectedConfiguredCentral.Profile;
        profile.Type            = result.Type;
        profile.Host            = result.Host;
        profile.Port            = result.Port;
        profile.SerialPort      = result.SerialPort;
        profile.BaudRate        = result.BaudRate;
        profile.AutoConnect     = result.AutoConnect;
        profile.StartupBehavior = result.StartupBehavior;

        OnPropertyChanged(nameof(SelectedConfiguredCentral));
        // refresh DisplayText
        var cur = SelectedConfiguredCentral;
        SelectedConfiguredCentral = null;
        SelectedConfiguredCentral = cur;
    }

    private void DeleteCentral()
    {
        if (SelectedConfiguredCentral == null) return;
        ConfiguredCentrals.Remove(SelectedConfiguredCentral);
        RenumberCentrals();
        SelectedConfiguredCentral = ConfiguredCentrals.Count > 0 ? ConfiguredCentrals[0] : null;
    }

    private void RenumberCentrals()
    {
        for (int i = 0; i < ConfiguredCentrals.Count; i++)
            ConfiguredCentrals[i].Index = i + 1;
    }

    private void ApplySelectedProfile()
    {
        var p = SelectedConfiguredCentral?.Profile;
        if (p == null) return;

        _suppressAutoDefaults = true;
        DccCentralType       = p.Type;
        DccCentralHost       = p.Host;
        DccCentralPort       = p.Port;
        DccCentralSerialPort = p.SerialPort;
        DccCentralBaudRate   = AvailableBaudRates.Contains(p.BaudRate) ? p.BaudRate : 19200;
        AutoConnect          = p.AutoConnect;
        _suppressAutoDefaults = false;
        TestConnectionCommand.NotifyCanExecuteChanged();
    }
    // ────────────────────────────────────────────────────────────────────────

    public void SetDccTestLocomotiveProvider(Func<object?> provider, INotifyPropertyChanged? notifier = null)    {
        _dccTestLocomotiveProvider = provider ?? throw new ArgumentNullException(nameof(provider));

        if (_dccTestLocomotiveNotifier != null)
            _dccTestLocomotiveNotifier.PropertyChanged -= OnDccTestLocomotiveProviderChanged;

        _dccTestLocomotiveNotifier = notifier;
        if (_dccTestLocomotiveNotifier != null)
            _dccTestLocomotiveNotifier.PropertyChanged += OnDccTestLocomotiveProviderChanged;

        OnPropertyChanged(nameof(SelectedLocomotiveForDccTest));
    }

    public void OpenDccCentralTabForLocomotive(object? locomotiveOrAddress)
    {
        _dccTestLocomotiveOverride = locomotiveOrAddress;
        SelectedSettingsTabIndex = DccCentralTabIndex;
        TestHandler.UseLocoAddressFrom(locomotiveOrAddress);
        OnPropertyChanged(nameof(SelectedLocomotiveForDccTest));
    }

    public void ClearDccTestLocomotiveOverride()
    {
        _dccTestLocomotiveOverride = null;
        OnPropertyChanged(nameof(SelectedLocomotiveForDccTest));
    }

    private void OnDccTestLocomotiveProviderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "SelectedLocomotive")
        {
            if (_dccTestLocomotiveOverride != null)
                _dccTestLocomotiveOverride = null;

            OnPropertyChanged(nameof(SelectedLocomotiveForDccTest));
        }
    }

    partial void OnSelectedDccCentralItemChanged(DccCentralListItem? value)
    {
        // Do enumu zapisujeme iba listy (reálne centrály). Klik na skupinu nič nemení.
        if (value == null)
            return;

        if (value.IsHeader)
        {
            // UX: klik na skupinu nech nezmení typ a nech sa ne"zasekne" zvýraznenie na skupine.
            SelectedDccCentralItem = FindItemByType(DccCentralType);
            TestConnectionCommand.NotifyCanExecuteChanged();
            return;
        }

        if (value.Type is DccCentralType t)
        {
            if (!value.IsImplemented)
            {
                TestConnectionCommand.NotifyCanExecuteChanged();
                return;
            }

            if (DccCentralType != t)
                DccCentralType = t;
        }

        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnDccCentralTypeChanged(DccCentralType value)
    {
        // Synchronizácia opačným smerom – keď sa nastaví typ (Load, ukladanie), vyberieme položku v ComboBoxe.
        SelectedDccCentralItem = FindItemByType(value);
        OnPropertyChanged(nameof(UsesSerialConnectionSettings));
        OnPropertyChanged(nameof(UsesNetworkConnectionSettings));

        // Informuj test-handler o aktuálnom skonfigurovanom type, aby vedel správne
        // vyhodnotiť dostupnosť Service Track aj v offline režime (napr. z21 start).
        if (TestHandler != null)
            TestHandler.ConfiguredCentralType = value;

        // Auto default port: iba keď používateľ port vedome nemenil
        if (!_suppressAutoDefaults && !_portTouchedByUser)
        {
            var defPort = DccCentralCatalog.GetDefaultPort(value);
            if (defPort.HasValue && defPort.Value is >= 1 and <= 65535)
                DccCentralPort = defPort.Value;
        }

        // Mark dirty if this is a project-level setting
        if (!_suppressAutoDefaults && HasProject && _mgr.Project != null && UseProjectForDcc)
            _mgr.Dirty.MarkDirty("dcc-type");

        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnDccCentralHostChanged(string value)
    {
        if (!_suppressAutoDefaults && HasProject && _mgr.Project != null && UseProjectForDcc)
            _mgr.Dirty.MarkDirty("dcc-host");
        
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnDccCentralPortChanged(int? value)
    {
        if (!_suppressAutoDefaults)
            _portTouchedByUser = true;

        // Mark dirty if this is a project-level setting
        if (!_suppressAutoDefaults && HasProject && _mgr.Project != null && UseProjectForDcc)
            _mgr.Dirty.MarkDirty("dcc-port");

        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnAutoConnectChanged(bool value)
    {
        if (!_suppressAutoDefaults && ConfiguredCentrals.Count > 0 && SelectedConfiguredCentral != null)
        {
            SelectedConfiguredCentral.Profile.AutoConnect = value;
            return;
        }

        if (!_suppressAutoDefaults && HasProject && _mgr.Project != null && UseProjectForDcc)
            _mgr.Dirty.MarkDirty("dcc-autoconnect");
    }

    partial void OnAutoSaveEnabledChanged(bool value)
    {
        if (value && AutoSaveIntervalMinutes <= 0)
            AutoSaveIntervalMinutes = 5;
    }

    partial void OnAutoSaveIntervalMinutesChanged(int value)
    {
        if (!AutoSaveEnabled)
            return;

        if (value <= 0)
            AutoSaveIntervalMinutes = 5;
    }

    partial void OnStartupModelClockHourChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 23);
        if (normalized != value)
            StartupModelClockHour = normalized;
    }

    partial void OnStartupModelClockMinuteChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 59);
        if (normalized != value)
            StartupModelClockMinute = normalized;
    }

    partial void OnUseProjectForDccChanged(bool value)
    {
        if (!_suppressAutoDefaults && HasProject && _mgr.Project != null)
            _mgr.Dirty.MarkDirty("dcc-override");
    }

    // ── Vypočítané informácie o mierke ─────────────────────────────────────

    private static string FormatModelLength(double realMm, double ratio)
    {
        var modelMm = realMm / ratio;
        if (modelMm >= 1000.0)
            return $"{modelMm / 1000.0:0.##} m";
        if (modelMm >= 10.0)
            return $"{modelMm / 10.0:0.#} cm";
        return $"{modelMm:0.0} mm";
    }

    private static string FormatModelSpeed(double realKmh, double ratio)
    {
        var modelKmh = realKmh / ratio;
        if (modelKmh < 1.0)
            return $"{modelKmh * 1000.0:0.##} m/h";
        return $"{modelKmh:0.###} km/h";
    }

    private static string FormatModelTime(double realSeconds, double ratio)
    {
        var modelSec = realSeconds / ratio;
        if (modelSec < 60.0)
            return $"{modelSec:0.#} s";
        return $"{modelSec / 60.0:0.#} min";
    }

    private double CurrentScaleDivisor =>
        TrackFlow.Services.Simulation.SimulationScaleResolver.ResolveScaleDivisor(Scale);

    public string ScaleInfoNameDisplay
    {
        get
        {
            var normalized = NormalizeScale(Scale);
            return normalized.ToUpperInvariant() switch
            {
                "H0" => "H0",
                "TT" => "TT",
                "N"  => "N",
                _    => normalized
            };
        }
    }

    public string ScaleInfo60KmhDisplay      => FormatModelSpeed(60.0,        CurrentScaleDivisor);
    public string ScaleInfo60MpsDisplay      => FormatModelSpeedMs(60.0,      CurrentScaleDivisor);
    public string ScaleInfo120KmhDisplay     => FormatModelSpeed(120.0,       CurrentScaleDivisor);
    public string ScaleInfo120MpsDisplay     => FormatModelSpeedMs(120.0,     CurrentScaleDivisor);
    public string ScaleInfo1KmDisplay          => FormatModelLength(1_000_000,  CurrentScaleDivisor);
    public string ScaleInfoLoco20mDisplay      => FormatModelLength(20_000,     CurrentScaleDivisor);
    public string ScaleInfoWagon26mDisplay     => FormatModelLength(26_000,     CurrentScaleDivisor);
    public string ScaleInfoSwitch30mDisplay    => FormatModelLength(30_000,     CurrentScaleDivisor);
    public string ScaleInfoModelHourDisplay    => FormatModelTime(3600.0,       CurrentScaleDivisor);
    public string ScaleInfoModelDayDisplay     => FormatModelTime(86_400.0,     CurrentScaleDivisor);
    public string ScaleInfoPlatform200mDisplay => FormatModelLength(200_000,    CurrentScaleDivisor);

    private static string FormatModelSpeedMs(double realKmh, double ratio)
    {
        var ms = realKmh / ratio / 3.6;
        if (ms < 0.01)
            return $"{ms * 100.0:0.##} cm/s";
        return $"{ms:0.####} m/s";
    }

    // ───────────────────────────────────────────────────────────────────────

    partial void OnScaleChanged(string value)
    {
        // Keep ComboBox selection in sync with persisted code.
        OnPropertyChanged(nameof(SelectedScaleItem));
        OnPropertyChanged(nameof(ScaleInfoNameDisplay));
        OnPropertyChanged(nameof(ScaleInfo60KmhDisplay));
        OnPropertyChanged(nameof(ScaleInfo60MpsDisplay));
        OnPropertyChanged(nameof(ScaleInfo120KmhDisplay));
        OnPropertyChanged(nameof(ScaleInfo120MpsDisplay));
        OnPropertyChanged(nameof(ScaleInfo1KmDisplay));
        OnPropertyChanged(nameof(ScaleInfoLoco20mDisplay));
        OnPropertyChanged(nameof(ScaleInfoWagon26mDisplay));
        OnPropertyChanged(nameof(ScaleInfoSwitch30mDisplay));
        OnPropertyChanged(nameof(ScaleInfoModelHourDisplay));
        OnPropertyChanged(nameof(ScaleInfoModelDayDisplay));
        OnPropertyChanged(nameof(ScaleInfoPlatform200mDisplay));

        // Dirty = project has unsaved changes indicator. Users expect this to react to scale changes
        // even if the scale is currently coming from app defaults (UseProjectForScale == false),
        // because the effective behavior of the open project changes immediately.
        if (!_suppressAutoDefaults)
            _mgr.Dirty.MarkDirty("scale");
    }

    partial void OnUseProjectForScaleChanged(bool value)
    {
        if (!_suppressAutoDefaults)
            _mgr.Dirty.MarkDirty("scale-override");
    }

    partial void OnMaxPathElementsChanged(int value)
    {
        if (!_suppressAutoDefaults && HasProject && _mgr.Project != null)
            _mgr.Dirty.MarkDirty("pathfinder-limits");
    }

    partial void OnMaxTurnoutsInPathChanged(int value)
    {
        if (!_suppressAutoDefaults && HasProject && _mgr.Project != null)
            _mgr.Dirty.MarkDirty("pathfinder-limits");
    }

    public void RefreshProjectState()
    {
        HasProject = !string.IsNullOrWhiteSpace(_mgr.CurrentProjectPath);
        OnPropertyChanged(nameof(HasOpenProject));
        OnPropertyChanged(nameof(CurrentProjectPath));
        OnPropertyChanged(nameof(CurrentProjectName));
    }

    public void Load()
    {
        ThrowIfDisposed();
        _suppressAutoDefaults = true;
        _portTouchedByUser = false;

        // ⚠️ DÔLEŽITÉ: NEVOLAŤ LoadApp() ak je projekt už otvorený!
        // LoadApp() resetuje CurrentProject na null, čo by vymazalo práve otvorený projekt.
        // LoadApp() sa volá len pri štarte aplikácie (v MainWindowViewModel konštruktore).
        // Tu načítavame len nastavenia z už načítaného projektu/app.

        // Keď je nastavená cesta projektu, berieme to ako "projekt otvorený".
        RefreshProjectState();

        // Globálne
        Language = _mgr.App.Language;
        AccentColor = _mgr.App.AccentColor;
        OpenLastProjectOnStartup = _mgr.App.OpenLastProjectOnStartup;
        DefaultProjectsDirectory = _mgr.App.DefaultProjectsDirectory ?? string.Empty;
        AutoSaveEnabled = _mgr.App.AutoSaveEnabled;
        AutoSaveIntervalMinutes = Math.Clamp(_mgr.App.AutoSaveIntervalMinutes, 0, 120);
        ShowTooltipsInApp = _mgr.App.ShowTooltipsInApp;
        ShowClockOnStartup = _mgr.App.ShowClockOnStartup;
        ShowClockStartPauseButton = _mgr.App.ShowClockStartPauseButton;
        SetModelClockTimeOnStartup = _mgr.App.SetModelClockTimeOnStartup;
        StartupModelClockHour = Math.Clamp(_mgr.App.StartupModelClockHour, 0, 23);
        StartupModelClockMinute = Math.Clamp(_mgr.App.StartupModelClockMinute, 0, 59);
        VisibleWagonsInTrain = _mgr.App.VisibleWagonsInTrain;
        EnableTransientRouteMessages = _mgr.App.EnableTransientRouteMessages;
        ShowTelemetryInStatusBar = _mgr.App.ShowTelemetryInStatusBar;
        RouteMessageTtlSuccessMs = Math.Clamp(_mgr.App.RouteMessageTtlSuccessMs, 0, 15000);
        RouteMessageTtlInfoMs = Math.Clamp(_mgr.App.RouteMessageTtlInfoMs, 0, 15000);
        RouteMessageTtlWarningMs = Math.Clamp(_mgr.App.RouteMessageTtlWarningMs, 0, 15000);

        // DCC: projekt override ak existuje, inak app default
        if (HasProject && _mgr.Project != null && _mgr.HasProjectDccOverride())
        {
            UseProjectForDcc = true;
            DccCentralType = _mgr.Project.DccCentralType ?? _mgr.App.DefaultDccCentralType;
            DccCentralHost = _mgr.Project.DccCentralHost ?? _mgr.App.DefaultDccCentralHost;
            DccCentralPort = _mgr.Project.DccCentralPort ?? _mgr.App.DefaultDccCentralPort;
            DccCentralSerialPort = _mgr.Project.DccSerialPort ?? _mgr.App.DefaultDccSerialPort;
            DccCentralBaudRate = _mgr.Project.DccBaudRate ?? _mgr.App.DefaultDccBaudRate;
            AutoConnect = _mgr.Project.AutoConnect ?? _mgr.App.DefaultAutoConnect;
        }
        else
        {
            UseProjectForDcc = false;
            DccCentralType = _mgr.App.DefaultDccCentralType;
            DccCentralHost = _mgr.App.DefaultDccCentralHost;
            DccCentralPort = _mgr.App.DefaultDccCentralPort;
            DccCentralSerialPort = _mgr.App.DefaultDccSerialPort;
            DccCentralBaudRate = _mgr.App.DefaultDccBaudRate;
            AutoConnect = _mgr.App.DefaultAutoConnect;
        }

        // Port môže byť null (keď je pole vymazané). Nech to nikdy nepadá.
        DccCentralPort ??= DccCentralCatalog.GetDefaultPort(DccCentralType);
        DccCentralBaudRate = AvailableBaudRates.Contains(DccCentralBaudRate) ? DccCentralBaudRate : 19200;
        RefreshAvailablePorts();

        // ─── Zoznam skonfigurovaných centrál ────────────────────────────────
        DisposeConfiguredCentralItems();
        ConfiguredCentrals.Clear();
        var effectiveProfiles = _mgr.GetEffectiveDccCentralProfiles();
        for (int i = 0; i < effectiveProfiles.Count; i++)
            ConfiguredCentrals.Add(new ConfiguredDccCentralItem(effectiveProfiles[i], i + 1));

        var selId = _mgr.GetEffectiveSelectedDccCentralProfileId();
        _selectedConfiguredCentral = selId.HasValue
            ? ConfiguredCentrals.FirstOrDefault(x => x.Profile.Id == selId.Value)
              ?? (ConfiguredCentrals.Count > 0 ? ConfiguredCentrals[0] : null)
            : (ConfiguredCentrals.Count > 0 ? ConfiguredCentrals[0] : null);
        OnPropertyChanged(nameof(SelectedConfiguredCentral));
        OnPropertyChanged(nameof(HasSelectedCentral));
        OnPropertyChanged(nameof(IsZ21Selected));
        EditCentralCommand.NotifyCanExecuteChanged();
        DeleteCentralCommand.NotifyCanExecuteChanged();
        TestHandler.SelectedCentralProfileId = _selectedConfiguredCentral?.Profile.Id;
        if (_selectedConfiguredCentral != null)
            ApplySelectedProfile();
        // ────────────────────────────────────────────────────────────────────

        // Initialize connection state dots and test-button state from the live service snapshot.
        RefreshConfiguredCentralConnectionStates();
        TestHandler.RefreshConnectionDependentUiSafe();

        // Mierka: projekt override ak existuje, inak app default.
        // Prázdne/neplatné hodnoty normalizujeme, aby ComboBox nikdy neostal bez výberu.
        if (HasProject && _mgr.Project != null && _mgr.Project.Scale != null)
        {
            UseProjectForScale = true;
            Scale = NormalizeScale(_mgr.Project.Scale);
        }
        else
        {
            UseProjectForScale = false;
            Scale = NormalizeScale(_mgr.App.DefaultScale);
        }

        // RoutePathfinder limity sú vždy projektové (neprepájajú sa do App defaults).
        if (HasProject && _mgr.Project != null)
        {
            MaxPathElements = Math.Clamp(_mgr.Project.MaxPathElements, 1, 200);
            MaxTurnoutsInPath = Math.Clamp(_mgr.Project.MaxTurnoutsInPath, 0, 50);
        }
        else
        {
            MaxPathElements = 15;
            MaxTurnoutsInPath = 5;
        }

        SimulationSpeedFactor = _mgr.Project != null
            ? ProjectSettingsData.NormalizeSimulationSpeedFactor(_mgr.Project.SimulationSpeedFactor)
            : ProjectSettingsData.DefaultSimulationSpeedFactor;

        BuildDccCentralList();

        // Vždy prebrieš test-handler s aktuálnym skonfigurovaným typom (aj keď Load
        // nastavil rovnakú hodnotu ako default a partial-event sa nezavolal).
        TestHandler.ConfiguredCentralType = DccCentralType;

        ConnectionTestResult = "";
        IsTestingConnection = false;
        TestConnectionCommand.NotifyCanExecuteChanged();

        _suppressAutoDefaults = false;
    }

    private void DisposeConfiguredCentralItems()
    {
        foreach (var item in ConfiguredCentrals)
            item.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SettingsViewModel));
    }

    private void BuildDccCentralList()
    {
        DccCentralItems.Clear();

        void Header(string name) => DccCentralItems.Add(DccCentralListItem.Header(name));
        void Item(string name, DccCentralType t, int indent, bool implemented)
            => DccCentralItems.Add(DccCentralListItem.Item(name, t, indent, implemented));

        foreach (var g in DccCentralCatalog.GetGroups())
        {
            Header(g.Name);
            foreach (var it in g.Items)
                Item(it.Name, it.Type, 1, it.IsImplemented);
        }

        // synchronizácia výberu po naplnení
        SelectedDccCentralItem = FindItemByType(DccCentralType);
    }

    private DccCentralListItem? FindItemByType(DccCentralType type)
        => DccCentralItems.FirstOrDefault(x => !x.IsHeader && x.Type == type);

    public bool Save()
    {
        // 1) Vždy ulož globálne UI preferencie
        _mgr.App.Language = Language;
        _mgr.App.AccentColor = AccentColor;
        _mgr.App.OpenLastProjectOnStartup = OpenLastProjectOnStartup;
        _mgr.App.DefaultProjectsDirectory = DefaultProjectsDirectory?.Trim() ?? string.Empty;
        var normalizedAutoSaveInterval = Math.Clamp(AutoSaveIntervalMinutes, 0, 120);
        if (AutoSaveEnabled && normalizedAutoSaveInterval <= 0)
            normalizedAutoSaveInterval = 5;

        _mgr.App.AutoSaveEnabled = AutoSaveEnabled;
        _mgr.App.AutoSaveIntervalMinutes = normalizedAutoSaveInterval;
        _mgr.App.ShowTooltipsInApp = ShowTooltipsInApp;
        _mgr.App.ShowClockOnStartup = ShowClockOnStartup;
        _mgr.App.ShowClockStartPauseButton = ShowClockStartPauseButton;
        _mgr.App.SetModelClockTimeOnStartup = SetModelClockTimeOnStartup;
        _mgr.App.StartupModelClockHour = Math.Clamp(StartupModelClockHour, 0, 23);
        _mgr.App.StartupModelClockMinute = Math.Clamp(StartupModelClockMinute, 0, 59);
        _mgr.App.VisibleWagonsInTrain = VisibleWagonsInTrain;
        _mgr.App.EnableTransientRouteMessages = EnableTransientRouteMessages;
        _mgr.App.ShowTelemetryInStatusBar = ShowTelemetryInStatusBar;
        _mgr.App.RouteMessageTtlSuccessMs = Math.Clamp(RouteMessageTtlSuccessMs, 0, 15000);
        _mgr.App.RouteMessageTtlInfoMs = Math.Clamp(RouteMessageTtlInfoMs, 0, 15000);
        _mgr.App.RouteMessageTtlWarningMs = Math.Clamp(RouteMessageTtlWarningMs, 0, 15000);

        // 2) DCC – buď do projektu, alebo do app default
        var persistedProfiles = ConfiguredCentrals
            .Select(item => item.Profile)
            .ToList();
        var selectedProfileId = SelectedConfiguredCentral?.Profile.Id;

        var portToSave =
            (DccCentralPort is >= 1 and <= 65535)
                ? DccCentralPort!.Value
                : (DccCentralCatalog.GetDefaultPort(DccCentralType) ?? 21105);

        if (HasProject && _mgr.Project != null && UseProjectForDcc)
        {
            _mgr.Project.DccCentralProfiles = persistedProfiles;
            _mgr.Project.SelectedDccCentralProfileId = selectedProfileId;
            _mgr.Project.DccCentralType = DccCentralType;
            _mgr.Project.DccCentralHost = DccCentralHost;
            _mgr.Project.DccCentralPort = portToSave;
            _mgr.Project.DccSerialPort  = DccCentralSerialPort;
            _mgr.Project.DccBaudRate    = DccCentralBaudRate;
            if (ConfiguredCentrals.Count == 0)
                _mgr.Project.AutoConnect = AutoConnect;
            else
                _mgr.Project.AutoConnect = null;
        }
        else
        {
            _mgr.App.DccCentralProfiles.Clear();
            foreach (var profile in persistedProfiles)
                _mgr.App.DccCentralProfiles.Add(profile);
            _mgr.App.SelectedDccCentralProfileId = selectedProfileId;

            _mgr.App.DefaultDccCentralType = DccCentralType;
            _mgr.App.DefaultDccCentralHost = DccCentralHost;
            _mgr.App.DefaultDccCentralPort = portToSave;
            _mgr.App.DefaultDccSerialPort  = DccCentralSerialPort;
            _mgr.App.DefaultDccBaudRate    = DccCentralBaudRate;
            if (ConfiguredCentrals.Count == 0)
                _mgr.App.DefaultAutoConnect = AutoConnect;

            if (HasProject && _mgr.Project != null)
            {
                _mgr.Project.DccCentralType = null;
                _mgr.Project.DccCentralHost = null;
                _mgr.Project.DccCentralPort = null;
                _mgr.Project.DccSerialPort  = null;
                _mgr.Project.DccBaudRate    = null;
                _mgr.Project.AutoConnect    = null;
                _mgr.Project.DccCentralProfiles = null;
                _mgr.Project.SelectedDccCentralProfileId = null;
            }
        }

        // 3) Mierka – buď do projektu, alebo do app default
        Scale = NormalizeScale(Scale);
        if (HasProject && _mgr.Project != null && UseProjectForScale)
        {
            _mgr.Project.Scale = Scale;
        }
        else
        {
            _mgr.App.DefaultScale = Scale;

            if (HasProject && _mgr.Project != null)
                _mgr.Project.Scale = null;
        }

        // 4) RoutePathfinder limity patria do ProjectSettingsData.
        if (HasProject)
        {
            var projectSettings = _mgr.EnsureProjectSettings();
            projectSettings.MaxPathElements = Math.Clamp(MaxPathElements, 1, 200);
            projectSettings.MaxTurnoutsInPath = Math.Clamp(MaxTurnoutsInPath, 0, 50);
        }

        if (_mgr.Project != null)
        {
            var projectSettings = _mgr.EnsureProjectSettings();
            SimulationSpeedFactor = ProjectSettingsData.NormalizeSimulationSpeedFactor(SimulationSpeedFactor);
            projectSettings.SimulationSpeedFactor = SimulationSpeedFactor;
        }

        // 5) Persist
        var okApp = _mgr.SaveApp();
        var okProject = true;

        if (HasProject && _mgr.Project != null)
            okProject = _mgr.SaveProject();

        return okApp && okProject;
    }

    internal static string NormalizeScale(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "H0";

        // Accept both canonical persisted values ("H0", "TT", "N") and legacy UI/display strings
        // like "H0 - 1/87" that may have been persisted by earlier buggy versions.
        var v = value.Trim().ToUpperInvariant();

        // Strip common decorations after whitespace or '-' (e.g. "TT - 1/120" -> "TT")
        var cutAt = v.IndexOfAny(new[] { ' ', '-', '/' });
        if (cutAt > 0)
            v = v.Substring(0, cutAt).Trim();

        return v switch
        {
            "H0" or "HO" => "H0",
            "TT" => "TT",
            "N" => "N",
            _ => "H0"
        };
    }

    private void OnSave()
    {
        ResetCommunicationTestPanels();
        var ok = Save();
        CloseRequested?.Invoke(ok);
    }

    private void OnCancel()
    {
        ResetCommunicationTestPanels();
        CloseRequested?.Invoke(false);
    }

    /// <summary>
    /// Clears all communication-test related messages so each Settings window open starts clean.
    /// </summary>
    public void ResetCommunicationTestPanels()
    {
        // Network probe (legacy test button) text
        ConnectionTestResult = string.Empty;
        IsTestingConnection = false;

        // CV1 communication test panel
        TestHandler.ClearAllTestResults();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _dccConnectionService.IsConnectedChanged -= OnDccIsConnectedChanged;
        if (_dccConnectionServiceConcrete != null)
            _dccConnectionServiceConcrete.ConnectionStateChanged -= OnDccConnectionStateChanged;

        if (_dccTestLocomotiveNotifier != null)
            _dccTestLocomotiveNotifier.PropertyChanged -= OnDccTestLocomotiveProviderChanged;

        DisposeConfiguredCentralItems();
        TestHandler.Dispose();
    }

    private bool CanTestConnection()
    {
        if (SelectedDccCentralItem is null || SelectedDccCentralItem.IsHeader || !SelectedDccCentralItem.IsImplemented)
            return false;

        if (!UsesNetworkConnectionSettings)
            return false;

        return !IsTestingConnection &&
               !string.IsNullOrWhiteSpace(DccCentralHost) &&
               DccCentralPort is >= 1 and <= 65535;
    }

    private async Task TestConnectionAsync()
    {
        if (!CanTestConnection())
            return;

        IsTestingConnection = true;
        ConnectionTestResult = "Testujem…";
        TestConnectionCommand.NotifyCanExecuteChanged();

        var typeName = DccCentralDisplayName.Get(DccCentralType);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            // 1) DNS / IP resolve
            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(DccCentralHost);
            }
            catch (Exception ex)
            {
                ConnectionTestResult = "DNS/resolve zlyhalo: " + ex.Message;
                return;
            }

            if (addresses.Length == 0)
            {
                ConnectionTestResult = "DNS/resolve: bez výsledku.";
                return;
            }

            // 2) orientačný test
            var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
            var port = DccCentralPort ?? DccCentralCatalog.GetDefaultPort(DccCentralType) ?? 21105;

            // Z21 / z21: máme UDP probe (serial) cez LAN_GET_SERIAL_NUMBER
            var isZ21Family = DccCentralType is DccCentralType.Z21 or DccCentralType.Z21Legacy;

            if (isZ21Family)
            {
                var udp = await TryZ21UdpProbeAsync(ip, port, cts.Token);
                var tcpOk = await TryTcpConnectAsync(ip, port, cts.Token);

                if (udp.Received)
                {
                    var serialText = udp.SerialNumber.HasValue ? $" S/N: {udp.SerialNumber.Value}" : "";
                    ConnectionTestResult = $"OK: {typeName} UDP odpoveď z {udp.From}:{udp.FromPort}.{serialText}";
                    return;
                }

                if (udp.Sent)
                {
                    ConnectionTestResult =
                        $"UDP odoslanie OK na {ip}:{port}, bez odpovede (timeout). " +
                        (tcpOk ? "TCP connect zároveň OK." : "TCP connect zlyhal (pri UDP-only zariadeniach je to normálne).");
                    return;
                }

                ConnectionTestResult =
                    $"UDP test zlyhal na {ip}:{port}. " +
                    (tcpOk ? "TCP connect OK." : "TCP connect zlyhal.");
                return;
            }

            // Ostatné: len základný UDP send test (bez očakávania odpovede)
            var sent = await TryUdpSendAsync(ip, port, cts.Token);
            var tcpOk2 = await TryTcpConnectAsync(ip, port, cts.Token);

            if (sent)
            {
                ConnectionTestResult =
                    $"UDP odoslanie OK na {ip}:{port}. " +
                    (tcpOk2 ? "TCP connect zároveň OK." : "TCP connect zlyhal (pri UDP-only zariadeniach je to normálne).");
                return;
            }

            ConnectionTestResult =
                $"UDP odoslanie zlyhalo na {ip}:{port}. " +
                (tcpOk2 ? "TCP connect OK." : "TCP connect zlyhal.");
        }
        catch (Exception ex)
        {
            ConnectionTestResult = "Chyba testu: " + ex.Message;
        }
        finally
        {
            IsTestingConnection = false;
            TestConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    private sealed record UdpProbeResult(bool Sent, bool Received, string From, int FromPort, uint? SerialNumber);

    private static async Task<UdpProbeResult> TryZ21UdpProbeAsync(IPAddress ip, int port, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Connect(ip, port);

            // Z21: LAN_GET_SERIAL_NUMBER
            // Request: 04 00 10 00
            var payload = new byte[] { 0x04, 0x00, 0x10, 0x00 };
            await udp.SendAsync(payload, payload.Length);

            // pokus o odpoveď s timeoutom
            var receiveTask = udp.ReceiveAsync();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(800, ct));

            if (completed == receiveTask)
            {
                var r = receiveTask.Result;

                // Očakávaná odpoveď: 08 00 10 00 + 4B serial (LE)
                uint? serial = null;
                var data = r.Buffer;

                if (data is { Length: >= 8 } &&
                    data[0] == 0x08 && data[1] == 0x00 &&
                    data[2] == 0x10 && data[3] == 0x00)
                {
                    serial = (uint)(data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24));
                }

                return new UdpProbeResult(
                    Sent: true,
                    Received: true,
                    From: r.RemoteEndPoint.Address.ToString(),
                    FromPort: r.RemoteEndPoint.Port,
                    SerialNumber: serial);
            }

            return new UdpProbeResult(true, false, "", 0, null);
        }
        catch
        {
            return new UdpProbeResult(false, false, "", 0, null);
        }
    }

    private static async Task<bool> TryUdpSendAsync(IPAddress ip, int port, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Connect(ip, port);

            // GenericIpUdp: zatiaľ nemáme protokol -> pošleme 1B "ping" (nulový bajt)
            var payload = new byte[] { 0x00 };
            await udp.SendAsync(payload, payload.Length);

            ct.ThrowIfCancellationRequested();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryTcpConnectAsync(IPAddress ip, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var reg = ct.Register(() => SafeClose(client));
            await client.ConnectAsync(ip, port);
            return client.Connected;
        }
        catch
        {
            return false;
        }

        static void SafeClose(TcpClient c)
        {
            try { c.Close(); } catch { }
        }
    }
}

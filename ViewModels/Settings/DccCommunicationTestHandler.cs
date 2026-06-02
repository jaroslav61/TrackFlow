using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrackFlow.Models;
using TrackFlow.Services.Dcc;

namespace TrackFlow.ViewModels.Settings;

public enum DccCommunicationMessageKind
{
    None,
    Info,
    Success,
    Error
}

public partial class DccCommunicationTestHandler : ObservableObject, IDisposable
{
    private readonly IDccCommunicationTestService _dccService;
    private readonly IDccConnectionService _connectionService;
    private readonly SynchronizationContext? _uiContext;

    // Stores last test result per central profile (multi-central mode).
    // Key = DccCentralProfile.Id
    private readonly Dictionary<Guid, (string Result, DccCommunicationMessageKind Kind)> _perCentralResults = new();

    public DccCommunicationTestHandler(IDccCommunicationTestService dccService, IDccConnectionService connectionService)
    {
        _dccService = dccService ?? throw new ArgumentNullException(nameof(dccService));
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        _uiContext = SynchronizationContext.Current;
        TestCommand = new AsyncRelayCommand<object?>(ExecuteTestAsync, CanExecuteTest);
        _connectionService.IsConnectedChanged += OnConnectionStateChanged;

        // Multi-central: per-profile state changes can happen while IsAnyConnected stays true.
        // Listen to detailed events too, but keep IDccConnectionService as the main abstraction.
        if (_connectionService is DccConnectionService concrete)
            concrete.ConnectionStateChanged += OnConcreteConnectionStateChanged;
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

    [ObservableProperty]
    private bool _isTestingCommunication;

    [ObservableProperty]
    private int _decoderTimeoutMs = 3_000;

    [ObservableProperty]
    private string _testResult = string.Empty;

    [ObservableProperty]
    private DccCommunicationMessageKind _messageKind;

    [ObservableProperty]
    private bool _isServiceTrackProgrammingMode = true;

    [ObservableProperty]
    private int _testLocoAddress = 3;

    /// <summary>
    /// Aktuálne v Settings UI vybraný (skonfigurovaný) typ DCC centrály.
    /// Používa sa na vyhodnotenie dostupnosti Service Track AJ keď centrála
    /// nie je práve pripojená.
    /// </summary>
    private DccCentralType? _configuredCentralType;
    public DccCentralType? ConfiguredCentralType
    {
        get => _configuredCentralType;
        set
        {
            if (_configuredCentralType == value) return;
            _configuredCentralType = value;
            OnPropertyChanged(nameof(ConfiguredCentralType));
            OnPropertyChanged(nameof(IsZ21Start));
            OnPropertyChanged(nameof(IsServiceTrackUnavailable));
            OnPropertyChanged(nameof(IsServiceTrackAvailable));
            OnPropertyChanged(nameof(ServiceTrackDisabledTooltip));
            OnPropertyChanged(nameof(SupportsProgrammingTest));
            OnPropertyChanged(nameof(DisabledConnectionToolTip));
            OnPropertyChanged(nameof(DisabledConnectionHint));
            OnPropertyChanged(nameof(HasDisabledTestHint));
            OnPropertyChanged(nameof(IsTestButtonEnabled));
            TestCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsPomProgrammingMode
    {
        get => !IsServiceTrackProgrammingMode;
        set => IsServiceTrackProgrammingMode = !value;
    }

    public bool IsCvReadAvailableForSelectedMode => IsServiceTrackProgrammingMode;

    public bool IsTestButtonEnabled
        => IsConnected
           && SupportsProgrammingTest
           && !IsTestingCommunication
           && IsCvReadAvailableForSelectedMode;

    /// <summary>
    /// True when the currently selected central (Settings list row) is connected.
    /// In legacy/single-central mode, falls back to the global connection state.
    /// </summary>
    public bool IsConnected
    {
        get
        {
            if (_connectionService is DccConnectionService concrete && concrete.IsMultiCentralModeActive)
            {
                // Multi-central: the selection matters.
                if (SelectedCentralProfileId.HasValue)
                    return concrete.TryGetConnectedClient(SelectedCentralProfileId.Value, out _);

                // No selection -> treat as "any connected".
                return concrete.IsAnyConnected;
            }

            // Legacy / single-central
            return _connectionService.IsConnected;
        }
    }

    public bool IsNotConnected => !IsConnected;

    /// <summary>
    /// Profile currently selected in Settings (the highlighted row in the centrals list).
    /// Used to route the communication test to the correct device.
    /// </summary>
    private Guid? _selectedCentralProfileId;

    public Guid? SelectedCentralProfileId
    {
        get => _selectedCentralProfileId;
        set
        {
            if (_selectedCentralProfileId == value)
                return;

            // Persist current UI state under the previously selected profile.
            PersistCurrentUiState();

            _selectedCentralProfileId = value;
            OnPropertyChanged(nameof(SelectedCentralProfileId));

            // Load UI state for the newly selected profile.
            RestoreUiStateForSelection();

            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsNotConnected));
            OnPropertyChanged(nameof(SupportsProgrammingTest));
            OnPropertyChanged(nameof(DisabledConnectionToolTip));
            OnPropertyChanged(nameof(DisabledConnectionHint));
            OnPropertyChanged(nameof(HasDisabledTestHint));
            OnPropertyChanged(nameof(IsTestButtonEnabled));
            TestCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Clears all per-central cached results and resets the currently displayed UI.
    /// Call this when Settings window closes so the next open starts clean.
    /// </summary>
    public void ClearAllTestResults()
    {
        _perCentralResults.Clear();
        IsTestingCommunication = false;
        MessageKind = DccCommunicationMessageKind.None;
        TestResult = string.Empty;
    }

    public void Dispose()
    {
        _connectionService.IsConnectedChanged -= OnConnectionStateChanged;

        if (_connectionService is DccConnectionService concrete)
            concrete.ConnectionStateChanged -= OnConcreteConnectionStateChanged;
    }

    private void PersistCurrentUiState()
    {
        if (_connectionService is not DccConnectionService concrete || !concrete.IsMultiCentralModeActive)
            return;

        if (!_selectedCentralProfileId.HasValue)
            return;

        _perCentralResults[_selectedCentralProfileId.Value] = (TestResult ?? string.Empty, MessageKind);
    }

    private void RestoreUiStateForSelection()
    {
        if (_connectionService is not DccConnectionService concrete || !concrete.IsMultiCentralModeActive)
            return;

        if (!_selectedCentralProfileId.HasValue)
        {
            // No selection -> keep UI empty.
            MessageKind = DccCommunicationMessageKind.None;
            TestResult = string.Empty;
            return;
        }

        if (_perCentralResults.TryGetValue(_selectedCentralProfileId.Value, out var state))
        {
            MessageKind = state.Kind;
            TestResult = state.Result;
        }
        else
        {
            MessageKind = DccCommunicationMessageKind.None;
            TestResult = string.Empty;
        }
    }

    public bool SupportsProgrammingTest
    {
        get
        {
            if (TryGetSelectedConnectedClient(out var selectedClient))
                return selectedClient is IDccProgrammingClient;

            if (IsConnected)
                return _connectionService.Client is IDccProgrammingClient;

            return _configuredCentralType switch
            {
                DccCentralType.Z21 => true,
                DccCentralType.Z21Legacy => true,
                DccCentralType.NanoX_S88 => true,
                _ => false
            };
        }
    }

    private bool TryGetSelectedConnectedClient(out IDccCentralClient client)
    {
        client = null!;

        if (!SelectedCentralProfileId.HasValue)
            return false;

        if (_connectionService is not DccConnectionService concrete)
            return false;

        if (!concrete.IsMultiCentralModeActive)
            return false;

        return concrete.TryGetConnectedClient(SelectedCentralProfileId.Value, out client);
    }

    public bool HasDisabledTestHint => !string.IsNullOrWhiteSpace(DisabledConnectionHint);

    /// <summary>
    /// True ak je (pripojená alebo skonfigurovaná) centrála Roco z21 START.
    /// Slúži UI len ako informačný príznak – Service Track v UI NEBLOKUJEME
    /// (z21 start zvláda Service Mode príkazy cez prepnutie hlavnej trate).
    /// </summary>
    public bool IsZ21Start
    {
        get
        {
            // 1) Ak sme pripojení a poznáme reálny HwType, ten je smerodajný.
            if (IsConnected && _connectionService.Client is Z21Client z21
                && z21.HardwareType != Z21HardwareType.Unknown)
            {
                return z21.HardwareType == Z21HardwareType.Z21Start
                    || z21.HardwareType == Z21HardwareType.Z21Small;
            }

            // 2) Inak sa rozhodneme podľa skonfigurovaného typu v Settings UI.
            //    DccCentralType.Z21Legacy = "Roco/Fleischmann z21" (štartovacia).
            return _configuredCentralType == DccCentralType.Z21Legacy;
        }
    }

    /// <summary>
    /// True ak (pripojená alebo skonfigurovaná) centrála reálne nedokáže urobiť service-mode CV-read
    /// (napr. samostatný booster bez vlastného DCC generátora). z21 start NIE JE v tejto skupine,
    /// pretože Service Mode príkazy zvláda krátkym prepnutím hlavnej trate.
    /// </summary>
    public bool IsServiceTrackUnavailable
    {
        get
        {
            // 1) Reálny HwType pripojenej centrály má prednosť.
            if (IsConnected && _connectionService.Client is Z21Client z21
                && z21.HardwareType != Z21HardwareType.Unknown)
            {
                return !z21.HardwareType.SupportsServiceModeProgramming();
            }

            // 2) Fallback na skonfigurovaný typ – pri unknown / bežných centrálach necháme dostupné.
            return false;
        }
    }

    public bool IsServiceTrackAvailable => !IsServiceTrackUnavailable;

    public string ServiceTrackDisabledTooltip => IsServiceTrackUnavailable
        ? "Centrála nepodporuje Service Track"
        : string.Empty;

    public bool HasTestResult => !string.IsNullOrWhiteSpace(TestResult);

    public string DisabledConnectionToolTip
        => !IsCvReadAvailableForSelectedMode
            ? "Čítanie CV je dostupné iba v režime Service Track."
            : !IsConnected
            ? "DCC centrála musí byť pripojená."
            : !SupportsProgrammingTest
                ? "Čítanie CV pre túto centrálu zatiaľ nie je implementované."
                : string.Empty;

    public string DisabledConnectionHint
        => !IsCvReadAvailableForSelectedMode
            ? "Režim POM nepodporuje čítanie CV."
            : !IsConnected
            ? "DCC centrála musí byť pripojená."
            : !SupportsProgrammingTest
                ? "Čítanie CV pre túto centrálu zatiaľ nie je implementované."
                : string.Empty;

    public string TestResultBackground => MessageKind switch
    {
        DccCommunicationMessageKind.Success => "#DCFCE7",
        DccCommunicationMessageKind.Error => "#FEE2E2",
        DccCommunicationMessageKind.Info => "#FEF3C7",
        _ => "Transparent"
    };

    public string TestResultBorderBrush => MessageKind switch
    {
        DccCommunicationMessageKind.Success => "#86EFAC",
        DccCommunicationMessageKind.Error => "#FCA5A5",
        DccCommunicationMessageKind.Info => "#FCD34D",
        _ => "Transparent"
    };

    public IAsyncRelayCommand<object?> TestCommand { get; }
    partial void OnIsTestingCommunicationChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(IsTestButtonEnabled));
        TestCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsServiceTrackProgrammingModeChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(IsPomProgrammingMode));
        OnPropertyChanged(nameof(IsCvReadAvailableForSelectedMode));
        OnPropertyChanged(nameof(IsTestButtonEnabled));
        OnPropertyChanged(nameof(DisabledConnectionToolTip));
        OnPropertyChanged(nameof(DisabledConnectionHint));
        OnPropertyChanged(nameof(HasDisabledTestHint));
        TestCommand.NotifyCanExecuteChanged();
    }

    partial void OnTestResultChanged(string value)
    {
        _ = value;
        OnPropertyChanged(nameof(HasTestResult));
    }

    partial void OnMessageKindChanged(DccCommunicationMessageKind value)
    {
        _ = value;
        OnPropertyChanged(nameof(TestResultBackground));
        OnPropertyChanged(nameof(TestResultBorderBrush));
    }

    private void OnConnectionStateChanged(bool isConnected)
    {
        _ = isConnected;
        DispatchToUi(RefreshConnectionDependentUi);
    }

    private void OnConcreteConnectionStateChanged(DccConnectionStateChange change)
    {
        _ = change;
        // Any per-central change can affect the selected-row derived state.
        DispatchToUi(RefreshConnectionDependentUi);
    }

    /// <summary>
    /// Public hook for owners (SettingsViewModel) to force-refresh the derived properties.
    /// Safe to call from any thread.
    /// </summary>
    public void RefreshConnectionDependentUiSafe()
        => DispatchToUi(RefreshConnectionDependentUi);

    private void RefreshConnectionDependentUi()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsNotConnected));
        OnPropertyChanged(nameof(IsZ21Start));
        OnPropertyChanged(nameof(IsServiceTrackUnavailable));
        OnPropertyChanged(nameof(IsServiceTrackAvailable));
        OnPropertyChanged(nameof(ServiceTrackDisabledTooltip));
        OnPropertyChanged(nameof(SupportsProgrammingTest));
        OnPropertyChanged(nameof(DisabledConnectionToolTip));
        OnPropertyChanged(nameof(DisabledConnectionHint));
        OnPropertyChanged(nameof(HasDisabledTestHint));
        OnPropertyChanged(nameof(IsTestButtonEnabled));

        TestCommand.NotifyCanExecuteChanged();
    }

    private bool CanExecuteTest(object? parameter) => IsTestButtonEnabled;

    /// <summary>
    /// Predvyplní POM adresu lokomotívy z objektu/adresy, ktorú posiela nadradené UI.
    /// Podporuje rovnaké typy ako CommandParameter pre <see cref="TestCommand"/>.
    /// </summary>
    public void UseLocoAddressFrom(object? locomotiveOrAddress)
    {
        TestLocoAddress = ResolveLocoAddress(locomotiveOrAddress);
    }


    private async Task ExecuteTestAsync(object? commandParameter)
    {
        if (!CanExecuteTest(commandParameter))
        {
            return;
        }

        // Snapshot: which profile is being tested. User can change selection while the test is running.
        var testedProfileId = (_connectionService is DccConnectionService c && c.IsMultiCentralModeActive)
            ? SelectedCentralProfileId
            : null;

        // Snapshot the configured type + display name at the moment the test starts.
        // (User can change selection while the async test is running.)
        var testedCentralType = ConfiguredCentralType;
        var testedCentralName = ResolveCentralDisplayName(testedCentralType);

        IsTestingCommunication = true;
        MessageKind = DccCommunicationMessageKind.Info;
        SetResultForProfile(testedProfileId, "Testujem komunikáciu…", DccCommunicationMessageKind.Info);

        try
        {
            var locoAddress = ResolveLocoAddress(commandParameter);
            var mode = IsServiceTrackProgrammingMode
                ? DccProgrammingTestMode.ServiceTrack
                : DccProgrammingTestMode.ProgramOnMain;

            // Prefer the selected profile's connected client in multi-central mode.
            if (TryGetSelectedConnectedClient(out var selectedClient))
            {
                var timeoutMs = Math.Clamp(DecoderTimeoutMs, 1_000, 5_000);

                if (selectedClient is not IDccProgrammingClient programmingClient)
                    throw new NotSupportedException("Čítanie CV pre túto centrálu zatiaľ nie je implementované.");

                var cv1 = await programmingClient.ReadCvAsync(1, mode, timeoutMs, locoAddress);
                var modeText = mode == DccProgrammingTestMode.ServiceTrack
                    ? "Programovacia koľaj"
                    : $"POM na hlavnej trati, loco {locoAddress}";

                SetResultForProfile(testedProfileId, $"Úspešne:  CV1 = {cv1} ({modeText})", DccCommunicationMessageKind.Success);
                return;
            }

            // Fallback (legacy): use the generic service which uses the primary connection.
            var request = new DccCommunicationTestRequest(DecoderTimeoutMs, mode, locoAddress);
            var text = await _dccService.TestReadCv1Async(request);
            SetResultForProfile(testedProfileId, text, DccCommunicationMessageKind.Success);
        }
        catch (TimeoutException)
        {
            SetResultForProfile(testedProfileId, BuildTimeoutMessage(testedCentralType, testedCentralName), DccCommunicationMessageKind.Error);
        }
        catch (SocketException)
        {
            // typicky z21 sieť (DNS, connect, unreachable)
            SetResultForProfile(testedProfileId, $"Spojenie s {testedCentralName} zlyhalo. Skontrolujte IP adresu a sieť.", DccCommunicationMessageKind.Error);
        }
        catch (UnauthorizedAccessException)
        {
            // typicky COM port alebo práva
            SetResultForProfile(testedProfileId, "Nepodarilo sa otvoriť port. Skontrolujte pripojenie a či ho nepoužíva iný program.", DccCommunicationMessageKind.Error);
        }
        catch (NotSupportedException)
        {
            SetResultForProfile(testedProfileId, "Čítanie CV1 nie je pre túto centrálu podporované.", DccCommunicationMessageKind.Error);
        }
        catch (InvalidOperationException)
        {
            SetResultForProfile(testedProfileId, $"Centrála {testedCentralName} nie je pripojená.", DccCommunicationMessageKind.Error);
        }
        catch (OperationCanceledException)
        {
            SetResultForProfile(testedProfileId, "Test bol zrušený.", DccCommunicationMessageKind.Error);
        }
        catch (Exception)
        {
            SetResultForProfile(testedProfileId, "Test zlyhal. Skúste znova.", DccCommunicationMessageKind.Error);
        }
        finally
        {
            IsTestingCommunication = false;
        }
    }

    private static string BuildTimeoutMessage(DccCentralType? testedCentralType, string testedCentralName)
    {
        // Používateľský text bez technických detailov.
        // z21: sieťový timeout; serial centrálky: typicky napájanie/kábel.
        return testedCentralType is DccCentralType.Z21 or DccCentralType.Z21Legacy
            ? $"Spojenie so {testedCentralName} zlyhalo. Skontrolujte IP adresu a sieť."
            : "Centrála neodpovedá v limite. Skontrolujte napájanie trafa.";
    }

    private static string ResolveCentralDisplayName(DccCentralType? configuredType)
        => configuredType.HasValue
            ? DccCentralDisplayName.Get(configuredType.Value)
            : "centrálou";

    private void SetResultForProfile(Guid? profileId, string text, DccCommunicationMessageKind kind)
    {
        // Persist per profile in multi-central mode.
        if (profileId.HasValue)
            _perCentralResults[profileId.Value] = (text, kind);

        // Only update the currently visible panel when the tested profile is the one selected.
        // Otherwise, keep the UI showing the selected central's state.
        if (!profileId.HasValue || profileId == SelectedCentralProfileId)
        {
            MessageKind = kind;
            TestResult = text;
        }
    }

    private int ResolveLocoAddress(object? commandParameter)
    {
        var parameterAddress = TryGetLocoAddress(commandParameter);
        if (parameterAddress is >= 1 and <= 9999)
        {
            TestLocoAddress = parameterAddress.Value;
            return parameterAddress.Value;
        }

        return Math.Clamp(TestLocoAddress, 1, 9999);
    }

    private static int? TryGetLocoAddress(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case int i:
                return i;
            case uint ui when ui <= int.MaxValue:
                return (int)ui;
            case long l when l >= int.MinValue && l <= int.MaxValue:
                return (int)l;
            case short s:
                return s;
            case byte b:
                return b;
            case decimal dec when dec >= int.MinValue && dec <= int.MaxValue:
                return (int)dec;
            case double dbl when dbl >= int.MinValue && dbl <= int.MaxValue:
                return (int)dbl;
            case float flt when flt >= int.MinValue && flt <= int.MaxValue:
                return (int)flt;
            case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                return parsed;
            case Locomotive locomotive:
                return locomotive.DccAddress;
            case LocoRecord record:
                return record.Address;
            default:
                return TryReadIntProperty(value, "DccAddress")
                    ?? TryReadIntProperty(value, "Address");
        }
    }

    private static int? TryReadIntProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property == null)
            return null;

        var propertyValue = property.GetValue(value);
        return TryGetLocoAddress(propertyValue);
    }
}




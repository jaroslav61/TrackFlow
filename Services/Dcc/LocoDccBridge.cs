using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models;

namespace TrackFlow.Services.Dcc;

/// <summary>
/// Sleduje zmeny na runtime kollekcii Locomotive a posiela DCC príkazy cez DccConnectionService.
/// Registruje sa na PropertyChanged každej aktívnej lokomotívy.
/// Plynulé zrýchľovanie a brzdenie zabezpečuje LocomotiveMotionController.
/// </summary>
public sealed class LocoDccBridge : IDisposable
{
    private readonly DccConnectionService _dcc;
    private readonly LocomotiveMotionController _motionController;
    private ObservableCollection<Locomotive>? _locomotives;

    public LocoDccBridge(DccConnectionService dcc)
    {
        _dcc             = dcc ?? throw new ArgumentNullException(nameof(dcc));
        _motionController = new LocomotiveMotionController(new DccClientProxy(dcc));
    }

    /// <summary>Napojí bridge na kolekciu lokomotív (zvyčajne SmartStripsViewModel.Locomotives).</summary>
    public void Attach(ObservableCollection<Locomotive> locomotives)
    {
        if (_locomotives != null)
            Detach();

        _locomotives = locomotives;
        _locomotives.CollectionChanged += OnCollectionChanged;

        foreach (var loco in _locomotives)
            loco.PropertyChanged += OnLocoPropertyChanged;
    }

    public void Detach()
    {
        if (_locomotives == null) return;
        _locomotives.CollectionChanged -= OnCollectionChanged;
        foreach (var loco in _locomotives)
            loco.PropertyChanged -= OnLocoPropertyChanged;
        _locomotives = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (var item in e.NewItems)
                if (item is Locomotive loco)
                    loco.PropertyChanged += OnLocoPropertyChanged;

        if (e.OldItems != null)
            foreach (var item in e.OldItems)
                if (item is Locomotive loco)
                    loco.PropertyChanged -= OnLocoPropertyChanged;
    }

    private bool _trackStopped; // true po E-Stop – treba poslať PowerOn pred ďalším drive príkazom

    private void OnLocoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Locomotive loco) return;
        if (!_dcc.Client.IsConnected) return;

        var address = loco.DccAddress;
        if (address < 1) return;

        switch (e.PropertyName)
        {
            case nameof(Locomotive.IsActive):
                // Pri aktivácii resetujeme pamätaný stav funkcií –
                // centrála si pamätá vlastný stav, UI začína od nuly
                if (loco.IsActive)
                    ResetFunctionState(address);
                break;

            case nameof(Locomotive.TargetSpeed):
            case nameof(Locomotive.CurrentDisplaySpeed):
            case nameof(Locomotive.IsForward):
            case nameof(Locomotive.IsReverse):
                var forward = loco.IsForward || (!loco.IsForward && !loco.IsReverse);

                if (loco.Record != null)
                {
                    // Plynulý pohyb cez LocomotiveMotionController (0–100 → 0–1000 VirtualSpeed)
                    var virtualTarget = Math.Clamp(loco.TargetSpeed * 10.0, 0.0, 1000.0);

                    if (_trackStopped && loco.TargetSpeed > 0)
                    {
                        _trackStopped = false;
                        _ = _dcc.Client.TrackPowerOnAsync();
                    }

                    _motionController.SetTarget(loco.Record, virtualTarget, forward);
                }
                else
                {
                    // Fallback ak Record nie je k dispozícii – priamy príkaz bez rampingu
                    var speed = Math.Clamp(loco.CurrentDisplaySpeed, 0, 126);
                    if (_trackStopped && speed > 0)
                    {
                        _trackStopped = false;
                        _ = SendPowerOnThenSpeedAsync(address, speed, forward);
                    }
                    else
                    {
                        _ = _dcc.Client.SetLocomotiveSpeedAsync(address, speed, forward);
                    }
                }
                break;
        }
    }

    private async System.Threading.Tasks.Task SendPowerOnThenSpeedAsync(int address, int speed, bool forward)
    {
        await _dcc.Client.TrackPowerOnAsync();
        await _dcc.Client.SetLocomotiveSpeedAsync(address, speed, forward);
    }

    /// <summary>Okamžite pošle Emergency Stop na DCC centrálu.</summary>
    public void SendEmergencyStop()
    {
        _trackStopped = true;
        _ = _dcc.Client.EmergencyStopAsync();

        // Zastav aj MotionController pre všetky aktívne lokomotívy
        if (_locomotives != null)
            foreach (var loco in _locomotives)
                if (loco.Record != null)
                    _ = _motionController.EmergencyStopAsync(loco.Record);
    }

    // Stav funkcií: kľúč = "address:fnIndex", hodnota = true/false (zapnutá/vypnutá)
    private readonly System.Collections.Generic.Dictionary<string, bool> _fnState = new();

    /// <summary>Pošle stav funkcie pre danú lokomotívu (toggle ON/OFF).</summary>
    public void ToggleFunction(int address, int functionIndex)
    {
        if (!_dcc.Client.IsConnected) return;
        var key = $"{address}:{functionIndex}";
        _fnState.TryGetValue(key, out var current);
        var newState = !current;
        _fnState[key] = newState;
        _ = _dcc.Client.SetLocomotiveFunctionAsync(address, functionIndex, newState);
    }

    /// <summary>Pošle stav funkcie pre danú lokomotívu (explicitný stav).</summary>
    public void SendFunction(int address, int functionIndex, bool active)
    {
        if (!_dcc.Client.IsConnected) return;
        var key = $"{address}:{functionIndex}";
        _fnState[key] = active;
        _ = _dcc.Client.SetLocomotiveFunctionAsync(address, functionIndex, active);
    }

    /// <summary>Vymaže pamätaný stav všetkých funkcií pre danú adresu (napr. pri aktivácii loky).</summary>
    public void ResetFunctionState(int address)
    {
        var prefix = $"{address}:";
        var keys = new System.Collections.Generic.List<string>();
        foreach (var k in _fnState.Keys)
            if (k.StartsWith(prefix))
                keys.Add(k);
        foreach (var k in keys)
            _fnState.Remove(k);
    }

    /// <summary>Vráti aktuálny stav funkcie (true=zapnutá).</summary>
    public bool GetFunctionState(int address, int functionIndex)
    {
        _fnState.TryGetValue($"{address}:{functionIndex}", out var state);
        return state;
    }

    public void Dispose()
    {
        Detach();
        _ = _motionController.DisposeAsync();
    }

    /// <summary>
    /// Proxy ktoré vždy deleguje na aktuálny DccConnectionService.Client.
    /// Zabezpečuje že LocomotiveMotionController používa správneho klienta
    /// aj po zmene typu centrály.
    /// </summary>
    private sealed class DccClientProxy : IDccCentralClient
    {
        private readonly DccConnectionService _svc;
        public DccClientProxy(DccConnectionService svc) => _svc = svc;

        public bool IsConnected => _svc.Client.IsConnected;
        public uint? SerialNumber => _svc.Client.SerialNumber;

        public Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
            => _svc.Client.ConnectAsync(host, port, ct);

        public void Disconnect() => _svc.Client.Disconnect();

        public Task SetLocomotiveSpeedAsync(int address, int speed, bool forward, CancellationToken ct = default)
            => _svc.Client.SetLocomotiveSpeedAsync(address, speed, forward, ct);

        public Task SetLocomotiveFunctionAsync(int address, int functionIndex, bool active, CancellationToken ct = default)
            => _svc.Client.SetLocomotiveFunctionAsync(address, functionIndex, active, ct);

        public Task EmergencyStopAsync(CancellationToken ct = default)
            => _svc.Client.EmergencyStopAsync(ct);

        public Task TrackPowerOnAsync(CancellationToken ct = default)
            => _svc.Client.TrackPowerOnAsync(ct);

        public Task SetTurnoutAsync(int address, bool branch, bool activate, CancellationToken ct = default)
            => _svc.Client.SetTurnoutAsync(address, branch, activate, ct);
    }
}


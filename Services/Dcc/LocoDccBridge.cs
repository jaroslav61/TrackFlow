using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.ObjectModel;
using TrackFlow.Models;

namespace TrackFlow.Services.Dcc;

/// <summary>
/// Sleduje zmeny na runtime kollekcii Locomotive a posiela DCC príkazy cez DccConnectionService.
/// Registruje sa na PropertyChanged každej aktívnej lokomotívy.
/// </summary>
public sealed class LocoDccBridge : IDisposable
{
    private readonly DccConnectionService _dcc;
    private ObservableCollection<Locomotive>? _locomotives;

    public LocoDccBridge(DccConnectionService dcc)
    {
        _dcc = dcc ?? throw new ArgumentNullException(nameof(dcc));
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
                var speed = Math.Clamp(loco.CurrentDisplaySpeed, 0, 126);
                var forward = loco.IsForward || (!loco.IsForward && !loco.IsReverse);

                if (_trackStopped && speed > 0)
                {
                    _trackStopped = false;
                    _ = SendPowerOnThenSpeedAsync(address, speed, forward);
                }
                else
                {
                    _ = _dcc.Client.SetLocomotiveSpeedAsync(address, speed, forward);
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

    public void Dispose() => Detach();
}


using System;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TrackFlow.Helpers;
using TrackFlow.Services.Dcc;

namespace TrackFlow.ViewModels;

/// <summary>
/// Jedna položka DCC centrály v stavovom riadku.
/// Stavy: pripojená (zelená) / automaticky pripájam (oranžová) / odpojená (červená).
///
/// Telemetria (napätie/prúd) je delegovaná zo živého <see cref="IDccTelemetry"/> klienta.
/// Položka si SAMA prepája telemetriu pri každej globálnej zmene stavu pripojenia
/// (cez <see cref="Bind"/> → odber <c>DccConnectionService.ConnectionStateChanged</c>),
/// takže nezáleží, či bola vytvorená pred alebo po úspešnom Connect.
/// </summary>
public sealed class StatusBarCentralItem : ObservableObject, IDisposable
{
    private Func<bool>? _isTelemetryVisible;

    public Guid   ProfileId { get; init; }
    public string Name      { get; init; } = string.Empty;
    public bool   IsLast    { get; init; }
    public double? MainTrackCurrentLimitAmperes { get; init; }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(LedColor));
                OnPropertyChanged(nameof(StatusText));
                RaiseTelemetryTextChanged();
            }
        }
    }

    private bool _isReconnecting;
    /// <summary>True keď prebieha automatický reconnect (po výpadku spojenia).</summary>
    public bool IsReconnecting
    {
        get => _isReconnecting;
        set
        {
            if (SetProperty(ref _isReconnecting, value))
            {
                OnPropertyChanged(nameof(LedColor));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string LedColor   =>
        IsReconnecting ? "#FF9800" :           // oranžová – automaticky pripájam
        IsConnected    ? "#00C853" : "#D50000"; // zelená / červená

    public string StatusText =>
        IsReconnecting ? $"{Name} – automaticky pripájam…" :
        IsConnected    ? $"{Name} pripojená"   : $"{Name} odpojená";

    // ── Telemetria (delegovaná z aktuálne pripojeného IDccTelemetry zdroja) ──
    private IDccTelemetry?              _telemetrySource;
    private DccConnectionService?       _service;
    private Func<Guid, IDccTelemetry?>? _resolver;

    /// <summary>
    /// Telemetria je podporovaná iba ak máme zdroj a ten ju podporuje.
    /// POZOR: SCHVÁLNE nezávisí od lokálneho <see cref="IsConnected"/> – inak by sa
    /// text neaktualizoval pri race-condition medzi UI rebuildom a Connect eventom.
    /// </summary>
    public bool    IsTelemetrySupported => _telemetrySource != null && _telemetrySource.IsTelemetrySupported;
    public IDccTelemetry? TelemetrySource => _telemetrySource;
    public bool    IsBlackZ21           => _telemetrySource?.IsBlackZ21 ?? false;
    public double? MainVoltage          => _telemetrySource?.MainVoltage;
    public double? ProgVoltage          => _telemetrySource?.ProgVoltage;
    public double? TrackCurrent         => _telemetrySource?.TrackCurrent;
    public double? ProgTrackCurrent     => _telemetrySource?.ProgTrackCurrent;
    public double? CentralTemperature   => _telemetrySource?.CentralTemperature;

    /// <summary>
    /// Formátovaný telemetrický text pre stavový riadok.
    /// Prázdny iba keď telemetria nie je podporovaná alebo hodnoty ešte nedorazili.
    /// (NEzávislé od lokálneho IsConnected – pozeráme priamo na zdroj.)
    /// </summary>
    public string TelemetryText
    {
        get
        {
            if (!(_isTelemetryVisible?.Invoke() ?? true)) return string.Empty;
            if (!IsTelemetrySupported) return string.Empty;
            var v = MainVoltage;
            var a = TrackCurrent;
            if (v is null || a is null) return string.Empty;
            var ci = CultureInfo.InvariantCulture;
            return $"{v.Value.ToString("F1", ci)} V • {TelemetryFormatting.FormatCurrentAdaptive(a)}";
        }
    }

    public bool HasTelemetryText => !string.IsNullOrEmpty(TelemetryText);

    public void SetTelemetryVisibilityProvider(Func<bool>? isTelemetryVisible)
    {
        _isTelemetryVisible = isTelemetryVisible;
        RaiseTelemetryTextChanged();
    }

    /// <summary>
    /// Naviaže položku na živý <see cref="DccConnectionService"/>. Položka:
    ///  1) si okamžite skúsi vytiahnuť aktuálnu telemetriu (ak je už pripojené),
    ///  2) zapíše sa na globálny <c>ConnectionStateChanged</c> event a pri každom
    ///     <c>Connected</c> / <c>Disconnected</c> rámci pre vlastný profil sa pre-attachne.
    /// </summary>
    public void Bind(DccConnectionService service, Func<Guid, IDccTelemetry?> resolver)
    {
        // Odhlásime predchádzajúce odbery (idempotentne).
        UnsubscribeFromService();

        _service  = service;
        _resolver = resolver;

        if (_service != null)
            _service.ConnectionStateChanged += OnDccConnectionStateChanged;

        // Okamžitý pokus o resolve – ak je centrála už pripojená, hneď zachytíme telemetriu.
        TryResolveAndAttach();
    }

    private void OnDccConnectionStateChanged(DccConnectionStateChange change)
    {
        // Filtrujeme len udalosti pre náš profil. Pre multi-central: ProfileId je vyplnené.
        // Pre legacy single-central: ProfileId je null – v tom prípade reagujeme tiež,
        // pretože tam je len jedna centrála (naša).
        if (change.ProfileId.HasValue && change.ProfileId.Value != ProfileId)
            return;

        void Apply()
        {
            switch (change.Kind)
            {
                case DccConnectionChangeKind.Connected:
                    IsConnected    = true;
                    IsReconnecting = false;
                    TryResolveAndAttach();
                    break;

                case DccConnectionChangeKind.Disconnected:
                case DccConnectionChangeKind.ConnectFailed:
                    IsConnected    = false;
                    IsReconnecting = false;
                    AttachTelemetry(null);
                    break;

                case DccConnectionChangeKind.Reconnecting:
                    IsConnected    = false;
                    IsReconnecting = true;
                    AttachTelemetry(null);
                    break;
            }
        }

        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;
        if (dispatcher.CheckAccess()) Apply();
        else                          dispatcher.Post(Apply);
    }

    private void TryResolveAndAttach()
    {
        var live = _resolver?.Invoke(ProfileId);
        AttachTelemetry(live);
    }

    /// <summary>
    /// Pripojí (alebo odpojí, ak <paramref name="source"/> je null) telemetrický zdroj –
    /// typicky živú inštanciu <c>Z21Client</c>.
    /// UI sa automaticky aktualizuje cez <see cref="INotifyPropertyChanged"/>.
    /// </summary>
    public void AttachTelemetry(IDccTelemetry? source)
    {
        if (ReferenceEquals(_telemetrySource, source)) return;

        if (_telemetrySource != null)
            _telemetrySource.PropertyChanged -= OnTelemetryPropertyChanged;

        _telemetrySource = source;

        if (_telemetrySource != null)
            _telemetrySource.PropertyChanged += OnTelemetryPropertyChanged;

        RaiseAllTelemetryChanged();
    }

    private void OnTelemetryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Telemetria prichádza z UDP receive slučky (background thread).
        // Avalonia bindingy musíme notifikovať na UI threade.
        void Apply()
        {
            switch (e.PropertyName)
            {
                case nameof(IDccTelemetry.IsTelemetrySupported):
                case nameof(IDccTelemetry.IsBlackZ21):
                    OnPropertyChanged(nameof(IsTelemetrySupported));
                    OnPropertyChanged(nameof(IsBlackZ21));
                    RaiseTelemetryTextChanged();
                    break;
                case nameof(IDccTelemetry.MainVoltage):
                    OnPropertyChanged(nameof(MainVoltage));
                    RaiseTelemetryTextChanged();
                    break;
                case nameof(IDccTelemetry.ProgVoltage):
                    OnPropertyChanged(nameof(ProgVoltage));
                    break;
                case nameof(IDccTelemetry.TrackCurrent):
                    OnPropertyChanged(nameof(TrackCurrent));
                    RaiseTelemetryTextChanged();
                    break;
                case nameof(IDccTelemetry.ProgTrackCurrent):
                    OnPropertyChanged(nameof(ProgTrackCurrent));
                    break;
                case nameof(IDccTelemetry.CentralTemperature):
                    OnPropertyChanged(nameof(CentralTemperature));
                    break;
            }
        }

        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;
        if (dispatcher.CheckAccess()) Apply();
        else                          dispatcher.Post(Apply);
    }

    private void RaiseTelemetryTextChanged()
    {
        OnPropertyChanged(nameof(TelemetryText));
        OnPropertyChanged(nameof(HasTelemetryText));
    }

    private void RaiseAllTelemetryChanged()
    {
        OnPropertyChanged(nameof(TelemetrySource));
        OnPropertyChanged(nameof(IsTelemetrySupported));
        OnPropertyChanged(nameof(IsBlackZ21));
        OnPropertyChanged(nameof(MainVoltage));
        OnPropertyChanged(nameof(ProgVoltage));
        OnPropertyChanged(nameof(TrackCurrent));
        OnPropertyChanged(nameof(ProgTrackCurrent));
        OnPropertyChanged(nameof(CentralTemperature));
        RaiseTelemetryTextChanged();
    }

    private void UnsubscribeFromService()
    {
        if (_service != null)
            _service.ConnectionStateChanged -= OnDccConnectionStateChanged;
        _service  = null;
        _resolver = null;
    }

    public void Dispose()
    {
        UnsubscribeFromService();
        AttachTelemetry(null);
    }
}

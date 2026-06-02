using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TrackFlow.Helpers;
using TrackFlow.Models;
using TrackFlow.Services.Dcc;

namespace TrackFlow.ViewModels.Settings;

/// <summary>
/// Obálka okolo <see cref="DccCentralProfile"/> pre zobrazenie v ListBoxe.
/// </summary>
public sealed class ConfiguredDccCentralItem : ObservableObject, IDisposable
{
    public DccCentralProfile Profile { get; }

    public bool IsEnabled
    {
        get => Profile.IsEnabled;
        set
        {
            if (Profile.IsEnabled == value)
                return;

            Profile.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    // ── Telemetria (delegovaná z aktuálne pripojeného IDccTelemetry zdroja) ──
    private IDccTelemetry? _telemetrySource;

    public bool IsTelemetrySupported => _telemetrySource?.IsTelemetrySupported ?? false;
    public bool IsBlackZ21           => _telemetrySource?.IsBlackZ21 ?? false;
    public double? MainVoltage        => _telemetrySource?.MainVoltage;
    public double? ProgVoltage        => _telemetrySource?.ProgVoltage;
    public double? TrackCurrent       => _telemetrySource?.TrackCurrent;
    public double? ProgTrackCurrent   => _telemetrySource?.ProgTrackCurrent;
    public double? CentralTemperature => _telemetrySource?.CentralTemperature;

    /// <summary>
    /// Formátovaný telemetrický text pre stavový riadok.
    /// Prázdny reťazec, ak centrála telemetriu nepodporuje alebo hodnoty ešte nie sú dostupné.
    /// </summary>
    public string TelemetryText
    {
        get
        {
            if (!IsTelemetrySupported) return string.Empty;
            var v = MainVoltage;
            var a = TrackCurrent;
            if (v is null || a is null) return string.Empty;
            var ci = CultureInfo.InvariantCulture;
            return $"{v.Value.ToString("F1", ci)} V • {TelemetryFormatting.FormatCurrentAdaptive(a)}";
        }
    }

    /// <summary>
    /// Pripojí (alebo odpojí, ak <paramref name="source"/> je null) telemetrický zdroj.
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
        void Apply()
        {
            switch (e.PropertyName)
            {
                case nameof(IDccTelemetry.IsTelemetrySupported):
                    OnPropertyChanged(nameof(IsTelemetrySupported));
                    OnPropertyChanged(nameof(IsBlackZ21));
                    OnPropertyChanged(nameof(TelemetryText));
                    break;
                case nameof(IDccTelemetry.IsBlackZ21):
                    OnPropertyChanged(nameof(IsBlackZ21));
                    break;
                case nameof(IDccTelemetry.MainVoltage):
                    OnPropertyChanged(nameof(MainVoltage));
                    OnPropertyChanged(nameof(TelemetryText));
                    break;
                case nameof(IDccTelemetry.ProgVoltage):
                    OnPropertyChanged(nameof(ProgVoltage));
                    break;
                case nameof(IDccTelemetry.TrackCurrent):
                    OnPropertyChanged(nameof(TrackCurrent));
                    OnPropertyChanged(nameof(TelemetryText));
                    break;
                case nameof(IDccTelemetry.ProgTrackCurrent):
                    OnPropertyChanged(nameof(ProgTrackCurrent));
                    break;
                case nameof(IDccTelemetry.CentralTemperature):
                    OnPropertyChanged(nameof(CentralTemperature));
                    break;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private void RaiseAllTelemetryChanged()
    {
        OnPropertyChanged(nameof(IsTelemetrySupported));
        OnPropertyChanged(nameof(IsBlackZ21));
        OnPropertyChanged(nameof(MainVoltage));
        OnPropertyChanged(nameof(ProgVoltage));
        OnPropertyChanged(nameof(TrackCurrent));
        OnPropertyChanged(nameof(ProgTrackCurrent));
        OnPropertyChanged(nameof(CentralTemperature));
        OnPropertyChanged(nameof(TelemetryText));
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(StateDotColor));
                OnPropertyChanged(nameof(StateTooltip));
            }
        }
    }

    private bool _isReconnecting;
    public bool IsReconnecting
    {
        get => _isReconnecting;
        private set
        {
            if (SetProperty(ref _isReconnecting, value))
            {
                OnPropertyChanged(nameof(StateDotColor));
                OnPropertyChanged(nameof(StateTooltip));
            }
        }
    }

    /// <summary>
    /// Farba bodky v zozname (zelená = pripojené, oranžová = automaticky pripájam, červená = odpojené).
    /// </summary>
    public string StateDotColor
        => IsConnected
            ? "#22C55E"
            : IsReconnecting
                ? "#F59E0B"
                : "#EF4444";

    public string StateTooltip
        => IsConnected
            ? "Pripojené"
            : IsReconnecting
                ? "Automaticky pripájam…"
                : "Odpojené";

    private int _index;
    public int Index
    {
        get => _index;
        set
        {
            if (SetProperty(ref _index, value))
                OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string DisplayText
    {
        get
        {
            var typeName = GetTypeName(Profile.Type);
            var connInfo = Profile.Type == DccCentralType.NanoX_S88
                ? Profile.SerialPort
                : Profile.Host;
            return $"{Index}: {typeName}  {connInfo}";
        }
    }

    /// <summary>Prvý stĺpec v ListBoxe: poradové číslo + názov centrály.</summary>
    public string NameText => $"{Index}: {GetTypeName(Profile.Type)}";

    /// <summary>Druhý stĺpec v ListBoxe: IP adresa alebo COM port.</summary>
    public string ConnectionText => Profile.Type == DccCentralType.NanoX_S88
        ? Profile.SerialPort
        : Profile.Host;

    public ConfiguredDccCentralItem(DccCentralProfile profile, int index)
    {
        Profile = profile;
        _index = index;
    }

    public void Dispose() => AttachTelemetry(null);

    public void UpdateConnectionState(bool isConnected, bool isReconnecting)
    {
        // Priority: Connected wins over Reconnecting.
        IsConnected = isConnected;
        IsReconnecting = !isConnected && isReconnecting;
    }

    private static string GetTypeName(DccCentralType type)
    {
        foreach (var g in DccCentralCatalog.GetGroups())
            foreach (var it in g.Items)
                if (it.Type == type)
                    return it.Name;
        return type.ToString();
    }
}


using System;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TrackFlow.Helpers;
using TrackFlow.Services.Dcc;

namespace TrackFlow.ViewModels.Dcc;

public sealed class DccTelemetryWidgetViewModel : ObservableObject, IDisposable
{
    private const string LowLoadBrush = "#2ECC71";
    private const string MediumLoadBrush = "#F1C40F";
    private const string HighLoadBrush = "#E74C3C";

    private readonly IDccTelemetry? _telemetrySource;
    private readonly double? _configuredMainTrackCurrentLimitAmperes;

    public DccTelemetryWidgetViewModel(string name, IDccTelemetry? telemetrySource, double? configuredMainTrackCurrentLimitAmperes = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Neznámy systém" : name;
        _telemetrySource = telemetrySource;
        _configuredMainTrackCurrentLimitAmperes = configuredMainTrackCurrentLimitAmperes;

        if (_telemetrySource != null)
            _telemetrySource.PropertyChanged += OnTelemetryPropertyChanged;
    }

    public string Name { get; }

    public bool IsBlackZ21 => _telemetrySource?.IsBlackZ21 ?? false;

    public double MainTrackCurrentMaximum
        => TelemetryFormatting.ResolveMainTrackCurrentMaximum(IsBlackZ21, _configuredMainTrackCurrentLimitAmperes);

    public string MainTrackCurrentMaximumText => TelemetryFormatting.FormatCurrentLimit(MainTrackCurrentMaximum);

    public string MainVoltageText => FormatVoltage(_telemetrySource?.MainVoltage);

    public string MainTrackCurrentText => TelemetryFormatting.FormatCurrentAdaptive(_telemetrySource?.TrackCurrent);

    public string ProgTrackCurrentText
        => !IsBlackZ21
            ? "—"
            : TelemetryFormatting.FormatCurrentAdaptive(_telemetrySource?.ProgTrackCurrent);

    public string TemperatureText => FormatTemperature(_telemetrySource?.CentralTemperature);

    public double TrackCurrentProgressValue => Math.Clamp(_telemetrySource?.TrackCurrent ?? 0d, 0d, MainTrackCurrentMaximum);

    public string MainTrackCurrentBarBrush
    {
        get
        {
            if (MainTrackCurrentMaximum <= 0d)
                return LowLoadBrush;

            var ratio = TrackCurrentProgressValue / MainTrackCurrentMaximum;
            if (ratio >= 0.85d)
                return HighLoadBrush;
            if (ratio >= 0.60d)
                return MediumLoadBrush;
            return LowLoadBrush;
        }
    }

    private void OnTelemetryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        void Apply()
        {
            switch (e.PropertyName)
            {
                case null:
                case nameof(IDccTelemetry.IsBlackZ21):
                case nameof(IDccTelemetry.MainVoltage):
                case nameof(IDccTelemetry.TrackCurrent):
                case nameof(IDccTelemetry.ProgTrackCurrent):
                case nameof(IDccTelemetry.CentralTemperature):
                    RaiseAllTelemetryChanged();
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
        OnPropertyChanged(nameof(IsBlackZ21));
        OnPropertyChanged(nameof(MainTrackCurrentMaximum));
        OnPropertyChanged(nameof(MainTrackCurrentMaximumText));
        OnPropertyChanged(nameof(MainVoltageText));
        OnPropertyChanged(nameof(MainTrackCurrentText));
        OnPropertyChanged(nameof(ProgTrackCurrentText));
        OnPropertyChanged(nameof(TemperatureText));
        OnPropertyChanged(nameof(TrackCurrentProgressValue));
        OnPropertyChanged(nameof(MainTrackCurrentBarBrush));
    }

    private static string FormatVoltage(double? value)
        => value.HasValue
            ? $"{value.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} V"
            : "— V";

    private static string FormatTemperature(double? value)
        => value.HasValue
            ? $"{value.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} °C"
            : "— °C";

    public void Dispose()
    {
        if (_telemetrySource != null)
            _telemetrySource.PropertyChanged -= OnTelemetryPropertyChanged;
    }
}


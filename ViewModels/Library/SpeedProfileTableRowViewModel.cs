using System;
using System.Globalization;
using System.Linq;
using ReactiveUI;

namespace TrackFlow.ViewModels.Library;

public sealed class SpeedProfileTableRowViewModel : ReactiveObject
{
    private const int MaxStep = 126;

    private int _step;
    private double _fwdRawSpeedKmh;
    private double _bwdRawSpeedKmh;
    private string _fwdStatus = string.Empty;
    private string _bwdStatus = string.Empty;
    private bool _fwdIsManual;
    private bool _bwdIsManual;

    public SpeedProfileTableRowViewModel(int step, double fwdRawSpeedKmh, double bwdRawSpeedKmh, string fwdStatus, string bwdStatus, bool fwdIsManual, bool bwdIsManual)
    {
        _step = ClampStep(step);
        _fwdRawSpeedKmh = NormalizeSpeed(fwdRawSpeedKmh);
        _bwdRawSpeedKmh = NormalizeSpeed(bwdRawSpeedKmh);
        _fwdStatus = fwdStatus;
        _bwdStatus = bwdStatus;
        _fwdIsManual = fwdIsManual;
        _bwdIsManual = bwdIsManual;
    }

    public int Step
    {
        get => _step;
        set
        {
            var normalized = ClampStep(value);
            this.RaiseAndSetIfChanged(ref _step, normalized);
            this.RaisePropertyChanged(nameof(StepText));
        }
    }

    public double FwdRawSpeed
    {
        get => _fwdRawSpeedKmh;
        set
        {
            var normalized = NormalizeSpeed(value);
            if (Math.Abs(_fwdRawSpeedKmh - normalized) < 0.001)
                return;

            this.RaiseAndSetIfChanged(ref _fwdRawSpeedKmh, normalized);
            this.RaisePropertyChanged(nameof(FwdRawSpeedText));
        }
    }

    public double BwdRawSpeed
    {
        get => _bwdRawSpeedKmh;
        set
        {
            var normalized = NormalizeSpeed(value);
            if (Math.Abs(_bwdRawSpeedKmh - normalized) < 0.001)
                return;

            this.RaiseAndSetIfChanged(ref _bwdRawSpeedKmh, normalized);
            this.RaisePropertyChanged(nameof(BwdRawSpeedText));
        }
    }

    public string FwdStatus
    {
        get => _fwdStatus;
        set
        {
            if (string.Equals(_fwdStatus, value, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _fwdStatus, value ?? string.Empty);
            this.RaisePropertyChanged(nameof(StatusSummary));
            this.RaisePropertyChanged(nameof(TypeBadgeText));
            this.RaisePropertyChanged(nameof(TypeBadgeBackground));
            this.RaisePropertyChanged(nameof(TypeBadgeBorder));
            this.RaisePropertyChanged(nameof(TypeBadgeForeground));
        }
    }

    public string BwdStatus
    {
        get => _bwdStatus;
        set
        {
            if (string.Equals(_bwdStatus, value, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _bwdStatus, value ?? string.Empty);
            this.RaisePropertyChanged(nameof(StatusSummary));
            this.RaisePropertyChanged(nameof(TypeBadgeText));
            this.RaisePropertyChanged(nameof(TypeBadgeBackground));
            this.RaisePropertyChanged(nameof(TypeBadgeBorder));
            this.RaisePropertyChanged(nameof(TypeBadgeForeground));
        }
    }

    public bool FwdIsManual
    {
        get => _fwdIsManual;
        set
        {
            if (_fwdIsManual == value)
                return;

            this.RaiseAndSetIfChanged(ref _fwdIsManual, value);
            this.RaisePropertyChanged(nameof(StatusSummary));
            this.RaisePropertyChanged(nameof(TypeBadgeText));
            this.RaisePropertyChanged(nameof(TypeBadgeBackground));
            this.RaisePropertyChanged(nameof(TypeBadgeBorder));
            this.RaisePropertyChanged(nameof(TypeBadgeForeground));
        }
    }

    public bool BwdIsManual
    {
        get => _bwdIsManual;
        set
        {
            if (_bwdIsManual == value)
                return;

            this.RaiseAndSetIfChanged(ref _bwdIsManual, value);
            this.RaisePropertyChanged(nameof(StatusSummary));
            this.RaisePropertyChanged(nameof(TypeBadgeText));
            this.RaisePropertyChanged(nameof(TypeBadgeBackground));
            this.RaisePropertyChanged(nameof(TypeBadgeBorder));
            this.RaisePropertyChanged(nameof(TypeBadgeForeground));
        }
    }

    public string? StepText
    {
        get => Step.ToString(CultureInfo.InvariantCulture);
        set
        {
            var text = value ?? string.Empty;
            if (int.TryParse(new string(text.Where(char.IsDigit).ToArray()), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                Step = parsed;
        }
    }

    public string? FwdRawSpeedText
    {
        get => FwdRawSpeed.ToString("0.0", CultureInfo.InvariantCulture);
        set
        {
            var normalized = (value ?? string.Empty).Trim().Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
                FwdRawSpeed = parsed;
        }
    }

    public string? BwdRawSpeedText
    {
        get => BwdRawSpeed.ToString("0.0", CultureInfo.InvariantCulture);
        set
        {
            var normalized = (value ?? string.Empty).Trim().Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
                BwdRawSpeed = parsed;
        }
    }

    public bool IsManual => FwdIsManual || BwdIsManual;

    public string TypeBadgeText => IsManual ? "M" : "A";
    public string TypeBadgeBackground => IsManual ? "#FEF3C7" : "#DBEAFE";
    public string TypeBadgeBorder => IsManual ? "#F59E0B" : "#60A5FA";
    public string TypeBadgeForeground => IsManual ? "#92400E" : "#1D4ED8";
    public string StatusSummary => string.Join(" / ", new[] { FwdStatus, BwdStatus }.Where(static s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal));

    private static int ClampStep(int value)
        => Math.Clamp(value, 0, MaxStep);

    private static double NormalizeSpeed(double value)
        => Math.Round(Math.Max(0, value), 1);
}



using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI;
using Serilog;
using TrackFlow.Models;
using TrackFlow.Models.Calibration;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.Services.Dcc;
using TrackFlow.ViewModels.Calibration;
using System.Diagnostics;

namespace TrackFlow.ViewModels.Library;

public enum AiDiagnosticSeverity
{
    Ok,
    Warning,
    Error
}

public enum AiDiagnosticProblemType
{
    Stable,
    DirectionAsymmetry,
    LowSteps,
    MidBand,
    HighSpeed
}

public enum AiDiagnosticCauseType
{
    Stable,
    MechanicalResistance,
    StartupCvTuning,
    MidBandSmoothing,
    TopCurveInstability,
    SingleDirectionIssue
}

public sealed class LocomotiveSpeedEditorViewModel : ReactiveObject
{
    private const double ChartLeft = 58;
    private const double ChartTop = 22;
    private const double ChartWidth = 824;
    private const double ChartHeight = 522;
    private const int DefaultChartMaxSpeed = 120;
    private const int DefaultChartMaxStep = 127;
    private const int DccMaxStep = 126;

    /// <summary>
    /// Mapuje zobrazovací bod grafu (1–28) na reálny DCC krok (1–126).
    /// Bod 1 → DCC 4, Bod 28 → DCC 126, lineárne.
    /// </summary>
    public static int MapChartStepToDcc(int chartStep)
    {
        if (chartStep <= 1) return 4;
        if (chartStep >= 28) return DccMaxStep;
        return (int)Math.Round(4 + (chartStep - 1) * (DccMaxStep - 4) / 27.0);
    }

    private const double MarkerHitRadius = 18;

    private const double PerformanceChartLeft = 32;
    private const double PerformanceChartTop = 10;
    private const double PerformanceChartWidth = 228;
    private const double PerformanceChartHeight = 118;

    private readonly ObservableCollection<CalibrationLocomotiveOption> _availableLocomotives = new();
    private readonly ObservableCollection<CalibrationIndicatorOption> _allIndicators = new();

    private CalibrationLocomotiveOption? _selectedLocomotive;
    private CalibrationIndicatorOption? _selectedStartBlock;
    private CalibrationIndicatorOption? _selectedMiddleBlock;
    private CalibrationIndicatorOption? _selectedEndBlock;
    private string _selectedScale = "1:87 (H0)";
    private string _selectedMaxModelSpeed = "120 km/h";
    private CalibrationMethodItemViewModel? _selectedMethod;
    private double _pauseSeconds = 5.0;
    private string _pauseSecondsText = "5";
    private double _runoutDistanceCm = 30;
    private string _runoutDistanceCmText = "30";
    private int _blockLengthCm = 100;
    private string _blockLengthCmText = "100";
    private CancellationTokenSource? _calibrationCts;
    private bool _isCalibrationRunning;

    private enum MeasurementState
    {
        Idle,
        WaitingForMid,
        Measuring
    }

    private MeasurementState _measurementState = MeasurementState.Idle;

    private DateTime _measurementStart;

    private (
        int ModuleAddress,
        int PortNumber,
        Guid? ProfileId
        ) _measurementMidKey;

    private (
        int ModuleAddress,
        int PortNumber,
        Guid? ProfileId
        ) _measurementEndKey;

    private TaskCompletionSource<double>? _measurementCompletion;

    private CancellationTokenSource? _measurementTimeout;

    private int _measurementAddress;

    private int _measurementSpeed;

    private bool _measurementForward;

    public bool IsCalibrationRunning
    {
        get => _isCalibrationRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isCalibrationRunning, value);
            this.RaisePropertyChanged(nameof(StartAutomatedSequenceButtonText));
        }
    }

    public string StartAutomatedSequenceButtonText
        => _isCalibrationRunning ? "⏹ Zastaviť meranie" : "▶ Štart automatického merania";

    private double _calibrationProgress;
    private string _calibrationStatusText = "Pripravené na kalibráciu.";
    private string _selectedLocomotiveDisplayName = string.Empty;
    private IImage? _selectedLocomotiveImage;
    private double _currentSpeedKmh;
    private string _engineStatusText = "Stav diagnostiky: OK";
    private AiDiagnosticSeverity _engineStatusSeverity = AiDiagnosticSeverity.Ok;
    private AiDiagnosticProblemType _engineStatusProblemType = AiDiagnosticProblemType.Stable;
    private AiDiagnosticCauseType _engineStatusCauseType = AiDiagnosticCauseType.Stable;
    private AiDiagnosticsComputation? _pendingDiagnosticsComputation;
    private AiDiagnosticsSnapshot? _displayedDiagnosticsSnapshot;
    private string _analysisSummaryText = string.Empty;
    private string _aiRecommendationText = string.Empty;
    private string _recommendedCvTweaksText = string.Empty;
    private string _maxForwardSpeedText = string.Empty;
    private string _maxBackwardSpeedText = string.Empty;
    private string _averageDifferenceText = string.Empty;
    private string _maxDeviationText = string.Empty;
    private string _maxForwardSpeedValueText = "—";
    private string _maxForwardSpeedDetailText = string.Empty;
    private string _maxBackwardSpeedValueText = "—";
    private string _maxBackwardSpeedDetailText = string.Empty;
    private string _averageDifferenceValueText = "—";
    private string _averageDifferenceDetailText = string.Empty;
    private string _maxDeviationValueText = "—";
    private string _maxDeviationDetailText = string.Empty;
    private string _forwardStop10Text = string.Empty;
    private string _backwardStop10Text = string.Empty;
    private string _forwardStop60Text = string.Empty;
    private string _backwardStop60Text = string.Empty;
    private string _forwardStop120Text = string.Empty;
    private string _backwardStop120Text = string.Empty;
    private string _forwardCurvePathData = string.Empty;
    private string _backwardCurvePathData = string.Empty;
    private string _forwardAreaPathData = string.Empty;
    private string _backwardAreaPathData = string.Empty;
    private RelativePoint _forwardGradientStart = new(0, 0, RelativeUnit.Relative);
    private RelativePoint _forwardGradientEnd = new(1, 1, RelativeUnit.Relative);
    private RelativePoint _backwardGradientStart = new(0, 0, RelativeUnit.Relative);
    private RelativePoint _backwardGradientEnd = new(1, 1, RelativeUnit.Relative);
    private string _forwardMarkerPathData = string.Empty;
    private string _backwardMarkerPathData = string.Empty;
    private string _varianceAreaPathData = string.Empty;
    private string _frictionCurvePathData = string.Empty;
    private string _powerCurvePathData = string.Empty;
    private string _frictionPeakSummaryText = string.Empty;
    private string _powerUsageSummaryText = string.Empty;
    private string _performanceEmptyStateText = "Na indikátor asymetrie treba body v oboch smeroch.";
    private string _mechanicalCriticalStepText = "kritický stupeň: —";
    private double _mechanicalChartAxisMaximum = 15.0;
    private int _currentChartMaxStep = DefaultChartMaxStep;
    private int _currentChartMaxSpeed = DefaultChartMaxSpeed;
    private string _decoderStepAxisTitle = $"Rýchlostný stupeň dekodéra (0-{DefaultChartMaxStep})";
    private SpeedProfileTableRowViewModel? _selectedSpeedProfileRow;
    private CalibrationMeasurementPointViewModel? _selectedForwardMeasurementPoint;
    private CalibrationMeasurementPointViewModel? _selectedBackwardMeasurementPoint;
    private int _selectedProfileTabIndex;
    private bool _hasPendingProfileProjectChanges;
    private bool _isManualPlacementMode;
    private bool _isUpdatingMeasurementPoint;
    private bool _isUpdatingSpeedProfileRow;
    private CalibrationMeasurementPointViewModel? _draggedMeasurementPoint;
    private ObservableCollection<CalibrationMeasurementPointViewModel>? _draggedMeasurementCollection;
    private readonly RelayCommand _addPointManuallyCommand;

    public LocomotiveSpeedEditorViewModel()
    {
        AvailableLocomotives = _availableLocomotives;
        StartBlockOptions = new ObservableCollection<CalibrationIndicatorOption>();
        MiddleBlockOptions = new ObservableCollection<CalibrationIndicatorOption>();
        EndBlockOptions = new ObservableCollection<CalibrationIndicatorOption>();
        ScaleOptions = new ObservableCollection<string>
        {
            "1:87 (H0)",
            "1:120 (TT)",
            "1:160 (N)",
            "1:43.5 (0)"
        };
        CalibrationMethods = new ObservableCollection<CalibrationMethodItemViewModel>(LoadCalibrationMethods());
        CalibrationMethodDisplayNames =
            new ObservableCollection<string>(CalibrationMethods.Select(option => option.Description));
        _selectedMethod = CalibrationMethods.FirstOrDefault();
        MaxModelSpeedOptions = new ObservableCollection<string>
        {
            "60 km/h",
            "80 km/h",
            "100 km/h",
            "120 km/h",
            "140 km/h"
        };

        ForwardMeasurementPoints = new ObservableCollection<CalibrationMeasurementPointViewModel>();
        BackwardMeasurementPoints = new ObservableCollection<CalibrationMeasurementPointViewModel>();
        SpeedProfileRows = new ObservableCollection<SpeedProfileTableRowViewModel>();
        ForwardSpeedProfileRows = new ObservableCollection<SpeedProfileTableRowViewModel>();
        BackwardSpeedProfileRows = new ObservableCollection<SpeedProfileTableRowViewModel>();
        ForwardCurveMarkers = new ObservableCollection<CurveMarkerViewModel>();
        BackwardCurveMarkers = new ObservableCollection<CurveMarkerViewModel>();
        ReferenceGuides = new ObservableCollection<ReferenceGuideViewModel>();
        XAxisLabels =
            new ObservableCollection<AxisLabelViewModel>(BuildHorizontalAxisLabels(_currentChartMaxStep, ChartLeft,
                ChartWidth, 553));
        VerticalGridLines = new ObservableCollection<ChartGridLineViewModel>(
            BuildVerticalGridLines(_currentChartMaxStep, ChartLeft, ChartTop, ChartWidth, ChartHeight));
        XAxisTickMarks = new ObservableCollection<ChartGridLineViewModel>(BuildXAxisTickMarks(_currentChartMaxStep,
            ChartLeft, ChartTop, ChartWidth, ChartHeight));
        YAxisLabels =
            new ObservableCollection<AxisLabelViewModel>(BuildVerticalAxisLabels(_currentChartMaxSpeed, ChartTop,
                ChartHeight));
        HorizontalGridLines = new ObservableCollection<ChartGridLineViewModel>(
            BuildHorizontalGridLines(_currentChartMaxSpeed, ChartLeft, ChartTop, ChartWidth, ChartHeight));

        StartAutomatedSequenceCommand = new RelayCommand(StartAutomatedSequence);
        _addPointManuallyCommand = new RelayCommand(AddPointManually, () => CanAddPointManually);
        AddPointManuallyCommand = _addPointManuallyCommand;
        VerifyCommand = new RelayCommand(VerifyCalibration);
        SaveProfileCommand = new RelayCommand(SaveProfile);

        SyncLocomotives(Array.Empty<LocoRecord>(), null);
        SyncProjectIndicators(Array.Empty<string>());
        RefreshBlockSelections();
        UpdateDerivedState();
    }

    public ObservableCollection<CalibrationLocomotiveOption> AvailableLocomotives { get; }
    public ObservableCollection<CalibrationIndicatorOption> StartBlockOptions { get; }
    public ObservableCollection<CalibrationIndicatorOption> MiddleBlockOptions { get; }
    public ObservableCollection<CalibrationIndicatorOption> EndBlockOptions { get; }
    public ObservableCollection<CalibrationMethodItemViewModel> CalibrationMethods { get; }
    public ObservableCollection<string> CalibrationMethodDisplayNames { get; }
    public ObservableCollection<CalibrationMethodItemViewModel> CalibrationMethodOptions => CalibrationMethods;
    public ObservableCollection<string> ScaleOptions { get; }
    public ObservableCollection<string> MaxModelSpeedOptions { get; }
    public ObservableCollection<SpeedProfileTableRowViewModel> SpeedProfileRows { get; }
    public ObservableCollection<SpeedProfileTableRowViewModel> ForwardSpeedProfileRows { get; }
    public ObservableCollection<SpeedProfileTableRowViewModel> BackwardSpeedProfileRows { get; }
    public ObservableCollection<CalibrationMeasurementPointViewModel> ForwardMeasurementPoints { get; }
    public ObservableCollection<CalibrationMeasurementPointViewModel> BackwardMeasurementPoints { get; }
    public ObservableCollection<CurveMarkerViewModel> ForwardCurveMarkers { get; }
    public ObservableCollection<CurveMarkerViewModel> BackwardCurveMarkers { get; }
    public ObservableCollection<ReferenceGuideViewModel> ReferenceGuides { get; }
    public ObservableCollection<AxisLabelViewModel> XAxisLabels { get; }
    public ObservableCollection<ChartGridLineViewModel> VerticalGridLines { get; }
    public ObservableCollection<ChartGridLineViewModel> XAxisTickMarks { get; }
    public ObservableCollection<AxisLabelViewModel> YAxisLabels { get; }
    public ObservableCollection<ChartGridLineViewModel> HorizontalGridLines { get; }

    public int CurrentChartMaxStep
    {
        get => _currentChartMaxStep;
        private set => this.RaiseAndSetIfChanged(ref _currentChartMaxStep, value);
    }

    public int CurrentChartMaxSpeed
    {
        get => _currentChartMaxSpeed;
        private set => this.RaiseAndSetIfChanged(ref _currentChartMaxSpeed, value);
    }

    public string DecoderStepAxisTitle
    {
        get => _decoderStepAxisTitle;
        private set => this.RaiseAndSetIfChanged(ref _decoderStepAxisTitle, value);
    }

    public SpeedProfileTableRowViewModel? SelectedSpeedProfileRow
    {
        get => _selectedSpeedProfileRow;
        set => this.RaiseAndSetIfChanged(ref _selectedSpeedProfileRow, value);
    }

    public ICommand StartAutomatedSequenceCommand { get; }
    public ICommand AddPointManuallyCommand { get; }
    public ICommand VerifyCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public bool CanSaveActiveProfile => SelectedProfileTabIndex is 0 or 1;
    public bool CanAddPointManually => SelectedProfileTabIndex is 0 or 1;

    public bool IsForwardProfileSelected
    {
        get => SelectedProfileTabIndex == 0;
        set
        {
            if (value)
                SelectedProfileTabIndex = 0;
        }
    }

    public bool IsBackwardProfileSelected
    {
        get => SelectedProfileTabIndex == 1;
        set
        {
            if (value)
                SelectedProfileTabIndex = 1;
        }
    }

    public bool IsBothProfilesSelected
    {
        get => SelectedProfileTabIndex == 2;
        set
        {
            if (value)
                SelectedProfileTabIndex = 2;
        }
    }

    public bool IsForwardProfileVisible => SelectedProfileTabIndex is 0 or 2;
    public bool IsBackwardProfileVisible => SelectedProfileTabIndex is 1 or 2;

    public string SelectedProfileDisplayName => SelectedProfileTabIndex switch
    {
        1 => "Profil dozadu",
        2 => "Obidva profily",
        _ => "Profil dopredu"
    };

    public string SelectedProfileDisplayForeground => SelectedProfileTabIndex switch
    {
        1 => "#D92424",
        2 => "#475569",
        _ => "#1976D2"
    };

    public bool IsManualPlacementMode
    {
        get => _isManualPlacementMode;
        private set => this.RaiseAndSetIfChanged(ref _isManualPlacementMode, value);
    }

    public bool IsDraggingChartPoint => _draggedMeasurementPoint != null;

    public bool HasPendingProfileProjectChanges
    {
        get => _hasPendingProfileProjectChanges;
        set => this.RaiseAndSetIfChanged(ref _hasPendingProfileProjectChanges, value);
    }

    public Action? MarkProfileDirty { get; set; }
    public Func<bool>? PersistProfileChanges { get; set; }

    /// <summary>
    /// Injektovaný delegate pre ovládanie lokomotívy počas kalibrácie.
    /// Parametre: address, speed (0=stop, 1..126), forward, ct.
    /// </summary>
    public Func<int, int, bool, CancellationToken, Task>? DriveLocoAsync { get; set; }

    /// <summary>
    /// Injektovaný DCC connection service pre priamy prístup k FeedbackStateChanged eventu.
    /// </summary>
    public DccConnectionService? CalibrationDcc { get; set; }

    public CalibrationLocomotiveOption? SelectedLocomotive
    {
        get => _selectedLocomotive;
        set
        {
            if (EqualityComparer<CalibrationLocomotiveOption?>.Default.Equals(_selectedLocomotive, value))
                return;

            var previousLocomotive = _selectedLocomotive?.Source;
            PersistMeasurementsToLocomotive(previousLocomotive);

            this.RaiseAndSetIfChanged(ref _selectedLocomotive, value);
            SelectedLocomotiveDisplayName = value?.DisplayName ?? string.Empty;
            SelectedLocomotiveImage = value?.Thumbnail;
            this.RaisePropertyChanged(nameof(PerformancePowerLegendText));
            ResetManualInteractionState();
            ApplyLocomotiveDefaults(value?.Source);
            LoadMeasurementsFromLocomotive(value?.Source);
            UpdateCalibrationStatus(value?.Source == null
                ? "Vyberte lokomotívu v ľavom gride pre úpravu rýchlostného profilu."
                : $"Načítaný rýchlostný profil lokomotívy {value.DisplayName}.");
        }
    }

    public CalibrationIndicatorOption? SelectedStartBlock
    {
        get => _selectedStartBlock;
        set
        {
            if (ReferenceEquals(_selectedStartBlock, value))
                return;

            this.RaiseAndSetIfChanged(ref _selectedStartBlock, value);
            RefreshBlockSelections();
        }
    }

    public CalibrationIndicatorOption? SelectedMiddleBlock
    {
        get => _selectedMiddleBlock;
        set
        {
            if (ReferenceEquals(_selectedMiddleBlock, value))
                return;

            this.RaiseAndSetIfChanged(ref _selectedMiddleBlock, value);
            RefreshEndBlocks();
        }
    }

    public CalibrationIndicatorOption? SelectedEndBlock
    {
        get => _selectedEndBlock;
        set
        {
            if (ReferenceEquals(_selectedEndBlock, value))
                return;

            this.RaiseAndSetIfChanged(ref _selectedEndBlock, value);
        }
    }

    public string SelectedScale
    {
        get => _selectedScale;
        set
        {
            if (string.Equals(_selectedScale, value, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _selectedScale, value);
            UpdateCalibrationStatus($"Scale set to {value}.");
        }
    }

    public string SelectedMaxModelSpeed
    {
        get => _selectedMaxModelSpeed;
        set
        {
            if (string.Equals(_selectedMaxModelSpeed, value, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _selectedMaxModelSpeed, value);
            SetChartMaxSpeed(ParseMaxSpeed(value));
        }
    }

    public double PauseSeconds
    {
        get => _pauseSeconds;
        set
        {
            var normalized = Math.Round(Math.Clamp(value, 0.0, 15.0), 1);
            if (Math.Abs(_pauseSeconds - normalized) < 0.001)
                return;

            this.RaiseAndSetIfChanged(ref _pauseSeconds, normalized);

            var formatted = FormatPauseSeconds(normalized);
            if (!string.Equals(_pauseSecondsText, formatted, StringComparison.Ordinal))
                this.RaiseAndSetIfChanged(ref _pauseSecondsText, formatted, nameof(PauseSecondsText));
        }
    }

    public string PauseSecondsText
    {
        get => _pauseSecondsText;
        set
        {
            var normalizedText = NormalizePauseSecondsText(value);
            if (string.Equals(_pauseSecondsText, normalizedText, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _pauseSecondsText, normalizedText);

            if (TryParsePauseSeconds(normalizedText, out var parsed))
            {
                var clamped = Math.Round(Math.Clamp(parsed, 0.0, 15.0), 1);
                if (Math.Abs(_pauseSeconds - clamped) >= 0.001)
                    this.RaiseAndSetIfChanged(ref _pauseSeconds, clamped, nameof(PauseSeconds));

                var formatted = FormatPauseSeconds(clamped);
                if (!string.Equals(_pauseSecondsText, formatted, StringComparison.Ordinal))
                    this.RaiseAndSetIfChanged(ref _pauseSecondsText, formatted);
            }
        }
    }

    public double RunoutDistanceCm
    {
        get => _runoutDistanceCm;
        set
        {
            var normalized = Math.Round(Math.Clamp(value, 0.0, 999.99), 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(_runoutDistanceCm - normalized) < 0.001)
                return;

            this.RaiseAndSetIfChanged(ref _runoutDistanceCm, normalized);

            var formatted = FormatRunoutDistance(normalized);
            if (!string.Equals(_runoutDistanceCmText, formatted, StringComparison.Ordinal))
                this.RaiseAndSetIfChanged(ref _runoutDistanceCmText, formatted, nameof(RunoutDistanceCmText));
        }
    }

    public string RunoutDistanceCmText
    {
        get => _runoutDistanceCmText;
        set
        {
            var normalizedText = NormalizeRunoutDistanceText(value);
            if (string.Equals(_runoutDistanceCmText, normalizedText, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _runoutDistanceCmText, normalizedText);

            if (TryParseRunoutDistance(normalizedText, out var parsed))
            {
                var clamped = Math.Round(Math.Clamp(parsed, 0.0, 999.99), 2, MidpointRounding.AwayFromZero);
                if (Math.Abs(_runoutDistanceCm - clamped) >= 0.001)
                    this.RaiseAndSetIfChanged(ref _runoutDistanceCm, clamped, nameof(RunoutDistanceCm));

                var formatted = FormatRunoutDistance(clamped);
                if (!string.Equals(_runoutDistanceCmText, formatted, StringComparison.Ordinal))
                    this.RaiseAndSetIfChanged(ref _runoutDistanceCmText, formatted);
            }
        }
    }

    /// <summary>Dĺžka meracieho (stredného) bloku v cm. Rozsah 5–500 cm. Default 100 cm.</summary>
    public int BlockLengthCm
    {
        get => _blockLengthCm;
        set
        {
            var clamped = Math.Clamp(value, 5, 500);
            if (_blockLengthCm == clamped) return;
            this.RaiseAndSetIfChanged(ref _blockLengthCm, clamped);
            var formatted = clamped.ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(_blockLengthCmText, formatted, StringComparison.Ordinal))
                this.RaiseAndSetIfChanged(ref _blockLengthCmText, formatted, nameof(BlockLengthCmText));
        }
    }

    public string BlockLengthCmText
    {
        get => _blockLengthCmText;
        set
        {
            var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (string.Equals(_blockLengthCmText, digits, StringComparison.Ordinal)) return;
            this.RaiseAndSetIfChanged(ref _blockLengthCmText, digits);
            if (int.TryParse(digits, out var parsed))
            {
                var clamped = Math.Clamp(parsed, 5, 500);
                if (_blockLengthCm != clamped)
                    this.RaiseAndSetIfChanged(ref _blockLengthCm, clamped, nameof(BlockLengthCm));
            }
        }
    }

    public double CalibrationProgress
    {
        get => _calibrationProgress;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (Math.Abs(_calibrationProgress - normalized) < 0.001)
                return;

            this.RaiseAndSetIfChanged(ref _calibrationProgress, normalized);
            this.RaisePropertyChanged(nameof(CalibrationProgressText));
        }
    }

    public string CalibrationProgressText => $"{CalibrationProgress:0}% Complete";

    public string CalibrationStatusText
    {
        get => _calibrationStatusText;
        private set => this.RaiseAndSetIfChanged(ref _calibrationStatusText, value);
    }


    public string SelectedLocomotiveDisplayName
    {
        get => _selectedLocomotiveDisplayName;
        private set => this.RaiseAndSetIfChanged(ref _selectedLocomotiveDisplayName, value);
    }

    public IImage? SelectedLocomotiveImage
    {
        get => _selectedLocomotiveImage;
        private set => this.RaiseAndSetIfChanged(ref _selectedLocomotiveImage, value);
    }

    public bool IsPointContactsMethod
    {
        get => SelectedMethod?.Method is CalibrationMethod.AutomaticFullProfileMomentary
            or CalibrationMethod.AutomaticSingleStepMomentary
            or CalibrationMethod.BrakingCompensationTestMomentary;
        set
        {
            if (value)
                SelectedMethod = CalibrationMethods.FirstOrDefault(option =>
                    option.Method == CalibrationMethod.AutomaticSingleStepMomentary);
        }
    }

    public bool IsBlockOccupancyMethod
    {
        get => SelectedMethod?.Method is CalibrationMethod.AutomaticFullProfileOccupancy
            or CalibrationMethod.AutomaticSingleStepOccupancy
            or CalibrationMethod.BrakingCompensationTestOccupancy;
        set
        {
            if (value)
                SelectedMethod = CalibrationMethods.FirstOrDefault(option =>
                    option.Method == CalibrationMethod.AutomaticSingleStepOccupancy);
        }
    }

    public bool IsExternalDeviceMethod
    {
        get => SelectedMethod?.Method == CalibrationMethod.ManualExternalDevice;
        set
        {
            if (value)
                SelectedMethod =
                    CalibrationMethods.FirstOrDefault(option =>
                        option.Method == CalibrationMethod.ManualExternalDevice);
        }
    }

    public bool IsBlockConfigurationEnabled => !IsExternalDeviceMethod;

    public bool IsStartBlockEnabled => GetBlockSelectionEnabledState().StartEnabled;

    public bool IsMiddleBlockEnabled => GetBlockSelectionEnabledState().MiddleEnabled;

    public bool IsEndBlockEnabled => GetBlockSelectionEnabledState().EndEnabled;

    public string SelectedCalibrationMethodTooltip
        => SelectedMethod?.Description ?? string.Empty;

    public CalibrationMethodItemViewModel? SelectedCalibrationMethodOption
    {
        get => SelectedMethod;
        set => SelectedMethod = value;
    }

    public string SelectedCalibrationMethod
    {
        get => SelectedMethod?.Description ?? string.Empty;
        set
        {
            var selected = string.IsNullOrWhiteSpace(value)
                ? null
                : CalibrationMethods.FirstOrDefault(option =>
                    string.Equals(option.Description, value, StringComparison.Ordinal));
            SelectedMethod = selected;
        }
    }

    public CalibrationMethodItemViewModel? SelectedMethod
    {
        get => _selectedMethod;
        set
        {
            if (ReferenceEquals(_selectedMethod, value))
                return;

            this.RaiseAndSetIfChanged(ref _selectedMethod, value);
            this.RaisePropertyChanged(nameof(SelectedCalibrationMethodOption));
            this.RaisePropertyChanged(nameof(SelectedCalibrationMethod));
            this.RaisePropertyChanged(nameof(SelectedCalibrationMethodTooltip));
            this.RaisePropertyChanged(nameof(IsPointContactsMethod));
            this.RaisePropertyChanged(nameof(IsBlockOccupancyMethod));
            this.RaisePropertyChanged(nameof(IsExternalDeviceMethod));
            this.RaisePropertyChanged(nameof(IsBlockConfigurationEnabled));
            this.RaisePropertyChanged(nameof(IsStartBlockEnabled));
            this.RaisePropertyChanged(nameof(IsMiddleBlockEnabled));
            this.RaisePropertyChanged(nameof(IsEndBlockEnabled));

            ClearDisabledBlockSelections();

            UpdateCalibrationStatus(GetCalibrationMethodStatus(value));
        }
    }

    public string ForwardCurvePathData
    {
        get => _forwardCurvePathData;
        private set => this.RaiseAndSetIfChanged(ref _forwardCurvePathData, value);
    }

    public string BackwardCurvePathData
    {
        get => _backwardCurvePathData;
        private set => this.RaiseAndSetIfChanged(ref _backwardCurvePathData, value);
    }

    public string ForwardAreaPathData
    {
        get => _forwardAreaPathData;
        private set => this.RaiseAndSetIfChanged(ref _forwardAreaPathData, value);
    }

    public string BackwardAreaPathData
    {
        get => _backwardAreaPathData;
        private set => this.RaiseAndSetIfChanged(ref _backwardAreaPathData, value);
    }

    public RelativePoint ForwardGradientStart
    {
        get => _forwardGradientStart;
        private set => this.RaiseAndSetIfChanged(ref _forwardGradientStart, value);
    }

    public RelativePoint ForwardGradientEnd
    {
        get => _forwardGradientEnd;
        private set => this.RaiseAndSetIfChanged(ref _forwardGradientEnd, value);
    }

    public RelativePoint BackwardGradientStart
    {
        get => _backwardGradientStart;
        private set => this.RaiseAndSetIfChanged(ref _backwardGradientStart, value);
    }

    public RelativePoint BackwardGradientEnd
    {
        get => _backwardGradientEnd;
        private set => this.RaiseAndSetIfChanged(ref _backwardGradientEnd, value);
    }

    public string ForwardMarkerPathData
    {
        get => _forwardMarkerPathData;
        private set => this.RaiseAndSetIfChanged(ref _forwardMarkerPathData, value);
    }

    public string BackwardMarkerPathData
    {
        get => _backwardMarkerPathData;
        private set => this.RaiseAndSetIfChanged(ref _backwardMarkerPathData, value);
    }

    public string VarianceAreaPathData
    {
        get => _varianceAreaPathData;
        private set => this.RaiseAndSetIfChanged(ref _varianceAreaPathData, value);
    }

    public string FrictionCurvePathData
    {
        get => _frictionCurvePathData;
        private set => this.RaiseAndSetIfChanged(ref _frictionCurvePathData, value);
    }

    public string PowerCurvePathData
    {
        get => _powerCurvePathData;
        private set => this.RaiseAndSetIfChanged(ref _powerCurvePathData, value);
    }

    public string EngineStatusText
    {
        get => _engineStatusText;
        private set => this.RaiseAndSetIfChanged(ref _engineStatusText, value);
    }

    public AiDiagnosticSeverity EngineStatusSeverity
    {
        get => _engineStatusSeverity;
        private set => this.RaiseAndSetIfChanged(ref _engineStatusSeverity, value);
    }

    public AiDiagnosticProblemType EngineStatusProblemType
    {
        get => _engineStatusProblemType;
        private set => this.RaiseAndSetIfChanged(ref _engineStatusProblemType, value);
    }

    public AiDiagnosticCauseType EngineStatusCauseType
    {
        get => _engineStatusCauseType;
        private set => this.RaiseAndSetIfChanged(ref _engineStatusCauseType, value);
    }

    public string EngineStatusBackground => EngineStatusSeverity switch
    {
        AiDiagnosticSeverity.Ok => "#E4F8E8",
        AiDiagnosticSeverity.Warning => "#FFF4E5",
        AiDiagnosticSeverity.Error => "#FDEAEA",
        _ => "#E4F8E8"
    };

    public string EngineStatusBorderBrush => EngineStatusSeverity switch
    {
        AiDiagnosticSeverity.Ok => "#B7E4C0",
        AiDiagnosticSeverity.Warning => "#F7C97D",
        AiDiagnosticSeverity.Error => "#F1A8A8",
        _ => "#B7E4C0"
    };

    public string EngineStatusForeground => EngineStatusSeverity switch
    {
        AiDiagnosticSeverity.Ok => "#166534",
        AiDiagnosticSeverity.Warning => "#9A5B00",
        AiDiagnosticSeverity.Error => "#991B1B",
        _ => "#166534"
    };

    public string EngineStatusIconText => EngineStatusSeverity switch
    {
        AiDiagnosticSeverity.Ok => "●",
        AiDiagnosticSeverity.Warning => "⚠",
        AiDiagnosticSeverity.Error => "⛔",
        _ => "●"
    };

    public double EngineStatusIconFontSize => EngineStatusSeverity switch
    {
        AiDiagnosticSeverity.Ok => 12,
        AiDiagnosticSeverity.Warning => 14,
        AiDiagnosticSeverity.Error => 16,
        _ => 12
    };

    public string AnalysisSummaryText
    {
        get => _analysisSummaryText;
        private set => this.RaiseAndSetIfChanged(ref _analysisSummaryText, value);
    }

    public string AiRecommendationText
    {
        get => _aiRecommendationText;
        private set => this.RaiseAndSetIfChanged(ref _aiRecommendationText, value);
    }

    public string RecommendedCvTweaksText
    {
        get => _recommendedCvTweaksText;
        private set => this.RaiseAndSetIfChanged(ref _recommendedCvTweaksText, value);
    }

    public string MaxForwardSpeedText
    {
        get => _maxForwardSpeedText;
        private set => this.RaiseAndSetIfChanged(ref _maxForwardSpeedText, value);
    }

    public string MaxBackwardSpeedText
    {
        get => _maxBackwardSpeedText;
        private set => this.RaiseAndSetIfChanged(ref _maxBackwardSpeedText, value);
    }

    public string AverageDifferenceText
    {
        get => _averageDifferenceText;
        private set => this.RaiseAndSetIfChanged(ref _averageDifferenceText, value);
    }

    public string MaxDeviationText
    {
        get => _maxDeviationText;
        private set => this.RaiseAndSetIfChanged(ref _maxDeviationText, value);
    }

    public string MaxForwardSpeedValueText
    {
        get => _maxForwardSpeedValueText;
        private set => this.RaiseAndSetIfChanged(ref _maxForwardSpeedValueText, value);
    }

    public string MaxForwardSpeedDetailText
    {
        get => _maxForwardSpeedDetailText;
        private set => this.RaiseAndSetIfChanged(ref _maxForwardSpeedDetailText, value);
    }

    public string MaxBackwardSpeedValueText
    {
        get => _maxBackwardSpeedValueText;
        private set => this.RaiseAndSetIfChanged(ref _maxBackwardSpeedValueText, value);
    }

    public string MaxBackwardSpeedDetailText
    {
        get => _maxBackwardSpeedDetailText;
        private set => this.RaiseAndSetIfChanged(ref _maxBackwardSpeedDetailText, value);
    }

    public string AverageDifferenceValueText
    {
        get => _averageDifferenceValueText;
        private set => this.RaiseAndSetIfChanged(ref _averageDifferenceValueText, value);
    }

    public string AverageDifferenceDetailText
    {
        get => _averageDifferenceDetailText;
        private set => this.RaiseAndSetIfChanged(ref _averageDifferenceDetailText, value);
    }

    public string MaxDeviationValueText
    {
        get => _maxDeviationValueText;
        private set => this.RaiseAndSetIfChanged(ref _maxDeviationValueText, value);
    }

    public string MaxDeviationDetailText
    {
        get => _maxDeviationDetailText;
        private set => this.RaiseAndSetIfChanged(ref _maxDeviationDetailText, value);
    }

    public string FrictionPeakSummaryText
    {
        get => _frictionPeakSummaryText;
        private set => this.RaiseAndSetIfChanged(ref _frictionPeakSummaryText, value);
    }

    public string PowerUsageSummaryText
    {
        get => _powerUsageSummaryText;
        private set => this.RaiseAndSetIfChanged(ref _powerUsageSummaryText, value);
    }

    public string PerformanceEmptyStateText
    {
        get => _performanceEmptyStateText;
        private set => this.RaiseAndSetIfChanged(ref _performanceEmptyStateText, value);
    }

    public string MechanicalCriticalStepText
    {
        get => _mechanicalCriticalStepText;
        private set => this.RaiseAndSetIfChanged(ref _mechanicalCriticalStepText, value);
    }

    public double MechanicalChartAxisMaximum
    {
        get => _mechanicalChartAxisMaximum;
        private set
        {
            var resolved = Math.Max(1.0, value);
            if (Math.Abs(_mechanicalChartAxisMaximum - resolved) < 0.001)
                return;

            _mechanicalChartAxisMaximum = resolved;
            this.RaisePropertyChanged(nameof(MechanicalChartAxisMaximum));
            this.RaisePropertyChanged(nameof(MechanicalYAxisQuarterLabel));
            this.RaisePropertyChanged(nameof(MechanicalYAxisMidLabel));
            this.RaisePropertyChanged(nameof(MechanicalYAxisThreeQuarterLabel));
            this.RaisePropertyChanged(nameof(MechanicalYAxisTopLabel));
            this.RaisePropertyChanged(nameof(MechanicalGreenBandTop));
            this.RaisePropertyChanged(nameof(MechanicalGreenBandHeight));
            this.RaisePropertyChanged(nameof(MechanicalOrangeBandTop));
            this.RaisePropertyChanged(nameof(MechanicalOrangeBandHeight));
            this.RaisePropertyChanged(nameof(MechanicalRedBandHeight));
            this.RaisePropertyChanged(nameof(MechanicalIdealThresholdTop));
            this.RaisePropertyChanged(nameof(MechanicalIdealThresholdVisible));
            this.RaisePropertyChanged(nameof(MechanicalWarningThresholdTop));
            this.RaisePropertyChanged(nameof(MechanicalWarningThresholdVisible));
        }
    }

    public string MechanicalYAxisQuarterLabel => FormatMechanicalAxisLabel(MechanicalChartAxisMaximum * 0.25);

    public string MechanicalYAxisMidLabel => FormatMechanicalAxisLabel(MechanicalChartAxisMaximum * 0.5);

    public string MechanicalYAxisThreeQuarterLabel => FormatMechanicalAxisLabel(MechanicalChartAxisMaximum * 0.75);

    public string MechanicalYAxisTopLabel => FormatMechanicalAxisLabel(MechanicalChartAxisMaximum);

    public double MechanicalGreenBandTop =>
        CalculateMechanicalChartY(Math.Min(5.0, MechanicalChartAxisMaximum), MechanicalChartAxisMaximum);

    public double MechanicalGreenBandHeight => CalculateMechanicalChartBottom() - MechanicalGreenBandTop;

    public double MechanicalOrangeBandTop =>
        CalculateMechanicalChartY(Math.Min(12.0, MechanicalChartAxisMaximum), MechanicalChartAxisMaximum);

    public double MechanicalOrangeBandHeight => Math.Max(0, MechanicalGreenBandTop - MechanicalOrangeBandTop);

    public double MechanicalRedBandHeight => MechanicalChartAxisMaximum <= 12.0
        ? 0
        : Math.Max(0, MechanicalOrangeBandTop - PerformanceChartTop);

    public double MechanicalIdealThresholdTop => CalculateMechanicalChartY(5.0, MechanicalChartAxisMaximum);

    public bool MechanicalIdealThresholdVisible => MechanicalChartAxisMaximum >= 5.0;

    public double MechanicalWarningThresholdTop => CalculateMechanicalChartY(12.0, MechanicalChartAxisMaximum);

    public bool MechanicalWarningThresholdVisible => MechanicalChartAxisMaximum >= 12.0;

    public string PerformanceQuarterStepLabel =>
        CalculateQuarterStepLabel(CurrentChartMaxStep).ToString(CultureInfo.InvariantCulture);

    public string PerformanceMidStepLabel => (CurrentChartMaxStep / 2).ToString(CultureInfo.InvariantCulture);

    public string PerformanceThreeQuarterStepLabel =>
        CalculateThreeQuarterStepLabel(CurrentChartMaxStep).ToString(CultureInfo.InvariantCulture);

    public string PerformanceMaxStepLabel => CurrentChartMaxStep.ToString(CultureInfo.InvariantCulture);

    public string PerformancePowerLegendText
    {
        get
        {
            var ratedPower = SelectedLocomotive?.Source?.Power ?? 0;
            return ratedPower > 0
                ? $"Využitie výkonu ({ratedPower} kW)"
                : "Využitie výkonu";
        }
    }

    public string ForwardStop10Text
    {
        get => _forwardStop10Text;
        private set => this.RaiseAndSetIfChanged(ref _forwardStop10Text, value);
    }

    public string BackwardStop10Text
    {
        get => _backwardStop10Text;
        private set => this.RaiseAndSetIfChanged(ref _backwardStop10Text, value);
    }

    public string ForwardStop60Text
    {
        get => _forwardStop60Text;
        private set => this.RaiseAndSetIfChanged(ref _forwardStop60Text, value);
    }

    public string BackwardStop60Text
    {
        get => _backwardStop60Text;
        private set => this.RaiseAndSetIfChanged(ref _backwardStop60Text, value);
    }

    public string ForwardStop120Text
    {
        get => _forwardStop120Text;
        private set => this.RaiseAndSetIfChanged(ref _forwardStop120Text, value);
    }

    public string BackwardStop120Text
    {
        get => _backwardStop120Text;
        private set => this.RaiseAndSetIfChanged(ref _backwardStop120Text, value);
    }

    public double CurrentSpeedKmh
    {
        get => _currentSpeedKmh;
        private set
        {
            var normalized = Math.Round(Math.Max(0, value), 1);
            if (Math.Abs(_currentSpeedKmh - normalized) < 0.001)
                return;

            this.RaiseAndSetIfChanged(ref _currentSpeedKmh, normalized);
            this.RaisePropertyChanged(nameof(CurrentSpeedText));
            this.RaisePropertyChanged(nameof(GaugeNeedleAngle));
        }
    }

    public string CurrentSpeedText => CurrentSpeedKmh.ToString("0.0", CultureInfo.InvariantCulture);

    public double GaugeNeedleAngle => -90 + Math.Clamp(CurrentSpeedKmh / Math.Max(1, CurrentChartMaxSpeed), 0, 1) * 180;

    public int SelectedProfileTabIndex
    {
        get => _selectedProfileTabIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, 2);
            if (_selectedProfileTabIndex == normalized)
                return;

            this.RaiseAndSetIfChanged(ref _selectedProfileTabIndex, normalized);
            ResetManualInteractionState();
            this.RaisePropertyChanged(nameof(CanSaveActiveProfile));
            this.RaisePropertyChanged(nameof(CanAddPointManually));
            this.RaisePropertyChanged(nameof(IsForwardProfileSelected));
            this.RaisePropertyChanged(nameof(IsBackwardProfileSelected));
            this.RaisePropertyChanged(nameof(IsBothProfilesSelected));
            this.RaisePropertyChanged(nameof(IsForwardProfileVisible));
            this.RaisePropertyChanged(nameof(IsBackwardProfileVisible));
            this.RaisePropertyChanged(nameof(SelectedProfileDisplayName));
            this.RaisePropertyChanged(nameof(SelectedProfileDisplayForeground));
            _addPointManuallyCommand.NotifyCanExecuteChanged();
        }
    }

    public CalibrationMeasurementPointViewModel? SelectedForwardMeasurementPoint
    {
        get => _selectedForwardMeasurementPoint;
        set
        {
            if (ReferenceEquals(_selectedForwardMeasurementPoint, value))
                return;

            this.RaiseAndSetIfChanged(ref _selectedForwardMeasurementPoint, value);
            if (value != null)
                CurrentSpeedKmh = value.CalculatedSpeedKmh;
        }
    }

    public CalibrationMeasurementPointViewModel? SelectedBackwardMeasurementPoint
    {
        get => _selectedBackwardMeasurementPoint;
        set
        {
            if (ReferenceEquals(_selectedBackwardMeasurementPoint, value))
                return;

            this.RaiseAndSetIfChanged(ref _selectedBackwardMeasurementPoint, value);
            if (value != null)
                CurrentSpeedKmh = value.CalculatedSpeedKmh;
        }
    }

    public bool HandleChartPointerPressed(Point position)
    {
        Log.Information(
            "Speed chart pointer pressed. Loco={Locomotive}, Tab={TabIndex}, ManualMode={ManualMode}, Position=({X:0.##},{Y:0.##}), Thread={ThreadId}",
            GetSelectedLocomotiveName(),
            SelectedProfileTabIndex,
            IsManualPlacementMode,
            position.X,
            position.Y,
            Environment.CurrentManagedThreadId);

        if (!EnsureDirectionalProfileSelected("Úprava bodu v grafe"))
            return false;

        var targetCollection = GetActiveMeasurementCollection();
        if (targetCollection == null || !TryMapCanvasToChart(position, out var step, out var speed))
        {
            Log.Warning(
                "Speed chart pointer ignored. Loco={Locomotive}, Tab={TabIndex}, ManualMode={ManualMode}, Position=({X:0.##},{Y:0.##})",
                GetSelectedLocomotiveName(),
                SelectedProfileTabIndex,
                IsManualPlacementMode,
                position.X,
                position.Y);
            return false;
        }

        var nearestPoint = FindNearestPoint(targetCollection, position);
        if (nearestPoint != null)
        {
            _draggedMeasurementPoint = nearestPoint;
            _draggedMeasurementCollection = targetCollection;
            Log.Information(
                "Speed chart drag started. Loco={Locomotive}, Step={Step}, Speed={Speed:0.0}, Manual={IsManual}",
                GetSelectedLocomotiveName(),
                nearestPoint.Step,
                nearestPoint.CalculatedSpeedKmh,
                nearestPoint.IsManual);
            ApplyChartPointEdit(nearestPoint, step, speed, updateStatus: false, persistChanges: false,
                markProjectDirty: false, sortCollection: false);
            SelectMeasurementPoint(nearestPoint);
            return true;
        }

        if (!IsManualPlacementMode)
            return false;

        var point = CreateManualChartPoint(step, speed);
        UpsertMeasurementPoint(targetCollection, point);
        SubscribeToMeasurementPoint(point);
        SortMeasurements();
        PersistMeasurementsToLocomotive(SelectedLocomotive?.Source);
        MarkProfileDirty?.Invoke();
        UpdateDerivedState();
        SelectMeasurementPoint(point);
        Log.Information(
            "Manual speed point added. Loco={Locomotive}, Direction={Direction}, Step={Step}, Speed={Speed:0.0}, TotalPoints={TotalPoints}",
            GetSelectedLocomotiveName(),
            point.Direction,
            point.Step,
            point.CalculatedSpeedKmh,
            targetCollection.Count);
        UpdateCalibrationStatus(
            $"Manuálny bod {point.Step} / {point.CalculatedSpeedKmh:0.0} km/h bol pridaný do profilu {GetActiveDirectionDisplayName().ToLowerInvariant()}.");
        return true;
    }

    public bool HandleChartPointerMoved(Point position)
    {
        if (_draggedMeasurementPoint == null || _draggedMeasurementCollection == null)
            return false;

        if (!TryMapCanvasToChart(position, out var step, out var speed))
            return false;

        ApplyChartPointEdit(_draggedMeasurementPoint, step, speed, updateStatus: false, persistChanges: false,
            markProjectDirty: false, sortCollection: false);
        SelectMeasurementPoint(_draggedMeasurementPoint);
        return true;
    }

    public void HandleChartPointerReleased()
    {
        if (_draggedMeasurementPoint == null || _draggedMeasurementCollection == null)
            return;

        FinalizeMeasurementEdit(_draggedMeasurementPoint, _draggedMeasurementCollection,
            $"Bod kroku {_draggedMeasurementPoint.Step} bol upravený priamo v grafe.");
        ClearDraggedMeasurementState();
    }

    public void ReportChartInteractionFailure(string interactionName, Exception exception)
    {
        ResetManualInteractionState();

        var safeName = string.IsNullOrWhiteSpace(interactionName) ? "Úprava grafu" : interactionName.Trim();
        var safeException = exception ?? new Exception("Neznáma chyba pri úprave grafu.");
        Log.Error(safeException,
            "Speed chart interaction failure. Interaction={Interaction}, Loco={Locomotive}, Tab={TabIndex}", safeName,
            GetSelectedLocomotiveName(), SelectedProfileTabIndex);
        UpdateCalibrationStatus($"{safeName}: {safeException.GetType().Name}: {safeException.Message}");
    }

    public void SyncLocomotives(IEnumerable<LocoRecord> locomotives, LocoRecord? selected)
    {
        var currentId = SelectedLocomotive?.Source?.Id;
        var items = locomotives
            .Select(BuildLocomotiveOption)
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        AvailableLocomotives.Clear();
        foreach (var item in items)
            AvailableLocomotives.Add(item);

        var preferred = selected?.Id ?? currentId;
        SelectedLocomotive = AvailableLocomotives.FirstOrDefault(item =>
                                 string.Equals(item.Source?.Id, preferred, StringComparison.OrdinalIgnoreCase))
                             ?? AvailableLocomotives.FirstOrDefault();
    }

    public void SyncProjectIndicators(IEnumerable<string> indicators)
    {
        var options = indicators
            .Where(static indicator => !string.IsNullOrWhiteSpace(indicator))
            .Select(static indicator => indicator.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(static indicator => indicator, StringComparer.CurrentCultureIgnoreCase)
            .Select(static indicator => new CalibrationIndicatorOption(
                indicator,
                "●",
                "avares://TrackFlow/Assets/Appicons/16/cont_ind.png",
                "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png",
                isActive: true))
            .ToList();

        SyncProjectIndicators(options);
    }

    public void SyncProjectIndicators(IEnumerable<CalibrationIndicatorOption> indicators)
    {
        var normalized = indicators
            .Where(static indicator => !string.IsNullOrWhiteSpace(indicator.DisplayName))
            .GroupBy(static indicator => indicator.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static indicator => indicator.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ReplaceCollection(_allIndicators, normalized);
        ReplaceCollection(StartBlockOptions, normalized);

        if (SelectedStartBlock != null && !StartBlockOptions.Contains(SelectedStartBlock))
            SelectedStartBlock = null;

        if (SelectedMiddleBlock != null && !MiddleBlockOptions.Contains(SelectedMiddleBlock))
            SelectedMiddleBlock = null;

        if (SelectedEndBlock != null && !EndBlockOptions.Contains(SelectedEndBlock))
            SelectedEndBlock = null;

        RefreshBlockSelections();
    }

    /// <summary>
    /// Aktualizuje IsActive na existujúcich CalibrationIndicatorOption objektoch podľa aktuálneho
    /// runtime stavu BlockIndicator-ov z layoutu. Volaním tejto metódy sa ikona v comboboxe
    /// okamžite zmení bez nutnosti prestavby celého zoznamu.
    /// </summary>
    public void SyncIndicatorActiveStates(IEnumerable<TrackFlow.Models.Layout.BlockElement> blocks)
    {
        // Zostavíme slovník: IndicatorId → IsActive (pre presné párovanie podľa Guid).
        var byId = new Dictionary<Guid, bool>();
        // Záložný slovník: DisplayName (normalized) → IsActive (pre párovanie podľa mena).
        var byName = new Dictionary<string, bool>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var block in blocks)
        {
            foreach (var indicator in block.Indicators)
            {
                byId[indicator.Id] = indicator.IsActive;
                if (!string.IsNullOrWhiteSpace(indicator.Name))
                    byName[indicator.Name.Trim()] = indicator.IsActive;
            }
        }

        foreach (var option in _allIndicators)
        {
            bool? resolved = null;
            if (option.IndicatorId.HasValue && byId.TryGetValue(option.IndicatorId.Value, out var activeById))
                resolved = activeById;
            else if (!string.IsNullOrWhiteSpace(option.DisplayName) &&
                     byName.TryGetValue(option.DisplayName, out var activeByName))
                resolved = activeByName;

            if (resolved.HasValue)
                option.IsActive = resolved.Value;
        }
    }

    private static CalibrationMeasurementPointViewModel CreatePoint(int step, string direction, double timeSeconds,
        double rawSpeedKmh, double calculatedSpeedKmh, string status, bool isManual = false)
        => new(step, direction, timeSeconds, rawSpeedKmh, calculatedSpeedKmh, status, isManual);

    private static IReadOnlyList<CalibrationMethodItemViewModel> LoadCalibrationMethods()
    {
        var methods = new[]
        {
            (CalibrationMethod.AutomaticFullProfileOccupancy,
                "Automatické meranie kompletného rýchlostného profilu (detektory obsadenia)"),
            (CalibrationMethod.AutomaticFullProfileMomentary,
                "Automatické meranie kompletného rýchlostného profilu (momentové kontakty)"),
            (CalibrationMethod.AutomaticSingleStepOccupancy,
                "Automatické meranie jedného rýchlostného stupňa (detektory obsadenia)"),
            (CalibrationMethod.AutomaticSingleStepMomentary,
                "Automatické meranie jedného rýchlostného stupňa (momentové kontakty)"),
            (CalibrationMethod.BrakingCompensationTestOccupancy, "Test kompenzácie brzdenia (detektory obsadenia)"),
            (CalibrationMethod.BrakingCompensationTestMomentary, "Test kompenzácie brzdenia (momentové kontakty)"),
            (CalibrationMethod.ManualExternalDevice, "Manuálne meranie pomocou externého zariadenia")
        };

        Bitmap? sprite = null;
        try
        {
            var assetUri = new Uri("avares://TrackFlow/Assets/Appicons/bank/kalibracia.png");
            if (AssetLoader.Exists(assetUri))
            {
                using var stream = AssetLoader.Open(assetUri);
                sprite = new Bitmap(stream);
            }
        }
        catch
        {
        }

        return methods
            .Select((item, index) =>
                new CalibrationMethodItemViewModel(item.Item1, item.Item2, CreateCalibrationMethodIcon(sprite, index)))
            .ToList();
    }

    private static IImage? CreateCalibrationMethodIcon(Bitmap? sprite, int index)
    {
        if (sprite == null)
            return null;

        const int sliceHeight = 15;
        var top = index * sliceHeight;
        if (top + sliceHeight > sprite.PixelSize.Height)
            return null;

        return new CroppedBitmap(sprite, new PixelRect(0, top, sprite.PixelSize.Width, sliceHeight));
    }

    private static string GetCalibrationMethodStatus(CalibrationMethodItemViewModel? method)
        => method?.Method switch
        {
            CalibrationMethod.AutomaticFullProfileOccupancy =>
                "Zvolené automatické meranie kompletného profilu pomocou detektorov obsadenia.",
            CalibrationMethod.AutomaticFullProfileMomentary =>
                "Zvolené automatické meranie kompletného profilu pomocou momentových kontaktov.",
            CalibrationMethod.AutomaticSingleStepOccupancy =>
                "Zvolené automatické meranie jedného rýchlostného stupňa pomocou detektorov obsadenia.",
            CalibrationMethod.AutomaticSingleStepMomentary =>
                "Zvolené automatické meranie jedného rýchlostného stupňa pomocou momentových kontaktov.",
            CalibrationMethod.BrakingCompensationTestOccupancy =>
                "Zvolený test kompenzácie brzdenia pomocou detektorov obsadenia.",
            CalibrationMethod.BrakingCompensationTestMomentary =>
                "Zvolený test kompenzácie brzdenia pomocou momentových kontaktov.",
            CalibrationMethod.ManualExternalDevice => "Zvolené manuálne meranie pomocou externého zariadenia.",
            _ => "Pripravené na kalibráciu."
        };

    private void RefreshBlockSelections()
    {
        RefreshMiddleBlocks();
        RefreshEndBlocks();
    }

    private void RefreshMiddleBlocks()
    {
        ReplaceCollection(MiddleBlockOptions, _allIndicators);

        if (SelectedMiddleBlock != null && !MiddleBlockOptions.Contains(SelectedMiddleBlock))
            SelectedMiddleBlock = null;
    }

    private void RefreshEndBlocks()
    {
        ReplaceCollection(EndBlockOptions, _allIndicators);

        if (SelectedEndBlock != null && !EndBlockOptions.Contains(SelectedEndBlock))
            SelectedEndBlock = null;
    }

    private void ApplyLocomotiveDefaults(LocoRecord? record)
    {
        SetDecoderStepRange(record?.DecoderType);

        if (record == null)
        {
            SelectedMaxModelSpeed = ChooseNearestMaxSpeed(DefaultChartMaxSpeed);
            SetChartMaxSpeed(DefaultChartMaxSpeed);
            return;
        }

        if (!string.IsNullOrWhiteSpace(record.Scale))
            SelectedScale = NormalizeScale(record.Scale);

        if (record.MaxSpeed > 0)
        {
            SelectedMaxModelSpeed = ChooseNearestMaxSpeed(record.MaxSpeed);
            SetChartMaxSpeed(record.MaxSpeed);
        }
        else
        {
            SelectedMaxModelSpeed = ChooseNearestMaxSpeed(DefaultChartMaxSpeed);
            SetChartMaxSpeed(DefaultChartMaxSpeed);
        }
    }

    public void SetDecoderStepRange(string? decoderType = null)
    {
        const int resolvedMaxStep = DefaultChartMaxStep;
        if (resolvedMaxStep == CurrentChartMaxStep
            && string.Equals(DecoderStepAxisTitle, $"Rýchlostný stupeň dekodéra (0-{resolvedMaxStep})",
                StringComparison.Ordinal))
            return;

        CurrentChartMaxStep = resolvedMaxStep;
        DecoderStepAxisTitle = $"Rýchlostný stupeň dekodéra (0-{resolvedMaxStep})";
        this.RaisePropertyChanged(nameof(PerformanceQuarterStepLabel));
        this.RaisePropertyChanged(nameof(PerformanceMidStepLabel));
        this.RaisePropertyChanged(nameof(PerformanceThreeQuarterStepLabel));
        this.RaisePropertyChanged(nameof(PerformanceMaxStepLabel));
        ReplaceCollection(XAxisLabels, BuildHorizontalAxisLabels(resolvedMaxStep, ChartLeft, ChartWidth, 553));
        ReplaceCollection(VerticalGridLines,
            BuildVerticalGridLines(resolvedMaxStep, ChartLeft, ChartTop, ChartWidth, ChartHeight));
        ReplaceCollection(XAxisTickMarks,
            BuildXAxisTickMarks(resolvedMaxStep, ChartLeft, ChartTop, ChartWidth, ChartHeight));
        UpdateDerivedState();
    }

    public void SetChartMaxSpeed(int maxSpeed)
    {
        var resolvedMaxSpeed = maxSpeed > 0 ? maxSpeed : DefaultChartMaxSpeed;
        if (resolvedMaxSpeed == CurrentChartMaxSpeed)
            return;

        CurrentChartMaxSpeed = resolvedMaxSpeed;
        ReplaceCollection(YAxisLabels, BuildVerticalAxisLabels(resolvedMaxSpeed, ChartTop, ChartHeight));
        ReplaceCollection(HorizontalGridLines,
            BuildHorizontalGridLines(resolvedMaxSpeed, ChartLeft, ChartTop, ChartWidth, ChartHeight));
        this.RaisePropertyChanged(nameof(GaugeNeedleAngle));
        UpdateDerivedState();
    }

    private void StartAutomatedSequence()
    {
        // Ak prebieha meranie, zruš ho
        if (_calibrationCts != null)
        {
            _calibrationCts.Cancel();
            _calibrationCts.Dispose();
            _calibrationCts = null;
            UpdateCalibrationStatus("Meranie prerušené.");
            CalibrationProgress = 0;
            return;
        }

        if (!EnsureDirectionalProfileSelected("Automatické meranie"))
            return;

        var loco = SelectedLocomotive?.Source;
        if (loco == null)
        {
            UpdateCalibrationStatus("Vyberte lokomotívu pred spustením merania.");
            return;
        }

        if (SelectedStartBlock == null || SelectedMiddleBlock == null || SelectedEndBlock == null)
        {
            UpdateCalibrationStatus("Nastavte štartovací, merací (Stred) a koncový blok.");
            return;
        }

        if (DriveLocoAsync == null)
        {
            UpdateCalibrationStatus("DCC centrála nie je pripojená.");
            return;
        }

        _calibrationCts = new CancellationTokenSource();
        IsCalibrationRunning = true;
        _ = RunFullProfileCalibrationAsync(loco.Address, _calibrationCts.Token);
    }

    /// <summary>
    /// Meranie kompletného rýchlostného profilu pomocou troch detektorov obsadenia.
    /// 15 meracích bodov, oba smery jazdy v každom cykle.
    /// </summary>
    private async Task RunFullProfileCalibrationAsync(int locoAddress, CancellationToken ct)
    {
        const int TotalPoints = 15;
        const int FirstStep   = 11;   // TC začína od kroku 11 (Internal=83/1000 * 126 ≈ 11)
        const int LastStep    = 127;  // TC končí na kroku 127 (max)
        var dccSteps = Enumerable.Range(0, TotalPoints)
            .Select(i => (int)Math.Round(FirstStep + i * (double)(LastStep - FirstStep) / (TotalPoints - 1)))
            .Distinct()
            .OrderBy(s => s)
            .ToArray();

        var scaleRatio = ParseScaleRatio(SelectedScale);
        var blockM = BlockLengthCm / 100.0;

        UpdateCalibrationStatus(
            $"Meranie spustené. {TotalPoints} bodov, dĺžka bloku {BlockLengthCm} cm, mierka 1:{scaleRatio:0}.");
        CalibrationProgress = 0;

        try
        {
            for (var i = 0; i < dccSteps.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var dccStep = dccSteps[i];

                // ── Meranie dopredu ───────────────────────────────────────
                UpdateCalibrationStatus($"Bod {i + 1}/{TotalPoints} (krok {dccStep}) — jazda dopredu...");
                var fwdTime = await MeasurePassthroughAsync(locoAddress, dccStep, forward: true, ct);

                if (fwdTime > 0)
                {
                    var rawKmh = blockM / fwdTime * 3.6;
                    var modelKmh = Math.Round(rawKmh * scaleRatio, 2);
                    var point = CreatePoint(dccStep, "Dopredu", fwdTime, rawKmh, modelKmh, "Automatické meranie");
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        UpsertMeasurementPoint(ForwardMeasurementPoints, point);
                        SyncSpeedProfileRowsFromMeasurements();
                        UpdateDerivedState();
                    });
                }

                await Task.Delay(TimeSpan.FromSeconds(PauseSeconds), ct);
                ct.ThrowIfCancellationRequested();

                // ── Meranie dozadu ────────────────────────────────────────
                UpdateCalibrationStatus($"Bod {i + 1}/{TotalPoints} (krok {dccStep}) — jazda dozadu...");
                var bwdTime = await MeasurePassthroughAsync(locoAddress, dccStep, forward: false, ct);

                if (bwdTime > 0)
                {
                    var rawKmh = blockM / bwdTime * 3.6;
                    var modelKmh = Math.Round(rawKmh * scaleRatio, 2);
                    var point = CreatePoint(dccStep, "Dozadu", bwdTime, rawKmh, modelKmh, "Automatické meranie");
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        UpsertMeasurementPoint(BackwardMeasurementPoints, point);
                        SyncSpeedProfileRowsFromMeasurements();
                        UpdateDerivedState();
                    });
                }

                await Task.Delay(TimeSpan.FromSeconds(PauseSeconds), ct);
                CalibrationProgress = (double)(i + 1) / TotalPoints * 100.0;
            }

            CalibrationProgress = 100;
            PersistMeasurementsToLocomotive(SelectedLocomotive?.Source);
            UpdateCalibrationStatus($"Meranie dokončené. {TotalPoints} bodov zaznamenaných.");
        }
        catch (OperationCanceledException)
        {
            try { await SendCalibrationSpeedAsync(locoAddress, 0, true); } catch { }
            UpdateCalibrationStatus("Meranie prerušené užívateľom.");
            CalibrationProgress = 0;
        }
        catch (Exception ex)
        {
            try { await SendCalibrationSpeedAsync(locoAddress, 0, true); } catch { }
            UpdateCalibrationStatus($"Chyba merania: {ex.Message}");
            Log.Warning(ex, "RunFullProfileCalibrationAsync zlyhalo");
        }
        finally
        {
            _calibrationCts?.Dispose();
            _calibrationCts = null;
            IsCalibrationRunning = false;
        }
    }

    /// <summary>
    /// Jeden priechod: Štart→Mid (štart stopiek)→End (stop stopiek).
    /// Podľa TC manuálu: pred štartom sa čaká kým Mid blok je voľný.
    /// RunOut: po dosiahnutí End bloku loko pokračuje kým neopustí End blok.
    /// Bez časového limitu — reaguje len na detektory.
    /// </summary>
    private async Task<double> MeasurePassthroughAsync(int address, int dccStep, bool forward, CancellationToken ct)
    {
        var middleBlock = SelectedMiddleBlock!;
        var endBlock    = forward ? SelectedEndBlock! : SelectedStartBlock!;

        var midKey = (middleBlock.ModuleAddress, middleBlock.PortNumber, middleBlock.DccCentralProfileId);
        var endKey = (endBlock.ModuleAddress,    endBlock.PortNumber,    endBlock.DccCentralProfileId);

        // ── 1. Čakaj kým Mid blok je voľný (TC: Centre must be turned off before start) ──
        // Čakáme na IsActive=False feedback pre Mid blok.
        // Ak príde do 500ms, pokračujeme. Ak nie, predpokladáme že je voľný.
        {
            var midFreeCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnMidFree(DccFeedbackStateChange change)
            {
                if (change.IsActive) return;
                if ((change.ModuleAddress, change.PortNumber, change.ProfileId) != midKey) return;
                midFreeCompletion.TrySetResult(true);
            }

            CalibrationDcc!.FeedbackStateChanged += OnMidFree;
            try
            {
                // Čakaj max PauseSeconds kým Mid blok hlási voľný — potom pokračuj
                await Task.WhenAny(midFreeCompletion.Task, Task.Delay(TimeSpan.FromSeconds(Math.Max(PauseSeconds, 1)), ct));
            }
            catch (OperationCanceledException) { CalibrationDcc!.FeedbackStateChanged -= OnMidFree; throw; }
            finally { CalibrationDcc!.FeedbackStateChanged -= OnMidFree; }
        }

        ct.ThrowIfCancellationRequested();

        // ── 2. Nastav stav a spusti loko ─────────────────────────────────────
        _measurementMidKey = midKey;
        _measurementEndKey = endKey;
        _measurementState  = MeasurementState.WaitingForMid;
        _measurementStart  = default;
        _measurementAddress = address;
        _measurementSpeed   = dccStep;
        _measurementForward = forward;
        _measurementCompletion = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => _measurementCompletion.TrySetCanceled(ct));

        CalibrationDcc!.FeedbackStateChanged += OnFeedback;

        TrackFlowDoctorService.Instance.Diagnose("Kalibrácia",
            $"Spúšťam loko: krok={dccStep} forward={forward} " +
            $"MidKey=({_measurementMidKey.ModuleAddress},{_measurementMidKey.PortNumber}) " +
            $"EndKey=({_measurementEndKey.ModuleAddress},{_measurementEndKey.PortNumber})");

        await SendCalibrationSpeedAsync(address, dccStep, forward);

        // ── 3. Čakaj na dokončenie merania (Mid→End) — bez časového limitu ───
        double elapsed;
        try
        {
            elapsed = await _measurementCompletion.Task;
            TrackFlowDoctorService.Instance.Diagnose("Kalibrácia", $"Meranie dokončené: elapsed={elapsed:F3}s");
        }
        catch
        {
            CalibrationDcc!.FeedbackStateChanged -= OnFeedback;
            _measurementCompletion = null;
            _measurementState = MeasurementState.Idle;
            await SendCalibrationSpeedAsync(address, 0, forward);
            throw;
        }

        // ── 4. RunOut ─────────────────────────────────────────────────────────
        // TC spôsob: meranie skončilo vstupom do End bloku.
        // Dobeh — loko pokračuje ešte RunoutDistanceCm pred zastavením
        // aby mala dostatok dráhy v End bloku pred otočením.
        if (RunoutDistanceCm > 0 && elapsed > 0)
        {
            var speedMs = BlockLengthCm / 100.0 / elapsed;
            var runoutSeconds = RunoutDistanceCm / 100.0 / speedMs;
            try { await Task.Delay(TimeSpan.FromSeconds(runoutSeconds), ct); } catch { }
        }

        // ── 5. Zastav loko ────────────────────────────────────────────────────
        CalibrationDcc!.FeedbackStateChanged -= OnFeedback;
        _measurementCompletion = null;
        _measurementState = MeasurementState.Idle;
        await SendCalibrationSpeedAsync(address, 0, forward);

        return elapsed;
    }

    /// <summary>Posiela DCC príkaz — rovnaký vzor ako CV57.SendLocoSpeedAsync.</summary>
    private async Task SendCalibrationSpeedAsync(int address, int speed, bool forward)
    {
        try
        {
            if (CalibrationDcc == null) return;

            IDccCentralClient client;
            var loco = SelectedLocomotive?.Source;
            if (loco?.AssignedCentralProfileId.HasValue == true &&
                CalibrationDcc.TryGetConnectedClient(loco.AssignedCentralProfileId.Value, out var assigned))
                client = assigned;
            else
                client = CalibrationDcc.Client;

            if (!client.IsConnected) return;

            // Z21Client interne throttluje pakety kratšie ako ~80ms od seba a vtedy
            // ich TICHO zahodí (bez výnimky). Aby sme mali istotu že DCC príkaz
            // skutočne odišiel, pošleme ho dvakrát s malým odstupom.
            await client.SetLocomotiveSpeedAsync(address, speed, forward);
            await Task.Delay(120);
            await client.SetLocomotiveSpeedAsync(address, speed, forward);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SendCalibrationSpeedAsync zlyhalo");
        }
    }

    private void OnFeedback(DccFeedbackStateChange change)
    {
        var key = (change.ModuleAddress, change.PortNumber, change.ProfileId);

        TrackFlowDoctorService.Instance.Diagnose("Kalibrácia",
            $"OnFeedback: ModAddr={change.ModuleAddress} Port={change.PortNumber} IsActive={change.IsActive} " +
            $"State={_measurementState} MidKey=({_measurementMidKey.ModuleAddress},{_measurementMidKey.PortNumber}) " +
            $"EndKey=({_measurementEndKey.ModuleAddress},{_measurementEndKey.PortNumber})");

        // Vstup do Mid bloku → štart stopiek
        if (change.IsActive &&
            _measurementState == MeasurementState.WaitingForMid &&
            key == _measurementMidKey)
        {
            _measurementStart = DateTime.UtcNow;
            _measurementState = MeasurementState.Measuring;
            TrackFlowDoctorService.Instance.Diagnose("Kalibrácia", $"✓ Mid vstup — štart stopiek t={_measurementStart:HH:mm:ss.fff}");
            return;
        }

        // Vstup do End bloku → stop stopiek (TC spôsob)
        if (change.IsActive &&
            _measurementState == MeasurementState.Measuring &&
            key == _measurementEndKey)
        {
            var elapsed = (DateTime.UtcNow - _measurementStart).TotalSeconds;
            TrackFlowDoctorService.Instance.Diagnose("Kalibrácia", $"✓ End vstup — stop stopiek elapsed={elapsed:F3}s");
            _measurementCompletion?.TrySetResult(elapsed);
        }
    }
    
  /// <summary>Extrahuje číselný pomer mierky z reťazca napr. "1:87 (H0)" → 87.0</summary>
    private static double ParseScaleRatio(string scale)
    {
        var m = System.Text.RegularExpressions.Regex.Match(scale, @"1:(\d+)");
        return m.Success && double.TryParse(m.Groups[1].Value, out var ratio) ? ratio : 87.0;
    }

    private void AddPointManually()
    {
        Log.Information(
            "Manual speed mode toggle requested. Loco={Locomotive}, Tab={TabIndex}, ManualModeBefore={ManualMode}, Thread={ThreadId}",
            GetSelectedLocomotiveName(),
            SelectedProfileTabIndex,
            IsManualPlacementMode,
            Environment.CurrentManagedThreadId);

        if (!EnsureDirectionalProfileSelected("Manuálne pridanie bodu"))
            return;

        IsManualPlacementMode = !IsManualPlacementMode;
        Log.Information(
            "Manual speed mode toggled. Loco={Locomotive}, Tab={TabIndex}, ManualModeAfter={ManualMode}",
            GetSelectedLocomotiveName(),
            SelectedProfileTabIndex,
            IsManualPlacementMode);
        UpdateCalibrationStatus(IsManualPlacementMode
            ? $"Režim manuálneho vkladania je aktívny pre profil {GetActiveDirectionDisplayName().ToLowerInvariant()}. Kliknite do grafu alebo potiahnite existujúci bod."
            : $"Režim manuálneho vkladania bol vypnutý pre profil {GetActiveDirectionDisplayName().ToLowerInvariant()}.");
    }

    private void VerifyCalibration()
    {
        CalibrationProgress = Math.Max(CalibrationProgress, 82);
        UpdateDerivedState();
        UpdateCalibrationStatus("Overenie dokončené.");
    }

    private void SaveProfile()
    {
        if (!EnsureDirectionalProfileSelected("Uloženie profilu"))
            return;

        if (SelectedLocomotive?.Source == null)
        {
            UpdateCalibrationStatus("Vyberte lokomotívu v ľavom gride, potom uložte profil.");
            return;
        }

        CalibrationProgress = 100;
        PersistMeasurementsToLocomotive(SelectedLocomotive.Source);
        PersistDiagnosticsSnapshotToLocomotive(SelectedLocomotive.Source, randomizeSelections: true);

        var profileScope = SelectedProfileTabIndex switch
        {
            0 => "Profil dopredu",
            1 => "Profil dozadu",
            _ => "Rýchlostný profil"
        };

        var persistedProjectState = PersistProfileChanges?.Invoke();
        if (persistedProjectState == false)
        {
            UpdateCalibrationStatus(
                $"{profileScope} lokomotívy {GetSelectedLocomotiveName()} bol uložený, ale zmeny projektu sa nepodarilo potvrdiť.");
            return;
        }

        UpdateCalibrationStatus($"{profileScope} lokomotívy {GetSelectedLocomotiveName()} bol uložený.");
    }

    public void InitializeProfiles()
    {
        if (SelectedLocomotive?.Source == null)
        {
            UpdateCalibrationStatus("Inicializácia profilu: najprv vyberte lokomotívu v ľavom gride.");
            return;
        }

        ResetManualInteractionState();
        ReplaceMeasurementCollection(ForwardMeasurementPoints, Array.Empty<CalibrationMeasurementPointViewModel>());
        ReplaceMeasurementCollection(BackwardMeasurementPoints, Array.Empty<CalibrationMeasurementPointViewModel>());
        SelectedForwardMeasurementPoint = null;
        SelectedBackwardMeasurementPoint = null;
        SelectedSpeedProfileRow = null;
        CurrentSpeedKmh = 0;

        PersistMeasurementsToLocomotive(SelectedLocomotive.Source);
        SyncSpeedProfileRowsFromMeasurements();
        UpdateDerivedState();
        MarkProfileDirty?.Invoke();
        UpdateCalibrationStatus(
            $"Profily lokomotívy {GetSelectedLocomotiveName()} boli inicializované. Všetky namerané RAW dáta pre oba smery boli vymazané.");
    }

    private void SortMeasurements()
    {
        ReplaceMeasurementCollection(ForwardMeasurementPoints,
            ForwardMeasurementPoints.OrderBy(point => point.Step).ToList());
        ReplaceMeasurementCollection(BackwardMeasurementPoints,
            BackwardMeasurementPoints.OrderBy(point => point.Step).ToList());
        SyncSpeedProfileRowsFromMeasurements();
    }

    private bool EnsureDirectionalProfileSelected(string actionName)
    {
        if (SelectedLocomotive?.Source == null)
        {
            UpdateCalibrationStatus($"{actionName}: najprv vyberte lokomotívu v ľavom gride.");
            return false;
        }

        if (SelectedProfileTabIndex is 0 or 1)
            return true;

        UpdateCalibrationStatus($"{actionName}: vyberte Profil dopredu alebo Profil dozadu.");
        return false;
    }

    private ObservableCollection<CalibrationMeasurementPointViewModel>? GetActiveMeasurementCollection()
        => SelectedProfileTabIndex switch
        {
            0 => ForwardMeasurementPoints,
            1 => BackwardMeasurementPoints,
            _ => null
        };

    private string GetActiveDirectionDisplayName()
        => SelectedProfileTabIndex switch
        {
            1 => "Dozadu",
            2 => "Obidva profily",
            _ => "Dopredu"
        };

    private string GetSelectedLocomotiveName()
        => SelectedLocomotive?.DisplayName ?? "bez výberu";

    private void UpsertMeasurementPoint(ObservableCollection<CalibrationMeasurementPointViewModel> target,
        CalibrationMeasurementPointViewModel point)
    {
        var existing = target.FirstOrDefault(item => item.Step == point.Step);
        if (existing != null)
        {
            UnsubscribeFromMeasurementPoint(existing);
            target.Remove(existing);
        }

        target.Add(point);
    }

    private void LoadMeasurementsFromLocomotive(LocoRecord? record)
    {
        var forward = record?.ForwardSpeedProfilePoints?
                          .OrderBy(point => point.Step)
                          .Select(ToViewModel)
                          .ToList()
                      ?? new List<CalibrationMeasurementPointViewModel>();

        var backward = record?.BackwardSpeedProfilePoints?
                           .OrderBy(point => point.Step)
                           .Select(ToViewModel)
                           .ToList()
                       ?? new List<CalibrationMeasurementPointViewModel>();

        ReplaceMeasurementCollection(ForwardMeasurementPoints, forward);
        ReplaceMeasurementCollection(BackwardMeasurementPoints, backward);
        SelectedForwardMeasurementPoint = null;
        SelectedBackwardMeasurementPoint = null;
        SelectedSpeedProfileRow = null;
        CurrentSpeedKmh = 0;
        SyncSpeedProfileRowsFromMeasurements();
        UpdateDerivedState();
        RefreshDisplayedDiagnostics(record);
    }

    private void PersistMeasurementsToLocomotive(LocoRecord? record, bool persistForward = true,
        bool persistBackward = true)
    {
        if (record == null)
            return;

        if (persistForward)
        {
            record.ForwardSpeedProfilePoints = ForwardMeasurementPoints
                .OrderBy(point => point.Step)
                .Select(ToModel)
                .ToList();
        }

        if (persistBackward)
        {
            record.BackwardSpeedProfilePoints = BackwardMeasurementPoints
                .OrderBy(point => point.Step)
                .Select(ToModel)
                .ToList();
        }
    }

    private static CalibrationMeasurementPointViewModel ToViewModel(LocoSpeedProfilePoint point)
        => CreatePoint(point.Step, point.Direction, point.TimeSeconds,  point.CalculatedSpeedKmh, point.CalculatedSpeedKmh,
            point.Status, point.IsManual || IsManualStatus(point.Status));

    private static LocoSpeedProfilePoint ToModel(CalibrationMeasurementPointViewModel point)
        => new()
        {
            Step = point.Step,
            Direction = point.Direction,
            TimeSeconds = point.TimeSeconds,
            RawSpeedKmh = point.CalculatedSpeedKmh,
            CalculatedSpeedKmh = point.CalculatedSpeedKmh,
            Status = point.Status,
            IsManual = point.IsManual
        };

    private void UpdateCalibrationStatus(string message)
    {
        CalibrationStatusText = message;
    }

    private void ResetManualInteractionState()
    {
        IsManualPlacementMode = false;
        ClearDraggedMeasurementState();
    }

    private void ClearDraggedMeasurementState()
    {
        _draggedMeasurementPoint = null;
        _draggedMeasurementCollection = null;
    }

    private void UpdateCurveGeometry()
    {
        var forwardPoints = ForwardMeasurementPoints.OrderBy(point => point.Step).ToList();
        var backwardPoints = BackwardMeasurementPoints.OrderBy(point => point.Step).ToList();

        ForwardCurvePathData = BuildCurvePath(forwardPoints, ChartLeft, ChartTop, ChartWidth, ChartHeight,
            CurrentChartMaxSpeed, CurrentChartMaxStep);
        BackwardCurvePathData = BuildCurvePath(backwardPoints, ChartLeft, ChartTop, ChartWidth, ChartHeight,
            CurrentChartMaxSpeed, CurrentChartMaxStep);
        ForwardAreaPathData = BuildCurveFillPath(forwardPoints, ChartLeft, ChartTop, ChartWidth, ChartHeight,
            CurrentChartMaxSpeed, CurrentChartMaxStep);
        BackwardAreaPathData = BuildCurveFillPath(backwardPoints, ChartLeft, ChartTop, ChartWidth, ChartHeight,
            CurrentChartMaxSpeed, CurrentChartMaxStep);
        (ForwardGradientStart, ForwardGradientEnd) = ComputePerpendicularGradient(forwardPoints, ChartLeft, ChartTop,
            ChartWidth, ChartHeight, CurrentChartMaxSpeed, CurrentChartMaxStep);
        (BackwardGradientStart, BackwardGradientEnd) = ComputePerpendicularGradient(backwardPoints, ChartLeft, ChartTop,
            ChartWidth, ChartHeight, CurrentChartMaxSpeed, CurrentChartMaxStep);
        ForwardMarkerPathData = BuildMarkerPath(forwardPoints, radius: 6.5, CurrentChartMaxStep, CurrentChartMaxSpeed);
        BackwardMarkerPathData =
            BuildMarkerPath(backwardPoints, radius: 6.0, CurrentChartMaxStep, CurrentChartMaxSpeed);
        VarianceAreaPathData = BuildVarianceAreaPath(forwardPoints, backwardPoints, ChartLeft, ChartTop, ChartWidth,
            ChartHeight, CurrentChartMaxSpeed, CurrentChartMaxStep);

        ForwardCurveMarkers.Clear();
        BackwardCurveMarkers.Clear();
        ReferenceGuides.Clear();

        foreach (var marker in BuildCurveMarkers(forwardPoints, "#1976D2", isBackward: false, CurrentChartMaxStep,
                     CurrentChartMaxSpeed))
            ForwardCurveMarkers.Add(marker);
        foreach (var marker in BuildCurveMarkers(backwardPoints, "#C44747", isBackward: true, CurrentChartMaxStep,
                     CurrentChartMaxSpeed))
            BackwardCurveMarkers.Add(marker);
        foreach (var guide in BuildReferenceGuides(forwardPoints, backwardPoints, CurrentChartMaxStep))
            ReferenceGuides.Add(guide);
    }

    private void UpdateDerivedState()
    {
        SyncSpeedProfileRowsFromMeasurements();
        UpdateCurveGeometry();

        if (ForwardMeasurementPoints.Count == 0 && BackwardMeasurementPoints.Count == 0)
        {
            MaxForwardSpeedText = string.Empty;
            MaxBackwardSpeedText = string.Empty;
            AverageDifferenceText = string.Empty;
            MaxDeviationText = string.Empty;
            MaxForwardSpeedValueText = "—";
            MaxForwardSpeedDetailText = "bez meraní";
            MaxBackwardSpeedValueText = "—";
            MaxBackwardSpeedDetailText = "bez meraní";
            AverageDifferenceValueText = "—";
            AverageDifferenceDetailText = "bez spoločných bodov";
            MaxDeviationValueText = "—";
            MaxDeviationDetailText = "bez odchýlky";
            ForwardStop10Text = string.Empty;
            BackwardStop10Text = string.Empty;
            ForwardStop60Text = string.Empty;
            BackwardStop60Text = string.Empty;
            ForwardStop120Text = string.Empty;
            BackwardStop120Text = string.Empty;
            FrictionCurvePathData = string.Empty;
            PowerCurvePathData = string.Empty;
            FrictionPeakSummaryText = string.Empty;
            PowerUsageSummaryText = string.Empty;
            PerformanceEmptyStateText = "Na indikátor asymetrie treba body v oboch smeroch.";
            MechanicalCriticalStepText = "kritický stupeň: —";
            MechanicalChartAxisMaximum = 20.0;
            _pendingDiagnosticsComputation = null;
            if (SelectedLocomotive?.Source == null)
                ClearDisplayedDiagnostics();
            CurrentSpeedKmh = 0;
            return;
        }

        var forwardMax = ForwardMeasurementPoints.OrderByDescending(point => point.CalculatedSpeedKmh).FirstOrDefault();
        var backwardMax = BackwardMeasurementPoints.OrderByDescending(point => point.CalculatedSpeedKmh)
            .FirstOrDefault();
        var matches = (from forward in ForwardMeasurementPoints
                join backward in BackwardMeasurementPoints on forward.Step equals backward.Step
                orderby forward.Step
                select new DiagnosticDifferenceSample(forward.Step, forward.CalculatedSpeedKmh,
                    backward.CalculatedSpeedKmh))
            .ToList();

        var averageDifference = matches.Count > 0 ? matches.Average(pair => (double)pair.Difference) : 0;
        DiagnosticDifferenceSample? maxDeviation = matches.Count > 0
            ? matches.OrderByDescending(pair => pair.Difference).First()
            : null;

        MaxForwardSpeedText = forwardMax == null
            ? "Max. dopredu: n/a"
            : $"Max. dopredu: {forwardMax.CalculatedSpeedKmh:0.0} km/h (stupeň {forwardMax.Step})";
        MaxBackwardSpeedText = backwardMax == null
            ? "Max. dozadu: n/a"
            : $"Max. dozadu: {backwardMax.CalculatedSpeedKmh:0.0} km/h (stupeň {backwardMax.Step})";
        AverageDifferenceText = $"Priemerný rozdiel: {averageDifference:0.0}%";
        MaxDeviationText = !maxDeviation.HasValue
            ? "Max. odchýlka: n/a"
            : $"Max. odchýlka: {maxDeviation.Value.Difference:0.0} km/h pri stupni {maxDeviation.Value.Step}";

        MaxForwardSpeedValueText = forwardMax == null ? "—" : $"{forwardMax.CalculatedSpeedKmh:0.0} km/h";
        MaxForwardSpeedDetailText = forwardMax == null ? "bez meraní" : $"stupeň {forwardMax.Step}";
        MaxBackwardSpeedValueText = backwardMax == null ? "—" : $"{backwardMax.CalculatedSpeedKmh:0.0} km/h";
        MaxBackwardSpeedDetailText = backwardMax == null ? "bez meraní" : $"stupeň {backwardMax.Step}";
        AverageDifferenceValueText = matches.Count == 0 ? "—" : $"{averageDifference:0.0}%";
        AverageDifferenceDetailText = matches.Count switch
        {
            0 => "bez spoločných bodov",
            1 => "1 spoločný bod",
            _ => $"{matches.Count} Spol. bodov"
        };
        MaxDeviationValueText = !maxDeviation.HasValue ? "—" : $"{maxDeviation.Value.Difference:0.0} km/h";
        MaxDeviationDetailText = !maxDeviation.HasValue ? "bez odchýlky" : $"stupeň {maxDeviation.Value.Step}";

        var mechanicalHealthSamples = BuildMechanicalHealthSamples(matches, CurrentChartMaxSpeed);
        // Os Y mini-grafu "Asymetria smerov" je zámerne zafixovaná v rozsahu 0–20 %.
        // Auto-scaling deformoval optické porovnanie medzi lokomotívami, a preto je nahradený
        // pevnou stupnicou, ktorá zodpovedá farebnému semaforu (0–5 % zelená, 5–12 % oranžová,
        // 12–20 % červená).
        MechanicalChartAxisMaximum = 20.0;
        FrictionCurvePathData =
            BuildMechanicalHealthPath(mechanicalHealthSamples, CurrentChartMaxStep, MechanicalChartAxisMaximum);
        PowerCurvePathData = string.Empty;
        PerformanceEmptyStateText = mechanicalHealthSamples.Count == 0
            ? "Na indikátor asymetrie treba body v oboch smeroch."
            : string.Empty;

        MechanicalHealthSample? peakMechanicalSample = null;
        for (var index = 0; index < mechanicalHealthSamples.Count; index++)
        {
            var candidate = mechanicalHealthSamples[index];
            if (peakMechanicalSample is null
                || candidate.DifferencePercent > peakMechanicalSample.Value.DifferencePercent + 1e-9
                || (Math.Abs(candidate.DifferencePercent - peakMechanicalSample.Value.DifferencePercent) <= 1e-9
                    && candidate.Step > peakMechanicalSample.Value.Step))
            {
                peakMechanicalSample = candidate;
            }
        }

        MechanicalCriticalStepText = !peakMechanicalSample.HasValue
            ? "kritický stupeň: —"
            : $"kritický stupeň: {peakMechanicalSample.Value.Step}";
        FrictionPeakSummaryText = !peakMechanicalSample.HasValue
            ? "max. asymetria: —"
            : $"max. asymetria: {peakMechanicalSample.Value.DifferencePercent:0.0}%";
        PowerUsageSummaryText = string.Empty;

        var forward10 = ComputeStoppingDistance(10, 0.95);
        var backward10 = ComputeStoppingDistance(10, 0.92);
        var forward60 = ComputeStoppingDistance(60, 0.62);
        var backward60 = ComputeStoppingDistance(60, 0.60);
        var forward120 = ComputeStoppingDistance(120, 0.31);
        var backward120 = ComputeStoppingDistance(120, 0.30);

        ForwardStop10Text = $"Dopredu: {forward10:0.#} cm";
        BackwardStop10Text = $"Dozadu: {backward10:0.#} cm";
        ForwardStop60Text = $"Dopredu: {forward60:0.#} cm";
        BackwardStop60Text = $"Dozadu: {backward60:0.#} cm";
        ForwardStop120Text = $"Dopredu: {forward120:0.#} cm";
        BackwardStop120Text = $"Dozadu: {backward120:0.#} cm";

        AiDiagnosticSeverity severity;
        if (averageDifference <= 2.0 && (!maxDeviation.HasValue || maxDeviation.Value.Difference <= 2.0))
        {
            severity = AiDiagnosticSeverity.Ok;
            SetEngineStatusSeverity(severity);
            EngineStatusText = "Stav diagnostiky: OK";
        }
        else if (averageDifference <= 4.0 && (!maxDeviation.HasValue || maxDeviation.Value.Difference <= 4.0))
        {
            severity = AiDiagnosticSeverity.Warning;
            SetEngineStatusSeverity(severity);
            EngineStatusText = "Stav diagnostiky: Upozornenie";
        }
        else
        {
            severity = AiDiagnosticSeverity.Error;
            SetEngineStatusSeverity(severity);
            EngineStatusText = "Stav diagnostiky: Zlé";
        }

        int? maxDeviationStep = maxDeviation.HasValue ? maxDeviation.Value.Step : null;
        double? maxDeviationDifference = maxDeviation.HasValue ? maxDeviation.Value.Difference : null;

        var problemType = DetermineDiagnosticProblemType(matches, severity, CurrentChartMaxStep);
        var causeType = DetermineDiagnosticCauseType(matches, severity, problemType, CurrentChartMaxStep);
        _pendingDiagnosticsComputation = new AiDiagnosticsComputation(severity, problemType, causeType,
            maxDeviationStep, maxDeviationDifference, PauseSeconds);

        RefreshLiveDiagnosticsPreviewIfNeeded(SelectedLocomotive?.Source);
    }

    private void SetEngineStatusSeverity(AiDiagnosticSeverity severity)
    {
        if (EngineStatusSeverity == severity)
            return;

        EngineStatusSeverity = severity;
        this.RaisePropertyChanged(nameof(EngineStatusBackground));
        this.RaisePropertyChanged(nameof(EngineStatusBorderBrush));
        this.RaisePropertyChanged(nameof(EngineStatusForeground));
        this.RaisePropertyChanged(nameof(EngineStatusIconText));
        this.RaisePropertyChanged(nameof(EngineStatusIconFontSize));
    }

    private readonly record struct DiagnosticDifferenceSample(int Step, double Forward, double Backward)
    {
        public double SignedDifference => Forward - Backward;
        public double Difference => Math.Abs(SignedDifference);
    }

    private readonly record struct MechanicalHealthSample(int Step, double DifferenceKmh, double DifferencePercent);

    private readonly record struct AiDiagnosticsComputation(
        AiDiagnosticSeverity Severity,
        AiDiagnosticProblemType ProblemType,
        AiDiagnosticCauseType CauseType,
        int? MaxDeviationStep,
        double? MaxDeviationDifference,
        double PauseSeconds);

    private readonly record struct AiDiagnosticsSnapshot(
        AiDiagnosticSeverity Severity,
        AiDiagnosticProblemType ProblemType,
        AiDiagnosticCauseType CauseType,
        string EngineStatusText,
        string AnalysisSummaryText,
        string AiRecommendationText,
        string RecommendedCvTweaksText);

    private void RefreshDisplayedDiagnostics(LocoRecord? record)
    {
        var savedSnapshot = TryLoadSavedDiagnosticsSnapshot(record);
        if (savedSnapshot.HasValue)
        {
            ApplyDiagnosticsSnapshot(savedSnapshot.Value);
            return;
        }

        if (_pendingDiagnosticsComputation.HasValue)
        {
            ApplyDiagnosticsSnapshot(BuildDiagnosticsSnapshot(_pendingDiagnosticsComputation.Value,
                _displayedDiagnosticsSnapshot, randomizeSelections: false));
            return;
        }

        ClearDisplayedDiagnostics();
    }

    private void RefreshLiveDiagnosticsPreviewIfNeeded(LocoRecord? record)
    {
        if (record == null || TryLoadSavedDiagnosticsSnapshot(record).HasValue ||
            !_pendingDiagnosticsComputation.HasValue)
            return;

        ApplyDiagnosticsSnapshot(BuildDiagnosticsSnapshot(_pendingDiagnosticsComputation.Value,
            _displayedDiagnosticsSnapshot, randomizeSelections: false));
    }

    private void PersistDiagnosticsSnapshotToLocomotive(LocoRecord? record, bool randomizeSelections)
    {
        if (record == null)
            return;

        if (!_pendingDiagnosticsComputation.HasValue)
        {
            ClearSavedDiagnosticsSnapshot(record);
            ClearDisplayedDiagnostics();
            return;
        }

        var previousSnapshot = TryLoadSavedDiagnosticsSnapshot(record);
        var snapshot =
            BuildDiagnosticsSnapshot(_pendingDiagnosticsComputation.Value, previousSnapshot, randomizeSelections);
        SaveDiagnosticsSnapshot(record, snapshot);
        ApplyDiagnosticsSnapshot(snapshot);
    }

    private static void SaveDiagnosticsSnapshot(LocoRecord record, AiDiagnosticsSnapshot snapshot)
    {
        record.SavedDiagnosticsEngineStatusText = snapshot.EngineStatusText;
        record.SavedDiagnosticsSeverity = snapshot.Severity.ToString();
        record.SavedDiagnosticsProblemType = snapshot.ProblemType.ToString();
        record.SavedDiagnosticsCauseType = snapshot.CauseType.ToString();
        record.SavedDiagnosticsAnalysisSummaryText = snapshot.AnalysisSummaryText;
        record.SavedDiagnosticsAiRecommendationText = snapshot.AiRecommendationText;
        record.SavedDiagnosticsRecommendedCvTweaksText = snapshot.RecommendedCvTweaksText;
    }

    private static void ClearSavedDiagnosticsSnapshot(LocoRecord record)
    {
        record.SavedDiagnosticsEngineStatusText = string.Empty;
        record.SavedDiagnosticsSeverity = string.Empty;
        record.SavedDiagnosticsProblemType = string.Empty;
        record.SavedDiagnosticsCauseType = string.Empty;
        record.SavedDiagnosticsAnalysisSummaryText = string.Empty;
        record.SavedDiagnosticsAiRecommendationText = string.Empty;
        record.SavedDiagnosticsRecommendedCvTweaksText = string.Empty;
    }

    private static AiDiagnosticsSnapshot? TryLoadSavedDiagnosticsSnapshot(LocoRecord? record)
    {
        if (record == null
            || string.IsNullOrWhiteSpace(record.SavedDiagnosticsEngineStatusText)
            || string.IsNullOrWhiteSpace(record.SavedDiagnosticsAnalysisSummaryText)
            || string.IsNullOrWhiteSpace(record.SavedDiagnosticsAiRecommendationText)
            || string.IsNullOrWhiteSpace(record.SavedDiagnosticsRecommendedCvTweaksText))
            return null;

        var severity =
            Enum.TryParse<AiDiagnosticSeverity>(record.SavedDiagnosticsSeverity, ignoreCase: true,
                out var parsedSeverity)
                ? parsedSeverity
                : AiDiagnosticSeverity.Ok;
        var problemType = Enum.TryParse<AiDiagnosticProblemType>(record.SavedDiagnosticsProblemType, ignoreCase: true,
            out var parsedProblemType)
            ? parsedProblemType
            : AiDiagnosticProblemType.Stable;
        var causeType = Enum.TryParse<AiDiagnosticCauseType>(record.SavedDiagnosticsCauseType, ignoreCase: true,
            out var parsedCauseType)
            ? parsedCauseType
            : AiDiagnosticCauseType.Stable;

        return new AiDiagnosticsSnapshot(
            severity,
            problemType,
            causeType,
            record.SavedDiagnosticsEngineStatusText,
            record.SavedDiagnosticsAnalysisSummaryText,
            record.SavedDiagnosticsAiRecommendationText,
            record.SavedDiagnosticsRecommendedCvTweaksText);
    }

    private void ApplyDiagnosticsSnapshot(AiDiagnosticsSnapshot snapshot)
    {
        _displayedDiagnosticsSnapshot = snapshot;
        SetEngineStatusSeverity(snapshot.Severity);
        EngineStatusProblemType = snapshot.ProblemType;
        EngineStatusCauseType = snapshot.CauseType;
        EngineStatusText = snapshot.EngineStatusText;
        AnalysisSummaryText = snapshot.AnalysisSummaryText;
        AiRecommendationText = snapshot.AiRecommendationText;
        RecommendedCvTweaksText = snapshot.RecommendedCvTweaksText;
    }

    private void ClearDisplayedDiagnostics()
    {
        _displayedDiagnosticsSnapshot = null;
        SetEngineStatusSeverity(AiDiagnosticSeverity.Ok);
        EngineStatusProblemType = AiDiagnosticProblemType.Stable;
        EngineStatusCauseType = AiDiagnosticCauseType.Stable;
        EngineStatusText = string.Empty;
        AnalysisSummaryText = string.Empty;
        AiRecommendationText = string.Empty;
        RecommendedCvTweaksText = string.Empty;
    }

    private static AiDiagnosticsSnapshot BuildDiagnosticsSnapshot(
        AiDiagnosticsComputation computation,
        AiDiagnosticsSnapshot? previousSnapshot,
        bool randomizeSelections)
    {
        var shouldRefreshPrimaryTexts = !previousSnapshot.HasValue
                                        || previousSnapshot.Value.Severity != computation.Severity
                                        || string.IsNullOrWhiteSpace(previousSnapshot.Value.AnalysisSummaryText)
                                        || string.IsNullOrWhiteSpace(previousSnapshot.Value.AiRecommendationText);
        var shouldRefreshCvText = shouldRefreshPrimaryTexts
                                  || !previousSnapshot.HasValue
                                  || previousSnapshot.Value.ProblemType != computation.ProblemType
                                  || previousSnapshot.Value.CauseType != computation.CauseType
                                  || string.IsNullOrWhiteSpace(previousSnapshot.Value.RecommendedCvTweaksText);

        return new AiDiagnosticsSnapshot(
            computation.Severity,
            computation.ProblemType,
            computation.CauseType,
            BuildEngineStatusText(computation.Severity),
            shouldRefreshPrimaryTexts
                ? BuildRandomAnalysisSummary(computation.Severity, computation.ProblemType, computation.CauseType,
                    computation.MaxDeviationStep, computation.MaxDeviationDifference, randomizeSelections)
                : previousSnapshot!.Value.AnalysisSummaryText,
            shouldRefreshPrimaryTexts
                ? BuildRandomAiRecommendation(computation.Severity, computation.ProblemType, computation.CauseType,
                    computation.MaxDeviationStep, computation.PauseSeconds, randomizeSelections)
                : previousSnapshot!.Value.AiRecommendationText,
            shouldRefreshCvText
                ? BuildRandomCvTweaksText(computation.Severity, computation.ProblemType, computation.CauseType,
                    randomizeSelections)
                : previousSnapshot!.Value.RecommendedCvTweaksText);
    }

    private static string BuildEngineStatusText(AiDiagnosticSeverity severity)
        => severity switch
        {
            AiDiagnosticSeverity.Ok => "Stav diagnostiky: OK",
            AiDiagnosticSeverity.Warning => "Stav diagnostiky: Upozornenie",
            _ => "Stav diagnostiky: Zlé"
        };

    private static AiDiagnosticProblemType DetermineDiagnosticProblemType(
        IReadOnlyList<DiagnosticDifferenceSample> matches,
        AiDiagnosticSeverity severity,
        int maxStep)
    {
        if (severity == AiDiagnosticSeverity.Ok || matches.Count == 0)
            return AiDiagnosticProblemType.Stable;

        var broadAsymmetryThreshold = severity == AiDiagnosticSeverity.Warning ? 2.0 : 3.0;
        var broadAsymmetryCount = matches.Count(match => match.Difference >= broadAsymmetryThreshold);
        if (broadAsymmetryCount >= Math.Max(2, (int)Math.Ceiling(matches.Count * 0.45)))
            return AiDiagnosticProblemType.DirectionAsymmetry;

        var primaryStep = matches
            .OrderByDescending(match => match.Difference)
            .ThenBy(match => match.Step)
            .First()
            .Step;

        var lowBoundary = Math.Max(4, (int)Math.Round(maxStep * 0.25, MidpointRounding.AwayFromZero));
        var highBoundary = Math.Max(lowBoundary + 1, (int)Math.Round(maxStep * 0.75, MidpointRounding.AwayFromZero));

        if (primaryStep <= lowBoundary)
            return AiDiagnosticProblemType.LowSteps;

        if (primaryStep >= highBoundary)
            return AiDiagnosticProblemType.HighSpeed;

        return AiDiagnosticProblemType.MidBand;
    }

    private static AiDiagnosticCauseType DetermineDiagnosticCauseType(
        IReadOnlyList<DiagnosticDifferenceSample> matches,
        AiDiagnosticSeverity severity,
        AiDiagnosticProblemType problemType,
        int maxStep)
    {
        if (severity == AiDiagnosticSeverity.Ok || matches.Count == 0)
            return AiDiagnosticCauseType.Stable;

        var lowBoundary = Math.Max(4, (int)Math.Round(maxStep * 0.25, MidpointRounding.AwayFromZero));
        var highBoundary = Math.Max(lowBoundary + 1, (int)Math.Round(maxStep * 0.75, MidpointRounding.AwayFromZero));

        var lowBandAverage = AverageDifference(matches.Where(match => match.Step <= lowBoundary));
        var highBandAverage = AverageDifference(matches.Where(match => match.Step >= highBoundary));
        var positiveDirectionCount = matches.Count(match => match.SignedDifference >= 1.0);
        var negativeDirectionCount = matches.Count(match => match.SignedDifference <= -1.0);
        var dominantDirectionRatio = Math.Max(positiveDirectionCount, negativeDirectionCount) /
                                     (double)Math.Max(1, matches.Count);

        if (dominantDirectionRatio >= 0.70 && highBandAverage >= lowBandAverage + 0.8 &&
            problemType is AiDiagnosticProblemType.DirectionAsymmetry or AiDiagnosticProblemType.HighSpeed)
            return AiDiagnosticCauseType.MechanicalResistance;

        if (dominantDirectionRatio >= 0.75)
            return AiDiagnosticCauseType.SingleDirectionIssue;

        return problemType switch
        {
            AiDiagnosticProblemType.LowSteps => AiDiagnosticCauseType.StartupCvTuning,
            AiDiagnosticProblemType.MidBand => AiDiagnosticCauseType.MidBandSmoothing,
            AiDiagnosticProblemType.HighSpeed => AiDiagnosticCauseType.TopCurveInstability,
            AiDiagnosticProblemType.DirectionAsymmetry => AiDiagnosticCauseType.MechanicalResistance,
            _ => AiDiagnosticCauseType.Stable
        };
    }

    private static double AverageDifference(IEnumerable<DiagnosticDifferenceSample> samples)
    {
        var items = samples.ToList();
        return items.Count == 0 ? 0 : items.Average(sample => sample.Difference);
    }

    private static string BuildRandomAnalysisSummary(
        AiDiagnosticSeverity severity,
        AiDiagnosticProblemType problemType,
        AiDiagnosticCauseType causeType,
        int? maxDeviationStep,
        double? maxDeviationDifference,
        bool randomizeSelections)
        => BuildCompositeMessage(
            "Analýza:",
            SelectRandom(GetAnalysisLeadVariants(severity), randomizeSelections),
            SelectRandom(GetAnalysisProblemVariants(problemType), randomizeSelections),
            SelectRandom(GetAnalysisCauseVariants(causeType), randomizeSelections),
            BuildDeviationSentence(maxDeviationStep, maxDeviationDifference, randomizeSelections));

    private static string BuildRandomAiRecommendation(
        AiDiagnosticSeverity severity,
        AiDiagnosticProblemType problemType,
        AiDiagnosticCauseType causeType,
        int? maxDeviationStep,
        double pauseSeconds,
        bool randomizeSelections)
    {
        var targetStepText = !maxDeviationStep.HasValue
            ? "kritického kroku"
            : $"stupňa {maxDeviationStep.Value}";
        var suggestedPauseText = $"{pauseSeconds + 1.0:0.0}s";

        return BuildCompositeMessage(
            "Odporúčanie:",
            SelectRandom(GetRecommendationLeadVariants(severity), randomizeSelections),
            SelectRandom(GetRecommendationProblemVariants(problemType, targetStepText), randomizeSelections),
            SelectRandom(GetRecommendationCauseVariants(causeType), randomizeSelections),
            SelectRandom(GetRecommendationVerificationVariants(severity, suggestedPauseText), randomizeSelections));
    }

    private static string BuildRandomCvTweaksText(
        AiDiagnosticSeverity severity,
        AiDiagnosticProblemType problemType,
        AiDiagnosticCauseType causeType,
        bool randomizeSelections)
    {
        return BuildCompositeMessage(
            string.Empty,
            SelectRandom(GetCvLeadVariants(severity), randomizeSelections),
            SelectRandom(GetCvAdjustmentVariants(problemType, causeType), randomizeSelections),
            SelectRandom(GetCvValidationVariants(problemType, causeType, severity), randomizeSelections));
    }

    private static string BuildCompositeMessage(string prefix, params string[] parts)
    {
        var body = string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()));
        return string.IsNullOrWhiteSpace(prefix)
            ? body
            : string.IsNullOrWhiteSpace(body)
                ? prefix
                : $"{prefix} {body}";
    }

    private static string BuildDeviationSentence(int? maxDeviationStep, double? maxDeviationDifference,
        bool randomizeSelections)
    {
        if (!maxDeviationStep.HasValue || !maxDeviationDifference.HasValue)
        {
            return SelectRandom(new[]
            {
                "Meranie zároveň neukazuje žiadnu ostrú špičku odchýlky.",
                "V zázname sa neobjavila žiadna výrazná bodová anomália.",
                "Krivka nemá žiadny dominantný bod s extrémnym úletom."
            }, randomizeSelections);
        }

        return SelectRandom(new[]
        {
            $"Najväčšia nameraná odchýlka je {maxDeviationDifference.Value:0.0} km/h pri stupni {maxDeviationStep.Value}.",
            $"Vrchol problému vychádza na stupeň {maxDeviationStep.Value} s rozdielom {maxDeviationDifference.Value:0.0} km/h.",
            $"Najvýraznejší rozdiel je pri stupni {maxDeviationStep.Value}, kde meranie ukazuje {maxDeviationDifference.Value:0.0} km/h.",
            $"Kritický bod sa sústreďuje pri stupni {maxDeviationStep.Value}; tam je rozdiel {maxDeviationDifference.Value:0.0} km/h.",
            $"AI označuje stupeň {maxDeviationStep.Value} ako najcitlivejší bod s odchýlkou {maxDeviationDifference.Value:0.0} km/h."
        }, randomizeSelections);
    }

    private static IReadOnlyList<string> GetAnalysisLeadVariants(AiDiagnosticSeverity severity)
        => severity switch
        {
            AiDiagnosticSeverity.Ok => new[]
            {
                "Profil pôsobí vyrovnane a čitateľne.",
                "Krivka je pokojná a drží si stabilný charakter.",
                "Meranie vyzerá konzistentne bez rušivých skokov.",
                "Výsledok zostáva v bezpečnej tolerancii.",
                "AI hodnotí priebeh ako stabilný a použiteľný.",
                "Jazdný profil pôsobí zdravo a predvídateľne."
            },
            AiDiagnosticSeverity.Warning => new[]
            {
                "Profil je stále použiteľný, ale nie je úplne čistý.",
                "Krivka už ukazuje drobné varovné známky odchýlky.",
                "Meranie je blízko tolerancie, no niektoré body si pýtajú pozornosť.",
                "AI vidí menší problém, ktorý sa oplatí doladiť.",
                "Výsledok je zatiaľ prijateľný, no nie ideálny.",
                "Jazdný profil potrebuje jemnú korekciu, nie však prestavbu od nuly."
            },
            _ => new[]
            {
                "Profil už presahuje bežnú toleranciu a žiada zásah.",
                "Krivka je nestabilná a výsledok nie je pripravený na finálne uloženie.",
                "AI vidí výraznejší problém, ktorý nemožno nechať bez úpravy.",
                "Meranie naznačuje, že profil nie je dostatočne zosúladený.",
                "Výsledok je za hranou odporúčanej tolerancie.",
                "Jazdný profil potrebuje výraznejšie doladenie alebo nové meranie."
            }
        };

    private static IReadOnlyList<string> GetAnalysisProblemVariants(AiDiagnosticProblemType problemType)
        => problemType switch
        {
            AiDiagnosticProblemType.Stable => new[]
            {
                "Rozdiely medzi smermi zostávajú rozumné v celom rozsahu krokov.",
                "Nízke kroky, stredné pásmo aj vysoká rýchlosť si držia podobný charakter.",
                "Rozjazd aj horná časť krivky pôsobia vyrovnane.",
                "Profil nemá jedno dominantné problémové pásmo.",
                "Meranie je rovnomerné od rozjazdu až po vysokú rýchlosť.",
                "Krivka pôsobí ucelene naprieč celým rozsahom dekodéra."
            },
            AiDiagnosticProblemType.DirectionAsymmetry => new[]
            {
                "Najviac vystupuje asymetria smerov naprieč profilom.",
                "Rozdiel medzi smermi je viditeľný vo viacerých častiach krivky.",
                "Hlavný problém tvorí nesúlad medzi smerom dopredu a dozadu.",
                "Krivka sa láme najmä kvôli asymetrii smerov.",
                "AI ako hlavný jav označuje rozdielne správanie oboch smerov.",
                "Dominantnou témou merania je asymetria medzi smermi."
            },
            AiDiagnosticProblemType.LowSteps => new[]
            {
                "Najcitlivejšia oblasť je rozjazd a nízke kroky.",
                "Problém sa sústreďuje do prvých stupňov a nízkych krokov.",
                "Krivka stráca plynulosť hlavne pri rozjazde.",
                "AI vidí najväčší rozptyl v nízkych krokoch.",
                "Ťažisko odchýlky leží v oblasti pomalého posunu a rozjazdu.",
                "Profil najviac kolíše práve v nízkych krokoch."
            },
            AiDiagnosticProblemType.MidBand => new[]
            {
                "Najviac sa rozchádza stredné pásmo krivky.",
                "Kritickým úsekom je stredné pásmo medzi rozjazdom a vysokou rýchlosťou.",
                "Rozptyl sa sústreďuje do strednej časti krivky.",
                "AI označuje stredné pásmo ako hlavné miesto odchýlky.",
                "Najslabšie vychádza práve stredné pásmo profilu.",
                "Hlavná nepresnosť je v strednej časti jazdného rozsahu."
            },
            _ => new[]
            {
                "Najväčšia neistota sa objavuje vo vysokej rýchlosti.",
                "Horná časť krivky pri vysokej rýchlosti nie je dostatočne pokojná.",
                "AI vidí problém hlavne v hornej časti krivky.",
                "Najcitlivejšie vychádza pásmo vysokej rýchlosti.",
                "Odchýlka rastie smerom k vysokej rýchlosti a koncu rozsahu.",
                "Najväčší rozptyl je v hornej časti profilu pri vysokej rýchlosti."
            }
        };

    private static IReadOnlyList<string> GetAnalysisCauseVariants(AiDiagnosticCauseType causeType)
        => causeType switch
        {
            AiDiagnosticCauseType.Stable => new[]
            {
                "AI v tom nevidí dominantný mechanický ani CV problém.",
                "Zatiaľ sa nepotvrdzuje zásadný problém s CV ani mechanikou.",
                "Profil nepôsobí tak, že by ho ťahal konkrétny mechanický odpor alebo zlá CV mapa.",
                "Aktuálne meranie neukazuje typický vzor pre poškodenú mechaniku ani zlé nastavenie CV.",
                "Neobjavuje sa výrazný signál mechanického odporu ani chybného ladenia CV.",
                "AI zatiaľ nevidí jedinú dominantnú príčinu problému, skôr stabilný výsledok."
            },
            AiDiagnosticCauseType.MechanicalResistance => new[]
            {
                "Vzorec odchýlky pripomína mechanický odpor alebo zvýšené trenie pohonu.",
                "AI tu vidí náznak mechanického odporu v spriahadlách, prevodoch alebo ložiskách.",
                "Rozptyl vyzerá ako dôsledok trenia pohonu a odporu súpravy.",
                "Priebeh má podpis mechanického odporu, ktorý brzdí profil viac než samotné CV.",
                "Najpravdepodobnejšou príčinou je zvýšený mechanický odpor alebo trenie v pohone.",
                "AI spája tento tvar krivky skôr s mechanikou než s jedným izolovaným CV parametrom."
            },
            AiDiagnosticCauseType.StartupCvTuning => new[]
            {
                "Najpravdepodobnejšie treba doladiť rozjazdové CV, hlavne CV2 a prípadne CV3.",
                "Vzorec chyby zodpovedá skôr nevyladenému rozjazdu cez CV2 alebo CV3.",
                "AI tento obraz číta ako problém s rozjazdovým nastavením CV.",
                "Nízke kroky pôsobia tak, akoby CV2 alebo rozjazdová mapa nebola úplne trafená.",
                "Príčina sa pravdepodobne skrýva v jemnom doladení CV2 a prechodu z rozjazdu.",
                "Profil naznačuje, že rozjazdové CV potrebujú ešte jemné nastavenie."
            },
            AiDiagnosticCauseType.MidBandSmoothing => new[]
            {
                "Najpravdepodobnejšou príčinou je slabšie vyhladenie stredného pásma.",
                "AI tu vidí problém vo vyhladení strednej časti krivky, typicky okolo CV3 alebo CV4.",
                "Vzorec odchýlky zodpovedá tomu, keď stredné pásmo nie je dostatočne vyhladené.",
                "Stredná časť profilu pôsobí, akoby potrebovala jemnejšie vyhladenie mapy CV.",
                "Najviac tomu sedí nedoladené vyhladenie v strednom pásme.",
                "AI spája túto odchýlku najmä s plynulosťou a vyhladením stredu krivky."
            },
            AiDiagnosticCauseType.TopCurveInstability => new[]
            {
                "Najpravdepodobnejšou príčinou je nestabilná horná časť krivky, najmä okolo CV5.",
                "AI tu vidí skôr problém vo vrchole krivky a vo vysokej rýchlosti než v rozjazde.",
                "Vzorec odchýlky pripomína nedoladenú hornú časť mapy CV a vysokú rýchlosť.",
                "Horná časť profilu pôsobí, akoby CV5 alebo limit vysokej rýchlosti neboli úplne pokojné.",
                "Najlepšie tomu zodpovedá nestabilita vo vysokej rýchlosti a vo vrchole krivky.",
                "AI odhaduje, že koreň problému je v hornej časti krivky a vo vysokej rýchlosti."
            },
            _ => new[]
            {
                "AI má podozrenie, že problém sa koncentruje iba v jednom smere jazdy.",
                "Vzorec odchýlky naznačuje, že slabší je len jeden smer a nie celý profil.",
                "Krivka vyzerá tak, ako keď problém vzniká iba v jednom smere jazdy.",
                "Najpravdepodobnejšie nejde o obojstrannú chybu, ale o problém len v jednom smere.",
                "AI odhaduje, že jeden smer je citeľne slabší alebo ťažší než druhý.",
                "Podpis merania ukazuje skôr na jednostranný problém než na globálnu chybu oboch smerov."
            }
        };

    private static IReadOnlyList<string> GetRecommendationLeadVariants(AiDiagnosticSeverity severity)
        => severity switch
        {
            AiDiagnosticSeverity.Ok => new[]
            {
                "Aktuálny profil môžete považovať za pripravený na prevádzku.",
                "Nie je nutný veľký zásah; stačí bežná overovacia jazda.",
                "Profil vyzerá zdravo a potrebuje už len finálne potvrdiť.",
                "AI nežiada zásadnú korekciu, skôr len bežné overenie.",
                "Výsledok je možné ponechať s minimálnou ďalšou kontrolou.",
                "V tejto chvíli stačí už len potvrdiť správanie v reálnom jazde."
            },
            AiDiagnosticSeverity.Warning => new[]
            {
                "Odporúčam jemný zásah bez resetu celého profilu.",
                "Stačí cielená korekcia, nie kompletná prestavba merania.",
                "Profil má zmysel doladiť lokálne a potom znovu overiť.",
                "AI navrhuje malý, ale presný zásah do problémovej oblasti.",
                "Oplatí sa spraviť jednu korekciu a následne kontrolné kolo.",
                "Najlepšia cesta je lokálne doladenie a opakované overenie."
            },
            _ => new[]
            {
                "Tu už treba výraznejší zásah a nové overenie.",
                "AI odporúča vrátiť sa k meraniu a postihnutú oblasť prekalibrácia.",
                "Profil ešte nie je vhodný na finálne uloženie bez ďalšej úpravy.",
                "Potrebné je nové cielené meranie a následné opätovné overenie.",
                "Najbezpečnejší postup je prekalibrácia problémovej časti profilu.",
                "Odporúčam neukladať profil bez ďalšieho merania a korekcie."
            }
        };

    private static IReadOnlyList<string> GetRecommendationProblemVariants(AiDiagnosticProblemType problemType,
        string targetStepText)
        => problemType switch
        {
            AiDiagnosticProblemType.Stable => new[]
            {
                "Skontrolujte profil len na krátkej overovacej jazde cez oblúk a výhybky.",
                "Ak chcete mať úplnú istotu, potvrďte správanie v reálnom posune aj vo vyššej rýchlosti.",
                "Stačí už len otestovať profil pod bežnou záťažou na koľajisku.",
                "Pre istotu prejdite krátky test od rozjazdu až po vyššiu rýchlosť.",
                "Skúšobná jazda cez typické problematické miesta už postačí ako finálny check.",
                "Na potvrdenie výsledku postačí jeden kontrolný prebeh s ľahkou záťažou."
            },
            AiDiagnosticProblemType.DirectionAsymmetry => new[]
            {
                $"Premerajte asymetriu smerov v okolí {targetStepText} a porovnajte oba smery na rovnakej trati.",
                $"Zamerajte sa na porovnanie smeru dopredu a dozadu, najmä okolo {targetStepText}.",
                $"Vyberte meranie, ktoré priamo porovná oba smery v oblasti {targetStepText}.",
                $"Najprv si potvrďte, kde sa asymetria smerov láme; typicky v okolí {targetStepText}.",
                $"Skontrolujte, či sa rozdiel medzi smermi opakuje aj pri novom meraní okolo {targetStepText}.",
                $"AI odporúča najprv znovu zmerať oba smery v oblasti {targetStepText}."
            },
            AiDiagnosticProblemType.LowSteps => new[]
            {
                $"Premerajte rozjazd a nízke kroky okolo {targetStepText}.",
                $"Zamerajte ďalšie meranie na rozjazdovú oblasť a prvé kroky pri {targetStepText}.",
                $"Najprv znovu otestujte pomalý posun a rozjazd v okolí {targetStepText}.",
                $"Skontrolujte správanie lokomotívy v nízkych krokoch, hlavne pri {targetStepText}.",
                $"AI odporúča opakovať meranie rozjazdu a prvých stupňov pri {targetStepText}.",
                $"Najviac pomôže nové meranie v oblasti rozjazdu a nízkych krokov pri {targetStepText}."
            },
            AiDiagnosticProblemType.MidBand => new[]
            {
                $"Premerajte stredné pásmo krivky v okolí {targetStepText}.",
                $"Zamerajte nové overenie na strednú časť profilu pri {targetStepText}.",
                $"AI odporúča zopakovať meranie v strednom pásme, hlavne okolo {targetStepText}.",
                $"Skontrolujte stredné pásmo pri {targetStepText} a porovnajte ho s okolitými bodmi.",
                $"Najviac pomôže nové meranie strednej časti krivky pri {targetStepText}.",
                $"Vráťte sa k strednému pásmu a spravte detailnejší prechod okolo {targetStepText}."
            },
            _ => new[]
            {
                $"Premerajte vysokú rýchlosť a hornú časť krivky okolo {targetStepText}.",
                $"Zamerajte ďalšie overenie na vysokú rýchlosť a vrchol krivky pri {targetStepText}.",
                $"AI odporúča zopakovať meranie v hornej časti profilu, hlavne okolo {targetStepText}.",
                $"Skontrolujte správanie lokomotívy vo vysokej rýchlosti pri {targetStepText}.",
                $"Najviac pomôže nové meranie vysokej rýchlosti a hornej časti krivky pri {targetStepText}.",
                $"Vráťte sa k pásmu vysokej rýchlosti a spravte detailnejší prechod okolo {targetStepText}."
            }
        };

    private static IReadOnlyList<string> GetRecommendationCauseVariants(AiDiagnosticCauseType causeType)
        => causeType switch
        {
            AiDiagnosticCauseType.Stable => new[]
            {
                "Ak sa nič nezmení v mechanike, ďalšia úprava CV pravdepodobne nebude nutná.",
                "Mechaniku aj CV stačí už len potvrdiť, nie prepisovať.",
                "Zásah do CV robte iba vtedy, ak sa problém zopakuje aj pri overovacej jazde.",
                "AI tu nevidí dôvod na preventívny reset alebo väčšiu zmenu CV mapy.",
                "Bez nového symptómu netreba zasahovať ani do mechaniky, ani do CV.",
                "Momentálne skôr potvrďte výsledok než aby ste menili ďalšie CV."
            },
            AiDiagnosticCauseType.MechanicalResistance => new[]
            {
                "Popri meraní skontrolujte aj mechanický odpor, trenie a voľný chod súpravy.",
                "Pred ďalším ladením CV overte spriahadlá, prevody a prípadné trenie pohonu.",
                "AI odporúča mechanickú kontrolu ešte pred agresívnejším zásahom do CV.",
                "Najprv odstráňte mechanický odpor, až potom dolaďujte CV mapu.",
                "Skúste potvrdiť, či problém nevzniká trením alebo odporu v mechanike.",
                "Ak mechanický odpor zostane, samotná úprava CV nemusí stačiť."
            },
            AiDiagnosticCauseType.StartupCvTuning => new[]
            {
                "Hlavný zásah smerujte do CV2 a prípadne do prechodu cez CV3.",
                "AI odporúča najprv skúsiť jemnú korekciu rozjazdových CV.",
                "Rozjazd dolaďte cez CV2 a len potom overte, či treba meniť ďalšie parametre.",
                "Najviac získa profil z jemného zásahu do rozjazdovej časti CV mapy.",
                "Skúste najprv zmeniť rozjazdové CV a až potom riešte mechaniku.",
                "Koreň problému pravdepodobne neleží vo vysokej rýchlosti, ale v rozjazdových CV."
            },
            AiDiagnosticCauseType.MidBandSmoothing => new[]
            {
                "Zásah smerujte najmä do vyhladenia stredného pásma cez CV3 alebo CV4.",
                "AI odporúča plynulejší prechod v strede krivky namiesto zásahu do maxima.",
                "Najviac pomôže vyhladiť strednú časť mapy CV.",
                "Najprv dolaďte vyhladenie stredu profilu, potom meranie zopakujte.",
                "Skúste sa sústrediť na plynulosť v strede krivky, nie na rozjazd ani maximum.",
                "Stredné pásmo si pýta jemné vyhladenie a pokojnejší prechod medzi bodmi."
            },
            AiDiagnosticCauseType.TopCurveInstability => new[]
            {
                "Najväčší zmysel má zásah do vrcholu krivky a do CV5.",
                "AI odporúča stabilizovať hornú časť krivky skôr než meniť rozjazd.",
                "Skúste jemne skrotiť vysokú rýchlosť a až potom znova merať.",
                "Najprv dolaďte hornú časť mapy CV, potom kontrolujte zvyšok profilu.",
                "Kľúčová bude korekcia vo vysokej rýchlosti, nie v nízkych krokoch.",
                "Najväčší efekt prinesie pokojnejšia horná časť krivky a jemnejšia práca s CV5."
            },
            _ => new[]
            {
                "Pozornosť venujte tomu, či problém nevzniká iba v jednom smere jazdy.",
                "Vzorec odchýlky naznačuje, že slabší je len jeden smer a nie celý profil.",
                "Krivka vyzerá tak, ako keď problém vzniká iba v jednom smere jazdy.",
                "Najpravdepodobnejšie nejde o obojstrannú chybu, ale o problém len v jednom smere.",
                "AI odhaduje, že jeden smer je citeľne slabší alebo ťažší než druhý.",
                "Podpis merania ukazuje skôr na jednostranný problém než na globálnu chybu oboch smerov."
            }
        };

    private static IReadOnlyList<string> GetRecommendationVerificationVariants(AiDiagnosticSeverity severity,
        string suggestedPauseText)
        => severity switch
        {
            AiDiagnosticSeverity.Ok => new[]
            {
                "Na záver stačí krátke potvrdenie v bežnej prevádzke.",
                "Jedno kontrolné kolo navyše bude už len poistka správneho výsledku.",
                "Ak sa profil správa rovnako aj na koľajisku, môžete ho pokojne ponechať.",
                "Overenie s dlhšou pauzou nie je nutné, ale môže poslúžiť ako finálna kontrola.",
                "Výsledok postačí potvrdiť jednou pokojnou jazdou bez ďalšej veľkej zmeny.",
                "Stačí už len finálny check bez agresívnych zásahov."
            },
            AiDiagnosticSeverity.Warning => new[]
            {
                $"Po korekcii skúste nové overenie s intervalom pauzy približne {suggestedPauseText}.",
                $"Následné kontrolné kolo urobte s mierne dlhšou pauzou okolo {suggestedPauseText}.",
                $"Overenie zopakujte s pokojnejším tempom a pauzou {suggestedPauseText}.",
                $"Po úprave si nechajte dlhší odstup medzi meraniami, približne {suggestedPauseText}.",
                $"AI odporúča po zásahu ešte jedno kolo s pauzou {suggestedPauseText}.",
                $"Po korekcii profil skontrolujte znova s intervalom {suggestedPauseText}."
            },
            _ => new[]
            {
                $"Celé overenie zopakujte s dlhším intervalom pauzy približne {suggestedPauseText}.",
                $"Po zásahu prejdite celé meranie znovu a medzi pokusmi nechajte pauzu {suggestedPauseText}.",
                $"AI odporúča nový kompletný prebeh s pauzou {suggestedPauseText} medzi krokmi overenia.",
                $"Profil potvrďte až po novom kole meraní s intervalom {suggestedPauseText}.",
                $"Bez opakovania celého overenia s pauzou {suggestedPauseText} profil zatiaľ neukladajte.",
                $"Po výraznejšej korekcii spravte kompletný re-test s pauzou {suggestedPauseText}."
            }
        };

    private static IReadOnlyList<string> GetCvLeadVariants(AiDiagnosticSeverity severity)
        => severity switch
        {
            AiDiagnosticSeverity.Ok => new[]
            {
                "CV zásah momentálne nie je nutný.",
                "Mapu CV netreba meniť agresívne.",
                "AI teraz nežiada výraznú úpravu CV.",
                "Profil je dostatočne dobrý aj bez ďalších zásahov do CV.",
                "Najlepšia voľba je zatiaľ ponechať CV bez veľkej zmeny.",
                "Skôr kontrolujte stav mechaniky než aby ste prepisovali CV."
            },
            AiDiagnosticSeverity.Warning => new[]
            {
                "Odporúčané úpravy CV: spravte len malý cielený zásah.",
                "Odporúčané úpravy CV: jemná korekcia by mala stačiť.",
                "Odporúčané úpravy CV: dolaďujte len problémovú časť profilu.",
                "Odporúčané úpravy CV: postačí menší zásah bez resetu celej mapy.",
                "Odporúčané úpravy CV: upravte len to, čo súvisí s hlavným symptómom.",
                "Odporúčané úpravy CV: držte sa skôr jemných krokov než veľkých skokov."
            },
            _ => new[]
            {
                "Odporúčané úpravy CV: potrebný je výraznejší a presnejší zásah.",
                "Odporúčané úpravy CV: bez úpravy kritickej časti profilu sa výsledok nezlepší.",
                "Odporúčané úpravy CV: problémová oblasť si pýta citeľnejšiu korekciu.",
                "Odporúčané úpravy CV: nestačí kozmetická zmena, treba cielene zasiahnuť.",
                "Odporúčané úpravy CV: odporúčam upraviť hlavný zdroj odchýlky, nie len okraje profilu.",
                "Odporúčané úpravy CV: sústreďte sa na korekciu jadra problému."
            }
        };

    private static IReadOnlyList<string> GetCvAdjustmentVariants(AiDiagnosticProblemType problemType,
        AiDiagnosticCauseType causeType)
        => causeType switch
        {
            AiDiagnosticCauseType.Stable => new[]
            {
                "Stačí bežná kontrola čistoty kolies, odberu prúdu a voľného chodu prevodov.",
                "Ako prevenciu len skontrolujte čistotu kolies a mechaniku bez ďalšieho prepisu mapy.",
                "Ak zostane jazda tichá a plynulá, ďalšie CV zásahy odložte.",
                "Zamerajte sa skôr na údržbu než na ďalšie ladenie CV.",
                "Skontrolujte len bežné veci okolo kontaktov a mechaniky.",
                "Preventívna kontrola mechaniky bude teraz užitočnejšia než zmena CV."
            },
            AiDiagnosticCauseType.MechanicalResistance => new[]
            {
                "Najprv preverte mechanický odpor v spriahadlách, prevodoch a ložiskách; až potom dolaďujte CV3 alebo CV4.",
                "Skontrolujte trenie pohonu a ak treba, až následne jemne upravte CV pre prechod medzi smermi.",
                "Najväčší efekt prinesie odstránenie mechanického odporu a až potom malé doladenie prechodových CV.",
                "Preverte, či súpravu nebrzdí mechanika, a až potom riešte jemnú korekciu CV pre asymetriu smerov.",
                "Bez kontroly mechaniky by úprava CV mohla len maskovať mechanický odpor.",
                "Najprv riešte trenie a odpor, potom až dolaďujte CV mapu podľa nového merania."
            },
            AiDiagnosticCauseType.StartupCvTuning => new[]
            {
                "Znížte alebo jemne upravte CV2 a podľa potreby dolaďte prechod cez CV3.",
                "Sústreďte sa na CV2, prípadne malú korekciu CV3 pre pokojnejší rozjazd.",
                "Najprv doladte rozjazd cez CV2 a len jemne upravte nasledovné kroky cez CV3.",
                "Skúste malý zásah do CV2 a sledujte, či sa rozjazd v nízkych krokoch upokojí.",
                "Rozjazdové CV upravujte po malých krokoch, aby sa nízke kroky neprehupli opačným smerom.",
                "Najväčší efekt by mala priniesť jemná práca s CV2 a s prechodom do prvých stupňov."
            },
            AiDiagnosticCauseType.MidBandSmoothing => new[]
            {
                "Dolaďte vyhladenie stredného pásma cez CV3 alebo CV4 a sledujte plynulosť prechodu medzi bodmi.",
                "Upravte strednú časť krivky jemným zásahom do CV3/CV4 namiesto zásahu do maxima.",
                "Najväčší prínos prinesie hladší priebeh stredného pásma a pokojnejší prechod medzi bodmi.",
                "Skúste jemne vyhladiť strednú časť mapy CV a až potom znovu merať.",
                "Ak ostáva problém v strede, dolaďujte stredné pásmo a nie rozjazd ani CV5.",
                "Stredná časť krivky si pýta vyhladenie skôr než zvýšenie alebo zníženie maxima."
            },
            AiDiagnosticCauseType.TopCurveInstability => new[]
            {
                "Najväčší zmysel má zásah do vrcholu krivky a do CV5.",
                "AI odporúča stabilizovať hornú časť krivky skôr než meniť rozjazd.",
                "Skúste znížiť agresivitu vrcholu krivky cez CV5 a potom znovu overte vysokú rýchlosť.",
                "Horná časť profilu dolaďte cez CV5 tak, aby vysoká rýchlosť prestala kolísať.",
                "Najväčší efekt má jemná korekcia CV5 a stabilizácia vrcholu krivky.",
                "Vysokú rýchlosť dolaďte po malých krokoch cez CV5 a priebežne overujte."
            },
            _ => new[]
            {
                "Porovnajte oba smery oddelene a dolaďujte len ten smer, ktorý je citeľne slabší.",
                "Ak problém vzniká iba v jednom smere, nemeňte naslepo celý profil, ale zamerajte sa na slabší smer.",
                "Najprv potvrďte, ktorý smer je horší, a potom dolaďujte CV podľa neho.",
                "Pri jednostrannom probléme je lepšie cieliť na slabší smer než prepisovať obidva rovnako.",
                "Dolaďujte a kontrolujte najmä smer, ktorý zaostáva alebo pôsobí ťažšie.",
                "Jednostranný problém si pýta oddelený pohľad na dopredu a spätný smer."
            }
        };

    private static IReadOnlyList<string> GetCvValidationVariants(
        AiDiagnosticProblemType problemType,
        AiDiagnosticCauseType causeType,
        AiDiagnosticSeverity severity)
    {
        if (severity == AiDiagnosticSeverity.Ok)
        {
            return new[]
            {
                "Pred finálnym uložením už stačí len bežná kontrola spriahadiel a kontaktov.",
                "Po overovacej jazde môžete profil ponechať bez ďalších zmien.",
                "Ak sa správanie zopakuje aj na koľajisku, profil je pripravený na uloženie.",
                "Na záver potvrďte len stabilitu jazdy pri reálnom zaťažení.",
                "Po tejto kontrole už ďalšie zásahy pravdepodobne nebudú potrebné.",
                "Finálne uloženie má zmysel po krátkej praktickej skúške."
            };
        }

        var problemHint = problemType switch
        {
            AiDiagnosticProblemType.LowSteps => "nízkych krokoch a rozjazde",
            AiDiagnosticProblemType.MidBand => "strednom pásme",
            AiDiagnosticProblemType.HighSpeed => "hornej časti krivky a vo vysokej rýchlosti",
            AiDiagnosticProblemType.DirectionAsymmetry => "porovnaní oboch smerov",
            _ => "kontrolnom meraní"
        };

        var causeHint = causeType switch
        {
            AiDiagnosticCauseType.MechanicalResistance => "mechanický odpor spriahadiel a pohonu",
            AiDiagnosticCauseType.StartupCvTuning => "správanie CV2 a prechod rozjazdu",
            AiDiagnosticCauseType.MidBandSmoothing => "vyhladenie stredného pásma",
            AiDiagnosticCauseType.TopCurveInstability => "stabilitu CV5 a vysokej rýchlosti",
            AiDiagnosticCauseType.SingleDirectionIssue => "slabší smer jazdy",
            _ => "celkovú stabilitu profilu"
        };

        return new[]
        {
            $"Po úprave znovu skontrolujte správanie v {problemHint} a potvrďte {causeHint}.",
            $"Pred uložením ešte raz overte profil v {problemHint} a sledujte {causeHint}.",
            $"Následná jazda má potvrdiť zlepšenie v {problemHint}; zároveň sledujte {causeHint}.",
            $"AI odporúča finálnu kontrolu v {problemHint} so zameraním na {causeHint}.",
            $"Pred finálnym uložením ešte raz prejdite meranie v {problemHint} a skontrolujte {causeHint}.",
            $"Po korekcii sa zamerajte na {problemHint} a potvrďte, že sa upokojilo aj {causeHint}."
        };
    }

    private static string SelectRandom(IReadOnlyList<string> options, bool randomizeSelections)
    {
        if (options.Count == 0)
            return string.Empty;

        return randomizeSelections ? options[Random.Shared.Next(options.Count)] : options[0];
    }

    private static double ComputeStoppingDistance(double speedKmh, double frictionFactor)
        => Math.Round((speedKmh * speedKmh) / 32.0 * frictionFactor, 1);

    private static List<AxisLabelViewModel> BuildHorizontalAxisLabels(int maxStep, double plotOffset, double plotLength,
        double top)
    {
        var items = new List<AxisLabelViewModel>();
        var plotRight = plotOffset + plotLength;

        foreach (var value in BuildStepAxisValues(maxStep))
        {
            var ratio = GetChartStepRatio(value, maxStep);
            var text = value.ToString(CultureInfo.InvariantCulture);
            items.Add(new AxisLabelViewModel(
                CalculateHorizontalAxisLabelLeft(plotOffset + (ratio * plotLength), text, plotRight),
                top, text));
        }

        return items;
    }

    private static List<ChartGridLineViewModel> BuildVerticalGridLines(int maxStep, double left, double top,
        double width, double height)
    {
        return BuildStepAxisValues(maxStep)
            .Where(value => value > 0 && value < maxStep)
            .Select(value =>
            {
                var x = left + GetChartStepRatio(value, maxStep) * width;
                return new ChartGridLineViewModel(new Point(x, top), new Point(x, top + height));
            })
            .ToList();
    }

    private static List<ChartGridLineViewModel> BuildXAxisTickMarks(int maxStep, double left, double top, double width,
        double height)
    {
        var baselineY = top + height;
        const double tickHeight = 8;

        return BuildStepAxisValues(maxStep)
            .Select(value =>
            {
                var x = left + GetChartStepRatio(value, maxStep) * width;
                return new ChartGridLineViewModel(new Point(x, baselineY), new Point(x, baselineY + tickHeight));
            })
            .ToList();
    }

    private static List<int> BuildStepAxisValues(int maxStep)
    {
        maxStep = Math.Max(1, maxStep);

        if (maxStep <= 14)
            return Enumerable.Range(0, maxStep + 1).ToList();

        // Pre rozsah 0-127 (DCC kroky) vyznač presne 7 rovnomerne rozložených hodnôt
        if (maxStep > 28)
        {
            const int TickCount = 7;
            var ticks = Enumerable.Range(0, TickCount)
                .Select(i => (int)Math.Round(i * (double)maxStep / (TickCount - 1)))
                .Distinct()
                .ToList();
            if (ticks[^1] != maxStep)
                ticks.Add(maxStep);
            return ticks;
        }

        var values = new List<int>();
        var majorStep = ChooseDecoderStepAxisMajorStep(maxStep);
        for (var value = 0; value < maxStep; value += majorStep)
        {
            if (values.Count == 0 || values[^1] != value)
                values.Add(value);
        }

        if (values[^1] != maxStep)
            values.Add(maxStep);

        return values;
    }

    private static int ChooseDecoderStepAxisMajorStep(int maxStep)
        => maxStep <= 28 ? 4 : 14;

    private static List<AxisLabelViewModel> BuildVerticalAxisLabels(int maxSpeed, double plotOffset, double plotLength)
    {
        var items = new List<AxisLabelViewModel>();
        foreach (var value in BuildSpeedAxisValues(maxSpeed))
        {
            var ratio = value / (double)Math.Max(1, maxSpeed);
            var text = value.ToString(CultureInfo.InvariantCulture);
            // Vycentruj text okolo čiary (-8), ale nikdy nedovoľ aby presiahol nad vrch plochy grafu.
            var top = Math.Max(plotOffset, (plotOffset + plotLength - (ratio * plotLength)) - 8);
            items.Add(new AxisLabelViewModel(CalculateVerticalAxisLabelLeft(text), top, text));
        }

        return items;
    }

    private static List<ChartGridLineViewModel> BuildHorizontalGridLines(int maxSpeed, double left, double top,
        double width, double height)
    {
        return BuildSpeedAxisValues(maxSpeed)
            .Where(value => value > 0)
            .Select(value =>
            {
                var ratio = value / (double)Math.Max(1, maxSpeed);
                var y = top + height - (ratio * height);
                return new ChartGridLineViewModel(new Point(left, y), new Point(left + width, y));
            })
            .ToList();
    }

    private static List<int> BuildSpeedAxisValues(int maxSpeed)
    {
        maxSpeed = Math.Max(1, maxSpeed);
        var step = ChooseSpeedAxisStep(maxSpeed);
        var values = new List<int>();
        for (var value = 0; value < maxSpeed; value += step)
            values.Add(value);

        if (values.Count == 0 || values[^1] != maxSpeed)
            values.Add(maxSpeed);

        return values;
    }

    private static int ChooseSpeedAxisStep(int maxSpeed)
        => maxSpeed <= 60 ? 10 : 20;

    private static double CalculateVerticalAxisLabelLeft(string text)
        => -4;

    private IEnumerable<CurveMarkerViewModel> BuildCurveMarkers(
        IEnumerable<CalibrationMeasurementPointViewModel> points, string accent, bool isBackward, int maxStep,
        double maxSpeed)
    {
        foreach (var point in points)
        {
            var x = ChartLeft + GetChartStepRatio(point.Step, maxStep) * ChartWidth;
            var y = ChartTop + ChartHeight -
                    (Math.Clamp(point.CalculatedSpeedKmh, 0, maxSpeed) / Math.Max(1, maxSpeed)) * ChartHeight;
            var directionLabel = isBackward ? "Dozadu" : "Dopredu";
            var label = point.IsManual
                ? $"{directionLabel} • manuálny\nKrok {point.Step} • {point.CalculatedSpeedKmh:0.0} km/h"
                : $"{directionLabel}\nKrok {point.Step} • {point.CalculatedSpeedKmh:0.0} km/h";
            var caption = $"{point.CalculatedSpeedKmh:0.0} km/h";
            var fill = "#FFFFFF";
            var stroke = point.IsManual ? "#F59E0B" : accent;
            var captionForeground = point.IsManual ? "#9A6700" : "#0F172A";
            var size = point.IsManual ? 16 : 14;
            yield return new CurveMarkerViewModel(
                x - (size / 2),
                y - (size / 2),
                label,
                caption,
                fill,
                stroke,
                captionForeground,
                size,
                size,
                isBackward ? -10 : -12,
                isBackward ? 18 : -22);
        }
    }

    private CalibrationMeasurementPointViewModel CreateManualChartPoint(int step, double speed)
        => CreatePoint(step, GetActiveDirectionDisplayName(), EstimateTimeSeconds(speed), speed, speed,
            "Manuálne zadané", isManual: true);

    private static bool IsManualStatus(string? status)
        => !string.IsNullOrWhiteSpace(status) && status.Contains("manu", StringComparison.CurrentCultureIgnoreCase);

    private static double EstimateTimeSeconds(double calculatedSpeedKmh)
        => Math.Round(Math.Max(0.5, 8.4 - (Math.Clamp(calculatedSpeedKmh, 0, DefaultChartMaxSpeed) / 18.0)), 1);

    private bool TryMapCanvasToChart(Point position, out int step, out double speed)
    {
        step = 0;
        speed = 0;

        if (position.X < ChartLeft || position.X > ChartLeft + ChartWidth || position.Y < ChartTop ||
            position.Y > ChartTop + ChartHeight)
            return false;

        var xRatio = Math.Clamp((position.X - ChartLeft) / ChartWidth, 0, 1);
        var yRatio = 1 - Math.Clamp((position.Y - ChartTop) / ChartHeight, 0, 1);
        step = (int)Math.Round(xRatio * CurrentChartMaxStep, MidpointRounding.AwayFromZero);
        speed = Math.Round(Math.Clamp(yRatio * CurrentChartMaxSpeed, 0, CurrentChartMaxSpeed), 1);
        return true;
    }

    private CalibrationMeasurementPointViewModel? FindNearestPoint(
        IEnumerable<CalibrationMeasurementPointViewModel> points, Point canvasPosition)
    {
        CalibrationMeasurementPointViewModel? candidate = null;
        var bestDistance = MarkerHitRadius;

        foreach (var point in points)
        {
            var x = ChartLeft + GetChartStepRatio(point.Step, CurrentChartMaxStep) * ChartWidth;
            var y = ChartTop + ChartHeight -
                    (Math.Clamp(point.CalculatedSpeedKmh, 0, CurrentChartMaxSpeed) /
                     Math.Max(1, CurrentChartMaxSpeed)) * ChartHeight;
            var distance = Math.Sqrt(Math.Pow(canvasPosition.X - x, 2) + Math.Pow(canvasPosition.Y - y, 2));
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                candidate = point;
            }
        }

        return candidate;
    }

    private void ApplyChartPointEdit(CalibrationMeasurementPointViewModel point, int step, double speed,
        bool updateStatus, bool persistChanges, bool markProjectDirty, bool sortCollection)
    {
        _isUpdatingMeasurementPoint = true;
        try
        {
            point.Step = Math.Clamp(step, 0, CurrentChartMaxStep);
            point.CalculatedSpeedKmh = Math.Round(Math.Clamp(speed, 0, CurrentChartMaxSpeed), 1);
            point.CalculatedSpeedKmh = point.CalculatedSpeedKmh;
            point.TimeSeconds = EstimateTimeSeconds(point.CalculatedSpeedKmh);
            point.Status = "Manuálne zadané";
            point.IsManual = true;
        }
        finally
        {
            _isUpdatingMeasurementPoint = false;
        }

        if (sortCollection)
            SortMeasurements();

        if (persistChanges)
            PersistMeasurementsToLocomotive(SelectedLocomotive?.Source);
        if (markProjectDirty)
            MarkProfileDirty?.Invoke();

        UpdateDerivedState();
        CurrentSpeedKmh = point.CalculatedSpeedKmh;

        if (updateStatus)
            UpdateCalibrationStatus($"Bod kroku {point.Step} bol nastavený na {point.CalculatedSpeedKmh:0.0} km/h.");
    }

    private void FinalizeMeasurementEdit(CalibrationMeasurementPointViewModel point,
        ObservableCollection<CalibrationMeasurementPointViewModel> collection, string statusMessage)
    {
        EnsureUniqueStep(point, collection);
        SortMeasurements();
        PersistMeasurementsToLocomotive(SelectedLocomotive?.Source);
        MarkProfileDirty?.Invoke();
        UpdateDerivedState();
        SelectMeasurementPoint(point);
        UpdateCalibrationStatus(statusMessage);
    }

    private void EnsureUniqueStep(CalibrationMeasurementPointViewModel point,
        ObservableCollection<CalibrationMeasurementPointViewModel> collection)
    {
        var duplicate = collection.FirstOrDefault(item => !ReferenceEquals(item, point) && item.Step == point.Step);
        if (duplicate == null)
            return;

        UnsubscribeFromMeasurementPoint(duplicate);
        collection.Remove(duplicate);
    }

    private void SelectMeasurementPoint(CalibrationMeasurementPointViewModel point)
    {
        if (ForwardMeasurementPoints.Contains(point))
        {
            SelectedForwardMeasurementPoint =
                ForwardMeasurementPoints.FirstOrDefault(item =>
                    ReferenceEquals(item, point) || item.Step == point.Step);
            return;
        }

        if (BackwardMeasurementPoints.Contains(point))
            SelectedBackwardMeasurementPoint =
                BackwardMeasurementPoints.FirstOrDefault(item =>
                    ReferenceEquals(item, point) || item.Step == point.Step);
    }

    private void SubscribeToMeasurementPoint(CalibrationMeasurementPointViewModel point)
    {
        point.PropertyChanged -= OnMeasurementPointPropertyChanged;
        point.PropertyChanged += OnMeasurementPointPropertyChanged;
    }

    private void UnsubscribeFromMeasurementPoint(CalibrationMeasurementPointViewModel point)
    {
        point.PropertyChanged -= OnMeasurementPointPropertyChanged;
    }

    private void ReplaceMeasurementCollection(ObservableCollection<CalibrationMeasurementPointViewModel> target,
        IEnumerable<CalibrationMeasurementPointViewModel> items)
    {
        foreach (var point in target.ToList())
            UnsubscribeFromMeasurementPoint(point);

        target.Clear();
        foreach (var item in items)
        {
            SubscribeToMeasurementPoint(item);
            target.Add(item);
        }
    }

    private void OnMeasurementPointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdatingMeasurementPoint || sender is not CalibrationMeasurementPointViewModel point)
            return;

        if (e.PropertyName is not nameof(CalibrationMeasurementPointViewModel.Step)
            and not nameof(CalibrationMeasurementPointViewModel.CalculatedSpeedKmh))
            return;

        var collection = ForwardMeasurementPoints.Contains(point)
            ? ForwardMeasurementPoints
            : BackwardMeasurementPoints;
        ApplyChartPointEdit(point, point.Step, point.CalculatedSpeedKmh, updateStatus: false, persistChanges: false,
            markProjectDirty: false, sortCollection: false);
        FinalizeMeasurementEdit(point, collection, $"Tabuľková hodnota pre krok {point.Step} bola aktualizovaná.");
    }

    private void SyncSpeedProfileRowsFromMeasurements()
    {
        var selectedRow = SelectedSpeedProfileRow;
        var selectedStep = selectedRow?.Step;
        var selectedWasForwardRow = selectedRow != null && ForwardSpeedProfileRows.Contains(selectedRow);
        var selectedWasBackwardRow = selectedRow != null && BackwardSpeedProfileRows.Contains(selectedRow);

        var rows = (from step in ForwardMeasurementPoints.Select(point => point.Step)
                .Concat(BackwardMeasurementPoints.Select(point => point.Step)).Distinct().OrderBy(step => step)
            let forward = ForwardMeasurementPoints.FirstOrDefault(point => point.Step == step)
            let backward = BackwardMeasurementPoints.FirstOrDefault(point => point.Step == step)
            select new SpeedProfileTableRowViewModel(
                step,
                forward?.CalculatedSpeedKmh ?? 0,
                backward?.CalculatedSpeedKmh ?? 0,
                forward?.Status ?? string.Empty,
                backward?.Status ?? string.Empty,
                forward?.IsManual ?? false,
                backward?.IsManual ?? false)).ToList();

        var forwardRows = ForwardMeasurementPoints
            .OrderBy(point => point.Step)
            .Select(point => new SpeedProfileTableRowViewModel(
                point.Step,
                point.CalculatedSpeedKmh,
                0,
                point.Status,
                string.Empty,
                point.IsManual,
                false))
            .ToList();

        var backwardRows = BackwardMeasurementPoints
            .OrderBy(point => point.Step)
            .Select(point => new SpeedProfileTableRowViewModel(
                point.Step,
                0,
                point.CalculatedSpeedKmh,
                string.Empty,
                point.Status,
                false,
                point.IsManual))
            .ToList();

        ReplaceSpeedProfileRows(rows);
        ReplaceSpeedProfileRows(ForwardSpeedProfileRows, forwardRows);
        ReplaceSpeedProfileRows(BackwardSpeedProfileRows, backwardRows);

        if (!selectedStep.HasValue)
        {
            SelectedSpeedProfileRow = null;
            return;
        }

        if (selectedWasForwardRow)
        {
            SelectedSpeedProfileRow = ForwardSpeedProfileRows.FirstOrDefault(row => row.Step == selectedStep.Value);
            return;
        }

        if (selectedWasBackwardRow)
        {
            SelectedSpeedProfileRow = BackwardSpeedProfileRows.FirstOrDefault(row => row.Step == selectedStep.Value);
            return;
        }

        SelectedSpeedProfileRow = SpeedProfileRows.FirstOrDefault(row => row.Step == selectedStep.Value);
    }

    private void ReplaceSpeedProfileRows(IEnumerable<SpeedProfileTableRowViewModel> rows)
        => ReplaceSpeedProfileRows(SpeedProfileRows, rows);

    private void ReplaceSpeedProfileRows(ObservableCollection<SpeedProfileTableRowViewModel> target,
        IEnumerable<SpeedProfileTableRowViewModel> rows)
    {
        foreach (var row in target.ToList())
            UnsubscribeFromSpeedProfileRow(row);

        _isUpdatingSpeedProfileRow = true;
        try
        {
            target.Clear();
            foreach (var row in rows)
            {
                SubscribeToSpeedProfileRow(row);
                target.Add(row);
            }
        }
        finally
        {
            _isUpdatingSpeedProfileRow = false;
        }
    }

    private void SubscribeToSpeedProfileRow(SpeedProfileTableRowViewModel row)
    {
        row.PropertyChanged -= OnSpeedProfileRowPropertyChanged;
        row.PropertyChanged += OnSpeedProfileRowPropertyChanged;
    }

    private void UnsubscribeFromSpeedProfileRow(SpeedProfileTableRowViewModel row)
    {
        row.PropertyChanged -= OnSpeedProfileRowPropertyChanged;
    }

    private void OnSpeedProfileRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdatingSpeedProfileRow || sender is not SpeedProfileTableRowViewModel row)
            return;

        if (e.PropertyName is nameof(SpeedProfileTableRowViewModel.FwdRawSpeed)
            or nameof(SpeedProfileTableRowViewModel.BwdRawSpeed))
        {
            UpdateMeasurementPointFromRow(row, e.PropertyName == nameof(SpeedProfileTableRowViewModel.FwdRawSpeed));
        }
    }

    private void UpdateMeasurementPointFromRow(SpeedProfileTableRowViewModel row, bool isForward)
    {
        var collection = isForward ? ForwardMeasurementPoints : BackwardMeasurementPoints;
        var speed = isForward ? row.FwdRawSpeed : row.BwdRawSpeed;
        var direction = isForward ? "Dopredu" : "Dozadu";
        var point = collection.FirstOrDefault(item => item.Step == row.Step);
        var status = isForward ? row.FwdStatus : row.BwdStatus;
        var isManual = isForward ? row.FwdIsManual : row.BwdIsManual;

        if (point == null)
        {
            point = CreatePoint(row.Step, direction, EstimateTimeSeconds(speed), speed, speed,
                string.IsNullOrWhiteSpace(status) ? "Tabuľková hodnota" : status,
                isManual: isManual || IsManualStatus(status));
            UpsertMeasurementPoint(collection, point);
            SubscribeToMeasurementPoint(point);
        }

        point.Status = string.IsNullOrWhiteSpace(status) ? point.Status : status;
        point.IsManual = isManual || IsManualStatus(point.Status);

        _isUpdatingMeasurementPoint = true;
        try
        {
            point.Step = Math.Clamp(row.Step, 0, CurrentChartMaxStep);
            point.CalculatedSpeedKmh = Math.Round(Math.Clamp(speed, 0, CurrentChartMaxSpeed), 1);
            point.CalculatedSpeedKmh = point.CalculatedSpeedKmh;
            point.TimeSeconds = EstimateTimeSeconds(point.CalculatedSpeedKmh);
        }
        finally
        {
            _isUpdatingMeasurementPoint = false;
        }

        FinalizeMeasurementEdit(point, collection, $"Tabuľková hodnota pre krok {row.Step} bola aktualizovaná.");
    }

    private static IEnumerable<ReferenceGuideViewModel> BuildReferenceGuides(
        IReadOnlyList<CalibrationMeasurementPointViewModel> forwardPoints,
        IReadOnlyList<CalibrationMeasurementPointViewModel> backwardPoints, int maxStep)
    {
        foreach (var point in forwardPoints.Where(point => point.Step is 10 or 60 or 90))
        {
            var x = ChartLeft + GetChartStepRatio(point.Step, maxStep) * ChartWidth;
            yield return new ReferenceGuideViewModel(x, point.Step == 90 ? "predicted max" : null);
        }

        var backwardPredicted = backwardPoints.OrderByDescending(point => point.Step).FirstOrDefault();
        if (backwardPredicted != null)
        {
            var x = ChartLeft + GetChartStepRatio(backwardPredicted.Step, maxStep) * ChartWidth;
            yield return new ReferenceGuideViewModel(x, null);
        }
    }

    private static string BuildCurvePath(IEnumerable<CalibrationMeasurementPointViewModel> points, double left,
        double top, double width, double height, double maxSpeed, int maxStep)
    {
        var ordered = points.OrderBy(point => point.Step).ToList();
        if (ordered.Count == 0)
            return string.Empty;

        var pathPoints = new List<(int Step, double Speed)>();
        if (ordered[0].Step > 0 || ordered[0].CalculatedSpeedKmh > 0.001)
            pathPoints.Add((0, 0));

        pathPoints.AddRange(ordered.Select(point => (point.Step, point.CalculatedSpeedKmh)));

        var sb = new StringBuilder();
        for (var index = 0; index < pathPoints.Count; index++)
        {
            var point = pathPoints[index];
            var x = left + GetChartStepRatio(point.Step, maxStep) * width;
            var y = top + height - (point.Speed / maxSpeed) * height;
            sb.Append(index == 0 ? "M " : " L ")
                .Append(Format(x))
                .Append(' ')
                .Append(Format(y));
        }

        return sb.ToString();
    }

    private static string BuildVarianceAreaPath(IReadOnlyList<CalibrationMeasurementPointViewModel> forwardPoints,
        IReadOnlyList<CalibrationMeasurementPointViewModel> backwardPoints, double left, double top, double width,
        double height, double maxSpeed, int maxStep)
    {
        if (forwardPoints.Count == 0 || backwardPoints.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        var forward = forwardPoints.OrderBy(point => point.Step).ToList();
        var backward = backwardPoints.OrderBy(point => point.Step).Reverse().ToList();

        for (var index = 0; index < forward.Count; index++)
        {
            var point = forward[index];
            var x = left + GetChartStepRatio(point.Step, maxStep) * width;
            var y = top + height - (point.CalculatedSpeedKmh / maxSpeed) * height;
            sb.Append(index == 0 ? "M " : " L ")
                .Append(Format(x))
                .Append(' ')
                .Append(Format(y));
        }

        foreach (var point in backward)
        {
            var x = left + GetChartStepRatio(point.Step, maxStep) * width;
            var y = top + height - (point.CalculatedSpeedKmh / maxSpeed) * height;
            sb.Append(" L ")
                .Append(Format(x))
                .Append(' ')
                .Append(Format(y));
        }

        sb.Append(" Z");
        return sb.ToString();
    }

    private static string BuildMarkerPath(IEnumerable<CalibrationMeasurementPointViewModel> points, double radius,
        int maxStep, double maxSpeed)
    {
        var ordered = points.OrderBy(point => point.Step).ToList();
        if (ordered.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var point in ordered)
        {
            var x = ChartLeft + GetChartStepRatio(point.Step, maxStep) * ChartWidth;
            var y = ChartTop + ChartHeight -
                    (Math.Clamp(point.CalculatedSpeedKmh, 0, maxSpeed) / Math.Max(1, maxSpeed)) * ChartHeight;
            sb.Append("M ")
                .Append(Format(x - radius))
                .Append(' ')
                .Append(Format(y))
                .Append(" A ")
                .Append(Format(radius))
                .Append(' ')
                .Append(Format(radius))
                .Append(" 0 1 0 ")
                .Append(Format(x + radius))
                .Append(' ')
                .Append(Format(y))
                .Append(" A ")
                .Append(Format(radius))
                .Append(' ')
                .Append(Format(radius))
                .Append(" 0 1 0 ")
                .Append(Format(x - radius))
                .Append(' ')
                .Append(Format(y))
                .Append(" Z ");
        }

        return sb.ToString().Trim();
    }

    private static string BuildCurveFillPath(IEnumerable<CalibrationMeasurementPointViewModel> points, double left,
        double top, double width, double height, double maxSpeed, int maxStep)
    {
        var ordered = points.OrderBy(point => point.Step).ToList();
        if (ordered.Count == 0)
            return string.Empty;

        var pathPoints = new List<(int Step, double Speed)>();
        if (ordered[0].Step > 0 || ordered[0].CalculatedSpeedKmh > 0.001)
            pathPoints.Add((0, 0));

        pathPoints.AddRange(ordered.Select(point => (point.Step, point.CalculatedSpeedKmh)));

        var baselineY = top + height;
        var firstX = left + GetChartStepRatio(pathPoints[0].Step, maxStep) * width;
        var lastX = left + GetChartStepRatio(pathPoints[^1].Step, maxStep) * width;

        var sb = new StringBuilder();
        sb.Append("M ")
            .Append(Format(firstX))
            .Append(' ')
            .Append(Format(baselineY));

        foreach (var point in pathPoints)
        {
            var x = left + GetChartStepRatio(point.Step, maxStep) * width;
            var y = top + height - (point.Speed / maxSpeed) * height;
            sb.Append(" L ")
                .Append(Format(x))
                .Append(' ')
                .Append(Format(y));
        }

        sb.Append(" L ")
            .Append(Format(lastX))
            .Append(' ')
            .Append(Format(baselineY))
            .Append(" Z");

        return sb.ToString();
    }

    private static double GetChartStepRatio(int step, int maxStep)
        => Math.Clamp(step, 0, Math.Max(1, maxStep)) / (double)Math.Max(1, maxStep);

    /// <summary>
    /// Vypočíta gradientovú čiaru kolmú na priebeh krivky tak, aby iso-farebné pásy
    /// kopírovali uhol stúpania krivky. StartPoint leží na začiatku krivky pri X-osi
    /// (najsýtejšia farba), EndPoint smeruje kolmo od krivky smerom nadol/doprava
    /// (priehľadná farba) a dosiahne pravý dolný roh výplne.
    /// </summary>
    private static (RelativePoint Start, RelativePoint End) ComputePerpendicularGradient(
        IEnumerable<CalibrationMeasurementPointViewModel> points,
        double left, double top, double width, double height,
        double maxSpeed, int maxStep)
    {
        var fallbackStart = new RelativePoint(0, 0, RelativeUnit.Relative);
        var fallbackEnd = new RelativePoint(1, 1, RelativeUnit.Relative);

        var ordered = points.OrderBy(point => point.Step).ToList();
        if (ordered.Count == 0)
            return (fallbackStart, fallbackEnd);

        // Path začína buď na (0,0) alebo na prvom meranom bode (zhodne s BuildCurveFillPath).
        var firstStep = ordered[0].Step > 0 || ordered[0].CalculatedSpeedKmh > 0.001 ? 0 : ordered[0].Step;
        var firstX = left + GetChartStepRatio(firstStep, maxStep) * width;
        var firstY = top + height; // baseline (X-os)

        var last = ordered[^1];
        var lastX = left + GetChartStepRatio(last.Step, maxStep) * width;
        var lastY = top + height - (Math.Clamp(last.CalculatedSpeedKmh, 0, maxSpeed) / Math.Max(1, maxSpeed)) * height;

        var dx = lastX - firstX;
        var dy = lastY - firstY; // záporné, lebo Y rastie nadol
        var lenSquared = dx * dx + dy * dy;
        if (lenSquared < 1)
            return (fallbackStart, fallbackEnd);

        // Smer pozdĺž krivky: d = (dx, dy). Kolmica nasmerovaná do výplne (vpravo dole): n = (-dy, dx).
        // Gradientová čiara: Start = (firstX, firstY) na krivke (pretína X-os),
        // End = projekcia pravého dolného rohu výplne (lastX, baselineY) na kolmicu zo Startu.
        // Vzdialenosť po kolmici k tomuto bodu = dx * (-dy) / |d|.
        // EndPoint = Start + n_unit * vzdialenosť = (firstX + dx*dy^2 / |d|^2,  firstY - dx^2*dy / |d|^2).
        var endX = firstX + dx * dy * dy / lenSquared;
        var endY = firstY - dx * dx * dy / lenSquared;

        return (new RelativePoint(firstX, firstY, RelativeUnit.Absolute),
            new RelativePoint(endX, endY, RelativeUnit.Absolute));
    }

    private static double CalculateHorizontalAxisLabelLeft(double axisX, string text, double plotRight = double.MaxValue)
    {
        var centeredLeft = axisX - (text.Length switch
        {
            1 => 4,
            2 => 10,
            _ => 16
        });
        var labelWidth = text.Length switch { 1 => 8, 2 => 20, _ => 32 };
        return Math.Min(centeredLeft, plotRight - labelWidth);
    }

    private static int CalculateQuarterStepLabel(int maxStep)
        => (int)Math.Round(Math.Max(1, maxStep) / 4.0, MidpointRounding.AwayFromZero);

    private static int CalculateThreeQuarterStepLabel(int maxStep)
        => (int)Math.Round(Math.Max(1, maxStep) * 3.0 / 4.0, MidpointRounding.AwayFromZero);

    private static double CalculateMechanicalChartY(double valuePercent, double axisMaximumPercent)
    {
        var resolvedMaximum = Math.Max(1.0, axisMaximumPercent);
        var clampedRatio = Math.Clamp(valuePercent / resolvedMaximum, 0, 1);
        return PerformanceChartTop + PerformanceChartHeight - clampedRatio * PerformanceChartHeight;
    }

    private static double CalculateMechanicalChartBottom()
        => PerformanceChartTop + PerformanceChartHeight;

    private static string FormatMechanicalAxisLabel(double value)
    {
        if (Math.Abs(value - Math.Round(value)) < 0.001)
            return Math.Round(value).ToString("0", CultureInfo.CurrentCulture);

        return value.ToString(value >= 10 ? "0.#" : "0.0", CultureInfo.CurrentCulture);
    }

    private static double CalculateDirectionAsymmetryPercent(double forwardSpeed, double backwardSpeed,
        double referenceMaxSpeed)
    {
        // Percento asymetrie sa zámerne počíta voči maximálnej konštrukčnej rýchlosti lokomotívy
        // (Vmax). Pre modelára je to jediný zrozumiteľný a stabilný referenčný parameter, vďaka
        // ktorému nízke rýchlostné stupne neprodukujú vizuálne extrémy.
        var reference = Math.Max(1.0, referenceMaxSpeed);
        return Math.Clamp(Math.Abs(forwardSpeed - backwardSpeed) / reference * 100.0, 0, 100);
    }

    private static IReadOnlyList<MechanicalHealthSample> BuildMechanicalHealthSamples(
        IReadOnlyList<DiagnosticDifferenceSample> matches, int referenceMaxSpeed)
    {
        if (matches.Count == 0)
            return Array.Empty<MechanicalHealthSample>();

        return matches
            .Select(sample => new MechanicalHealthSample(
                sample.Step,
                sample.Difference,
                CalculateDirectionAsymmetryPercent(sample.Forward, sample.Backward, referenceMaxSpeed)))
            .ToList();
    }

    private static string BuildMechanicalHealthPath(IReadOnlyList<MechanicalHealthSample> samples, int maxStep,
        double axisMaximumPercent)
    {
        if (samples.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        for (var index = 0; index < samples.Count; index++)
        {
            var point = samples[index];
            var x = PerformanceChartLeft + GetChartStepRatio(point.Step, maxStep) * PerformanceChartWidth;
            var y = CalculateMechanicalChartY(point.DifferencePercent, axisMaximumPercent);
            sb.Append(index == 0 ? "M " : " L ")
                .Append(Format(x))
                .Append(' ')
                .Append(Format(y));
        }

        return sb.ToString();
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static CalibrationLocomotiveOption BuildLocomotiveOption(LocoRecord record)
    {
        var display = string.IsNullOrWhiteSpace(record.Number)
            ? record.Name
            : $"{record.Name} / {record.Number}";

        return new CalibrationLocomotiveOption(display, record, LoadThumbnail(record.IconName));
    }

    private static IImage? LoadThumbnail(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
            return null;

        try
        {
            if (IconRegistry.TryGet(iconName, out var fullPath) && File.Exists(fullPath))
                return new Bitmap(fullPath);
        }
        catch
        {
        }

        return null;
    }

    private static string NormalizeScale(string? scale)
    {
        if (string.IsNullOrWhiteSpace(scale))
            return "1:87 (H0)";

        if (scale.Contains("1:120", StringComparison.OrdinalIgnoreCase) ||
            scale.StartsWith("TT", StringComparison.OrdinalIgnoreCase))
            return "1:120 (TT)";
        if (scale.Contains("1:160", StringComparison.OrdinalIgnoreCase) ||
            scale.StartsWith("N", StringComparison.OrdinalIgnoreCase))
            return "1:160 (N)";
        if (scale.Contains("1:43", StringComparison.OrdinalIgnoreCase) ||
            scale.StartsWith("0", StringComparison.OrdinalIgnoreCase))
            return "1:43.5 (0)";

        return "1:87 (H0)";
    }

    private string ChooseNearestMaxSpeed(int maxSpeed)
    {
        var candidate = MaxModelSpeedOptions
            .Select(option => new { Option = option, Speed = ParseMaxSpeed(option) })
            .OrderBy(option => Math.Abs(option.Speed - maxSpeed))
            .ThenBy(option => option.Speed)
            .FirstOrDefault();

        return candidate?.Option ?? "120 km/h";
    }

    private static int ParseMaxSpeed(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 120;
    }


    private static string NormalizePauseSecondsText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder();
        var hasSeparator = false;
        var fractionalDigits = 0;

        foreach (var ch in value.Trim())
        {
            if (char.IsDigit(ch))
            {
                if (hasSeparator)
                {
                    if (fractionalDigits >= 1)
                        continue;

                    fractionalDigits++;
                }

                builder.Append(ch);
                continue;
            }

            if ((ch == '.' || ch == ',') && !hasSeparator)
            {
                if (builder.Length == 0)
                    builder.Append('0');

                builder.Append('.');
                hasSeparator = true;
            }
        }

        var normalized = builder.ToString();
        if (!TryParsePauseSeconds(normalized, out var parsed))
            return normalized;

        return FormatPauseSeconds(Math.Round(Math.Clamp(parsed, 0.0, 15.0), 1));
    }

    private static bool TryParsePauseSeconds(string? text, out double parsed)
        => double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out parsed);

    private static string FormatPauseSeconds(double value)
        => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string NormalizeRunoutDistanceText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder();
        var hasSeparator = false;
        var fractionalDigits = 0;

        foreach (var ch in value.Trim())
        {
            if (char.IsDigit(ch))
            {
                if (hasSeparator)
                {
                    if (fractionalDigits >= 2)
                        continue;

                    fractionalDigits++;
                }

                builder.Append(ch);
                continue;
            }

            if ((ch == '.' || ch == ',') && !hasSeparator)
            {
                if (builder.Length == 0)
                    builder.Append('0');

                builder.Append('.');
                hasSeparator = true;
            }
        }

        var normalized = builder.ToString();
        if (!TryParseRunoutDistance(normalized, out var parsed))
            return normalized;

        return FormatRunoutDistance(Math.Round(Math.Clamp(parsed, 0.0, 999.99), 2, MidpointRounding.AwayFromZero));
    }

    private static bool TryParseRunoutDistance(string? text, out double parsed)
        => double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out parsed);

    private static string FormatRunoutDistance(double value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    private (bool StartEnabled, bool MiddleEnabled, bool EndEnabled) GetBlockSelectionEnabledState()
    {
        var selectedIndex = SelectedMethod is null ? -1 : CalibrationMethods.IndexOf(SelectedMethod);
        return selectedIndex switch
        {
            // 1. metoda
            0 => (true, true, true),
            // 2. metoda
            1 => (true, false, true),
            // 3. metoda
            2 => (true, true, true),
            // 4. metoda
            3 => (true, false, true),
            // 5. metoda
            4 => (false, true, false),
            // 6. metoda
            5 => (true, false, false),
            // 7. metoda a fallback
            _ => (false, false, false)
        };
    }

    private void ClearDisabledBlockSelections()
    {
        var state = GetBlockSelectionEnabledState();

        if (!state.StartEnabled && _selectedStartBlock is not null)
            SelectedStartBlock = null;

        if (!state.MiddleEnabled && _selectedMiddleBlock is not null)
            SelectedMiddleBlock = null;

        if (!state.EndEnabled && _selectedEndBlock is not null)
            SelectedEndBlock = null;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}

public sealed class CalibrationLocomotiveOption : ReactiveObject
{
    public CalibrationLocomotiveOption(string displayName, LocoRecord? source, IImage? thumbnail)
    {
        DisplayName = displayName;
        Source = source;
        Thumbnail = thumbnail;
    }

    public string DisplayName { get; }
    public LocoRecord? Source { get; }
    public IImage? Thumbnail { get; }
}

public sealed class CalibrationMethodOption : ReactiveObject
{
    public CalibrationMethodOption(string displayName, string tooltip)
    {
        DisplayName = displayName;
        Tooltip = tooltip;
    }

    public string DisplayName { get; }
    public string Tooltip { get; }

    public override string ToString() => DisplayName;
}

public sealed class CalibrationIndicatorOption : ReactiveObject
{
    private static readonly ConcurrentDictionary<string, IImage?> IconCache = new(StringComparer.Ordinal);
    private readonly string _activeIconPath;
    private readonly string _inactiveIconPath;
    private bool _isActive;

    /// <summary>
    /// Hlavný konštruktor s oddelenými cestami ikony pre aktívny a neaktívny stav.
    /// </summary>
    public CalibrationIndicatorOption(
        string displayName,
        string iconGlyph,
        string activeIconPath,
        string inactiveIconPath,
        bool isActive = false,
        Guid? indicatorId = null,
        int moduleAddress = 0,
        int portNumber = 0,
        Guid? dccCentralProfileId = null)
    {
        DisplayName = displayName;
        IconGlyph = iconGlyph;
        _activeIconPath = activeIconPath;
        _inactiveIconPath = inactiveIconPath;
        _isActive = isActive;
        IndicatorId = indicatorId;
        ModuleAddress = moduleAddress;
        PortNumber = portNumber;
        DccCentralProfileId = dccCentralProfileId;
    }

    /// <summary>
    /// Spätne kompatibilný konštruktor — obe stavy používajú rovnakú ikonu.
    /// </summary>
    public CalibrationIndicatorOption(string displayName, string iconGlyph, string iconPath)
        : this(displayName, iconGlyph, iconPath, iconPath)
    {
    }

    public string DisplayName { get; }
    public string IconGlyph { get; }

    /// <summary>Voliteľné ID zdrojového BlockIndicator-a pre presné párovanie pri live aktualizácii.</summary>
    public Guid? IndicatorId { get; }

    /// <summary>Adresa R-Bus/S88 modulu pre párovanie s DccFeedbackStateChange.</summary>
    public int ModuleAddress { get; }

    /// <summary>Port na module pre párovanie s DccFeedbackStateChange.</summary>
    public int PortNumber { get; }

    /// <summary>ID DCC profilu pre párovanie s DccFeedbackStateChange.</summary>
    public Guid? DccCentralProfileId { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
                return;
            this.RaiseAndSetIfChanged(ref _isActive, value);
            this.RaisePropertyChanged(nameof(IconPath));
            this.RaisePropertyChanged(nameof(IconImage));
        }
    }

    public string IconPath => _isActive ? _activeIconPath : _inactiveIconPath;

    public IImage? IconImage => LoadIcon(_isActive ? _activeIconPath : _inactiveIconPath);

    private static IImage? LoadIcon(string assetUri)
        => IconCache.GetOrAdd(assetUri, static uri =>
        {
            try
            {
                var resolved = new Uri(uri);
                using var stream = AssetLoader.Open(resolved);
                return new Bitmap(stream);
            }
            catch
            {
                try
                {
                    var fileName = Path.GetFileName(new Uri(uri).AbsolutePath);
                    var searchDir = AppDomain.CurrentDomain.BaseDirectory;

                    for (var i = 0; i <= 6; i++)
                    {
                        var candidate = Path.Combine(searchDir, "Assets", "Appicons", "16", fileName);
                        if (File.Exists(candidate))
                        {
                            using var fileStream = File.OpenRead(candidate);
                            return new Bitmap(fileStream);
                        }

                        searchDir = Path.GetDirectoryName(searchDir) ?? searchDir;
                    }
                }
                catch
                {
                }

                return null;
            }
        });

    public override string ToString() => DisplayName;
}

public sealed class CalibrationMeasurementPointViewModel : ReactiveObject
{
    private int _step;
    private double _timeSeconds;
    private double _rawSpeedKmh;
    private double _calculatedSpeedKmh;
    private string _status;
    private bool _isManual;

    public CalibrationMeasurementPointViewModel(int step, string direction, double timeSeconds, double rawSpeedKmh,
        double calculatedSpeedKmh, string status, bool isManual)
    {
        _step = step;
        Direction = direction;
        _timeSeconds = timeSeconds;
        _rawSpeedKmh = rawSpeedKmh;
        _calculatedSpeedKmh = calculatedSpeedKmh;
        _status = status;
        _isManual = isManual;
    }

    public int Step
    {
        get => _step;
        set
        {
            var normalized = Math.Clamp(value, 0, 126);
            if (_step == normalized)
                return;

            this.RaiseAndSetIfChanged(ref _step, normalized);
            this.RaisePropertyChanged(nameof(StepText));
        }
    }

    public string Direction { get; }

    public double TimeSeconds
    {
        get => _timeSeconds;
        set => this.RaiseAndSetIfChanged(ref _timeSeconds, Math.Round(Math.Max(0, value), 1));
    }

    public double RawSpeedKmh
    {
        get => _rawSpeedKmh;
        set => this.RaiseAndSetIfChanged(ref _rawSpeedKmh, Math.Round(Math.Max(0, value), 1));
    }

    public double CalculatedSpeedKmh
    {
        get => _calculatedSpeedKmh;
        set
        {
            var normalized = Math.Round(Math.Max(0, value), 1);
            if (Math.Abs(_calculatedSpeedKmh - normalized) < 0.001)
                return;

            this.RaiseAndSetIfChanged(ref _calculatedSpeedKmh, normalized);
            this.RaisePropertyChanged(nameof(CalculatedSpeedText));
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (string.Equals(_status, value, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _status, value);
            this.RaisePropertyChanged(nameof(StatusBadgeText));
        }
    }

    public bool IsManual
    {
        get => _isManual;
        set
        {
            if (_isManual == value)
                return;

            this.RaiseAndSetIfChanged(ref _isManual, value);
            this.RaisePropertyChanged(nameof(StatusBadgeText));
            this.RaisePropertyChanged(nameof(StatusBadgeBackground));
            this.RaisePropertyChanged(nameof(StatusBadgeBorder));
            this.RaisePropertyChanged(nameof(StatusBadgeForeground));
        }
    }

    public string StepText
    {
        get => Step.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(new string((value ?? string.Empty).Where(char.IsDigit).ToArray()), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed))
                Step = parsed;
        }
    }

    public string CalculatedSpeedText
    {
        get => CalculatedSpeedKmh.ToString("0.0", CultureInfo.InvariantCulture);
        set
        {
            var normalized = (value ?? string.Empty).Trim().Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                    out var parsed))
                CalculatedSpeedKmh = parsed;
        }
    }

    public string StatusBadgeText => IsManual ? "M" : "A";
    public string StatusBadgeBackground => IsManual ? "#FEF3C7" : "#DBEAFE";
    public string StatusBadgeBorder => IsManual ? "#F59E0B" : "#60A5FA";
    public string StatusBadgeForeground => IsManual ? "#92400E" : "#1D4ED8";
}

public sealed class CurveMarkerViewModel : ReactiveObject
{
    public CurveMarkerViewModel(double left, double top, string label, string caption, string fill, string stroke,
        string captionForeground, double width, double height, double labelOffsetX, double labelOffsetY)
    {
        Left = left;
        Top = top;
        Label = label;
        Caption = caption;
        Fill = fill;
        Stroke = stroke;
        CaptionForeground = captionForeground;
        Width = width;
        Height = height;
        LabelOffsetX = labelOffsetX;
        LabelOffsetY = labelOffsetY;
    }

    public double Left { get; }
    public double Top { get; }
    public string Label { get; }
    public string Caption { get; }
    public string Fill { get; }
    public string Stroke { get; }
    public string CaptionForeground { get; }
    public double Width { get; }
    public double Height { get; }
    public double LabelOffsetX { get; }
    public double LabelOffsetY { get; }
}

public sealed class ReferenceGuideViewModel : ReactiveObject
{
    public ReferenceGuideViewModel(double x, string? caption)
    {
        X = x;
        Caption = caption;
    }

    public double X { get; }
    public string? Caption { get; }
}

public sealed class ChartGridLineViewModel : ReactiveObject
{
    public ChartGridLineViewModel(Point startPoint, Point endPoint)
    {
        StartPoint = startPoint;
        EndPoint = endPoint;
    }

    public Point StartPoint { get; }
    public Point EndPoint { get; }
}

public sealed class AxisLabelViewModel : ReactiveObject
{
    public AxisLabelViewModel(double left, double top, string text)
    {
        Left = left;
        Top = top;
        Text = text;
    }

    public double Left { get; }
    public double Top { get; }
    public string Text { get; }
}
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TrackFlow.Models;

public sealed class LocoFunctionDef
{
    private static readonly Dictionary<string, IImage?> _iconCache = new(StringComparer.Ordinal);

    // Meno → avares:// cesta (musí zodpovedať FunctionIconChoices v LocomotivesWindowViewModel)
    private static readonly Dictionary<string, string> _iconPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["-- Zvoľte funkciu --"] = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-func.png",
        ["Svetlo"]               = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-light.png",
        ["Zvuk"]                 = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-sound.png",
        ["Motor"]                = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-engine.png",
        ["Spriahlo"]             = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-coupler.png",
        ["Dym"]                  = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-smoke.png",
        ["Píšťala"]              = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-whistle.png",
        ["Húkačka"]              = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-horn.png",
        ["Zvon"]                 = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-bell.png",
        ["Užívateľská"]          = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-user.png",
    };

    // 0 = L/F0, 1..28 = F1..F28
    public int Slot { get; set; }

    public string Name { get; set; } = string.Empty;
    /// <summary>Meno ikony (napr. "Svetlo") – preloží sa na cestu cez _iconPaths.</summary>
    public string Icon { get; set; } = string.Empty;
    public string Type { get; set; } = "Dekodér";
    public string Control { get; set; } = "Prepínač";
    public bool Inverted { get; set; }

    /// <summary>Načítaná ikona pre binding v dashboarde.</summary>
    [JsonIgnore]
    public IImage? IconImage => LoadIcon(Icon);

    /// <summary>Slot formátovaný ako F0, F1… pre tooltip.</summary>
    [JsonIgnore]
    public string SlotLabel => Slot == 0 ? "L/F0" : $"F{Slot}";

    private static IImage? LoadIcon(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return null;

        // Preložiť meno na cestu ak treba
        var path = nameOrPath.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
            ? nameOrPath
            : _iconPaths.TryGetValue(nameOrPath, out var mapped) ? mapped : null;

        if (path == null) return null;
        if (_iconCache.TryGetValue(path, out var cached)) return cached;
        try
        {
            var uri = new Uri(path, UriKind.Absolute);
            using var s = AssetLoader.Open(uri);
            var bmp = new Bitmap(s);
            _iconCache[path] = bmp;
            return bmp;
        }
        catch
        {
            _iconCache[path] = null;
            return null;
        }
    }
}

public sealed class LocoRecord : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private int _address = 3; // 1..10239
    private string _description = string.Empty;
    private string _iconName = string.Empty;

    // Nové perzistentné polia: typ, dĺžka (cm) a hmotnosť (t)
    private string? _type;
    private int _lengthMm = 0; // uložené v cm
    private double _weightT = 0.0; // uložené v tonách

    // Doplnené polia pre dekodér a systém
    private string? _decoderType;
    private string? _dccSystemName;
    private Guid? _assignedCentralProfileId;

    // DCC konfigurácia viazaná priamo na profil lokomotívy (záložka „Dekodér (CV)")
    private bool _isDccProgrammingEnabled;
    private int _minSpeedCv;
    private int _midSpeedCv;
    private int _maxSpeedCv;
    private int _accelerationCv;
    private int _brakingCv;
    private int _cv57;
    private bool _isDisableDynamicsForMeasurement;
    private int _brakeCorrection;
    private double _brakeCompensationForward;
    private double _brakeCompensationBackward;
    private int _cv29Value;
    private bool _isBemfEnabled;
    private bool _isAnalogOperationEnabled;
    private bool _isInvertDirectionEnabled;
    private bool _isSoundDecoder;

    // Doplnené polia pre základné údaje a údržbu
    private string _number = string.Empty; // číslo lokomotívy
    private string _homeDepot = string.Empty; // domovské depo
    private int _maxSpeed = 0; // maximálna rýchlosť km/h
    private int _power = 0; // výkon kW
    private int _minRadius = 0; // minimálny polomer mm
    private string _epoch = string.Empty; // epocha
    private string _scale = string.Empty; // mierka
    private string _contactPointForward = string.Empty; // kontaktný bod dopredu
    private string _contactPointBackward = string.Empty; // kontaktný bod dozadu
    
    // Polia pre štatistiku a údržbu
    private int _totalKm = 0; // najazdené kilometre
    private DateTime? _lastRunDate = null; // posledná jazda
    private DateTime? _lastMaintenanceDate = null; // posledná údržba  
    private TimeSpan _totalOperationTime = TimeSpan.Zero; // celkový čas prevádzky

    // Definícia funkcií F0..F28 (perzistencia do projektu)
    private List<LocoFunctionDef> _functions = new();
    private List<LocoSpeedProfilePoint> _forwardSpeedProfilePoints = new();
    private List<LocoSpeedProfilePoint> _backwardSpeedProfilePoints = new();
    private string _savedDiagnosticsEngineStatusText = string.Empty;
    private string _savedDiagnosticsSeverity = string.Empty;
    private string _savedDiagnosticsProblemType = string.Empty;
    private string _savedDiagnosticsCauseType = string.Empty;
    private string _savedDiagnosticsAnalysisSummaryText = string.Empty;
    private string _savedDiagnosticsAiRecommendationText = string.Empty;
    private string _savedDiagnosticsRecommendedCvTweaksText = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public int Address
    {
        get => _address;
        set
        {
            if (!SetProperty(ref _address, value))
                return;

            OnPropertyChanged(nameof(DccAddress));
        }
    }

    public int DccAddress
    {
        get => _address;
        set
        {
            if (!SetProperty(ref _address, value))
                return;

            OnPropertyChanged(nameof(Address));
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string IconName
    {
        get => _iconName;
        set => SetProperty(ref _iconName, value);
    }

    public string? Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    public int lengthMm
    {
        get => _lengthMm;
        set => SetProperty(ref _lengthMm, value);
    }

    public double WeightT
    {
        get => _weightT;
        set => SetProperty(ref _weightT, value);
    }

    public string? DecoderType
    {
        get => _decoderType;
        set => SetProperty(ref _decoderType, value);
    }

    public string? DccSystemName
    {
        get => _dccSystemName;
        set => SetProperty(ref _dccSystemName, value);
    }
    
    private string? _decoderManufacturer;
    public string? DecoderManufacturer
    {
        get => _decoderManufacturer;
        set => SetProperty(ref _decoderManufacturer, value);
    }

    private string? _decoderModel;
    public string? DecoderModel
    {
        get => _decoderModel;
        set => SetProperty(ref _decoderModel, value);
    }

    private string? _decoderInterface;
    public string? DecoderInterface
    {
        get => _decoderInterface;
        set => SetProperty(ref _decoderInterface, value);
    }
    
    public Guid? AssignedCentralProfileId
    {
        get => _assignedCentralProfileId;
        set => SetProperty(ref _assignedCentralProfileId, value);
    }

    public bool IsDccProgrammingEnabled
    {
        get => _isDccProgrammingEnabled;
        set
        {
            if (!SetProperty(ref _isDccProgrammingEnabled, value))
                return;

            if (!value)
            {
                ResetDccConfiguration();
            }
        }
    }
    
    public bool IsSoundDecoder
    {
        get => _isSoundDecoder;
        set => SetProperty(ref _isSoundDecoder, value);
    }

    public int MinSpeedCv
    {
        get => _minSpeedCv;
        set => SetProperty(ref _minSpeedCv, value);
    }

    public int MidSpeedCv
    {
        get => _midSpeedCv;
        set => SetProperty(ref _midSpeedCv, value);
    }

    public int MaxSpeedCv
    {
        get => _maxSpeedCv;
        set => SetProperty(ref _maxSpeedCv, value);
    }

    public int AccelerationCv
    {
        get => _accelerationCv;
        set => SetProperty(ref _accelerationCv, value);
    }

    public int BrakingCv
    {
        get => _brakingCv;
        set => SetProperty(ref _brakingCv, value);
    }

    public int Cv57
    {
        get => _cv57;
        set => SetProperty(ref _cv57, Math.Clamp(value, 0, 255));
    }

    public bool IsDisableDynamicsForMeasurement
    {
        get => _isDisableDynamicsForMeasurement;
        set
        {
            var coercedValue = _isDccProgrammingEnabled && value;
            SetProperty(ref _isDisableDynamicsForMeasurement, coercedValue);
        }
    }

    public int BrakeCorrection
    {
        get => _brakeCorrection;
        set
        {
            var coerced = Math.Clamp(value, -100, 100);
            if (!SetProperty(ref _brakeCorrection, coerced))
                return;

            // Legacy value drives both directions when old data/API sets only BrakeCorrection.
            var mirroredDirectional = Math.Clamp((double)coerced, 0.0, 100.0);
            if (_brakeCompensationForward != mirroredDirectional)
            {
                _brakeCompensationForward = mirroredDirectional;
                OnPropertyChanged(nameof(BrakeCompensationForward));
            }

            if (_brakeCompensationBackward != mirroredDirectional)
            {
                _brakeCompensationBackward = mirroredDirectional;
                OnPropertyChanged(nameof(BrakeCompensationBackward));
            }
        }
    }

    public double BrakeCompensationForward
    {
        get => _brakeCompensationForward;
        set
        {
            var coerced = Math.Clamp(value, 0.0, 100.0);
            if (!SetProperty(ref _brakeCompensationForward, coerced))
                return;

            SyncLegacyBrakeCorrectionFromDirectional();
        }
    }

    public double BrakeCompensationBackward
    {
        get => _brakeCompensationBackward;
        set
        {
            var coerced = Math.Clamp(value, 0.0, 100.0);
            if (!SetProperty(ref _brakeCompensationBackward, coerced))
                return;

            SyncLegacyBrakeCorrectionFromDirectional();
        }
    }

    public int Cv29Value
    {
        get => _cv29Value;
        set => SetProperty(ref _cv29Value, value);
    }

    private bool _isRailComEnabled;
    private bool _isSpeedTableEnabled;

    public bool IsBemfEnabled
    {
        get => _isBemfEnabled;
        set => SetProperty(ref _isBemfEnabled, value);
    }

    public bool IsRailComEnabled
    {
        get => _isRailComEnabled;
        set => SetProperty(ref _isRailComEnabled, value);
    }

    public bool IsSpeedTableEnabled
    {
        get => _isSpeedTableEnabled;
        set => SetProperty(ref _isSpeedTableEnabled, value);
    }

    public bool IsAnalogOperationEnabled
    {
        get => _isAnalogOperationEnabled;
        set => SetProperty(ref _isAnalogOperationEnabled, value);
    }

    public bool IsInvertDirectionEnabled
    {
        get => _isInvertDirectionEnabled;
        set => SetProperty(ref _isInvertDirectionEnabled, value);
    }

    private void ResetDccConfiguration()
    {
        MinSpeedCv = 0;
        MidSpeedCv = 0;
        MaxSpeedCv = 0;
        AccelerationCv = 0;
        BrakingCv = 0;
        Cv29Value = 0;
        IsInvertDirectionEnabled = false;
        IsAnalogOperationEnabled = false;
        IsRailComEnabled = false;
        IsSpeedTableEnabled = false;
        IsBemfEnabled = false;
        IsDisableDynamicsForMeasurement = false;
        BrakeCorrection = 0;
        BrakeCompensationForward = 0;
        BrakeCompensationBackward = 0;
    }

    private void SyncLegacyBrakeCorrectionFromDirectional()
    {
        var legacy = Math.Clamp((int)Math.Round((_brakeCompensationForward + _brakeCompensationBackward) / 2.0, MidpointRounding.AwayFromZero), 0, 100);
        if (_brakeCorrection == legacy)
            return;

        _brakeCorrection = legacy;
        OnPropertyChanged(nameof(BrakeCorrection));
    }

    public List<LocoFunctionDef> Functions
    {
        get => _functions;
        set => SetProperty(ref _functions, value ?? new List<LocoFunctionDef>());
    }

    public List<LocoSpeedProfilePoint> ForwardSpeedProfilePoints
    {
        get => _forwardSpeedProfilePoints;
        set => SetProperty(ref _forwardSpeedProfilePoints, value ?? new List<LocoSpeedProfilePoint>());
    }

    public List<LocoSpeedProfilePoint> BackwardSpeedProfilePoints
    {
        get => _backwardSpeedProfilePoints;
        set => SetProperty(ref _backwardSpeedProfilePoints, value ?? new List<LocoSpeedProfilePoint>());
    }

    public string SavedDiagnosticsEngineStatusText
    {
        get => _savedDiagnosticsEngineStatusText;
        set => SetProperty(ref _savedDiagnosticsEngineStatusText, value ?? string.Empty);
    }

    public string SavedDiagnosticsSeverity
    {
        get => _savedDiagnosticsSeverity;
        set => SetProperty(ref _savedDiagnosticsSeverity, value ?? string.Empty);
    }

    public string SavedDiagnosticsProblemType
    {
        get => _savedDiagnosticsProblemType;
        set => SetProperty(ref _savedDiagnosticsProblemType, value ?? string.Empty);
    }

    public string SavedDiagnosticsCauseType
    {
        get => _savedDiagnosticsCauseType;
        set => SetProperty(ref _savedDiagnosticsCauseType, value ?? string.Empty);
    }

    public string SavedDiagnosticsAnalysisSummaryText
    {
        get => _savedDiagnosticsAnalysisSummaryText;
        set => SetProperty(ref _savedDiagnosticsAnalysisSummaryText, value ?? string.Empty);
    }

    public string SavedDiagnosticsAiRecommendationText
    {
        get => _savedDiagnosticsAiRecommendationText;
        set => SetProperty(ref _savedDiagnosticsAiRecommendationText, value ?? string.Empty);
    }

    public string SavedDiagnosticsRecommendedCvTweaksText
    {
        get => _savedDiagnosticsRecommendedCvTweaksText;
        set => SetProperty(ref _savedDiagnosticsRecommendedCvTweaksText, value ?? string.Empty);
    }

    public string Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    public string HomeDepot
    {
        get => _homeDepot;
        set => SetProperty(ref _homeDepot, value);
    }

    public int MaxSpeed
    {
        get => _maxSpeed;
        set => SetProperty(ref _maxSpeed, value);
    }

    public int Power
    {
        get => _power;
        set => SetProperty(ref _power, value);
    }

    public int MinRadius
    {
        get => _minRadius;
        set => SetProperty(ref _minRadius, value);
    }

    public string Epoch
    {
        get => _epoch;
        set => SetProperty(ref _epoch, value);
    }

    public string Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, value);
    }

    public string ContactPointForward
    {
        get => _contactPointForward;
        set => SetProperty(ref _contactPointForward, value ?? string.Empty);
    }

    public string ContactPointBackward
    {
        get => _contactPointBackward;
        set => SetProperty(ref _contactPointBackward, value ?? string.Empty);
    }

    public int TotalKm
    {
        get => _totalKm;
        set => SetProperty(ref _totalKm, value);
    }

    public DateTime? LastRunDate
    {
        get => _lastRunDate;
        set => SetProperty(ref _lastRunDate, value);
    }

    public DateTime? LastMaintenanceDate
    {
        get => _lastMaintenanceDate;
        set => SetProperty(ref _lastMaintenanceDate, value);
    }

    public TimeSpan TotalOperationTime
    {
        get => _totalOperationTime;
        set => SetProperty(ref _totalOperationTime, value);
    }

    public void NotifyAllPropertiesChanged()
    {
        OnPropertyChanged(string.Empty);
    }
}
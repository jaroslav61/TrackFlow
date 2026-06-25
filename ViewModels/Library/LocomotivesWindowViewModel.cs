using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using System.IO;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Services.Dcc;

namespace TrackFlow.ViewModels.Library;

public partial class LocomotivesWindowViewModel : ObservableObject
{
    public enum EditorMode
    {
        Viewing,
        Adding,
        Editing
    }

    private readonly SettingsManager _settings;
    private readonly IEnumerable<LayoutElement>? _speedCalibrationLayoutElements;
    private readonly IDccConnectionService? _dccConnectionService;
    private bool _isSaving;
    private int _editorLoadScopeDepth;
    private bool _isCoercingAddress;
    private bool _functionsDirty;
    private bool _profileProjectDirty;
    private bool _isAddressProgrammingBusy;
    private LocoRecord? _selectionBeforeAdd;
    private LocoRecord? _selectedLocomotive;

    public LocomotiveSpeedEditorViewModel SpeedEditor { get; }

    public ObservableCollection<LocoRecord> Locomotives => _settings.ProjectLocomotives;
    public ObservableCollection<IconItem> AvailableIcons { get; } = new();
    public ObservableCollection<IconItem> IconComboItems { get; } = new();

    public ObservableCollection<string> LocomotiveTypes { get; } =
        new() { "-- Zvoľte typ lokomotívy --", "Parna", "Dieselová", "Elektricka" };

    /// <summary>Jeden riadok v ComboBoxe DCC centrály.</summary>
    public sealed class DccCentralProfileItem
    {
        public Guid   Id          { get; }
        public string DisplayText { get; }

        public DccCentralProfileItem(Guid id, string displayText)
        {
            Id          = id;
            DisplayText = displayText;
        }

        public override string ToString() => DisplayText;
    }

    public ObservableCollection<DccCentralProfileItem> DigitalSystems { get; } = new();

    /// <summary>True ak je aspoň jedna aktívna/povolená DCC centrála v efektívnom scope nastavení.</summary>
    public bool HasConfiguredDigitalSystems => _settings.GetEffectiveEnabledDccCentralProfiles().Count > 0;

    public ObservableCollection<string> EpochChoices { get; } = new()
        { "-- Zvoľte epochu --", "I (1835-1920)", "II (1920-1950)", "III (1950-1970)", "IV (1970-1990)", "V (1990-2010)", "VI (2010-súčasnosť)" };

    public ObservableCollection<string> ScaleChoices { get; } = new()
        { "-- Zvoľte mierku --", "H0 (1:87)", "TT (1:120)", "N (1:160)", "Z (1:220)", "0 (1:43.5)", "G (1:22.5)" };

    // --- Vlastnosti pre UI ---

    private string _selectedLocomotiveType = "-- Zvoľte typ lokomotívy --";

    public string SelectedLocomotiveType
    {
        get => _selectedLocomotiveType;
        set
        {
            if (SetProperty(ref _selectedLocomotiveType, value)) MarkDirtyAndRevalidate();
        }
    }

    // Statický zoznam štandardizovaných rozhraní DCC dekodérov
    public static IReadOnlyList<string> DecoderInterfaces { get; } = new[]
    {
        "NEM 651 (6-pin)",
        "NEM 652 (8-pin)",
        "Next18",
        "PluX8",
        "PluX12",
        "PluX16",
        "PluX22",
        "MTC21 / Zip (21-pin)"
    };

    public static IReadOnlyList<string> DecoderManufacturers { get; } = new[]
    {
        "-- Výrobca --",
        "ZIMO",
        "ESU",
        "Doehler & Haass",
        "Digitrax",
        "TCS",
        "Lenz",
        "Kühn",
        "Fleischmann",
        "Roco",
        "Märklin",
        "Trix",
        "Uhlenbrock",
    };

    private string _selectedDecoderManufacturer = "-- Výrobca --";
    public string SelectedDecoderManufacturer
    {
        get => _selectedDecoderManufacturer;
        set
        {
            if (SetProperty(ref _selectedDecoderManufacturer, value))
                MarkDirtyAndRevalidate();
        }
    }

    private string _decoderModel = "";
    public string DecoderModel
    {
        get => _decoderModel;
        set
        {
            if (SetProperty(ref _decoderModel, value))
                MarkDirtyAndRevalidate();
        }
    }
    
    private string? _selectedDecoderInterface;
    public string? SelectedDecoderInterface
    {
        get => _selectedDecoderInterface;
        set
        {
            if (SetProperty(ref _selectedDecoderInterface, value))
                MarkDirtyAndRevalidate();  // ← pridať toto
        }
        
    }
    
    private DccCentralProfileItem? _selectedDigitalSystem = null;

    public DccCentralProfileItem? SelectedDigitalSystem
    {
        get => _selectedDigitalSystem;
        set
        {
            if (SetProperty(ref _selectedDigitalSystem, value)) MarkDirtyAndRevalidate();
        }
    }

    private string _selectedEpoch = "-- Zvoľte epochu --";

    public string SelectedEpoch
    {
        get => _selectedEpoch;
        set
        {
            if (SetProperty(ref _selectedEpoch, value)) MarkDirtyAndRevalidate();
        }
    }

    private string _selectedScale = "-- Zvoľte mierku --";

    public string SelectedScale
    {
        get => _selectedScale;
        set
        {
            if (SetProperty(ref _selectedScale, value)) MarkDirtyAndRevalidate();
        }
    }

    private int _lengthMm;
    private string _lengthMmText = "0";

    public int lengthMm
    {
        get => _lengthMm;
        set
        {
            if (_lengthMm == value) return;
            _lengthMm = value;
            _lengthMmText = value.ToString();
            OnPropertyChanged();
            OnPropertyChanged(nameof(lengthMmText));
            MarkDirtyAndRevalidate();
        }
    }

    public string lengthMmText
    {
        get => _lengthMmText;
        set
        {
            var digitsOnly = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digitsOnly.Length > 3)
                digitsOnly = digitsOnly.Substring(0, 3);

            if (SetProperty(ref _lengthMmText, digitsOnly))
            {
                if (int.TryParse(digitsOnly, out var val))
                {
                    _lengthMm = val;
                    OnPropertyChanged(nameof(lengthMm));
                }
                MarkDirtyAndRevalidate();
            }
        }
    }

    private double _weightT;

    public double WeightT
    {
        get => _weightT;
        set
        {
            if (Math.Abs(_weightT - value) < 0.0001) return;
            _weightT = value;
            _weightText = value.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(WeightT));
            OnPropertyChanged(nameof(WeightText));
            MarkDirtyAndRevalidate();
        }
    }

    private string _weightText = "0";

    public string WeightText
    {
        get => _weightText;
        set
        {
            var digitsOnly = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digitsOnly.Length > 3)
                digitsOnly = digitsOnly.Substring(0, 3);

            if (SetProperty(ref _weightText, digitsOnly))
            {
                if (double.TryParse(digitsOnly, out var val))
                {
                    _weightT = val;
                    OnPropertyChanged(nameof(WeightT));
                }

                MarkDirtyAndRevalidate();
            }
        }
    }

    private IconItem? _selectedIcon;

    public IconItem? SelectedIcon
    {
        get => _selectedIcon;
        set
        {
            if (SetProperty(ref _selectedIcon, value)) MarkDirtyAndRevalidate();
        }
    }

    [ObservableProperty] private LocoRecord? selected;
    [ObservableProperty] private EditorMode mode = EditorMode.Viewing;
    [ObservableProperty] private string saveButtonText = "Uložiť zmeny";
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string addressText = "3";
    [ObservableProperty] private string validationMessage = "";
    [ObservableProperty] private bool isSoundDecoder;

    private int _addressValue = 3;

    public int AddressValue
    {
        get => _addressValue;
        set
        {
            var coerced = Math.Clamp(value, DccAddressCodec.MinShortAddress, DccAddressCodec.MaxLongAddress);
            if (!SetProperty(ref _addressValue, coerced))
                return;

            var asText = coerced.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(AddressText, asText, StringComparison.Ordinal))
                return;

            _isCoercingAddress = true;
            AddressText = asText;
            _isCoercingAddress = false;
        }
    }

    public LocoRecord? SelectedLocomotive
    {
        get => _selectedLocomotive;
        private set
        {
            if (ReferenceEquals(_selectedLocomotive, value))
                return;

            if (_selectedLocomotive != null)
                _selectedLocomotive.PropertyChanged -= OnSelectedLocomotivePropertyChanged;

            _selectedLocomotive = value;

            if (_selectedLocomotive != null)
                _selectedLocomotive.PropertyChanged += OnSelectedLocomotivePropertyChanged;

            OnPropertyChanged(nameof(SelectedLocomotive));
            OnPropertyChanged(nameof(IsLocomotiveSelected));
            OnPropertyChanged(nameof(IsDccProgrammingEnabled));
            OnPropertyChanged(nameof(IsDisableDynamicsForMeasurement));
            OnPropertyChanged(nameof(IsGlobalDccProgrammingAvailable));
            OnPropertyChanged(nameof(AccelerationCv));
            OnPropertyChanged(nameof(BrakingCv));
            OnPropertyChanged(nameof(Cv57));
        }
    }

    public bool IsLocomotiveSelected => SelectedLocomotive != null;

    // Proxy vlastnosti kvôli spätnej kompatibilite v testoch / logike.
    // Stav sa drží výhradne v SelectedLocomotive.
    public bool IsDccProgrammingEnabled
    {
        get => SelectedLocomotive?.IsDccProgrammingEnabled ?? false;
        set
        {
            if (SelectedLocomotive == null || SelectedLocomotive.IsDccProgrammingEnabled == value)
                return;

            SelectedLocomotive.IsDccProgrammingEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDisableDynamicsForMeasurement));
        }
    }

    public bool IsDisableDynamicsForMeasurement
    {
        get => SelectedLocomotive?.IsDisableDynamicsForMeasurement ?? false;
        set
        {
            if (SelectedLocomotive == null || SelectedLocomotive.IsDisableDynamicsForMeasurement == value)
                return;

            SelectedLocomotive.IsDisableDynamicsForMeasurement = value;
            OnPropertyChanged();
        }
    }

    public bool IsGlobalDccProgrammingAvailable
        => SelectedLocomotive?.IsDccProgrammingEnabled == true
           && _dccConnectionService?.IsConnected == true;

    public int AccelerationCv
    {
        get => SelectedLocomotive?.AccelerationCv ?? 0;
        set
        {
            if (SelectedLocomotive == null || SelectedLocomotive.AccelerationCv == value) return;
            SelectedLocomotive.AccelerationCv = value;
            OnPropertyChanged();
        }
    }

    public int BrakingCv
    {
        get => SelectedLocomotive?.BrakingCv ?? 0;
        set
        {
            if (SelectedLocomotive == null || SelectedLocomotive.BrakingCv == value) return;
            SelectedLocomotive.BrakingCv = value;
            OnPropertyChanged();
        }
    }

    public int Cv57
    {
        get => SelectedLocomotive?.Cv57 ?? 0;
        set
        {
            if (SelectedLocomotive == null || SelectedLocomotive.Cv57 == value) return;
            SelectedLocomotive.Cv57 = value;
            OnPropertyChanged();
        }
    }

    private string _number = "";
    public string Number
    {
        get => _number;
        set { if (SetProperty(ref _number, value)) MarkDirtyAndRevalidate(); }
    }

    private string _homeDepot = "";
    public string HomeDepot
    {
        get => _homeDepot;
        set { if (SetProperty(ref _homeDepot, value)) MarkDirtyAndRevalidate(); }
    }

    private string _contactPointForward = "";
    public string ContactPointForward
    {
        get => _contactPointForward;
        set { if (SetProperty(ref _contactPointForward, value)) MarkDirtyAndRevalidate(); }
    }

    private string _contactPointBackward = "";
    public string ContactPointBackward
    {
        get => _contactPointBackward;
        set { if (SetProperty(ref _contactPointBackward, value)) MarkDirtyAndRevalidate(); }
    }

    private int _maxSpeed;
    private string _maxSpeedText = "0";

    public int MaxSpeed
    {
        get => _maxSpeed;
        set
        {
            if (_maxSpeed == value) return;
            _maxSpeed = value;
            _maxSpeedText = value.ToString();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MaxSpeedText));
            SpeedEditor.SetChartMaxSpeed(value);
            MarkDirtyAndRevalidate();
        }
    }

    public string MaxSpeedText
    {
        get => _maxSpeedText;
        set
        {
            var digitsOnly = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digitsOnly.Length > 3)
                digitsOnly = digitsOnly.Substring(0, 3);

            if (SetProperty(ref _maxSpeedText, digitsOnly))
            {
                if (int.TryParse(digitsOnly, out var val))
                {
                    _maxSpeed = val;
                    OnPropertyChanged(nameof(MaxSpeed));
                    SpeedEditor.SetChartMaxSpeed(val);
                }
                else
                {
                    _maxSpeed = 0;
                    OnPropertyChanged(nameof(MaxSpeed));
                    SpeedEditor.SetChartMaxSpeed(0);
                }
                MarkDirtyAndRevalidate();
            }
        }
    }

    private int _power;
    private string _powerText = "0";

    public int Power
    {
        get => _power;
        set
        {
            if (_power == value) return;
            _power = value;
            _powerText = value.ToString();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PowerText));
            MarkDirtyAndRevalidate();
        }
    }

    public string PowerText
    {
        get => _powerText;
        set
        {
            var digitsOnly = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digitsOnly.Length > 3)
                digitsOnly = digitsOnly.Substring(0, 3);

            if (SetProperty(ref _powerText, digitsOnly))
            {
                if (int.TryParse(digitsOnly, out var val))
                {
                    _power = val;
                    OnPropertyChanged(nameof(Power));
                }
                MarkDirtyAndRevalidate();
            }
        }
    }

    private int _minRadius;
    private string _minRadiusText = "0";

    public int MinRadius
    {
        get => _minRadius;
        set
        {
            if (_minRadius == value) return;
            _minRadius = value;
            _minRadiusText = value.ToString();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MinRadiusText));
            MarkDirtyAndRevalidate();
        }
    }

    public string MinRadiusText
    {
        get => _minRadiusText;
        set
        {
            var digitsOnly = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digitsOnly.Length > 3)
                digitsOnly = digitsOnly.Substring(0, 3);

            if (SetProperty(ref _minRadiusText, digitsOnly))
            {
                if (int.TryParse(digitsOnly, out var val))
                {
                    _minRadius = val;
                    OnPropertyChanged(nameof(MinRadius));
                }
                MarkDirtyAndRevalidate();
            }
        }
    }

    private string _totalKm = "";
    public string TotalKm
    {
        get => _totalKm;
        set { if (SetProperty(ref _totalKm, value)) MarkDirtyAndRevalidate(); }
    }

    private string _lastRunDateText = "";
    public string LastRunDateText
    {
        get => _lastRunDateText;
        set { if (SetProperty(ref _lastRunDateText, value)) MarkDirtyAndRevalidate(); }
    }

    private string _lastMaintenanceDateText = "";
    public string LastMaintenanceDateText
    {
        get => _lastMaintenanceDateText;
        set { if (SetProperty(ref _lastMaintenanceDateText, value)) MarkDirtyAndRevalidate(); }
    }

    private string _totalOperationTimeText = "";
    public string TotalOperationTimeText
    {
        get => _totalOperationTimeText;
        set { if (SetProperty(ref _totalOperationTimeText, value)) MarkDirtyAndRevalidate(); }
    }

    // =========================================================
    // FUNKCIE (F0..F28) – deterministický editor s FunctionEditModel
    // =========================================================

    public enum FuncMode { None, Add, Edit }

    /// <summary>Edit model pre pravý panel editora – nikdy sa nepíše priamo do DataGrid riadku.</summary>
    public sealed partial class FunctionEditModel : ObservableObject
    {
        [ObservableProperty] private string  slot         = "L/F0";
        [ObservableProperty] private string  functionName = "";
        [ObservableProperty] private FunctionIconItem? icon;
        [ObservableProperty] private string  type         = "Dekodér";
        [ObservableProperty] private string  control      = "Prepínač";
        [ObservableProperty] private int     altAddress   = 1;
        [ObservableProperty] private string  soundFilePath = "";
        [ObservableProperty] private double  soundPosition;
        [ObservableProperty] private double  soundVolume  = 5;
        [ObservableProperty] private bool    soundIsPlaying;
        [ObservableProperty] private int     soundDurationSeconds;
        [ObservableProperty] private bool    soundIsLoaded;

        public event EventHandler? FunctionDataChanged;

        partial void OnSlotChanged(string value) => FunctionDataChanged?.Invoke(this, EventArgs.Empty);
        partial void OnFunctionNameChanged(string value) => FunctionDataChanged?.Invoke(this, EventArgs.Empty);
        partial void OnIconChanged(FunctionIconItem? value) => FunctionDataChanged?.Invoke(this, EventArgs.Empty);
        partial void OnTypeChanged(string value) => FunctionDataChanged?.Invoke(this, EventArgs.Empty);
        partial void OnControlChanged(string value) => FunctionDataChanged?.Invoke(this, EventArgs.Empty);
        partial void OnAltAddressChanged(int value) => FunctionDataChanged?.Invoke(this, EventArgs.Empty);
        partial void OnSoundFilePathChanged(string value) => FunctionDataChanged?.Invoke(this, EventArgs.Empty);
        partial void OnSoundVolumeChanged(double value) => FunctionDataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Riadok tabuľky – len zobrazenie, žiadne editory v bunkách.</summary>
    public sealed partial class FunctionRow : ObservableObject
    {
        [ObservableProperty] private string           slot          = "L/F0";
        [ObservableProperty] private string           name          = "";
        [ObservableProperty] private FunctionIconItem? icon;
        [ObservableProperty] private string           type          = "Dekodér";
        [ObservableProperty] private string           control       = "Prepínač";
        [ObservableProperty] private int              altAddress    = 1;
        [ObservableProperty] private string           soundFilePath = "";
        [ObservableProperty] private double           soundPosition;
        [ObservableProperty] private double           soundVolume   = 5;
        [ObservableProperty] private bool             isDefined;
    }

    public ObservableCollection<FunctionRow> FunctionRows { get; } = new();

    // ── Výber v DataGrid (len na čítanie pre editor) ─────────────────────────
    private FunctionRow? _selectedFunctionRow;
    public FunctionRow? SelectedFunctionRow
    {
        get => _selectedFunctionRow;
        set
        {
            if (_selectedFunctionRow == value) return;
            _selectedFunctionRow = value;
            OnPropertyChanged();
            NotifyFunctionValidation();

            // Klik na riadok → načítaj do editora
            if (value != null)
                BeginEditFunctionInternal(value);
        }
    }

    // ── EditModel + režim ────────────────────────────────────────────────────
    private FunctionEditModel _currentFunction = new();
    public FunctionEditModel CurrentFunction
    {
        get => _currentFunction;
        private set
        {
            // Odpojíme starý handler
            if (_currentFunction != null)
                _currentFunction.FunctionDataChanged -= OnFunctionDataChanged;
            
            SetProperty(ref _currentFunction, value);
            
            // Pripojíme nový handler
            if (_currentFunction != null)
                _currentFunction.FunctionDataChanged += OnFunctionDataChanged;
        }
    }

    private void OnFunctionDataChanged(object? sender, EventArgs e)
    {
        // Zmeny v editore funkcií označia lokomotívu ako dirty
        ForceDirty();
        NotifyFunctionValidation();
    }

    private FuncMode _functionEditorMode = FuncMode.None;
    public FuncMode FunctionEditorMode
    {
        get => _functionEditorMode;
        private set
        {
            if (!SetProperty(ref _functionEditorMode, value)) return;
            OnPropertyChanged(nameof(IsFunctionEditorEnabled));
            OnPropertyChanged(nameof(FunctionValidationMessage));
            OnPropertyChanged(nameof(IsFunctionSaveEnabled));
            NotifyFunctionValidation();
        }
    }

    public bool IsFunctionEditorEnabled => FunctionEditorMode != FuncMode.None;
    public bool CanAddFunction => Selected != null || Mode == EditorMode.Adding;

    // ── Validácia editora funkcií ────────────────────────────────────────────
    public string FunctionValidationMessage
    {
        get
        {
            if (FunctionEditorMode == FuncMode.None) return "";
            if (string.IsNullOrWhiteSpace(CurrentFunction.Slot)) return "Zvoľte slot.";
            var duplicate = FunctionRows.Where(r =>
                r.Slot == CurrentFunction.Slot &&
                (FunctionEditorMode != FuncMode.Edit || r != _selectedFunctionRow));
            if (duplicate.Any()) return $"Slot {CurrentFunction.Slot} je už obsadený.";
            return "";
        }
    }

    public bool IsFunctionSaveEnabled =>
        (FunctionEditorMode == FuncMode.Add || FunctionEditorMode == FuncMode.Edit)
        && string.IsNullOrEmpty(FunctionValidationMessage);

    // ── Zoznamy pre editor ───────────────────────────────────────────────────
    public ObservableCollection<string> FunctionSlotChoices { get; } = new();

    public List<FunctionIconItem> FunctionIconChoices { get; } = new()
    {
        new FunctionIconItem { Name = "-- Zvoľte funkciu --", IconPath = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-func.png" },
        new FunctionIconItem { Name = "Svetlo",      IconPath = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-light.png" },
        new FunctionIconItem { Name = "Zvuk",        IconPath = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-sound.png" },
        new FunctionIconItem { Name = "Motor",       IconPath = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-engine.png" },
        new FunctionIconItem { Name = "Spriahlo",    IconPath = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-coupler.png" },
        new FunctionIconItem { Name = "Dym",         IconPath = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-smoke.png" },
        new FunctionIconItem { Name = "Píšťala",     IconPath = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-whistle.png" },
        new FunctionIconItem { Name = "Húkačka",     IconPath = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-horn.png" },
        new FunctionIconItem { Name = "Zvon",        IconPath = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-bell.png" },
        new FunctionIconItem { Name = "Užívateľská", IconPath = "avares://TrackFlow/Assets/FunctionsIcons/24/fx-user.png" },
    };

    public ObservableCollection<string> FunctionTypeChoices { get; } = new() { "Dekodér", "Zvukový súbor" };
    public ObservableCollection<string> FunctionControlChoices { get; } = new() { "Prepínač", "Tlačidlo" };

    // ── Zvuk – pomocník pre code-behind ─────────────────────────────────────
    // Tieto vlastnosti delegujú na CurrentFunction, code-behind ich sleduje cez PropertyChanged na CurrentFunction.
    public string FunctionSoundPlayButtonText => CurrentFunction.SoundIsPlaying ? "Stop" : "Prehrať";

    // Kompatibilita s code-behind (sleduje tieto mená cez nameof)
    public string  FunctionSoundFilePath
    {
        get => CurrentFunction.SoundFilePath;
        set { CurrentFunction.SoundFilePath = value; OnPropertyChanged(); }
    }
    public double  FunctionSoundPosition
    {
        get => CurrentFunction.SoundPosition;
        set { CurrentFunction.SoundPosition = value; OnPropertyChanged(); }
    }
    public double  FunctionSoundVolume
    {
        get => CurrentFunction.SoundVolume;
        set { CurrentFunction.SoundVolume = value; OnPropertyChanged(); }
    }
    public bool    FunctionSoundIsPlaying
    {
        get => CurrentFunction.SoundIsPlaying;
        set { CurrentFunction.SoundIsPlaying = value; OnPropertyChanged(); OnPropertyChanged(nameof(FunctionSoundPlayButtonText)); }
    }
    public int     FunctionSoundDurationSeconds
    {
        get => CurrentFunction.SoundDurationSeconds;
        set { CurrentFunction.SoundDurationSeconds = value; OnPropertyChanged(); }
    }
    public bool    FunctionSoundIsLoaded
    {
        get => CurrentFunction.SoundIsLoaded;
        set { CurrentFunction.SoundIsLoaded = value; OnPropertyChanged(); }
    }

    public bool IsGridEnabled => Mode == EditorMode.Viewing;
    public IAsyncRelayCommand ReadAddressCommand { get; }
    public IAsyncRelayCommand WriteAddressCommand { get; }
    public Action? RequestClose { get; set; }
    public Action? RequestFocusOnLoco { get; set; }

    /// <summary>
    /// Bezpečne vykoná akciu (napr. clear+set selection v DataGrid) bez toho,
    /// aby sa spustila kaskáda LoadSelectedToEditor a vyčistil editor.
    /// </summary>
    public void RunWithSaveGuard(Action action)
    {
        var prev = _isSaving;
        _isSaving = true;
        try { action(); }
        finally { _isSaving = prev; }
    }

    public LocomotivesWindowViewModel(
        SettingsManager settings,
        IEnumerable<LayoutElement>? speedCalibrationLayoutElements = null,
        IDccConnectionService? dccConnectionService = null)
    {
        _settings = settings;
        _speedCalibrationLayoutElements = speedCalibrationLayoutElements;
        _dccConnectionService = dccConnectionService;
        _settings.EnsureProjectSettings();
        SpeedEditor = new LocomotiveSpeedEditorViewModel();
        ReadAddressCommand = new AsyncRelayCommand(ReadAddressAsync, () => CanReadDecoderAddress);
        WriteAddressCommand = new AsyncRelayCommand(WriteAddressAsync, () => CanWriteDecoderAddress);
        SpeedEditor.MarkProfileDirty = MarkSpeedProfileDirty;
        SpeedEditor.PersistProfileChanges = PersistCatalogSnapshot;
        _settings.ProjectChanged += RefreshSpeedEditorIndicators;
        _settings.Dirty.DirtyChanged += RefreshSpeedEditorProfileDirtyState;
        RefreshSpeedEditorIndicators();
        RefreshSpeedEditorProfileDirtyState();

        // Pripojíme handler na zmeny v editore funkcií
        _currentFunction.FunctionDataChanged += OnFunctionDataChanged;

        // Keď sa zmenia nastavenia (pridanie/odobranie centrály), obnovíme zoznam DCC systémov.
        _settings.AppSettingsChanged += OnAppSettingsChangedRefreshDigitalSystems;

        LoadDigitalSystems();
        LoadAvailableIcons();

        if (_dccConnectionService != null)
            _dccConnectionService.IsConnectedChanged += OnDccConnectionIsConnectedChanged;

        IconComboItems.Clear();
        IconComboItems.Add(new IconItem("-- Vyberte lokomotívu --", string.Empty));
        foreach (var it in AvailableIcons) IconComboItems.Add(it);

        InitFunctionSlotChoices();

        using (SuspendEditorDirtyTracking())
        {
            Selected = Locomotives.FirstOrDefault();
            SpeedEditor.SyncLocomotives(Locomotives, Selected);
            LoadSelectedToEditor(); // vnútri sa zavolá LoadFunctionsFromRecord(Selected)
        }

        RefreshAddressProgrammingAvailability();
    }

    private void RefreshSpeedEditorIndicators()
    {
        var elements = _speedCalibrationLayoutElements ?? _settings.CurrentProject?.Layout?.Elements;
        var indicators = new List<CalibrationIndicatorOption>();

        if (elements != null)
        {
            var elementSnapshot = elements.ToList();

            indicators.AddRange(elementSnapshot
                .OfType<BlockElement>()
                .SelectMany(static block => block.Indicators.Select(indicator => BuildCalibrationIndicatorOption(block, indicator))));

            indicators.AddRange(elementSnapshot
                .OfType<SensorElement>()
                .Select(BuildCalibrationSensorOption));

            if (indicators.Count == 0)
            {
                indicators.AddRange(elementSnapshot
                    .OfType<BlockElement>()
                    .Where(static block => !string.IsNullOrWhiteSpace(block.Label))
                    .Select(static block => new CalibrationIndicatorOption(
                        block.Label.Trim(), "□",
                        "avares://TrackFlow/Assets/Appicons/16/cont_ind.png",
                        "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png",
                        block.IsOccupied)));
            }
        }

        SpeedEditor.SyncProjectIndicators(indicators);
    }

    /// <summary>
    /// Aktualizuje ikony aktívny/neaktívny v existujúcich CalibrationIndicatorOption objektoch
    /// bez prestavby celého zoznamu. Volajte po každej DCC spätnej väzbe, ktorá zmenila
    /// stav BlockIndicator-ov v layoute.
    /// </summary>
    public void RefreshCalibrationIndicatorStates()
    {
        var elements = _speedCalibrationLayoutElements ?? _settings.CurrentProject?.Layout?.Elements;
        if (elements == null)
            return;

        SpeedEditor.SyncIndicatorActiveStates(elements.OfType<BlockElement>());
    }

    private static CalibrationIndicatorOption BuildCalibrationIndicatorOption(BlockElement block, BlockIndicator indicator)
    {
        var blockLabel = string.IsNullOrWhiteSpace(block.Label)
            ? $"Blok {block.Id[..Math.Min(6, block.Id.Length)]}"
            : block.Label.Trim();

        var indicatorLabel = string.IsNullOrWhiteSpace(indicator.Name)
            ? $"Indikátor {indicator.Id.ToString("N")[..6]}"
            : NormalizeCalibrationIndicatorName(indicator.Name);

        var icon = indicator.Type switch
        {
            BlockIndicatorType.Flagman => "⚑",
            BlockIndicatorType.Virtual => "◇",
            _ => "●"
        };
        var (activeIconPath, inactiveIconPath) = indicator.Type switch
        {
            BlockIndicatorType.Flagman => (
                "avares://TrackFlow/Assets/Appicons/16/flag.png",
                "avares://TrackFlow/Assets/Appicons/16/flag_d.png"),
            BlockIndicatorType.Virtual => (
                "avares://TrackFlow/Assets/Appicons/16/virt_cont.png",
                "avares://TrackFlow/Assets/Appicons/16/virt_cont_d.png"),
            _ => (
                "avares://TrackFlow/Assets/Appicons/16/cont_ind.png",
                "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png")
        };

        var label = string.IsNullOrWhiteSpace(indicatorLabel) ? blockLabel : indicatorLabel;
        return new CalibrationIndicatorOption(label, icon, activeIconPath, inactiveIconPath, indicator.IsActive, indicator.Id);
    }

    private static CalibrationIndicatorOption BuildCalibrationSensorOption(SensorElement sensor)
    {
        var sensorLabel = string.IsNullOrWhiteSpace(sensor.Label)
            ? $"Senzor {sensor.SensorAddress}"
            : sensor.Label.Trim();

        // Senzory nemajú osobitnú disabled ikonu, použijeme rovnakú pre oba stavy.
        return new CalibrationIndicatorOption(sensorLabel, "◆",
            "avares://TrackFlow/Assets/Appicons/16/sim_cont.png",
            "avares://TrackFlow/Assets/Appicons/16/sim_cont.png");
    }

    private static string NormalizeCalibrationIndicatorName(string name)
    {
        var normalized = name.Trim();
        var prefixes = new[]
        {
            "Kontaktný indikátor ",
            "Kontaktový indikátor ",
            "Indikátor obsadenia ",
            "Indikátor "
        };

        foreach (var prefix in prefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
                return normalized[prefix.Length..].Trim();
        }

        return normalized;
    }

    private void InitFunctionSlotChoices()
    {
        FunctionSlotChoices.Clear();
        for (var i = 0; i <= 28; i++)
            FunctionSlotChoices.Add(i == 0 ? "L/F0" : $"F{i}");
    }


    private void LoadDigitalSystems()
    {
        // Zapamätáme aktuálne ID, aby sme mohli obnoviť výber po refresh-e.
        var previousId = _selectedDigitalSystem?.Id ?? Guid.Empty;

        DigitalSystems.Clear();

        // Prvá pevná položka: „Bez pripojenia“ (Id = Guid.Empty = sentinel pre null profil).
        DigitalSystems.Add(new DccCentralProfileItem(Guid.Empty, "Bez pripojenia"));

        // Načítame iba aktívne/povolené centrály z efektívneho scope (projekt alebo app).
        var profiles = _settings.GetEffectiveEnabledDccCentralProfiles();
        foreach (var profile in profiles)
        {
            var typeName = DccCentralDisplayName.Get(profile.Type);
            var connInfo = profile.Type == DccCentralType.NanoX_S88
                ? profile.SerialPort
                : profile.Host;
            var displayText = string.IsNullOrWhiteSpace(connInfo)
                ? typeName
                : $"{typeName}  ({connInfo})";
            DigitalSystems.Add(new DccCentralProfileItem(profile.Id, displayText));
        }

        // Obnovíme výber: ak predchádzajúci profil stále existuje, zachováme ho;
        // inak prepneme na „Bez pripojenia“ (prvá položka).
        var restored = DigitalSystems.FirstOrDefault(x => x.Id == previousId)
                       ?? DigitalSystems[0];

        if (!ReferenceEquals(_selectedDigitalSystem, restored))
        {
            _selectedDigitalSystem = restored;
            OnPropertyChanged(nameof(SelectedDigitalSystem));
        }

        OnPropertyChanged(nameof(HasConfiguredDigitalSystems));
    }

    private void OnAppSettingsChangedRefreshDigitalSystems()
    {
        LoadDigitalSystems();
    }

    /// <summary>
    /// Nájde položku v DigitalSystems zodpovedajúcu lokomotíve.
    /// Primárne podľa AssignedCentralProfileId, sekundárne podľa DccSystemName (legacy).
    /// Ak lokomotíva nemá priradenú centrálu, vráti pevnú položku „Bez pripojenia“ (Id = Guid.Empty).
    /// </summary>
    private DccCentralProfileItem ResolveDigitalSystemItem(LocoRecord loco)
    {
        if (loco.AssignedCentralProfileId.HasValue)
        {
            var byId = DigitalSystems.FirstOrDefault(x => x.Id == loco.AssignedCentralProfileId.Value);
            if (byId != null) return byId;
        }

        if (!string.IsNullOrEmpty(loco.DccSystemName))
        {
            var byName = DigitalSystems.FirstOrDefault(x =>
                x.Id != Guid.Empty &&
                x.DisplayText.Equals(loco.DccSystemName, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
        }

        // Fallback: „Bez pripojenia“ (vždy prvá položka).
        return DigitalSystems[0];
    }

    private void LoadAvailableIcons()
    {
        try
        {
            AvailableIcons.Clear();

            var start = AppDomain.CurrentDomain.BaseDirectory;
            var allowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
            var dir = start;
            for (var i = 0; i <= 6; i++)
            {
                var candidate = Path.Combine(dir, "Assets", "LocoIcons");
                if (Directory.Exists(candidate))
                {
                    var files = Directory.EnumerateFiles(candidate, "*.*")
                        .Where(f => allowedExt.Contains(Path.GetExtension(f)));
                    foreach (var f in files)
                    {
                        var full = Path.GetFullPath(f);
    
                        // PODMIENKA: Prepustíme len to, čo má v ceste LocoIcons
                        if (!full.Contains("LocoIcons", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var fileName = Path.GetFileName(f);
                        if (!AvailableIcons.Any(x => x.Name == fileName))
                        {
                            AvailableIcons.Add(new IconItem(fileName, full));
                            IconRegistry.Register(fileName, full);
                        }
                    }

                    break;
                }

                dir = Path.GetDirectoryName(dir) ?? dir;
            }

            // Publish/single-file fallback: load icon names from embedded avares resources.
            foreach (var fileName in VehicleIconLoader.GetEmbeddedVehicleIconFileNames())
            {
                if (!allowedExt.Contains(Path.GetExtension(fileName)))
                    continue;

                if (!AvailableIcons.Any(x => string.Equals(x.Name, fileName, StringComparison.OrdinalIgnoreCase)))
                    AvailableIcons.Add(new IconItem(fileName, fileName));
            }
        }
        catch
        {
        }
     }

    public string AddressKindText
    {
        get
        {
            if (!int.TryParse(AddressText, out var a)) return "(1…10239)";
            if (a < 1 || a > 10239) return "Neplatná (1…10239)";
            return a <= 127 ? "Krátka (1…127)" : "Dlhá (128…10239)";
        }
    }

    public bool IsAddressProgrammingBusy
    {
        get => _isAddressProgrammingBusy;
        private set
        {
            if (!SetProperty(ref _isAddressProgrammingBusy, value))
                return;

            RefreshAddressProgrammingAvailability();
        }
    }

    public bool IsServiceTrackAddressProgrammingAvailable
        => SelectedLocomotive != null
           && !IsAddressProgrammingBusy
           && SupportsServiceTrackAddressProgramming();

    public bool CanReadDecoderAddress => IsServiceTrackAddressProgrammingAvailable;

    public bool CanWriteDecoderAddress
    {
        get
        {
            if (!IsServiceTrackAddressProgrammingAvailable)
                return false;

            return int.TryParse(AddressText, out var address)
                   && DccAddressCodec.IsSupportedAddress(address);
        }
    }

    partial void OnSelectedChanged(LocoRecord? value)
    {
        if (Mode != EditorMode.Adding)
            SelectedLocomotive = value;

        if (!_isSaving)
            SpeedEditor.SyncLocomotives(Locomotives, value);

        OnPropertyChanged(nameof(CanAddFunction));
        AddFunctionCommand.NotifyCanExecuteChanged();
        RefreshAddressProgrammingAvailability();
        if (Mode != EditorMode.Adding) LoadSelectedToEditor();
    }

    private void OnSelectedLocomotivePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not LocoRecord)
            return;

        switch (e.PropertyName)
        {
            case nameof(LocoRecord.IsDccProgrammingEnabled):
                OnPropertyChanged(nameof(IsDccProgrammingEnabled));
                OnPropertyChanged(nameof(IsDisableDynamicsForMeasurement));
                OnPropertyChanged(nameof(IsGlobalDccProgrammingAvailable));
                MarkDirtyAndRevalidate();
                break;
            case nameof(LocoRecord.MinSpeedCv):
            case nameof(LocoRecord.MidSpeedCv):
            case nameof(LocoRecord.MaxSpeedCv):
            case nameof(LocoRecord.AccelerationCv):
            case nameof(LocoRecord.BrakingCv):
            case nameof(LocoRecord.Cv57):
            case nameof(LocoRecord.IsDisableDynamicsForMeasurement):
            case nameof(LocoRecord.BrakeCorrection):
            case nameof(LocoRecord.BrakeCompensationForward):
            case nameof(LocoRecord.BrakeCompensationBackward):
            case nameof(LocoRecord.Cv29Value):
            case nameof(LocoRecord.IsBemfEnabled):
            case nameof(LocoRecord.IsAnalogOperationEnabled):
            case nameof(LocoRecord.IsInvertDirectionEnabled):
                if (e.PropertyName == nameof(LocoRecord.IsDisableDynamicsForMeasurement))
                    OnPropertyChanged(nameof(IsDisableDynamicsForMeasurement));
                if (e.PropertyName == nameof(LocoRecord.AccelerationCv))
                    OnPropertyChanged(nameof(AccelerationCv));
                if (e.PropertyName == nameof(LocoRecord.BrakingCv))
                    OnPropertyChanged(nameof(BrakingCv));
                if (e.PropertyName == nameof(LocoRecord.Cv57))
                    OnPropertyChanged(nameof(Cv57));
                MarkDirtyAndRevalidate();
                break;
        }
    }

    partial void OnNameChanged(string value) => MarkDirtyAndRevalidate();
    partial void OnDescriptionChanged(string value) => MarkDirtyAndRevalidate();
    partial void OnIsSoundDecoderChanged(bool value) => MarkDirtyAndRevalidate();
    
    partial void OnAddressTextChanged(string value)
    {
        if (!_isCoercingAddress)
        {
            var digitsOnly = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digitsOnly.Length > 5) digitsOnly = digitsOnly.Substring(0, 5);

            if (!string.Equals(digitsOnly, value, StringComparison.Ordinal))
            {
                _isCoercingAddress = true;
                AddressText = digitsOnly;
                _isCoercingAddress = false;
                return;
            }
        }

        if (int.TryParse(value, out var parsedAddress))
        {
            var coercedAddress = Math.Clamp(parsedAddress, DccAddressCodec.MinShortAddress, DccAddressCodec.MaxLongAddress);
            if (_addressValue != coercedAddress)
            {
                _addressValue = coercedAddress;
                OnPropertyChanged(nameof(AddressValue));
            }
        }

        OnPropertyChanged(nameof(AddressKindText));
        RefreshAddressProgrammingAvailability();
        MarkDirtyAndRevalidate();
    }

    private void OnDccConnectionIsConnectedChanged(bool _)
    {
        OnPropertyChanged(nameof(IsGlobalDccProgrammingAvailable));
        RefreshAddressProgrammingAvailability();
    }

    private void RefreshAddressProgrammingAvailability()
    {
        OnPropertyChanged(nameof(IsAddressProgrammingBusy));
        OnPropertyChanged(nameof(IsServiceTrackAddressProgrammingAvailable));
        OnPropertyChanged(nameof(CanReadDecoderAddress));
        OnPropertyChanged(nameof(CanWriteDecoderAddress));
        ReadAddressCommand.NotifyCanExecuteChanged();
        WriteAddressCommand.NotifyCanExecuteChanged();
    }

    private bool SupportsServiceTrackAddressProgramming()
    {
        if (_dccConnectionService == null || !_dccConnectionService.IsConnected)
            return false;

        if (_dccConnectionService.Client is not IDccProgrammingClient)
            return false;

        return _dccConnectionService.Client switch
        {
            Z21Client z21 => z21.HardwareType.SupportsServiceModeProgramming(),
            SerialDccClient => true,
            _ => true
        };
    }

    private IDccProgrammingClient GetServiceTrackProgrammingClientOrThrow()
    {
        if (_dccConnectionService == null || !_dccConnectionService.IsConnected)
            throw new InvalidOperationException("DCC centrála nie je pripojená.");

        if (_dccConnectionService.Client is not IDccProgrammingClient programmingClient)
            throw new InvalidOperationException("Aktívna centrála nepodporuje DCC programovanie CV registrov.");

        if (_dccConnectionService.Client is Z21Client z21 && !z21.HardwareType.SupportsServiceModeProgramming())
            throw new InvalidOperationException("Aktívna centrála nepodporuje service-track programovanie.");

        return programmingClient;
    }

    private static int ParseEditedDccAddress(string addressText)
    {
        if (!int.TryParse(addressText, out var address) || !DccAddressCodec.IsSupportedAddress(address))
            throw new InvalidOperationException($"DCC adresa musí byť v rozsahu {DccAddressCodec.MinShortAddress}..{DccAddressCodec.MaxLongAddress}.");

        return address;
    }

    private async Task<int> ReadProgrammingCvAsync(int cvAddress, CancellationToken ct = default)
    {
        var programmingClient = GetServiceTrackProgrammingClientOrThrow();
        const int serviceTrackAddressPlaceholder = 0;
        return await programmingClient.ReadCvAsync(
            cvAddress,
            DccProgrammingTestMode.ServiceTrack,
            timeoutMs: 5000,
            locoAddress: serviceTrackAddressPlaceholder,
            ct: ct);
    }

    private async Task WriteProgrammingCvAsync(int cvAddress, int value, CancellationToken ct = default)
    {
        var programmingClient = GetServiceTrackProgrammingClientOrThrow();
        const int serviceTrackAddressPlaceholder = 0;
        await programmingClient.WriteCvAsync(
            cvAddress,
            value,
            DccProgrammingTestMode.ServiceTrack,
            timeoutMs: 5000,
            locoAddress: serviceTrackAddressPlaceholder,
            ct: ct);
    }

    public async Task WriteAllSpeedCvsAsync(LocoRecord loco, CancellationToken ct = default)
    {
        await WriteProgrammingCvAsync(2, loco.MinSpeedCv, ct);
        await WriteProgrammingCvAsync(6, loco.MidSpeedCv, ct);
        await WriteProgrammingCvAsync(5, loco.MaxSpeedCv, ct);
        await WriteProgrammingCvAsync(3, loco.AccelerationCv, ct);
        await WriteProgrammingCvAsync(4, loco.BrakingCv, ct);
        await WriteProgrammingCvAsync(57, loco.Cv57, ct);
    }
    
    public async Task WriteProgrammingCvsAsync(params (int CvAddress, int Value)[] cvs)
    {
        foreach (var (cvAddress, value) in cvs)
            await WriteProgrammingCvAsync(cvAddress, value);
    }
    
    private int ApplyCv29State(int cv29Value)
    {
        if (SelectedLocomotive != null)
        {
            SelectedLocomotive.Cv29Value = cv29Value;
            SelectedLocomotive.IsInvertDirectionEnabled = (cv29Value & 0x01) != 0;
            SelectedLocomotive.IsAnalogOperationEnabled = (cv29Value & 0x04) != 0;
            SelectedLocomotive.IsBemfEnabled = (cv29Value & 0x10) != 0;
        }

        return cv29Value;
    }

    private void ApplyAddressToEditor(int address)
    {
        var addressText = address.ToString(CultureInfo.InvariantCulture);

        if (SelectedLocomotive != null)
            SelectedLocomotive.DccAddress = address;

        if (!string.Equals(AddressText, addressText, StringComparison.Ordinal))
            AddressText = addressText;

        OnPropertyChanged(nameof(AddressKindText));
    }

    private async Task ReadAddressAsync()
    {
        IsAddressProgrammingBusy = true;

        try
        {
            var cv29 = ApplyCv29State(await ReadProgrammingCvAsync(29));

            if (!DccAddressCodec.UsesLongAddress(cv29))
            {
                var shortAddress = await ReadProgrammingCvAsync(1);
                if (!DccAddressCodec.IsShortAddress(shortAddress))
                    throw new InvalidOperationException($"Decoder vrátil neplatnú krátku adresu {shortAddress} v CV1.");

                ApplyAddressToEditor(shortAddress);
                return;
            }

            var cv17 = await ReadProgrammingCvAsync(17);
            var cv18 = await ReadProgrammingCvAsync(18);
            var longAddress = DccAddressCodec.DecodeLongAddress(cv17, cv18);
            ApplyAddressToEditor(longAddress);
        }
        catch (Exception ex)
        {
            TrackFlowDoctorService.Instance.Diagnose("DCC", $"❌ Načítanie DCC adresy zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
            throw;
        }
        finally
        {
            IsAddressProgrammingBusy = false;
        }
    }

    private async Task WriteAddressAsync()
    {
        var address = ParseEditedDccAddress(AddressText);
        IsAddressProgrammingBusy = true;

        try
        {
            var cv29 = ApplyCv29State(await ReadProgrammingCvAsync(29));

            if (DccAddressCodec.IsShortAddress(address))
            {
                await WriteProgrammingCvAsync(1, address);
                cv29 = DccAddressCodec.SetLongAddressFlag(cv29, useLongAddress: false);
                await WriteProgrammingCvAsync(29, cv29);
            }
            else
            {
                var (cv17, cv18) = DccAddressCodec.EncodeLongAddress(address);
                await WriteProgrammingCvAsync(17, cv17);
                await WriteProgrammingCvAsync(18, cv18);
                cv29 = DccAddressCodec.SetLongAddressFlag(cv29, useLongAddress: true);
                await WriteProgrammingCvAsync(29, cv29);
            }

            ApplyCv29State(cv29);
            ApplyAddressToEditor(address);
        }
        catch (Exception ex)
        {
            TrackFlowDoctorService.Instance.Diagnose("DCC", $"❌ Zápis DCC adresy zlyhal: {ex.Message}", DiagnosticLevel.Warning);
            throw;
        }
        finally
        {
            IsAddressProgrammingBusy = false;
        }
    }

    private void LoadSelectedToEditor()
    {
        if (_isSaving) return;

        using var _ = SuspendEditorDirtyTracking();

        if (Selected == null)
        {
            Name = "";
            Description = "";
            AddressText = "3";
            lengthMm = 0;
            WeightT = 0;
            SelectedIcon = IconComboItems.FirstOrDefault();
            SelectedLocomotiveType = LocomotiveTypes[0];
            SelectedDigitalSystem = null;
            SelectedEpoch = EpochChoices[0];
            SelectedScale = ScaleChoices[0];
            Number = "";
            HomeDepot = "";
            MaxSpeed = 0;
            Power = 0;
            MinRadius = 0;
            ContactPointForward = "0";
            ContactPointBackward = "0";
            TotalKm = "0";
            LastRunDateText = "";
            LastMaintenanceDateText = "";
            TotalOperationTimeText = "";
            IsSoundDecoder = false;
            LoadFunctionsFromRecord(null);
            SelectedDecoderManufacturer = "-- Výrobca --";
            DecoderModel = "";
            SelectedDecoderInterface = null;
        }
        else
        {
            Name = Selected.Name ?? "";
            Description = Selected.Description ?? "";
            AddressText = Selected.Address.ToString();
            SelectedIcon = IconComboItems.FirstOrDefault(i => i.Name == Selected.IconName) ??
                           IconComboItems.FirstOrDefault();
            SelectedLocomotiveType = Selected.Type ?? LocomotiveTypes[0];
            // Nájdeme centrálu podľa ID (primárne) alebo display textu (fallback pre staré dáta).
            SelectedDigitalSystem = ResolveDigitalSystemItem(Selected);
            SelectedEpoch = GetEpochWithYears(Selected.Epoch) ?? EpochChoices[0];
            SelectedScale = Selected.Scale ?? ScaleChoices[0];
            Number = Selected.Number ?? "";
            HomeDepot = Selected.HomeDepot ?? "";
            MaxSpeed = Selected.MaxSpeed;
            Power = Selected.Power;
            MinRadius = Selected.MinRadius;
            ContactPointForward = string.IsNullOrEmpty(Selected.ContactPointForward) ? "0" : Selected.ContactPointForward;
            ContactPointBackward = string.IsNullOrEmpty(Selected.ContactPointBackward) ? "0" : Selected.ContactPointBackward;
            TotalKm = Selected.TotalKm > 0 ? Selected.TotalKm.ToString() : "";
            LastRunDateText = Selected.LastRunDate?.ToString("dd.MM.yyyy") ?? "";
            LastMaintenanceDateText = Selected.LastMaintenanceDate?.ToString("dd.MM.yyyy") ?? "";
            var totalTime = Selected.TotalOperationTime;
            TotalOperationTimeText = totalTime > TimeSpan.Zero 
                ? $"{(int)totalTime.TotalHours}:{totalTime.Minutes:D2}" 
                : "";
            IsSoundDecoder = Selected.IsSoundDecoder;
            LoadFunctionsFromRecord(Selected);
            SelectedDecoderManufacturer = Selected.DecoderManufacturer ?? "-- Výrobca --";
            DecoderModel = Selected.DecoderModel ?? "";
            SelectedDecoderInterface = Selected.DecoderInterface;

            // CV display fields sa nikdy nenačítavajú z modelu — vždy štartujú na 0/false
            if (SelectedLocomotive != null)
            {
                SelectedLocomotive.IsDccProgrammingEnabled = false;
                SelectedLocomotive.MinSpeedCv = 0;
                SelectedLocomotive.MidSpeedCv = 0;
                SelectedLocomotive.MaxSpeedCv = 0;
                SelectedLocomotive.AccelerationCv = 0;
                SelectedLocomotive.BrakingCv = 0;
                SelectedLocomotive.Cv57 = 0;
            }
        }

        IsDirty = false;
        _functionsDirty = false;
        SetMode(EditorMode.Viewing);
        SpeedEditor.SyncLocomotives(Locomotives, Selected);
    }

    // =========================
    // Funkcie: editor
    // PRAVIDLO: Editor (CurrentFunction) NIKDY nezapisuje automaticky do tabuľky.
    // Zápis do tabuľky nastáva LEN pri AddFunction (nový riadok) alebo klik Uložiť zmeny (persistencia).
    // Pri kliku na riadok sa hodnoty len NAČÍTAJÚ do editora (jednosmerne).
    // =========================

    /// <summary>
    /// Klik na riadok v DataGrid → jednosmerne načíta hodnoty do CurrentFunction.
    /// Žiadny handler, žiadny spätný zápis.
    /// </summary>
    private void BeginEditFunctionInternal(FunctionRow row)
    {
        CurrentFunction = new FunctionEditModel
        {
            Slot           = row.Slot,
            FunctionName   = row.Icon?.Name ?? row.Name ?? "",
            Icon           = row.Icon ?? FunctionIconChoices.FirstOrDefault(),
            Type           = row.Type          ?? "Dekodér",
            Control        = row.Control       ?? "Prepínač",
            AltAddress     = row.AltAddress,
            SoundFilePath  = row.SoundFilePath ?? "",
            SoundPosition  = row.SoundPosition,
            SoundVolume    = row.SoundVolume,
            SoundIsPlaying = false,
        };
        FunctionEditorMode = FuncMode.Edit;
        NotifyFunctionValidation();
    }

    /// <summary>
    /// Pripraví editor na zadanie NOVEJ funkcie – prázdny stav, FuncMode.Add.
    /// </summary>
    private void ResetEditorForNewFunction()
    {
        CurrentFunction = new FunctionEditModel
        {
            Slot         = NextFreeSlot(),
            FunctionName = "",
            Icon         = FunctionIconChoices.FirstOrDefault(),
            Type         = "Dekodér",
            Control      = "Prepínač",
            AltAddress   = 1,
            SoundVolume  = 5,
        };

        _selectedFunctionRow = null;
        // Počas ukladania nenotifikujeme UI – zabránime strate focusu z LocoGrid
        if (!_isSaving)
            OnPropertyChanged(nameof(SelectedFunctionRow));

        FunctionEditorMode = FuncMode.Add;
        NotifyFunctionValidation();
    }

    /// <summary>
    /// Tlačidlo „Pridať funkciu":
    /// Vezme aktuálny stav editora, vytvorí nový riadok, pridá do tabuľky, resetuje editor.
    /// Funguje vždy – bez ohľadu na FuncMode.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddFunction))]
    private void AddFunction()
    {
        var fn   = _currentFunction;
        var slot = fn.Slot ?? NextFreeSlot();
        // Názov funkcie = meno ikony (ComboBox s ikonou je jediný vstup pre názov)
        var name = fn.Icon?.Name ?? (slot == "L/F0" ? "Svetlo" : slot);

        // Duplicitný slot: ak editujeme existujúci riadok, vynecháme ho z kontroly
        var isDuplicate = FunctionRows.Any(r =>
            r.Slot == slot && r != _selectedFunctionRow);

        if (isDuplicate)
        {
            NotifyFunctionValidation();
            return;
        }

        // Ak sme editovali existujúci riadok (Edit mode) – aktualizujeme ho
        if (FunctionEditorMode == FuncMode.Edit && _selectedFunctionRow != null)
        {
            var row          = _selectedFunctionRow;
            row.Slot         = slot;
            row.Name         = fn.Icon?.Name ?? name;
            row.Icon         = fn.Icon;
            row.Type         = fn.Type    ?? "Dekodér";
            row.Control      = fn.Control ?? "Prepínač";
            row.AltAddress   = fn.AltAddress;
            row.SoundFilePath = fn.SoundFilePath ?? "";
            row.SoundPosition = fn.SoundPosition;
            row.SoundVolume  = fn.SoundVolume;
        }
        else
        {
            // Add mode – vytvorí nový riadok
            FunctionRows.Add(new FunctionRow
            {
                Slot          = slot,
                Name          = name,
                Icon          = fn.Icon,
                Type          = fn.Type    ?? "Dekodér",
                Control       = fn.Control ?? "Prepínač",
                AltAddress    = fn.AltAddress,
                SoundFilePath = fn.SoundFilePath ?? "",
                SoundPosition = fn.SoundPosition,
                SoundVolume   = fn.SoundVolume,
                IsDefined     = true,
            });
        }

        // Vždy resetuj editor pre ďalší vstup
        ResetEditorForNewFunction();
        ForceDirty();
    }

    [RelayCommand(CanExecute = nameof(IsDeleteFunctionRowEnabled))]
    private void DeleteFunctionRow()
    {
        if (_selectedFunctionRow == null) return;

        var idx = FunctionRows.IndexOf(_selectedFunctionRow);
        FunctionRows.Remove(_selectedFunctionRow);

        _selectedFunctionRow = null;
        OnPropertyChanged(nameof(SelectedFunctionRow));

        if (FunctionRows.Count > 0)
        {
            var next = idx < FunctionRows.Count ? FunctionRows[idx] : FunctionRows.Last();
            _selectedFunctionRow = next;
            OnPropertyChanged(nameof(SelectedFunctionRow));
            BeginEditFunctionInternal(next);
        }
        else
        {
            ResetEditorForNewFunction();
        }

        ForceDirty();
    }

    public bool IsDeleteFunctionRowEnabled => _selectedFunctionRow != null;

    /// <summary>Označí lokomotívu ako zmenená – aj keď bola zmenená len funkcia.</summary>
    private void ForceDirty()
    {
        if (_isSaving || _editorLoadScopeDepth > 0) return;
        _functionsDirty = true;
        if (Mode == EditorMode.Viewing && Selected != null)
            SetMode(EditorMode.Editing);
        IsDirty = true;
        ValidationMessage = Validate(out _);
        SaveChangesCommand.NotifyCanExecuteChanged();
    }

    private void NotifyFunctionValidation()
    {
        OnPropertyChanged(nameof(FunctionValidationMessage));
        OnPropertyChanged(nameof(IsFunctionSaveEnabled));
        OnPropertyChanged(nameof(IsDeleteFunctionRowEnabled));
        OnPropertyChanged(nameof(CanAddFunction));
        AddFunctionCommand.NotifyCanExecuteChanged();
        DeleteFunctionRowCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleFunctionSoundPlay()
    {
        FunctionSoundIsPlaying = !FunctionSoundIsPlaying;
    }

    // =========================
    // Funkcie: persistencia
    // =========================

    private static int SlotStringToInt(string slot)
    {
        if (string.IsNullOrWhiteSpace(slot)) return 0;
        if (slot == "L/F0" || slot == "F0") return 0;
        if (slot.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(slot.Substring(1), out var n)) return n;
        return 0;
    }

    /// <summary>
    /// Aplikuje zmeny z editora funkcií (CurrentFunction) do aktuálne editovaného riadku (SelectedFunctionRow).
    /// Volá sa automaticky pred uložením lokomotívy, aby sa nezabudli neuložené zmeny v editore.
    /// </summary>
    private void ApplyCurrentFunctionToRow()
    {
        // Ak je editor v režime Edit a máme vybraný riadok, aplikuj zmeny
        if (FunctionEditorMode != FuncMode.Edit || _selectedFunctionRow == null)
            return;
        
        var fn = _currentFunction;
        var row = _selectedFunctionRow;
        var slot = fn.Slot ?? NextFreeSlot();
        var name = fn.Icon?.Name ?? (slot == "L/F0" ? "Svetlo" : slot);
        
        // Skontroluj duplicitný slot (vynechaj aktuálny riadok)
        var isDuplicate = FunctionRows.Any(r => r.Slot == slot && r != row);
        if (isDuplicate)
            return; // Neaplikuj zmeny, ak je slot duplicitný
        
        row.Slot = slot;
        row.Name = fn.Icon?.Name ?? name;
        row.Icon = fn.Icon;
        row.Type = fn.Type ?? "Dekodér";
        row.Control = fn.Control ?? "Prepínač";
        row.AltAddress = fn.AltAddress;
        row.SoundFilePath = fn.SoundFilePath ?? "";
        row.SoundPosition = fn.SoundPosition;
        row.SoundVolume = fn.SoundVolume;
    }

    private void LoadFunctionsFromRecord(LocoRecord? rec)
    {
        FunctionRows.Clear();
        _selectedFunctionRow = null;
        if (!_isSaving)
            OnPropertyChanged(nameof(SelectedFunctionRow));

        if (rec?.Functions != null && rec.Functions.Count > 0)
        {
            foreach (var def in rec.Functions)
            {
                FunctionRows.Add(new FunctionRow
                {
                    Slot          = def.Slot == 0 ? "L/F0" : $"F{def.Slot}",
                    Name          = def.Name ?? "",
                    Icon          = FunctionIconChoices.FirstOrDefault(i => i.Name == def.Icon),
                    Type          = string.IsNullOrWhiteSpace(def.Type)    ? "Dekodér"  : def.Type,
                    Control       = string.IsNullOrWhiteSpace(def.Control) ? "Prepínač" : def.Control,
                    AltAddress    = 1,
                    SoundFilePath = "",
                    SoundPosition = 0,
                    SoundVolume   = 5,
                    IsDefined     = true,
                });
            }
        }

        // Editor vždy pripravený na pridanie ďalšej funkcie
        ResetEditorForNewFunction();
    }

    private List<LocoFunctionDef> BuildFunctionDefsFromRows()
    {
        var defs = new List<LocoFunctionDef>(FunctionRows.Count);
        foreach (var row in FunctionRows)
        {
            var name = row.Name?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;
            var slot = SlotStringToInt(row.Slot);
            if (slot < 0 || slot > 28) continue;
            defs.Add(new LocoFunctionDef
            {
                Slot = slot, Name = name, Icon = row.Icon?.Name ?? "",
                Type = row.Type ?? "Dekodér", Control = row.Control ?? "Prepínač",
            });
        }
        return defs;
    }

    private string NextFreeSlot()
    {
        var used = FunctionRows.Select(r => r.Slot).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i <= 28; i++)
        {
            var s = i == 0 ? "L/F0" : $"F{i}";
            if (!used.Contains(s)) return s;
        }
        return "F28";
    }

    // =========================
    // Validácia / režimy / CRUD
    // =========================

    private void MarkDirtyAndRevalidate()
    {
        if (_isSaving || _editorLoadScopeDepth > 0) return;
        if (Mode == EditorMode.Viewing && Selected != null) SetMode(EditorMode.Editing);
        if (Mode != EditorMode.Viewing) IsDirty = true;
        ValidationMessage = Validate(out _);
        NotifyAllCanExecutes();
    }

    private IDisposable SuspendEditorDirtyTracking()
    {
        _editorLoadScopeDepth++;
        return new DirtyTrackingScope(this);
    }

    private sealed class DirtyTrackingScope : IDisposable
    {
        private readonly LocomotivesWindowViewModel _owner;
        private bool _disposed;

        public DirtyTrackingScope(LocomotivesWindowViewModel owner)
            => _owner = owner;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_owner._editorLoadScopeDepth > 0)
                _owner._editorLoadScopeDepth--;
        }
    }

    private string Validate(out int addr)
    {
        addr = 0;
        if (Mode == EditorMode.Viewing) return "";
        if (string.IsNullOrWhiteSpace(Name)) return "Zadajte názov.";
        if (!int.TryParse(AddressText, out addr)) return "Adresa musí byť číslo.";
        if (addr < 1 || addr > 10239) return "Adresa musí byť v rozsahu 1…10239.";
        return "";
    }

    private void SetMode(EditorMode newMode)
    {
        Mode = newMode;
        SaveButtonText = (Mode == EditorMode.Adding) ? "Uložiť" : "Uložiť zmeny";
        OnPropertyChanged(nameof(IsGridEnabled));
        OnPropertyChanged(nameof(CanAddFunction));
        NotifyAllCanExecutes();
    }

    private void NotifyAllCanExecutes()
    {
        AddCommand.NotifyCanExecuteChanged();
        SaveChangesCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        NotifyFunctionValidation();
        RefreshAddressProgrammingAvailability();
    }

    [RelayCommand(CanExecute = nameof(CanBeginAdd))]
    private void Add()
    {
        _selectionBeforeAdd = Selected;
        Selected = null;
        SetMode(EditorMode.Adding);
        SelectedLocomotive = new LocoRecord();
        Name = "";
        Description = "";
        AddressText = NextFreeAddress().ToString();
        lengthMm    = 0;
        WeightT     = 0;
        Number = "";
        HomeDepot = "";
        MaxSpeed = 0;
        Power = 0;
        MinRadius = 0;
        ContactPointForward = "0";
        ContactPointBackward = "0";
        TotalKm = "";
        LoadFunctionsFromRecord(null);
        IsDirty = false;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveChanges()
    {
        _isSaving = true;
        try
        {
            var msg = Validate(out var addr);
            if (!string.IsNullOrEmpty(msg)) return;

            // Pred uložením aplikuj zmeny z editora funkcií do aktuálne editovaného riadku
            ApplyCurrentFunctionToRow();
            
            var functionDefs = BuildFunctionDefsFromRows();

            if (Mode == EditorMode.Adding)
            {
                var selectedLocomotive = SelectedLocomotive;
                var rec = new LocoRecord
                {
                    Name         = Name.Trim(),
                    Address      = addr,
                    Description  = Description ?? "",
                    IconName     = SelectedIcon?.Name ?? "",
                    Type         = SelectedLocomotiveType == LocomotiveTypes[0] ? null : SelectedLocomotiveType,
                    lengthMm     = lengthMm,
                    WeightT      = WeightT,
                    DecoderType  = null,
                    AssignedCentralProfileId = (SelectedDigitalSystem == null || SelectedDigitalSystem.Id == Guid.Empty) ? null : SelectedDigitalSystem.Id,
                    DccSystemName = (SelectedDigitalSystem == null || SelectedDigitalSystem.Id == Guid.Empty) ? null : SelectedDigitalSystem.DisplayText,
                    Epoch        = SelectedEpoch == EpochChoices[0] ? string.Empty : ExtractEpochNumber(SelectedEpoch),
                    Scale        = SelectedScale == ScaleChoices[0] ? string.Empty : SelectedScale,
                    Number       = Number ?? "",
                    HomeDepot    = HomeDepot ?? "",
                    MaxSpeed     = MaxSpeed,
                    Power        = Power,
                    MinRadius    = MinRadius,
                    ContactPointForward  = ContactPointForward ?? "",
                    ContactPointBackward = ContactPointBackward ?? "",
                    TotalKm      = int.TryParse(TotalKm, out var tk) ? tk : 0,
                    LastRunDate  = ParseDate(LastRunDateText),
                    LastMaintenanceDate = ParseDate(LastMaintenanceDateText),
                    TotalOperationTime = ParseOperationTime(TotalOperationTimeText),
                    IsDisableDynamicsForMeasurement = selectedLocomotive?.IsDisableDynamicsForMeasurement ?? false,
                    BrakeCorrection = selectedLocomotive?.BrakeCorrection ?? 0,
                    BrakeCompensationForward = selectedLocomotive?.BrakeCompensationForward ?? 0,
                    BrakeCompensationBackward = selectedLocomotive?.BrakeCompensationBackward ?? 0,
                    Cv29Value = selectedLocomotive?.Cv29Value ?? 0,
                    IsBemfEnabled = selectedLocomotive?.IsBemfEnabled ?? false,
                    IsAnalogOperationEnabled = selectedLocomotive?.IsAnalogOperationEnabled ?? false,
                    IsInvertDirectionEnabled = selectedLocomotive?.IsInvertDirectionEnabled ?? false,
                    Functions    = functionDefs,
                    // Pre EditorMode.Adding:
                    IsSoundDecoder = IsSoundDecoder,
                    DecoderManufacturer = SelectedDecoderManufacturer == "-- Výrobca --" ? null : SelectedDecoderManufacturer,
                    DecoderModel = string.IsNullOrWhiteSpace(DecoderModel) ? null : DecoderModel,
                    DecoderInterface = SelectedDecoderInterface,
                };
                Locomotives.Add(rec);
                Selected = rec;
                SelectedLocomotive = rec;
            }
            else if (Selected != null)
            {
                Selected.Name         = Name.Trim();
                Selected.Address      = addr;
                Selected.Description  = Description ?? "";
                Selected.IconName     = SelectedIcon?.Name ?? "";
                Selected.Type         = SelectedLocomotiveType == LocomotiveTypes[0] ? null : SelectedLocomotiveType;
                Selected.lengthMm     = lengthMm;
                Selected.WeightT      = WeightT;
                Selected.DecoderType  = null;
                Selected.AssignedCentralProfileId = (SelectedDigitalSystem == null || SelectedDigitalSystem.Id == Guid.Empty) ? null : SelectedDigitalSystem.Id;
                Selected.DccSystemName = (SelectedDigitalSystem == null || SelectedDigitalSystem.Id == Guid.Empty) ? null : SelectedDigitalSystem.DisplayText;
                Selected.Epoch        = SelectedEpoch == EpochChoices[0] ? string.Empty : ExtractEpochNumber(SelectedEpoch);
                Selected.Scale        = SelectedScale == ScaleChoices[0] ? string.Empty : SelectedScale;
                Selected.Number       = Number ?? "";
                Selected.HomeDepot    = HomeDepot ?? "";
                Selected.MaxSpeed     = MaxSpeed;
                Selected.Power        = Power;
                Selected.MinRadius    = MinRadius;
                Selected.ContactPointForward  = ContactPointForward ?? "";
                Selected.ContactPointBackward = ContactPointBackward ?? "";
                Selected.TotalKm      = int.TryParse(TotalKm, out var tk) ? tk : 0;
                Selected.LastRunDate  = ParseDate(LastRunDateText);
                Selected.LastMaintenanceDate = ParseDate(LastMaintenanceDateText);
                Selected.TotalOperationTime = ParseOperationTime(TotalOperationTimeText);
                Selected.IsDisableDynamicsForMeasurement = SelectedLocomotive?.IsDisableDynamicsForMeasurement ?? Selected.IsDisableDynamicsForMeasurement;
                Selected.BrakeCorrection = SelectedLocomotive?.BrakeCorrection ?? Selected.BrakeCorrection;
                Selected.BrakeCompensationForward = SelectedLocomotive?.BrakeCompensationForward ?? Selected.BrakeCompensationForward;
                Selected.BrakeCompensationBackward = SelectedLocomotive?.BrakeCompensationBackward ?? Selected.BrakeCompensationBackward;
                Selected.Cv29Value = SelectedLocomotive?.Cv29Value ?? Selected.Cv29Value;
                Selected.IsBemfEnabled = SelectedLocomotive?.IsBemfEnabled ?? Selected.IsBemfEnabled;
                Selected.IsAnalogOperationEnabled = SelectedLocomotive?.IsAnalogOperationEnabled ?? Selected.IsAnalogOperationEnabled;
                Selected.IsInvertDirectionEnabled = SelectedLocomotive?.IsInvertDirectionEnabled ?? Selected.IsInvertDirectionEnabled;
                Selected.Functions    = functionDefs;
                Selected.IsSoundDecoder = IsSoundDecoder;
                Selected.DecoderManufacturer = SelectedDecoderManufacturer == "-- Výrobca --" ? null : SelectedDecoderManufacturer;
                Selected.DecoderModel = string.IsNullOrWhiteSpace(DecoderModel) ? null : DecoderModel;
                Selected.DecoderInterface = SelectedDecoderInterface;
            }

            // Uložíme si referenciu na editovanú lokomotívu PRED PersistAndSave,
            // pretože SaveCatalog robí Clear+Add na Locomotives kolekcii.
            var editedLoco = Selected;
            
            PersistAndSave();
            
            // Po uložení zmien prejdeme vždy do Viewing režimu
            // aby sa grid odomkol a používateľ mohol vybrať ďalšiu lokomotívu.
            IsDirty = false;
            _functionsDirty = false;
            
            // Načítame funkcie zo záznamu (resetuje editor funkcií do Add režimu)
            LoadFunctionsFromRecord(editedLoco);
            
            // Nastavíme režim na Viewing (odomkne grid)
            SetMode(EditorMode.Viewing);
            
            // KRITICKÉ: SaveCatalog robí Clear+Add na Locomotives kolekcii, čím DataGrid
            // stratí vizuálny SelectedItem. Selected v VM síce ukazuje na rovnakú referenciu,
            // ale binding sa neaktualizuje (rovnaká hodnota = bez notifikácie). Preto musíme
            // selection force-resetnúť cez null → editedLoco. _isSaving=true blokuje LoadSelectedToEditor.
            Selected = null;
            Selected = editedLoco;
            
            SpeedEditor.SyncLocomotives(Locomotives, editedLoco);

            // Revaliduj stav tlačidiel
            ValidationMessage = Validate(out _);
            NotifyAllCanExecutes();
        }
        finally
        {
            _isSaving = false;
            // Po dokončení ukladania vyvoláme focus na editovanú lokomotívu
            // (musí byť v finally, aby sa vyvolal aj keď LoadFunctionsFromRecord zmení selection)
            RequestFocusOnLoco?.Invoke();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (Selected == null) return;
        Locomotives.Remove(Selected);
        PersistAndSave();
        SpeedEditor.SyncLocomotives(Locomotives, Locomotives.FirstOrDefault());
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (Mode == EditorMode.Adding)
        {
            Selected = _selectionBeforeAdd;
            SelectedLocomotive = Selected;
        }
        LoadSelectedToEditor();
        
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    private bool CanBeginAdd() => Mode == EditorMode.Viewing;
    private bool CanSave()     => Mode != EditorMode.Viewing
                                  && (Mode == EditorMode.Adding || IsDirty || _functionsDirty)
                                  && string.IsNullOrEmpty(Validate(out _));
    private bool CanDelete()   => Mode == EditorMode.Viewing && Selected != null;
    private bool CanCancel()   => Mode != EditorMode.Viewing;

    private int NextFreeAddress()
    {
        var used = Locomotives.Select(l => l.Address).ToHashSet();
        for (var a = 3; a <= 10239; a++) if (!used.Contains(a)) return a;
        return 3;
    }

    private void PersistAndSave()
    {
        PersistCatalogSnapshot();
    }

    private void MarkSpeedProfileDirty()
    {
        _profileProjectDirty = true;
        _settings.Dirty.MarkDirty("speed-profile");
        RefreshSpeedEditorProfileDirtyState();
    }

    private void RefreshSpeedEditorProfileDirtyState()
    {
        var projectIsDirty = _settings.CurrentProject?.IsDirty ?? false;
        if (!projectIsDirty)
            _profileProjectDirty = false;

        SpeedEditor.HasPendingProfileProjectChanges = _profileProjectDirty && projectIsDirty;
    }

    private bool PersistCatalogSnapshot()
    {
        var selectedIdBeforeSave = Selected?.Id;
        var speedEditorSelectedId = SpeedEditor.SelectedLocomotive?.Source?.Id ?? selectedIdBeforeSave;
        var selectedProfileTabIndex = SpeedEditor.SelectedProfileTabIndex;
        var proj = new TrackFlowProject
            { Locomotives = Locomotives.ToList(), Wagons = _settings.ProjectWagons.ToList() };
        var saved = false;
        RunWithSaveGuard(() =>
        {
            saved = _settings.SaveCatalog(proj);
        });

        if (!saved)
            return false;

        var restoredSelection = Locomotives.FirstOrDefault(loco => string.Equals(loco.Id, selectedIdBeforeSave, StringComparison.OrdinalIgnoreCase))
            ?? Locomotives.FirstOrDefault(loco => string.Equals(loco.Id, speedEditorSelectedId, StringComparison.OrdinalIgnoreCase));

        if (restoredSelection != null && !ReferenceEquals(Selected, restoredSelection))
            Selected = restoredSelection;

        SpeedEditor.SyncLocomotives(Locomotives, restoredSelection);
        SpeedEditor.SelectedProfileTabIndex = selectedProfileTabIndex;

        _settings.NotifyProjectChanged();
        return true;
    }

    private static DateTime? ParseDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateTime.TryParseExact(text, "dd.MM.yyyy", CultureInfo.InvariantCulture, 
            DateTimeStyles.None, out var date))
            return date;
        return null;
    }

    private static TimeSpan ParseOperationTime(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return TimeSpan.Zero;
        var parts = text.Split(':');
        if (parts.Length == 2 && 
            int.TryParse(parts[0], out var hours) && 
            int.TryParse(parts[1], out var minutes))
            return new TimeSpan(hours, minutes, 0);
        return TimeSpan.Zero;
    }

    // Zmapuje epochu z DB (napr. "I") na formát s rokmi pre ComboBox (napr. "I (1835-1920)")
    private string? GetEpochWithYears(string? epochNumber)
    {
        if (string.IsNullOrWhiteSpace(epochNumber)) return null;
        
        // Nájdi v EpochChoices položku ktorá začína na epochNumber
        return EpochChoices.FirstOrDefault(e => 
            !e.StartsWith("--") && e.StartsWith(epochNumber + " "));
    }

    // Extrahuje číslo epochy z formátu s rokmi (napr. "I (1835-1920)" -> "I")
    private static string ExtractEpochNumber(string epochWithYears)
    {
        if (string.IsNullOrWhiteSpace(epochWithYears)) return string.Empty;
        
        // Ak obsahuje zátvorku, vráť len prvú časť pred medzerou
        var spaceIndex = epochWithYears.IndexOf(' ');
        if (spaceIndex > 0)
            return epochWithYears.Substring(0, spaceIndex);
        
        return epochWithYears;
    }
}
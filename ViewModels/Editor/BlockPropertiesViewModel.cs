using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using TrackFlow.Models.Layout;

namespace TrackFlow.ViewModels.Editor;

public class TickInfo
{
    public double X { get; set; }
    public string Label { get; set; } = string.Empty;
    public double Height { get; set; }
    
    // TIETO DVE VLASTNOSTI TI CHÝBALI A SPÔSOBOVALI CHYBY V XAML:
    public bool ShowLabel => !string.IsNullOrEmpty(Label);
    public double TextOffset => X - 12; // Vycentrovanie textu nad čiarkou
}

public sealed class DirectionalSignalOption
{
    public string? Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}

public partial class BlockPropertiesViewModel : ObservableObject
{
    private readonly BlockElement _block;
    private readonly System.Collections.Generic.Dictionary<string, SignalElement> _signalsById;
    private const double CanvasWidth = 420.0; // Šírka žltého obdĺžnika

    public event Action<bool>? CloseRequested;
    public event Action<System.Collections.Generic.IReadOnlyCollection<string>>? DirectionalSignalsHighlightRequested;

    public string WindowTitle => $"Blok - {BlockName}";

    // ── Všeobecné ──────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string blockName = string.Empty;

    [ObservableProperty] private int lengthMm;
    [ObservableProperty] private bool requestYellow;
    [ObservableProperty] private int  maxSpeedKmh;
    [ObservableProperty] private int  resSpeedKmh;
    [ObservableProperty] private bool allowForward;
    [ObservableProperty] private bool allowBackward;
    [ObservableProperty] private bool criticalSection;
    [ObservableProperty] private int  maxTrainlengthMm;
    [ObservableProperty] private string contactIndicatorColor = "Gray";

    partial void OnBlockNameChanged(string value)
    {
        // Uložiť do _block modelu
        _block.Label = value;
        
        // Aktualizuj názvy všetkých indikátorov
        UpdateIndicatorNames();
    }

    // ── Editor bloku - Kolekcie pre Binding ────────────────────────────────
    public ObservableCollection<TickInfo> UpperTicks { get; } = new();
    public ObservableCollection<TickInfo> LowerTicks { get; } = new();
    public ObservableCollection<BlockIndicatorViewModel> Indicators { get; } = new();

    // Má blok aspoň jeden indikátor?
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreSignalAndShuntingEnabled))]
    private bool hasIndicators;
    
    // Vybraný indikátor a jeho markery
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedIndicatorMarkers))]
    [NotifyPropertyChangedFor(nameof(CanAddMarkers))]
    [NotifyPropertyChangedFor(nameof(AreMarkersEnabled))]
    [NotifyPropertyChangedFor(nameof(AreSignalAndShuntingEnabled))]
    private BlockIndicatorViewModel? selectedIndicator;
    
    // Markery vybraného indikátora
    public ObservableCollection<IndicatorMarkerViewModel> SelectedIndicatorMarkers
    {
        get
        {
            if (SelectedIndicator == null) return new ObservableCollection<IndicatorMarkerViewModel>();
            return new ObservableCollection<IndicatorMarkerViewModel>(SelectedIndicator.Markers);
        }
    }
    
    // Vybraný marker v rámci vybraného indikátora
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeleteMarker))]
    [NotifyPropertyChangedFor(nameof(SelectedMarkerProperties))]
    [NotifyPropertyChangedFor(nameof(SelectedMarkerPositionCm))]
    [NotifyPropertyChangedFor(nameof(SelectedMarkerEndCm))]
    [NotifyPropertyChangedFor(nameof(SelectedMarkerSpeedValue))]
    [NotifyPropertyChangedFor(nameof(SelectedMarkerStopPosition))]
    [NotifyPropertyChangedFor(nameof(IsSelectedMarkerSpeedType))]
    [NotifyPropertyChangedFor(nameof(IsSelectedMarkerStopType))]
    [NotifyPropertyChangedFor(nameof(AreMarkersEnabled))]
    [NotifyPropertyChangedFor(nameof(AreMarkerPropertiesEnabled))]
    private IndicatorMarkerViewModel? selectedMarker;
    
    // Vlastnosti vybraného markeru (pre binding v UI)
    public int? SelectedMarkerPositionCm
    {
        get => SelectedMarker?.PositionCm;
        set { if (SelectedMarker != null && value.HasValue) SelectedMarker.PositionCm = value.Value; }
    }
    
    public int? SelectedMarkerEndCm
    {
        get => SelectedMarker?.EndPositionCm;
        set { if (SelectedMarker != null && value.HasValue) SelectedMarker.EndPositionCm = value.Value; }
    }
    
    public int? SelectedMarkerSpeedValue
    {
        get => SelectedMarker?.SpeedValue;
        set { if (SelectedMarker != null) SelectedMarker.SpeedValue = value; }
    }
    
    public string? SelectedMarkerStopPosition
    {
        get => SelectedMarker?.StopPosition;
        set { if (SelectedMarker != null) SelectedMarker.StopPosition = value; }
    }
    
    public bool CanAddMarkers => SelectedIndicator != null && HasIndicators;
    public bool CanDeleteMarker => SelectedMarker != null;
    public bool SelectedMarkerProperties => SelectedMarker != null;
    public bool IsSelectedMarkerSpeedType => SelectedMarker?.Type == MarkerType.Distance || SelectedMarker?.Type == MarkerType.Braking;
    public bool IsSelectedMarkerStopType => SelectedMarker?.Type == MarkerType.Stop;
    
    // ══ ENABLED/DISABLED LOGIKA ═══════════════════════════════════════════
    // Ak blok NEMÁ indikátor → všetky položky disabled
    // Ak blok MÁ indikátor → enabled len indikátory (combo box indikátorov)
    // Ak je VLOŽENÝ indikátor a je VYBRANÝ → enabled len spinnery Vzdialenosť, Dojazd a Pozícia vlaku. Markery disabled.
    // Ak je VYBRANÝ indikátor → enabled len markery
    
    // Indikátory - enabled vždy (môžem pridávať)
    public bool AreIndicatorsEnabled => true;
    
    // Typ signálu a Posun - enabled len ak má blok indikátor a NIE je vybraný marker
    public bool AreSignalAndShuntingEnabled => HasIndicators && SelectedMarker == null;
    
    // Markery - enabled len ak je vybraný indikátor a NIE je vybraný marker
    public bool AreMarkersEnabled => SelectedIndicator != null && SelectedMarker == null;
    
    // Vlastnosti markera (Vzdialenosť, Dojazd, Pozícia vlaku) - enabled len ak je vybraný marker
    public bool AreMarkerPropertiesEnabled => SelectedMarker != null;
    
    // Farba bloku - VŽDY ŠEDÁ (indikátory sú žlté segmenty na šedom pozadí)
    public string BlockFillBrush => "#A0A0A0";

    // ── Ostatné vlastnosti ─────────────────────────────────────────────────
    [ObservableProperty] private int bwdDistanceCm;
    [ObservableProperty] private int bwdBrakingCm;
    [ObservableProperty] private int bwdStopCm;
    [ObservableProperty] private int bwdActionCm;
    [ObservableProperty] private int fwdDistanceCm;
    [ObservableProperty] private int fwdBrakingCm;
    [ObservableProperty] private int fwdStopCm;
    [ObservableProperty] private int fwdActionCm;

    // Dojazd hodnoty pre každý marker
    [ObservableProperty] private int bwdDistanceEndCm;
    [ObservableProperty] private int bwdBrakingEndCm;
    [ObservableProperty] private int bwdStopEndCm;
    [ObservableProperty] private int bwdActionEndCm;
    [ObservableProperty] private int fwdDistanceEndCm;
    [ObservableProperty] private int fwdBrakingEndCm;
    [ObservableProperty] private int fwdStopEndCm;
    [ObservableProperty] private int fwdActionEndCm;

    public ObservableCollection<string> SignalTypeItems { get; } = new()
        { "Dvojstavové", "Trojstavové", "Štvorstavové", "Päťstavové" };
    [ObservableProperty] private string? selectedSignalType = null;

    public ObservableCollection<string> StopPositionItems { get; } = new()
        { "Čelo vlaku", "Stred vlaku", "Koniec vlaku" };
    [ObservableProperty] private string selectedStopPosition = "Čelo vlaku";

    [ObservableProperty] private bool allowShunting;

    // ── Návestidlá (smerové priradenie) ─────────────────────────────────────
    public ObservableCollection<DirectionalSignalOption> DirectionalSignalItems { get; } = new();

    private DirectionalSignalOption? _selectedSignalLeft;
    private DirectionalSignalOption? _selectedSignalRight;
    private DirectionalSignalOption? _selectedSignalUp;
    private DirectionalSignalOption? _selectedSignalDown;
    private string _signalDirectionWarning = string.Empty;

    public DirectionalSignalOption? SelectedSignalLeft
    {
        get => _selectedSignalLeft;
        set
        {
            if (SetProperty(ref _selectedSignalLeft, value))
            {
                RevalidateAllSignalDirections();
                RaiseDirectionalHighlightChanged();
            }
        }
    }

    public DirectionalSignalOption? SelectedSignalRight
    {
        get => _selectedSignalRight;
        set
        {
            if (SetProperty(ref _selectedSignalRight, value))
            {
                RevalidateAllSignalDirections();
                RaiseDirectionalHighlightChanged();
            }
        }
    }

    public DirectionalSignalOption? SelectedSignalUp
    {
        get => _selectedSignalUp;
        set
        {
            if (SetProperty(ref _selectedSignalUp, value))
            {
                RevalidateAllSignalDirections();
                RaiseDirectionalHighlightChanged();
            }
        }
    }

    public DirectionalSignalOption? SelectedSignalDown
    {
        get => _selectedSignalDown;
        set
        {
            if (SetProperty(ref _selectedSignalDown, value))
            {
                RevalidateAllSignalDirections();
                RaiseDirectionalHighlightChanged();
            }
        }
    }

    public string SignalDirectionWarning
    {
        get => _signalDirectionWarning;
        set
        {
            if (SetProperty(ref _signalDirectionWarning, value))
                OnPropertyChanged(nameof(HasSignalDirectionWarning));
        }
    }

    public bool HasSignalDirectionWarning => !string.IsNullOrWhiteSpace(SignalDirectionWarning);

    // ── Vlastnosti markera ─────────────────────────────────────────────────
    public ObservableCollection<string> MarkerTypeItems { get; } = new()
        { "Distance", "Braking", "Stop" };

    [ObservableProperty] private bool isMarkerSelected;
    [ObservableProperty] private string selectedMarkerKey  = string.Empty;
    [ObservableProperty] private string selectedMarkerName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMarkerSpeedType))]
    [NotifyPropertyChangedFor(nameof(IsMarkerStopType))]
    private string selectedMarkerType = "Distance";

    // Active flags – marker je umiestnený na diagrame
    [ObservableProperty] private bool fwdDistanceActive;
    [ObservableProperty] private bool fwdBrakingActive;
    [ObservableProperty] private bool fwdStopActive;
    [ObservableProperty] private bool fwdActionActive;
    [ObservableProperty] private bool bwdDistanceActive;
    [ObservableProperty] private bool bwdBrakingActive;
    [ObservableProperty] private bool bwdStopActive;
    [ObservableProperty] private bool bwdActionActive;

    [ObservableProperty] private double markerStartCm;
    [ObservableProperty] private double markerEndCm;
    [ObservableProperty] private double markerSliderValue;
    [ObservableProperty] private int    markerSpeedValue;

    public bool IsMarkerSpeedType => SelectedMarkerType == "Distance" || SelectedMarkerType == "Braking";
    public bool IsMarkerStopType => SelectedMarkerType == "Stop";

    private bool _updatingMarker;

    partial void OnMarkerSliderValueChanged(double value)
    {
        if (_updatingMarker) return;
        _updatingMarker = true;
        MarkerStartCm = value;
        SyncMarkerStartToBlock();
        _updatingMarker = false;
    }

    partial void OnMarkerStartCmChanged(double value)
    {
        if (_updatingMarker) return;
        _updatingMarker = true;
        MarkerSliderValue = value;
        SyncMarkerStartToBlock();
        _updatingMarker = false;
    }

    partial void OnMarkerEndCmChanged(double value)
    {
        if (_updatingMarker) return;
        // Synchronizuj EndCm hodnotu do správnej property pre vybraný marker
        int v = (int)value;
        switch (SelectedMarkerKey)
        {
            case "FwdDistance": FwdDistanceEndCm = v; break;
            case "FwdBraking":  FwdBrakingEndCm  = v; break;
            case "FwdStop":     FwdStopEndCm     = v; break;
            case "FwdAction":   FwdActionEndCm   = v; break;
            case "BwdDistance": BwdDistanceEndCm = v; break;
            case "BwdBraking":  BwdBrakingEndCm  = v; break;
            case "BwdStop":     BwdStopEndCm     = v; break;
            case "BwdAction":   BwdActionEndCm   = v; break;
        }
    }

    private void SyncMarkerStartToBlock()
    {
        int v = (int)MarkerStartCm;
        switch (SelectedMarkerKey)
        {
            case "FwdDistance": FwdDistanceCm = v; break;
            case "FwdBraking":  FwdBrakingCm  = v; break;
            case "FwdStop":     FwdStopCm     = v; break;
            case "FwdAction":   FwdActionCm   = v; break;
            case "BwdDistance": BwdDistanceCm = v; break;
            case "BwdBraking":  BwdBrakingCm  = v; break;
            case "BwdStop":     BwdStopCm     = v; break;
            case "BwdAction":   BwdActionCm   = v; break;
        }
    }

    [RelayCommand]
    private void SelectMarker(string key)
    {
        // KONTROLA: Blok musí mať aspoň jeden indikátor
        if (!HasIndicators)
        {
            // TODO: Zobraziť varovanie používateľovi
            return;
        }

        bool wasActive = key switch
        {
            "FwdDistance" => FwdDistanceActive,
            "FwdBraking"  => FwdBrakingActive,
            "FwdStop"     => FwdStopActive,
            "FwdAction"   => FwdActionActive,
            "BwdDistance" => BwdDistanceActive,
            "BwdBraking"  => BwdBrakingActive,
            "BwdStop"     => BwdStopActive,
            "BwdAction"   => BwdActionActive,
            _             => false
        };
        if (!wasActive)
        {
            // Vložiť marker na pozíciu 0 cm
            switch (key)
            {
                case "FwdDistance": FwdDistanceActive = true; FwdDistanceCm = 0; break;
                case "FwdBraking":  FwdBrakingActive  = true; FwdBrakingCm  = 0; break;
                case "FwdStop":     FwdStopActive     = true; FwdStopCm     = 0; break;
                case "FwdAction":   FwdActionActive   = true; FwdActionCm   = 0; break;
                case "BwdDistance": BwdDistanceActive = true; BwdDistanceCm = 0; break;
                case "BwdBraking":  BwdBrakingActive  = true; BwdBrakingCm  = 0; break;
                case "BwdStop":     BwdStopActive     = true; BwdStopCm     = 0; break;
                case "BwdAction":   BwdActionActive   = true; BwdActionCm   = 0; break;
            }
            
            // Priradí marker k prvému indikátoru
            var firstIndicator = Indicators.FirstOrDefault();
            if (firstIndicator != null)
            {
                SetMarkerIndicatorId(key, firstIndicator.Id);
            }
        }

        SelectedMarkerKey = key;
        IsMarkerSelected  = true;

        _updatingMarker = true;
        MarkerStartCm = key switch
        {
            "FwdDistance" => FwdDistanceCm,
            "FwdBraking"  => FwdBrakingCm,
            "FwdStop"     => FwdStopCm,
            "FwdAction"   => FwdActionCm,
            "BwdDistance" => BwdDistanceCm,
            "BwdBraking"  => BwdBrakingCm,
            "BwdStop"     => BwdStopCm,
            "BwdAction"   => BwdActionCm,
            _             => 0
        };
        MarkerSliderValue = MarkerStartCm;
        
        // Načítaj správnu EndCm hodnotu pre vybraný marker
        MarkerEndCm = key switch
        {
            "FwdDistance" => FwdDistanceEndCm,
            "FwdBraking"  => FwdBrakingEndCm,
            "FwdStop"     => FwdStopEndCm,
            "FwdAction"   => FwdActionEndCm,
            "BwdDistance" => BwdDistanceEndCm,
            "BwdBraking"  => BwdBrakingEndCm,
            "BwdStop"     => BwdStopEndCm,
            "BwdAction"   => BwdActionEndCm,
            _             => 0
        };
        _updatingMarker = false;

        SelectedMarkerType = key.Contains("Distance") ? "Distance"
                           : key.Contains("Braking")  ? "Braking"
                           : key.Contains("Action")   ? "Action"
                           : "Stop";
    }

    [RelayCommand]
    private void DeleteMarker()
    {
        switch (SelectedMarkerKey)
        {
            case "FwdDistance": FwdDistanceActive = false; FwdDistanceCm = 0; FwdDistanceEndCm = 0; break;
            case "FwdBraking":  FwdBrakingActive  = false; FwdBrakingCm  = 0; FwdBrakingEndCm  = 0; break;
            case "FwdStop":     FwdStopActive     = false; FwdStopCm     = 0; FwdStopEndCm     = 0; break;
            case "FwdAction":   FwdActionActive   = false; FwdActionCm   = 0; FwdActionEndCm   = 0; break;
            case "BwdDistance": BwdDistanceActive = false; BwdDistanceCm = 0; BwdDistanceEndCm = 0; break;
            case "BwdBraking":  BwdBrakingActive  = false; BwdBrakingCm  = 0; BwdBrakingEndCm  = 0; break;
            case "BwdStop":     BwdStopActive     = false; BwdStopCm     = 0; BwdStopEndCm     = 0; break;
            case "BwdAction":   BwdActionActive   = false; BwdActionCm   = 0; BwdActionEndCm   = 0; break;
        }
        SelectedMarkerKey = string.Empty;
        IsMarkerSelected  = false;
    }

    // ── Konštruktory ───────────────────────────────────────────────────────
    public BlockPropertiesViewModel() : this(new BlockElement(), Enumerable.Empty<SignalElement>()) { }

    public BlockPropertiesViewModel(BlockElement block)
        : this(block, Enumerable.Empty<SignalElement>())
    {
    }

    /// <summary>
    /// SettingsManager pre prístup ku globálnym DCC profilom – nastavuje sa zvonku
    /// pred otvorením okna (pre IndicatorPropertiesViewModel).
    /// </summary>
    public TrackFlow.Services.SettingsManager? SettingsManager { get; set; }

    public BlockPropertiesViewModel(BlockElement block, System.Collections.Generic.IEnumerable<SignalElement> availableSignals)
    {
        _block = block;
        _signalsById = availableSignals
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        DirectionalSignalItems.Add(new DirectionalSignalOption
        {
            Id = null,
            DisplayName = "-- bez priradenia --"
        });

        foreach (var signal in _signalsById.Values.OrderBy(s => s.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            var display = string.IsNullOrWhiteSpace(signal.Label)
                ? $"Návestidlo {signal.Id[..Math.Min(8, signal.Id.Length)]}"
                : signal.Label;
            DirectionalSignalItems.Add(new DirectionalSignalOption
            {
                Id = signal.Id,
                DisplayName = display
            });
        }

        LoadFromBlock();
    }

    private void LoadFromBlock()
    {
        BlockName          = _block.Label;
        lengthMm           = _block.lengthMm;
        RequestYellow      = _block.RequestYellow;
        MaxSpeedKmh        = _block.MaxSpeedKmh;
        ResSpeedKmh        = _block.ResSpeedKmh;
        AllowForward       = _block.AllowForward;
        AllowBackward      = _block.AllowBackward;
        CriticalSection    = _block.CriticalSection;
        MaxTrainlengthMm   = _block.MaxTrainlengthMm;
        BwdDistanceCm      = _block.BwdDistanceCm;
        BwdBrakingCm       = _block.BwdBrakingCm;
        BwdStopCm          = _block.BwdStopCm;
        BwdActionCm        = _block.BwdActionCm;
        FwdDistanceCm      = _block.FwdDistanceCm;
        FwdBrakingCm       = _block.FwdBrakingCm;
        FwdStopCm          = _block.FwdStopCm;
        FwdActionCm        = _block.FwdActionCm;
        
        BwdDistanceEndCm   = _block.BwdDistanceEndCm;
        BwdBrakingEndCm    = _block.BwdBrakingEndCm;
        BwdStopEndCm       = _block.BwdStopEndCm;
        BwdActionEndCm     = _block.BwdActionEndCm;
        FwdDistanceEndCm   = _block.FwdDistanceEndCm;
        FwdBrakingEndCm    = _block.FwdBrakingEndCm;
        FwdStopEndCm       = _block.FwdStopEndCm;
        FwdActionEndCm     = _block.FwdActionEndCm;

        FwdDistanceActive = _block.FwdDistanceCm > 0;
        FwdBrakingActive  = _block.FwdBrakingCm  > 0;
        FwdStopActive     = _block.FwdStopCm     > 0;
        FwdActionActive   = _block.FwdActionCm   > 0;
        BwdDistanceActive = _block.BwdDistanceCm > 0;
        BwdBrakingActive  = _block.BwdBrakingCm  > 0;
        BwdStopActive     = _block.BwdStopCm     > 0;
        BwdActionActive   = _block.BwdActionCm   > 0;
        
        // Ak je SignalType nastavený na defaultnú hodnotu, nech je SelectedSignalType null pre zobrazenie placeholderu
        SelectedSignalType = _block.SignalType switch
        {
            BlockSignalType.Dvojstavove  => null,  // Default hodnota - zobraz placeholder
            BlockSignalType.Trojstavove  => "Trojstavové",
            BlockSignalType.Stvorstavove => "Štvorstavové",
            BlockSignalType.Patstavove   => "Päťstavové",
            _                            => null,
        };
        SelectedStopPosition = _block.StopPosition switch
        {
            BlockStopPosition.StredVlaku => "Stred vlaku",
            BlockStopPosition.KoncaVlaku => "Koniec vlaku",
            _                            => "Čelo vlaku",
        };
        AllowShunting = _block.AllowShunting;

        SelectedSignalLeft = FindDirectionalSignalOption(_block.SignalLeftId);
        SelectedSignalRight = FindDirectionalSignalOption(_block.SignalRightId);
        SelectedSignalUp = FindDirectionalSignalOption(_block.SignalUpId);
        SelectedSignalDown = FindDirectionalSignalOption(_block.SignalDownId);

        RevalidateAllSignalDirections();
        RaiseDirectionalHighlightChanged();
        
        // Načítaj indikátory
        Indicators.Clear();
        foreach (var indicator in _block.Indicators)
        {
            var vm = new BlockIndicatorViewModel(indicator, lengthMm, CanvasWidth);
            Indicators.Add(vm);
        }
        HasIndicators = Indicators.Count > 0;
        
        UpdateScales();
    }

    private DirectionalSignalOption? FindDirectionalSignalOption(string? signalId)
    {
        if (string.IsNullOrWhiteSpace(signalId))
            return DirectionalSignalItems.FirstOrDefault();

        var normalized = signalId.Trim();
        return DirectionalSignalItems.FirstOrDefault(s =>
                   string.Equals(s.Id, normalized, StringComparison.OrdinalIgnoreCase))
               ?? DirectionalSignalItems.FirstOrDefault();
    }

    partial void OnLengthMmChanged(int value) => UpdateScales();

    private void UpdateScales()
    {
        UpperTicks.Clear();
        LowerTicks.Clear();

        if (lengthMm <= 0) return;

        double totalM = lengthMm / 100.0;
        
        // Rozumný krok: do 150m po 5m/20m, nad 150m po 10m/50m
        double tickStep = totalM > 150 ? 10 : 5;
        double labelStep = totalM > 150 ? 50 : 20;

        for (double m = 0; m <= totalM; m += tickStep)
        {
            double xPos = (m / totalM) * CanvasWidth;
            bool isMajor = Math.Abs(m % labelStep) < 0.001;
            string labelText = isMajor ? $"{(int)m}m" : string.Empty;

            // Horná (A->B): 0m vľavo
            UpperTicks.Add(new TickInfo { X = xPos, Label = labelText, Height = isMajor ? 15 : 8 });

            // Dolná (B->A): 0m vpravo
            LowerTicks.Add(new TickInfo { X = CanvasWidth - xPos, Label = labelText, Height = isMajor ? 15 : 8 });
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // ══ COMMANDY PRE MARKERY V INDIKÁTOROCH ═══════════════════════════════
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pridá marker do vybraného indikátora
    /// </summary>
    [RelayCommand]
    private void AddMarkerToSelectedIndicator(string markerTypeAndDirection)
    {
        if (SelectedIndicator == null || !HasIndicators) return;
        
        // Parse "FwdDistance" -> Type=Distance, Direction=Forward
        bool isForward = markerTypeAndDirection.StartsWith("Fwd");
        string typeStr = markerTypeAndDirection.Replace("Fwd", "").Replace("Bwd", "");
        
        if (!Enum.TryParse<MarkerType>(typeStr, out var type)) return;
        var direction = isForward ? MarkerDirection.Forward : MarkerDirection.Backward;
        
        // Pridaj marker na začiatok indikátora (pozícia 0 relatívne k indikátoru)
        var marker = SelectedIndicator.AddMarker(type, direction, 0);
        
        // Vyber nový marker
        SelectedMarker = marker;
        
        // Notifikuj UI o zmene markerov
        OnPropertyChanged(nameof(SelectedIndicatorMarkers));
    }

    /// <summary>
    /// Zmaže vybraný marker
    /// </summary>
    [RelayCommand]
    private void DeleteSelectedMarker()
    {
        if (SelectedIndicator == null || SelectedMarker == null) return;
        
        SelectedIndicator.RemoveMarker(SelectedMarker.Id);
        SelectedMarker = null;
        
        OnPropertyChanged(nameof(SelectedIndicatorMarkers));
        OnPropertyChanged(nameof(AreMarkersEnabled));
        OnPropertyChanged(nameof(AreMarkerPropertiesEnabled));
        OnPropertyChanged(nameof(AreSignalAndShuntingEnabled));
    }

    /// <summary>
    /// Vyber marker v indikátore
    /// </summary>
    [RelayCommand]
    private void SelectMarkerInIndicator(IndicatorMarkerViewModel marker)
    {
        SelectedMarker = marker;
        
        // Notifikuj zmeny enabled/disabled stavov
        OnPropertyChanged(nameof(AreMarkersEnabled));
        OnPropertyChanged(nameof(AreMarkerPropertiesEnabled));
        OnPropertyChanged(nameof(AreSignalAndShuntingEnabled));
    }

    /// <summary>
    /// Notifikuj zmenu pozície vybraného markera (volaná z code-behind pri drag)
    /// </summary>
    public void NotifySelectedMarkerPositionChanged()
    {
        OnPropertyChanged(nameof(SelectedMarkerPositionCm));
    }

    // ══════════════════════════════════════════════════════════════════════
    // ══ SAVE / CANCEL ══════════════════════════════════════════════════════
    // ══════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void Save()
    {
        _block.Label            = BlockName;
        _block.lengthMm         = lengthMm;
        _block.RequestYellow    = RequestYellow;
        _block.MaxSpeedKmh      = MaxSpeedKmh;
        _block.ResSpeedKmh      = ResSpeedKmh;
        _block.AllowForward     = AllowForward;
        _block.AllowBackward    = AllowBackward;
        _block.CriticalSection  = CriticalSection;
        _block.MaxTrainlengthMm = MaxTrainlengthMm;
        
        _block.FwdDistanceCm    = FwdDistanceCm;
        _block.FwdBrakingCm     = FwdBrakingCm;
        _block.FwdStopCm        = FwdStopCm;
        _block.FwdActionCm      = FwdActionCm;
        _block.BwdDistanceCm    = BwdDistanceCm;
        _block.BwdBrakingCm     = BwdBrakingCm;
        _block.BwdStopCm        = BwdStopCm;
        _block.BwdActionCm      = BwdActionCm;
        
        _block.FwdDistanceEndCm = FwdDistanceEndCm;
        _block.FwdBrakingEndCm  = FwdBrakingEndCm;
        _block.FwdStopEndCm     = FwdStopEndCm;
        _block.FwdActionEndCm   = FwdActionEndCm;
        _block.BwdDistanceEndCm = BwdDistanceEndCm;
        _block.BwdBrakingEndCm  = BwdBrakingEndCm;
        _block.BwdStopEndCm     = BwdStopEndCm;
        _block.BwdActionEndCm   = BwdActionEndCm;
        
        _block.SignalType = SelectedSignalType switch
        {
            "Dvojstavové"  => BlockSignalType.Dvojstavove,
            "Trojstavové"  => BlockSignalType.Trojstavove,
            "Štvorstavové" => BlockSignalType.Stvorstavove,
            "Päťstavové"   => BlockSignalType.Patstavove,
            _              => BlockSignalType.Dvojstavove,
        };
        _block.StopPosition = SelectedStopPosition switch
        {
            "Stred vlaku"  => BlockStopPosition.StredVlaku,
            "Koniec vlaku" => BlockStopPosition.KoncaVlaku,
            _              => BlockStopPosition.CeloVlaku,
        };
        _block.AllowShunting = AllowShunting;
        _block.SignalLeftId = NormalizeSignalId(SelectedSignalLeft?.Id);
        _block.SignalRightId = NormalizeSignalId(SelectedSignalRight?.Id);
        _block.SignalUpId = NormalizeSignalId(SelectedSignalUp?.Id);
        _block.SignalDownId = NormalizeSignalId(SelectedSignalDown?.Id);
        
        // Uložiť indikátory
        _block.Indicators.Clear();
        foreach (var vm in Indicators)
        {
            _block.Indicators.Add(vm.GetModel());
        }
        
        CloseRequested?.Invoke(true);
    }

    private static string? NormalizeSignalId(string? signalId)
        => string.IsNullOrWhiteSpace(signalId) ? null : signalId.Trim();

    [RelayCommand]
    private void Cancel()
    {
        DirectionalSignalsHighlightRequested?.Invoke(Array.Empty<string>());
        CloseRequested?.Invoke(false);
    }

    private void RaiseDirectionalHighlightChanged()
    {
        var ids = new[] { SelectedSignalLeft?.Id, SelectedSignalRight?.Id, SelectedSignalUp?.Id, SelectedSignalDown?.Id }
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

        DirectionalSignalsHighlightRequested?.Invoke(ids);
    }

    private void RevalidateAllSignalDirections()
    {
        var checks = new (NavigationDirection Direction, string? SignalId)[]
        {
            (NavigationDirection.Left, SelectedSignalLeft?.Id),
            (NavigationDirection.Right, SelectedSignalRight?.Id),
            (NavigationDirection.Up, SelectedSignalUp?.Id),
            (NavigationDirection.Down, SelectedSignalDown?.Id),
        };

        foreach (var check in checks)
        {
            ValidateSignalDirection(check.Direction, check.SignalId);
            if (HasSignalDirectionWarning)
                return;
        }

        SignalDirectionWarning = string.Empty;
    }

    private void ValidateSignalDirection(NavigationDirection direction, string? signalId)
    {
        if (string.IsNullOrWhiteSpace(signalId) || !_signalsById.TryGetValue(signalId, out var signal))
        {
            SignalDirectionWarning = string.Empty;
            return;
        }

        var facingDirection = ResolveSignalFacingDirection(signal.Rotation);
        if (facingDirection == direction)
        {
            SignalDirectionWarning = string.Empty;
            return;
        }

        SignalDirectionWarning = $"Varovanie: orientácia návestidla '{(string.IsNullOrWhiteSpace(signal.Label) ? signal.Id[..Math.Min(8, signal.Id.Length)] : signal.Label)}' pravdepodobne nezodpovedá smeru {DirectionToText(direction)}.";
    }

    private static NavigationDirection ResolveSignalFacingDirection(double rotation)
    {
        int rightAngle = ((int)Math.Round(rotation) % 360 + 360) % 360;
        rightAngle = ((rightAngle + 45) / 90) * 90 % 360;

        return rightAngle switch
        {
            90 => NavigationDirection.Right,
            180 => NavigationDirection.Down,
            270 => NavigationDirection.Left,
            _ => NavigationDirection.Up
        };
    }

    private static string DirectionToText(NavigationDirection direction)
        => direction switch
        {
            NavigationDirection.Left => "doľava",
            NavigationDirection.Right => "doprava",
            NavigationDirection.Up => "nahor",
            NavigationDirection.Down => "nadol",
            _ => "?"
        };

    // ══════════════════════════════════════════════════════════════════════
    // ── SPRÁVA INDIKÁTOROV ───────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pridá nový indikátor. Pri prvom vytvorí celý blok, ďalšie binárne delia vybraný.
    /// </summary>
    [RelayCommand]
    private void AddIndicator(BlockIndicatorType type)
    {
        if (lengthMm <= 0) return;

        if (Indicators.Count == 0)
        {
            // Prvý indikátor - pokryje celý blok
            var indicator = new BlockIndicator
            {
                Type = type,
                Name = GenerateIndicatorName(type, 1),
                StartCm = 0,
                EndCm = lengthMm,
                IsSelected = true
            };
            var vm = new BlockIndicatorViewModel(indicator, lengthMm, CanvasWidth);
            Indicators.Add(vm);
            SelectedIndicator = vm; // NASTAVENIE VYBRANÉHO INDIKÁTORA
        }
        else
        {
            // Binárne delenie vybraného indikátora
            var selected = Indicators.FirstOrDefault(i => i.IsSelected) ?? Indicators.Last();
            int mid = (selected.StartCm + selected.EndCm) / 2;
            int originalEndCm = selected.EndCm; // Ulož pôvodný koniec
            
            // Zmenši pôvodný indikátor na prvú polovicu
            selected.EndCm = mid;
            
            // Vytvor druhý indikátor na druhú polovicu
            int indicatorNumber = Indicators.Count + 1;
            var newIndicator = new BlockIndicator
            {
                Type = type,
                Name = GenerateIndicatorName(type, indicatorNumber),
                StartCm = mid,
                EndCm = originalEndCm, // Použij uložený pôvodný koniec
                IsSelected = true
            };
            
            // Zruš výber pôvodného
            selected.IsSelected = false;
            
            var vm = new BlockIndicatorViewModel(newIndicator, lengthMm, CanvasWidth);
            Indicators.Add(vm);
            SelectedIndicator = vm; // NASTAVENIE VYBRANÉHO INDIKÁTORA
            
            // Rozdeľ markery podľa pozície
            RedistributeMarkersAfterSplit(selected.Id, vm.Id, mid);
        }
        
        HasIndicators = Indicators.Count > 0;
    }

    /// <summary>
    /// Generuje názov indikátora podľa vzoru: "Typ indikátora + Názov bloku + poradové číslo".
    /// </summary>
    private string GenerateIndicatorName(BlockIndicatorType type, int number)
    {
        string typeName = type switch
        {
            BlockIndicatorType.Contact => "Kontaktný indikátor",
            BlockIndicatorType.Flagman => "Flagman",
            BlockIndicatorType.Virtual => "Virtuálny kontakt",
            _ => "Indikátor"
        };
        
        string blockName = string.IsNullOrWhiteSpace(BlockName) ? "Blok" : BlockName;
        return $"{typeName} {blockName}-{number}";
    }

    /// <summary>
    /// Aktualizuje názvy všetkých indikátorov po zmene názvu bloku.
    /// </summary>
    private void UpdateIndicatorNames()
    {
        for (int i = 0; i < Indicators.Count; i++)
        {
            var indicator = Indicators[i];
            var model = indicator.GetModel();
            int number = i + 1;
            
            // Vygeneruj nový názov
            string newName = GenerateIndicatorName(model.Type, number);
            
            // Aktualizuj názov v modeli
            model.Name = newName;
            
            // Notifikuj zmenu pre ToolTip
            OnPropertyChanged(nameof(Indicators));
        }
    }

    /// <summary>
    /// Zmení výber indikátora.
    /// </summary>
    public void SelectIndicator(Guid indicatorId)
    {
        foreach (var ind in Indicators)
        {
            ind.IsSelected = ind.Id == indicatorId;
            if (ind.IsSelected)
            {
                SelectedIndicator = ind;
            }
        }
        
        // Refresh markerov vybraného indikátora
        OnPropertyChanged(nameof(SelectedIndicatorMarkers));
        
        // Zruš výber markeru
        SelectedMarker = null;
        
        // Notifikuj zmeny enabled/disabled stavov
        OnPropertyChanged(nameof(AreMarkersEnabled));
        OnPropertyChanged(nameof(AreMarkerPropertiesEnabled));
        OnPropertyChanged(nameof(AreSignalAndShuntingEnabled));
    }

    /// <summary>
    /// Zmaže vybraný indikátor a jeho markery.
    /// </summary>
    [RelayCommand]
    private void DeleteSelectedIndicator()
    {
        var selected = Indicators.FirstOrDefault(i => i.IsSelected);
        if (selected == null) return;

        // Zmaž všetky markery patriace tomuto indikátoru
        DeleteMarkersForIndicator(selected.Id);
        
        Indicators.Remove(selected);
        
        HasIndicators = Indicators.Count > 0;
    }

    /// <summary>
    /// Prepočíta pozície markerov pri zmene šírky indikátora.
    /// </summary>
    public void RecalculateMarkersForIndicator(Guid indicatorId, int oldStartCm, int oldEndCm)
    {
        var indicator = Indicators.FirstOrDefault(i => i.Id == indicatorId);
        if (indicator == null) return;

        double oldWidth = oldEndCm - oldStartCm;
        double newWidth = indicator.EndCm - indicator.StartCm;
        if (oldWidth <= 0) return;

        double scale = newWidth / oldWidth;

        // Prepočítaj všetky markery patriace tomuto indikátoru
        // Forward markery
        if (FwdDistanceActive && GetMarkerIndicatorId("FwdDistance") == indicatorId)
        {
            int relativePos = FwdDistanceCm - oldStartCm;
            FwdDistanceCm = indicator.StartCm + (int)(relativePos * scale);
        }
        if (FwdBrakingActive && GetMarkerIndicatorId("FwdBraking") == indicatorId)
        {
            int relativePos = FwdBrakingCm - oldStartCm;
            FwdBrakingCm = indicator.StartCm + (int)(relativePos * scale);
        }
        if (FwdStopActive && GetMarkerIndicatorId("FwdStop") == indicatorId)
        {
            int relativePos = FwdStopCm - oldStartCm;
            FwdStopCm = indicator.StartCm + (int)(relativePos * scale);
        }
        if (FwdActionActive && GetMarkerIndicatorId("FwdAction") == indicatorId)
        {
            int relativePos = FwdActionCm - oldStartCm;
            FwdActionCm = indicator.StartCm + (int)(relativePos * scale);
        }

        // Backward markery
        if (BwdDistanceActive && GetMarkerIndicatorId("BwdDistance") == indicatorId)
        {
            int relativePos = BwdDistanceCm - oldStartCm;
            BwdDistanceCm = indicator.StartCm + (int)(relativePos * scale);
        }
        if (BwdBrakingActive && GetMarkerIndicatorId("BwdBraking") == indicatorId)
        {
            int relativePos = BwdBrakingCm - oldStartCm;
            BwdBrakingCm = indicator.StartCm + (int)(relativePos * scale);
        }
        if (BwdStopActive && GetMarkerIndicatorId("BwdStop") == indicatorId)
        {
            int relativePos = BwdStopCm - oldStartCm;
            BwdStopCm = indicator.StartCm + (int)(relativePos * scale);
        }
        if (BwdActionActive && GetMarkerIndicatorId("BwdAction") == indicatorId)
        {
            int relativePos = BwdActionCm - oldStartCm;
            BwdActionCm = indicator.StartCm + (int)(relativePos * scale);
        }
    }

    /// <summary>
    /// Rozdelí markery medzi dva indikátory pri binárnom delení.
    /// </summary>
    private void RedistributeMarkersAfterSplit(Guid firstIndicatorId, Guid secondIndicatorId, int splitPositionCm)
    {
        // Rozdeľ Forward markery
        if (FwdDistanceActive)
        {
            var indicatorId = FwdDistanceCm < splitPositionCm ? firstIndicatorId : secondIndicatorId;
            SetMarkerIndicatorId("FwdDistance", indicatorId);
        }
        if (FwdBrakingActive)
        {
            var indicatorId = FwdBrakingCm < splitPositionCm ? firstIndicatorId : secondIndicatorId;
            SetMarkerIndicatorId("FwdBraking", indicatorId);
        }
        if (FwdStopActive)
        {
            var indicatorId = FwdStopCm < splitPositionCm ? firstIndicatorId : secondIndicatorId;
            SetMarkerIndicatorId("FwdStop", indicatorId);
        }
        if (FwdActionActive)
        {
            var indicatorId = FwdActionCm < splitPositionCm ? firstIndicatorId : secondIndicatorId;
            SetMarkerIndicatorId("FwdAction", indicatorId);
        }

        // Rozdeľ Backward markery
        if (BwdDistanceActive)
        {
            var indicatorId = BwdDistanceCm < splitPositionCm ? firstIndicatorId : secondIndicatorId;
            SetMarkerIndicatorId("BwdDistance", indicatorId);
        }
        if (BwdBrakingActive)
        {
            var indicatorId = BwdBrakingCm < splitPositionCm ? firstIndicatorId : secondIndicatorId;
            SetMarkerIndicatorId("BwdBraking", indicatorId);
        }
        if (BwdStopActive)
        {
            var indicatorId = BwdStopCm < splitPositionCm ? firstIndicatorId : secondIndicatorId;
            SetMarkerIndicatorId("BwdStop", indicatorId);
        }
        if (BwdActionActive)
        {
            var indicatorId = BwdActionCm < splitPositionCm ? firstIndicatorId : secondIndicatorId;
            SetMarkerIndicatorId("BwdAction", indicatorId);
        }
    }

    /// <summary>
    /// Zmaže všetky markery patriace danému indikátoru.
    /// </summary>
    private void DeleteMarkersForIndicator(Guid indicatorId)
    {
        if (GetMarkerIndicatorId("FwdDistance") == indicatorId) { FwdDistanceActive = false; FwdDistanceCm = 0; }
        if (GetMarkerIndicatorId("FwdBraking") == indicatorId) { FwdBrakingActive = false; FwdBrakingCm = 0; }
        if (GetMarkerIndicatorId("FwdStop") == indicatorId) { FwdStopActive = false; FwdStopCm = 0; }
        if (GetMarkerIndicatorId("FwdAction") == indicatorId) { FwdActionActive = false; FwdActionCm = 0; }
        
        if (GetMarkerIndicatorId("BwdDistance") == indicatorId) { BwdDistanceActive = false; BwdDistanceCm = 0; }
        if (GetMarkerIndicatorId("BwdBraking") == indicatorId) { BwdBrakingActive = false; BwdBrakingCm = 0; }
        if (GetMarkerIndicatorId("BwdStop") == indicatorId) { BwdStopActive = false; BwdStopCm = 0; }
        if (GetMarkerIndicatorId("BwdAction") == indicatorId) { BwdStopActive = false; BwdActionCm = 0; }
    }

    // Helper metódy pre prácu s IndicatorId
    private Guid? GetMarkerIndicatorId(string markerKey) => markerKey switch
    {
        "FwdDistance" => _block.FwdDistanceIndicatorId,
        "FwdBraking" => _block.FwdBrakingIndicatorId,
        "FwdStop" => _block.FwdStopIndicatorId,
        "FwdAction" => _block.FwdActionIndicatorId,
        "BwdDistance" => _block.BwdDistanceIndicatorId,
        "BwdBraking" => _block.BwdBrakingIndicatorId,
        "BwdStop" => _block.BwdStopIndicatorId,
        "BwdAction" => _block.BwdActionIndicatorId,
        _ => null
    };

    private void SetMarkerIndicatorId(string markerKey, Guid? indicatorId)
    {
        switch (markerKey)
        {
            case "FwdDistance": _block.FwdDistanceIndicatorId = indicatorId; break;
            case "FwdBraking": _block.FwdBrakingIndicatorId = indicatorId; break;
            case "FwdStop": _block.FwdStopIndicatorId = indicatorId; break;
            case "FwdAction": _block.FwdActionIndicatorId = indicatorId; break;
            case "BwdDistance": _block.BwdDistanceIndicatorId = indicatorId; break;
            case "BwdBraking": _block.BwdBrakingIndicatorId = indicatorId; break;
            case "BwdStop": _block.BwdStopIndicatorId = indicatorId; break;
            case "BwdAction": _block.BwdActionIndicatorId = indicatorId; break;
        }
    }
}


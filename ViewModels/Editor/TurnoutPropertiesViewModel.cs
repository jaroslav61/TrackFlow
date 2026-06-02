using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;

namespace TrackFlow.ViewModels.Editor;

/// <summary>Helper trieda pre položku DCC systému v ComboBox.</summary>
public sealed class DccSystemItem
{
    public string Name { get; init; } = "";
    public DccCentralType? Type { get; init; }
    /// <summary>Id konkrétneho efektívneho DCC profilu pre aktuálny projekt/app scope. null = „Bez pripojenia“.</summary>
    public System.Guid? ProfileId { get; init; }
}

/// <summary>Helper trieda pre položku indikátora v ListBox.</summary>
public sealed class SensorItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconPath { get; init; } = "";
    public bool IsSelected { get; set; }
    
    /// <summary>Ikona načítaná ako IImage pre Avalonia binding.</summary>
    public IImage? Icon => LoadIcon(IconPath);
    
    private static IImage? LoadIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
            
        try
        {
            var uri = new Uri(path, UriKind.Absolute);
            using var stream = AssetLoader.Open(uri);
            var bitmap = new Bitmap(stream);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}

public partial class TurnoutPropertiesViewModel : ObservableObject
{
    private readonly TurnoutElement _turnout;
    private readonly LayoutEditorViewModel? _layoutVm;
    private readonly SettingsManager? _settingsManager;
    private bool _isInitializing;

    public string WindowTitle => "Vlastnosti výhybky";

    // ── Všeobecné ────────────────────────────────────────────────────────────

    /// <summary>Typ výhybky (readonly, určený z MarkerKey).</summary>
    public string TurnoutTypeName => _turnout.MarkerKey switch
    {
        "Turnout_L" => "Výhybka ľavá",
        "Turnout_R" => "Výhybka pravá",
        "TurnoutCurve_L" => "Výhybka oblúková ľavá",
        "TurnoutCurve_R" => "Výhybka oblúková pravá",
        "TurnoutL90" => "Výhybka ľavá 90°",
        "TurnoutR90" => "Výhybka pravá 90°",
        "Turnout_Y" => "Výhybka Y",
        "Turnout_3W" => "Výhybka trojcestná",
        "DoubleSlip" => "Výhybka dvojitá krížová",
        _ => "Výhybka"
    };

    [ObservableProperty] private string turnoutName;
    [ObservableProperty] private string description;
    [ObservableProperty] private TurnoutState initialState;
    [ObservableProperty] private int turnoutLength;

    // ── Pripojenie ───────────────────────────────────────────────────────────

    public ObservableCollection<DccSystemItem> DccSystems { get; } = new();

    [ObservableProperty] private DccSystemItem? selectedDccSystem;
    [ObservableProperty] private int pulseLength;
    [ObservableProperty] private bool useDefaultPulse;
    
    // DCC adresy s validáciou
    private int _dccAddress;
    public int DccAddress
    {
        get => _dccAddress;
        set
        {
            // Uložíme raw hodnotu - validácia beží cez DccAddressError
            if (SetProperty(ref _dccAddress, value))
            {
                OnPropertyChanged(nameof(DccAddressError));
                OnPropertyChanged(nameof(HasDccAddressError));
                OnPropertyChanged(nameof(CanSave));
                if (!_isInitializing)
                    SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }
    
    /// <summary>Chybová hláška pre DccAddress (ak je mimo rozsahu 0–2048).</summary>
    public string DccAddressError
    {
        get
        {
            return DccAccessoryAddressValidator.GetValidationError(_dccAddress);
        }
    }
    
    /// <summary>Indikátor viditeľnosti chybovej hlášky pre DccAddress.</summary>
    public bool HasDccAddressError => !string.IsNullOrEmpty(DccAddressError);
    
    [ObservableProperty] private bool reverseLogic;
    
    // Explicitná property pre DccAddressTwo (druhá adresa pre trojcestné výhybky)
    private int _dccAddressTwo;
    public int DccAddressTwo
    {
        get => _dccAddressTwo;
        set
        {
            // Uložíme raw hodnotu - validácia beží cez DccAddressTwoError
            if (SetProperty(ref _dccAddressTwo, value))
            {
                OnPropertyChanged(nameof(DccAddressTwoError));
                OnPropertyChanged(nameof(HasDccAddressTwoError));
                OnPropertyChanged(nameof(CanSave));
                if (!_isInitializing)
                    SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }
    
    /// <summary>Chybová hláška pre DccAddressTwo (ak je mimo rozsahu 0–2048).</summary>
    public string DccAddressTwoError
    {
        get
        {
            return DccAccessoryAddressValidator.GetValidationError(_dccAddressTwo);
        }
    }
    
    /// <summary>Indikátor viditeľnosti chybovej hlášky pre DccAddressTwo.</summary>
    public bool HasDccAddressTwoError => !string.IsNullOrEmpty(DccAddressTwoError);

    /// <summary>Povolenie tlačidla Uložiť – false ak je niektorá DCC adresa neplatná.</summary>
    public bool CanSave => !HasDccAddressError && !HasDccAddressTwoError;

    /// <summary>Enabled pre pole Dĺžka impulzu.</summary>
    public bool PulseLengthEnabled => !UseDefaultPulse;

    /// <summary>Zobrazenie druhej DCC adresy pre trojcestné výhybky a DoubleSlip.</summary>
    public bool ShowSecondAddress => _turnout.MarkerKey == "Turnout_3W" || _turnout.MarkerKey == "DoubleSlip";
    
    // ── Indikátory ───────────────────────────────────────────────────────────

    public ObservableCollection<SensorItem> AvailableSensors { get; } = new();

    // ── Podmienky ────────────────────────────────────────────────────────────

    [ObservableProperty] private int maxSpeed;
    [ObservableProperty] private int limitedSpeed;
    [ObservableProperty] private bool requestYellow;

    // ── Príkazy ──────────────────────────────────────────────────────────────

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IAsyncRelayCommand FindAddressCommand { get; }
    public event Action<bool>? CloseRequested;

    public TurnoutPropertiesViewModel() : this(new TurnoutElement(), null, null) { }

    public TurnoutPropertiesViewModel(
        TurnoutElement turnout,
        LayoutEditorViewModel? layoutVm,
        SettingsManager? settingsManager)
    {
        _turnout = turnout;
        _layoutVm = layoutVm;
        _settingsManager = settingsManager;
        _isInitializing = true;

        // Načítanie hodnôt z modelu
        turnoutName = turnout.Label ?? "";
        description = turnout.Description;
        initialState = turnout.InitialState;
        turnoutLength = turnout.TurnoutLength;

        pulseLength = turnout.PulseLength;
        useDefaultPulse = turnout.UseDefaultPulse;
        DccAddress = turnout.DccAddress;  // Použiť property pre validáciu
        DccAddressTwo = turnout.DccAddress2;  // Použiť property pre validáciu
        reverseLogic = turnout.ReverseLogic;

        maxSpeed = turnout.MaxSpeed;
        limitedSpeed = turnout.LimitedSpeed;
        requestYellow = turnout.RequestYellow;

        // Načítať zoznamy
        LoadDccSystems();
        LoadSensors();

        // Príkazy
        SaveCommand = new RelayCommand(OnSave, () => CanSave);
        CancelCommand = new RelayCommand(OnCancel);
        FindAddressCommand = new AsyncRelayCommand(FindNextFreeAddressAsync);

        _isInitializing = false;
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnUseDefaultPulseChanged(bool value)
    {
        OnPropertyChanged(nameof(PulseLengthEnabled));
    }

    private void LoadDccSystems()
    {
        DccSystems.Clear();

        // Prvá položka – pevná „Bez pripojenia“ (predstavuje null profil).
        DccSystems.Add(new DccSystemItem { Name = "Bez pripojenia", Type = null, ProfileId = null });

        // Iba reálne aktívne/povolené DCC centrály z efektívneho scope (projekt alebo app).
        if (_settingsManager != null)
        {
            var profiles = _settingsManager.GetEffectiveEnabledDccCentralProfiles();
            for (var i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                var addr = p.Type == DccCentralType.NanoX_S88
                    ? (string.IsNullOrWhiteSpace(p.SerialPort) ? "COM?" : p.SerialPort)
                    : $"{p.Host}:{p.Port}";
                DccSystems.Add(new DccSystemItem
                {
                    Name = $"{i + 1}: {p.Type} ({addr})",
                    Type = p.Type,
                    ProfileId = p.Id
                });
            }
        }

        // Predvolený výber:
        //  1) presný profil podľa Id (nový mechanizmus),
        //  2) fallback na legacy DccSystemType,
        //  3) inak „Bez pripojenia“.
        DccSystemItem? chosen = null;
        if (_turnout.DccCentralProfileId.HasValue)
            chosen = DccSystems.FirstOrDefault(x => x.ProfileId == _turnout.DccCentralProfileId);
        if (chosen == null && _turnout.DccSystemType.HasValue)
            chosen = DccSystems.FirstOrDefault(x => x.ProfileId != null && x.Type == _turnout.DccSystemType);

        SelectedDccSystem = chosen ?? DccSystems[0];
    }


    private void LoadSensors()
    {
        AvailableSensors.Clear();

        if (_layoutVm == null) return;

        // Načítaj všetky indikátory zo všetkých blokov v layoute
        foreach (var blockElement in _layoutVm.Elements.OfType<BlockElement>())
        {
            foreach (var indicator in blockElement.Indicators)
            {
                // Ikona neaktívneho indikátora (s _d.png koncovkou)
                string iconPath = indicator.Type switch
                {
                    BlockIndicatorType.Contact => "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png",
                    BlockIndicatorType.Flagman => "avares://TrackFlow/Assets/Appicons/16/flag_d.png",
                    BlockIndicatorType.Virtual => "avares://TrackFlow/Assets/Appicons/16/virt_cont_d.png",
                    _ => "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png"
                };
                
                // Použij Id indikátora namiesto SensorElement.Id
                AvailableSensors.Add(new SensorItem
                {
                    Id = indicator.Id.ToString(), // Guid -> string
                    Name = indicator.Name,
                    IconPath = iconPath,
                    IsSelected = _turnout.DetectorLinkIds.Contains(indicator.Id.ToString())
                });
            }
        }
    }

    private void OnSave()
    {
        // Uloženie do modelu
        _turnout.Label = TurnoutName;
        _turnout.Description = Description;
        _turnout.InitialState = InitialState;
        _turnout.TurnoutLength = TurnoutLength;

        // Ulož ID profilu (nový mechanizmus) aj legacy typ pre spätnú kompatibilitu.
        _turnout.DccCentralProfileId = SelectedDccSystem?.ProfileId;
        _turnout.DccSystemType = SelectedDccSystem?.Type;
        _turnout.PulseLength = PulseLength;
        _turnout.UseDefaultPulse = UseDefaultPulse;
        _turnout.DccAddress = DccAddress;
        _turnout.DccAddress2 = DccAddressTwo;  // ✅ Správne - veľké D
        _turnout.ReverseLogic = ReverseLogic;

        // Uloženie vybraných indikátorov
        _turnout.DetectorLinkIds.Clear();
        foreach (var sensor in AvailableSensors.Where(s => s.IsSelected))
            _turnout.DetectorLinkIds.Add(sensor.Id);

        _turnout.MaxSpeed = MaxSpeed;
        _turnout.LimitedSpeed = LimitedSpeed;
        _turnout.RequestYellow = RequestYellow;

        CloseRequested?.Invoke(true);
    }

    private void OnCancel()
    {
        CloseRequested?.Invoke(false);
    }

    private System.Threading.Tasks.Task FindNextFreeAddressAsync()
    {
        if (_layoutVm == null)
            return System.Threading.Tasks.Task.CompletedTask;

        // Obsadené sú obe adresy ostatných výhybiek: hlavná aj druhá (3W/DoubleSlip).
        var usedAddresses = new HashSet<int>(
            _layoutVm.Elements
                .OfType<TurnoutElement>()
                .Where(t => t.Id != _turnout.Id)
                .SelectMany(t => new[] { t.DccAddress, t.DccAddress2 })
                .Where(DccAccessoryAddressValidator.IsAssigned));

        // Začíname od aktuálnej adresy (ak je platná), inak od 1.
        var start = DccAccessoryAddressValidator.IsAssigned(DccAddress)
            ? DccAddress
            : DccAccessoryAddressValidator.FirstAssignedAddress;

        for (var i = 0; i < DccAccessoryAddressValidator.MaxAddress; i++)
        {
            var candidate = ((start - DccAccessoryAddressValidator.FirstAssignedAddress + i)
                             % DccAccessoryAddressValidator.MaxAddress)
                            + DccAccessoryAddressValidator.FirstAssignedAddress;
            if (usedAddresses.Contains(candidate))
                continue;

            DccAddress = candidate;
            break;
        }

        return System.Threading.Tasks.Task.CompletedTask;
    }
}


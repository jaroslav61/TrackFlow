using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.Services.Dcc;

namespace TrackFlow.ViewModels.Editor;

public partial class IndicatorPropertiesViewModel : ObservableObject, IDisposable
{
    private const string ActiveTestIconUri = "avares://TrackFlow/Assets/Appicons/16/cont_ind.png";
    private const string InactiveTestIconUri = "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png";
    private static IImage? _activeTestIconImage;
    private static IImage? _inactiveTestIconImage;

    private readonly BlockIndicator _indicator;
    private readonly SettingsManager? _settingsManager;
    private readonly DispatcherTimer? _testIndicatorTimer;
    private bool _lastKnownTestIndicatorState;

    public event Action<bool>? CloseRequested;

    public string WindowTitle => $"Vlastnosti indikátora - {IndicatorName}";

    // ── Typ indikátora ─────────────────────────────────────────────────────
    public BlockIndicatorType IndicatorType { get; }

    // ── Viditeľnosť záložiek podľa typu indikátora ────────────────────────
    // Všeobecne - všetci
    public bool ShowGeneralTab => true;
    
    // Pripojenie - iba Kontaktný indikátor
    public bool ShowConnectionTab => IndicatorType == BlockIndicatorType.Contact;
    
    // Indikátory - zatiaľ nikto (reserve pre budúcnosť)
    public bool ShowIndicatorsTab => false;
    
    // Spúšťač - iba Flagman
    public bool ShowTriggerTab => IndicatorType == BlockIndicatorType.Flagman;
    
    // Podmienky - Flagman a Virtuálny kontakt
    public bool ShowConditionsTab => IndicatorType == BlockIndicatorType.Flagman || 
                                      IndicatorType == BlockIndicatorType.Virtual;
    
    // Operácie - všetci
    public bool ShowOperationsTab => true;
    
    // Reset - všetci
    public bool ShowResetTab => true;

    // ── Všeobecné vlastnosti ──────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string indicatorName = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    // ── Pripojenie (iba pre Kontaktný indikátor) ───────────────────────────
    [ObservableProperty]
    private DccSystemItem? selectedDccSystem;

    [ObservableProperty]
    private int address = 1;

    [ObservableProperty]
    private int input = 1;

    /// <summary>Zoznam dostupných DCC centrál (prvá položka = „Bez pripojenia“).</summary>
    public ObservableCollection<DccSystemItem> DccSystems { get; } = new();

    [ObservableProperty]
    private int moduleAddress;

    [ObservableProperty]
    private int portNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContactBindingWarning))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string contactBindingWarning = string.Empty;

    public bool IsTestIndicatorActive => _indicator.IsActive;

    public bool HasContactBindingWarning => !string.IsNullOrWhiteSpace(ContactBindingWarning);

    public bool CanSave => !HasContactBindingWarning;

    public string TestIconPath => IsTestIndicatorActive
        ? ActiveTestIconUri
        : InactiveTestIconUri;

    public IImage? TestIconImage => IsTestIndicatorActive
        ? (_activeTestIconImage ??= TryLoadIcon(ActiveTestIconUri))
        : (_inactiveTestIconImage ??= TryLoadIcon(InactiveTestIconUri));

    // ── Spúšťač (iba pre Flagman) ──────────────────────────────────────────
    [ObservableProperty]
    private string triggerType = "None";

    [ObservableProperty]
    private string triggerCondition = string.Empty;

    // ── Podmienky (pre Flagman a Virtuálny kontakt) ────────────────────────
    [ObservableProperty]
    private string conditionExpression = string.Empty;

    // ── Operácie (pre všetkých) ────────────────────────────────────────────
    [ObservableProperty]
    private string operationsScript = string.Empty;

    // ── Reset (pre všetkých) ───────────────────────────────────────────────
    [ObservableProperty]
    private bool autoReset = true;

    [ObservableProperty]
    private int resetDelayMs = 1000;

    // ── Konštruktor ────────────────────────────────────────────────────────
    public IndicatorPropertiesViewModel() : this(new BlockIndicator { Type = BlockIndicatorType.Contact }, null) { }

    public IndicatorPropertiesViewModel(BlockIndicator indicator) : this(indicator, null) { }

    public IndicatorPropertiesViewModel(BlockIndicator indicator, SettingsManager? settingsManager)
    {
        _indicator = indicator;
        _settingsManager = settingsManager;
        IndicatorType = indicator.Type;
        LoadDccSystems();
        LoadFromModel();

        if (IndicatorType == BlockIndicatorType.Contact)
        {
            _lastKnownTestIndicatorState = _indicator.IsActive;
            _testIndicatorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _testIndicatorTimer.Tick += (_, _) => RefreshTestIndicatorState();
            _testIndicatorTimer.Start();
            RefreshContactBindingWarning();
        }
    }

    private void RefreshTestIndicatorState()
    {
        if (_lastKnownTestIndicatorState == _indicator.IsActive)
            return;

        _lastKnownTestIndicatorState = _indicator.IsActive;
        OnPropertyChanged(nameof(IsTestIndicatorActive));
        OnPropertyChanged(nameof(TestIconPath));
        OnPropertyChanged(nameof(TestIconImage));
    }

    private static IImage? TryLoadIcon(string assetUri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(assetUri));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private void LoadDccSystems()
    {
        DccSystems.Clear();

        // Prvá položka – pevná „Bez pripojenia“ (predstavuje null profil).
        DccSystems.Add(new DccSystemItem { Name = "Bez pripojenia", Type = null, ProfileId = null });

        if (_settingsManager != null)
        {
            var profiles = _settingsManager.GetEffectiveEnabledDccCentralProfiles();
            for (var i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                var addr = p.Type == DccCentralType.NanoX_S88
                    ? (string.IsNullOrWhiteSpace(p.SerialPort) ? "COM?" : p.SerialPort)
                    : $"{p.Host}:{p.Port}";
                var typeName = DccCentralDisplayName.Get(p.Type);
                DccSystems.Add(new DccSystemItem
                {
                    Name = $"{i + 1}: {typeName} ({addr})",
                    Type = p.Type,
                    ProfileId = p.Id
                });
            }
        }
    }

    private void LoadFromModel()
    {
        IndicatorName = _indicator.Name;
        Description = _indicator.Description ?? string.Empty;
        
        // Pripojenie (iba pre Contact)
        if (IndicatorType == BlockIndicatorType.Contact)
        {
            ModuleAddress = _indicator.ModuleAddress;
            PortNumber = _indicator.PortNumber;

            // Predvolený výber DCC centrály podľa uloženého ProfileId; fallback na „Bez pripojenia“.
            SelectedDccSystem = _indicator.DccCentralProfileId.HasValue
                ? DccSystems.FirstOrDefault(x => x.ProfileId == _indicator.DccCentralProfileId) ?? DccSystems[0]
                : DccSystems[0];
        }
        
        // Spúšťač (iba pre Flagman)
        if (IndicatorType == BlockIndicatorType.Flagman)
        {
            TriggerType = _indicator.TriggerType ?? "None";
            TriggerCondition = _indicator.TriggerCondition ?? string.Empty;
        }
        
        // Podmienky (pre Flagman a Virtual)
        if (IndicatorType == BlockIndicatorType.Flagman || IndicatorType == BlockIndicatorType.Virtual)
        {
            ConditionExpression = _indicator.ConditionExpression ?? string.Empty;
        }
        
        // Operácie (všetci)
        OperationsScript = _indicator.OperationsScript ?? string.Empty;
        
        // Reset (všetci)
        AutoReset = _indicator.AutoReset;
        ResetDelayMs = _indicator.ResetDelayMs;
    }

    partial void OnSelectedDccSystemChanged(DccSystemItem? value)
        => RefreshContactBindingWarning();

    partial void OnModuleAddressChanged(int value)
        => RefreshContactBindingWarning();

    partial void OnPortNumberChanged(int value)
        => RefreshContactBindingWarning();

    private void RefreshContactBindingWarning()
    {
        if (IndicatorType != BlockIndicatorType.Contact || _settingsManager?.CurrentProject?.Layout == null)
        {
            ContactBindingWarning = string.Empty;
            return;
        }

        if (SelectedDccSystem?.ProfileId == null || ModuleAddress < 1 || PortNumber < 1)
        {
            ContactBindingWarning = string.Empty;
            return;
        }

        var duplicates = _settingsManager.CurrentProject.Layout.Elements
            .OfType<BlockElement>()
            .SelectMany(block => block.Indicators
                .Where(static indicator => indicator.Type == BlockIndicatorType.Contact)
                .Where(indicator => indicator.Id != _indicator.Id
                    && indicator.DccCentralProfileId == SelectedDccSystem.ProfileId
                    && indicator.ModuleAddress == ModuleAddress
                    && indicator.PortNumber == PortNumber)
                .Select(indicator => new
                {
                    BlockLabel = string.IsNullOrWhiteSpace(block.Label) ? block.Id : block.Label,
                    IndicatorName = string.IsNullOrWhiteSpace(indicator.Name) ? indicator.Id.ToString() : indicator.Name
                }))
            .ToList();

        ContactBindingWarning = duplicates.Count == 0
            ? string.Empty
            : $"Rovnaký vstup už používa {string.Join(", ", duplicates.Select(x => $"{x.BlockLabel} / {x.IndicatorName}"))}. Jeden DCC vstup môže byť priradený iba jednému kontaktnému indikátoru.";
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanSave)
            return;

        _indicator.Name = IndicatorName;
        _indicator.Description = Description;
        
        // Pripojenie (iba pre Contact)
        if (IndicatorType == BlockIndicatorType.Contact)
        {
            _indicator.ModuleAddress = ModuleAddress;
            _indicator.PortNumber = PortNumber;
            _indicator.DccCentralProfileId = SelectedDccSystem?.ProfileId;
        }
        
        // Spúšťač (iba pre Flagman)
        if (IndicatorType == BlockIndicatorType.Flagman)
        {
            _indicator.TriggerType = TriggerType;
            _indicator.TriggerCondition = TriggerCondition;
        }
        
        // Podmienky (pre Flagman a Virtual)
        if (IndicatorType == BlockIndicatorType.Flagman || IndicatorType == BlockIndicatorType.Virtual)
        {
            _indicator.ConditionExpression = ConditionExpression;
        }
        
        // Operácie (všetci)
        _indicator.OperationsScript = OperationsScript;
        
        // Reset (všetci)
        _indicator.AutoReset = AutoReset;
        _indicator.ResetDelayMs = ResetDelayMs;
        
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    public void Dispose()
    {
        _testIndicatorTimer?.Stop();
    }
}


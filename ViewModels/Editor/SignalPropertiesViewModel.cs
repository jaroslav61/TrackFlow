using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrackFlow.Models.Layout;
using TrackFlow.Services;

namespace TrackFlow.ViewModels.Editor;

public sealed class SignalBlockOption
{
    public string? Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}

public sealed class SignalSystemOption
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public sealed class SignalProfileOption
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public partial class SignalPropertiesViewModel : ObservableObject
{
    private readonly SignalElement _signal;
    private bool _isBasicMode = true;

    public event Action<bool>? CloseRequested;

    public ObservableCollection<SignalBlockOption> AvailableBlocks { get; } = new();
    public ObservableCollection<SignalSystemOption> AvailableSystems { get; } = new();
    public ObservableCollection<SignalProfileOption> AvailableProfiles { get; } = new();

    public string WindowTitle => string.IsNullOrWhiteSpace(SignalName)
        ? "Vlastnosti signálu"
        : $"Signál - {SignalName}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string signalName = string.Empty;

    [ObservableProperty]
    private int dccAddress;

    [ObservableProperty]
    private SignalBlockOption? selectedProtectedBlock;

    [ObservableProperty]
    private SignalSystemOption? selectedSystem;

    [ObservableProperty]
    private SignalProfileOption? selectedProfile;

    /// <summary>
    /// Režim dekodéra: true = základný režim (4 adresy), false = rozšírený režim.
    /// Setter notifikuje aj IsExtendedMode pre prepínanie panelov v UI.
    /// </summary>
    public bool IsBasicMode
    {
        get => _isBasicMode;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isBasicMode, value))
                this.RaisePropertyChanged(nameof(IsExtendedMode));
        }
    }

    /// <summary>
    /// Read-only negácia IsBasicMode pre pohodlný binding viditeľnosti panelov.
    /// </summary>
    public bool IsExtendedMode => !IsBasicMode;

    public string DccAddressError
        => DccAccessoryAddressValidator.GetValidationError(DccAddress);

    public bool HasDccAddressError => !string.IsNullOrEmpty(DccAddressError);

    public bool CanSave => !HasDccAddressError;

    // ── Konštruktory ─────────────────────────────────────────────────────────

    /// <summary>Skrátený konštruktor – bez dostupných sústav (backward-compat).</summary>
    public SignalPropertiesViewModel(SignalElement signal, IEnumerable<BlockElement> blocks)
        : this(signal, blocks, Enumerable.Empty<SignalSystemDefinition>())
    {
    }

    /// <summary>Plný konštruktor – s dostupnými sústavami z projektu.</summary>
    public SignalPropertiesViewModel(
        SignalElement signal,
        IEnumerable<BlockElement> blocks,
        IEnumerable<SignalSystemDefinition> signalSystems)
    {
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));

        // ── Bloky ─────────────────────────────────────────────────────────
        AvailableBlocks.Add(new SignalBlockOption
        {
            Id = null,
            DisplayName = "-- bez väzby na blok --"
        });
        foreach (var block in blocks.OrderBy(b => b.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            var label = string.IsNullOrWhiteSpace(block.Label)
                ? $"Blok {block.Id[..Math.Min(8, block.Id.Length)]}"
                : block.Label;
            AvailableBlocks.Add(new SignalBlockOption { Id = block.Id, DisplayName = label });
        }

        // ── Sústavy ───────────────────────────────────────────────────────
        // Kombinuj zabudované + projektové (bez duplikátov).
        var builtinIds = new HashSet<string>(SignalSystemRegistry.BuiltinSystems.Select(s => s.Id));
        var allSystems = SignalSystemRegistry.BuiltinSystems
            .Concat(signalSystems.Where(s => !builtinIds.Contains(s.Id)))
            .ToList();

        foreach (var sys in allSystems)
            AvailableSystems.Add(new SignalSystemOption { Id = sys.Id, DisplayName = sys.Name });

        // ── Inicializuj hodnoty ───────────────────────────────────────────
        SignalName = signal.Label;
        DccAddress = signal.DccAddress;
        IsBasicMode = signal.IsBasicMode;
        SelectedProtectedBlock = AvailableBlocks.FirstOrDefault(b => b.Id == signal.ProtectsBlockId)
                                 ?? AvailableBlocks[0];

        // Vyber sústavu
        var systemId = string.IsNullOrWhiteSpace(signal.SignalSystemId)
            ? SignalSystemDefinition.DefaultSystemId
            : signal.SignalSystemId;
        SelectedSystem = AvailableSystems.FirstOrDefault(s => s.Id == systemId)
                         ?? AvailableSystems.FirstOrDefault();

        // Profily sa načítajú cez OnSelectedSystemChanged → zavolaj ručne
        RefreshProfiles(systemId, signal.SignalProfile);
    }

    // ── Reakcie na zmeny ─────────────────────────────────────────────────────

    partial void OnDccAddressChanged(int value)
    {
        _ = value;
        OnPropertyChanged(nameof(DccAddressError));
        OnPropertyChanged(nameof(HasDccAddressError));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSystemChanged(SignalSystemOption? value)
    {
        RefreshProfiles(value?.Id, null);
    }

    private void RefreshProfiles(string? systemId, string? currentProfileId)
    {
        AvailableProfiles.Clear();

        if (string.IsNullOrEmpty(systemId))
            return;

        var profiles = SignalSystemRegistry.GetProfiles(systemId);
        foreach (var p in profiles)
            AvailableProfiles.Add(new SignalProfileOption { Id = p.Id, DisplayName = p.DisplayName });

        // Zvol aktuálny profil, alebo prvý dostupný
        SelectedProfile = AvailableProfiles.FirstOrDefault(p => p.Id == currentProfileId)
                          ?? AvailableProfiles.FirstOrDefault();
    }

    // ── Príkazy ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _signal.Label = SignalName.Trim();
        _signal.DccAddress = DccAddress;
        _signal.IsBasicMode = IsBasicMode;
        _signal.ProtectsBlockId = SelectedProtectedBlock?.Id;
        _signal.SignalSystemId = SelectedSystem?.Id ?? SignalSystemDefinition.DefaultSystemId;
        _signal.SignalProfile = SelectedProfile?.Id;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    private bool RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    private void RaisePropertyChanged(string propertyName)
    {
        OnPropertyChanged(propertyName);
    }
}

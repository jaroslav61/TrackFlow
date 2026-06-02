using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TrackFlow.Models;
using TrackFlow.Services.Dcc;

namespace TrackFlow.ViewModels;

public partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty]
    private string message = "Systém je pripravený";

    [ObservableProperty]
    private string rightHint = "";

    [ObservableProperty]
    private bool isDccConnected;

    // ── Neuložené zmeny projektu (Step 10) ──────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirtyHint))]
    private bool isProjectDirty;

    /// <summary>Krátky text pre status bar, ak má projekt neuložené zmeny.</summary>
    public string DirtyHint => IsProjectDirty ? "Neuložené zmeny" : string.Empty;

    // Jednoduché riešenie bez triggerov/converterov – XAML si zoberie farbu priamo zo stringu.
    public string DccLedColor => IsDccConnected ? "#00C853" : "#D50000";

    partial void OnIsDccConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(DccLedColor));
    }
    
    // ── Režim aplikácie (Editor / Prevádzka) ────────────────────────────────
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeText))]
    [NotifyPropertyChangedFor(nameof(ModeBarColor))]
    [NotifyPropertyChangedFor(nameof(ModeIconColor))]
    private bool isOperationMode;
    
    /// <summary>Text režimu zobrazený v StatusBare.</summary>
    public string ModeText => IsOperationMode ? "🔴 PREVÁDZKA" : "✏️ EDITOR";
    
    /// <summary>Farba pozadia StatusBaru podľa režimu.</summary>
    public string ModeBarColor => IsOperationMode ? "#2E7D32" : "#1565C0"; // Zelená / Modrá
    
    /// <summary>Farba ikony režimu.</summary>
    public string ModeIconColor => IsOperationMode ? "#C8E6C9" : "#BBDEFB"; // Svetlo zelená / svetlo modrá
    
    /// <summary>Nastaví režim prevádzky alebo editora.</summary>
    public void SetOperationMode(bool isOperation)
    {
        IsOperationMode = isOperation;
    }

    // ── Multi-centrála: zoznam položiek pre StatusBar ────────────────────────────────────────

    public ObservableCollection<StatusBarCentralItem> CentralItems { get; } = new();

    private bool _hasNoDigitalSystems = true;
    public bool HasNoDigitalSystems
    {
        get => _hasNoDigitalSystems;
        private set
        {
            if (SetProperty(ref _hasNoDigitalSystems, value))
                OnPropertyChanged(nameof(HasDigitalSystems));
        }
    }

    public bool HasDigitalSystems => !HasNoDigitalSystems;

    /// <summary>
    /// Prestaví zoznam centrál v stavovom riadku.
    /// Volať po zmene nastavení, po Connect/Disconnect aj pri štarte.
    /// </summary>
    /// <param name="service">
    /// Živá inštancia <see cref="DccConnectionService"/>. Každá vytvorená položka sa
    /// na ňu zaháčkuje cez <see cref="StatusBarCentralItem.Bind"/> a pri každom
    /// budúcom <c>Connected</c>/<c>Disconnected</c> sa SAMA pre-attachne na telemetriu.
    /// Vďaka tomu nezáleží, či je centrála pripojená v momente vytvorenia položky.
    /// </param>
    /// <param name="telemetryResolver">
    /// Resolver, ktorý pre daný ProfileId vráti živý <see cref="IDccTelemetry"/>
    /// (typicky <c>Z21Client</c>). Pre profily bez aktívneho klienta vracia <c>null</c>.
    /// </param>
    public void UpdateCentrals(
        IReadOnlyList<DccCentralProfile> profiles,
        IReadOnlyCollection<Guid> connectedProfileIds,
        IReadOnlyCollection<Guid>? reconnectingProfileIds = null,
        DccConnectionService? service = null,
        Func<Guid, IDccTelemetry?>? telemetryResolver = null,
        Func<bool>? isTelemetryVisible = null)
    {
        var enabledProfiles = profiles.Where(p => p.IsEnabled).ToList();

        // Dispose starých položiek (odhlási globálne handlery + odpojí telemetriu).
        foreach (var old in CentralItems)
            old.Dispose();

        CentralItems.Clear();

        if (enabledProfiles.Count == 0)
        {
            HasNoDigitalSystems = true;
            return;
        }

        HasNoDigitalSystems = false;

        for (int i = 0; i < enabledProfiles.Count; i++)
        {
            var p = enabledProfiles[i];
            var isConnected    = connectedProfileIds.Any(id => id == p.Id);
            var isReconnecting = !isConnected && (reconnectingProfileIds?.Any(id => id == p.Id) ?? false);
            var item = new StatusBarCentralItem
            {
                ProfileId                    = p.Id,
                Name                         = DccCentralDisplayName.Get(p.Type),
                IsLast                       = i == enabledProfiles.Count - 1,
                MainTrackCurrentLimitAmperes = p.MainTrackCurrentLimitAmperes
            };
            item.IsConnected    = isConnected;
            item.IsReconnecting = isReconnecting;
            item.SetTelemetryVisibilityProvider(isTelemetryVisible);

            // KĽÚČOVÉ: položka sa SAMA zachytí na globálny ConnectionStateChanged
            // event a pri každom Connected/Disconnected pre svoj profil sa pre-attachne.
            // Bind() okamžite tiež spustí prvotný resolve, takže ak je centrála už
            // pripojená v čase rebuildu, telemetria sa pripojí ihneď.
            if (service != null && telemetryResolver != null)
                item.Bind(service, telemetryResolver);

            CentralItems.Add(item);
        }
    }
}

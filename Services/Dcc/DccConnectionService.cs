using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models;
using System.Diagnostics;

namespace TrackFlow.Services.Dcc;

public enum DccConnectionChangeKind
{
    Connected,
    Disconnected,
    Reconnecting,
    ConnectFailed,
    ClientChanged
}

public readonly record struct DccConnectionStateChange(
    DccConnectionChangeKind Kind,
    DccCentralType Type,
    bool IsConnected,
    uint? Serial,
    string Reason
)
{
    /// <summary>
    /// Non-null when the event was raised by a multi-central connection.
    /// Null for legacy single-central path.
    /// </summary>
    public Guid? ProfileId { get; init; }
}

public sealed record DccMonitorOptions(
    int IntervalMs,
    int FailThreshold,
    int ReconnectIntervalMs,
    int ReconnectBurstAttempts,
    int ReconnectIdleDelayMs,
    int ReconnectMaxWindowMs)
{
    public static DccMonitorOptions Default { get; } = new(
        IntervalMs: 2500,
        FailThreshold: 2,
        ReconnectIntervalMs: 2500,
        ReconnectBurstAttempts: 3,
        ReconnectIdleDelayMs: 5000,
        ReconnectMaxWindowMs: 60_000);
}

public sealed class DccConnectionService : IDccConnectionService, IDisposable
{
    private readonly SettingsManager _settingsManager;
    private readonly Func<DccCentralType, IDccCentralClient> _clientFactory;
    private readonly Func<IDccCentralClient, DccCentralType, bool>? _clientMatchesOverride;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly int _monitorIntervalMs;
    private readonly int _monitorFailThreshold;
    private readonly int _reconnectIntervalMs;
    private readonly int _reconnectBurstAttempts;
    private readonly int _reconnectIdleDelayMs;
    private readonly int _reconnectMaxWindowMs;

    // ── Multi-central registry ────────────────────────────────────────────────
    private readonly Dictionary<Guid, PerCentralConnection> _multiConnections = new();
    private readonly HashSet<Guid> _reconnectingProfileIds = new();
    private readonly object _multiLock = new();
    private DccMonitorOptions MonitorOptions => new(
        _monitorIntervalMs, _monitorFailThreshold, _reconnectIntervalMs,
        _reconnectBurstAttempts, _reconnectIdleDelayMs, _reconnectMaxWindowMs);

    /// <summary>IDs of profiles that are currently connected (multi-central mode).</summary>
    public IReadOnlyCollection<Guid> ConnectedProfileIds
    {
        get
        {
            lock (_multiLock)
                return _multiConnections.Values
                    .Where(c => c.IsConnected)
                    .Select(c => c.ProfileId)
                    .ToHashSet();
        }
    }

    /// <summary>IDs of profiles that are currently attempting auto-reconnect.</summary>
    public IReadOnlyCollection<Guid> ReconnectingProfileIds
    {
        get
        {
            lock (_multiLock)
                return _reconnectingProfileIds.ToHashSet();
        }
    }

    /// <summary>True when any connection (single or multi) is currently active.</summary>
    public bool IsAnyConnected
    {
        get
        {
            lock (_multiLock)
                if (_multiConnections.Count > 0)
                    return _multiConnections.Values.Any(c => c.IsConnected);
            return Client.IsConnected;
        }
    }

    /// <summary>
    /// True when this service currently owns per-profile connections (created by ConnectAll/ConnectMissing).
    /// In that case Settings UI can address centrals by their profile ID.
    /// </summary>
    public bool IsMultiCentralModeActive
    {
        get
        {
            lock (_multiLock)
                return _multiConnections.Count > 0;
        }
    }

    /// <summary>
    /// Multi-central helper: returns the connected client for the given profile ID.
    /// Returns false when the profile is unknown or currently disconnected.
    /// </summary>
    public bool TryGetConnectedClient(Guid profileId, out IDccCentralClient client)
    {
        lock (_multiLock)
        {
            if (_multiConnections.TryGetValue(profileId, out var conn) && conn.IsConnected)
            {
                client = conn.Client;
                return true;
            }
        }

        client = null!;
        return false;
    }

    /// <summary>
    /// Protocol-agnostic helper: returns the live telemetry-capable abstraction for a profile,
    /// or null when the profile is currently disconnected / no telemetry surface is available.
    /// Higher layers must only query capabilities via <see cref="IDccTelemetry.IsTelemetrySupported"/>.
    /// </summary>
    public bool TryGetConnectedTelemetry(Guid profileId, out IDccTelemetry telemetry)
    {
        telemetry = null!;

        if (!TryGetConnectedClient(profileId, out var client))
            return false;

        if (client is not IDccTelemetry candidate)
            return false;

        telemetry = candidate;
        return true;
    }

    public IDccCentralClient Client { get; private set; }

    public bool IsConnected => Client.IsConnected;

    public event Action<bool>? IsConnectedChanged;

    public event Action<DccConnectionStateChange>? ConnectionStateChanged;

    public event Action<DccFeedbackStateChange>? FeedbackStateChanged;

    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;

    private bool _disposed;

    private void Raise(DccConnectionChangeKind kind, DccCentralType type, bool isConnected, uint? serial, string reason, Guid? profileId = null, bool suppressIsConnectedChanged = false)
    {
        ConnectionStateChanged?.Invoke(new DccConnectionStateChange(kind, type, isConnected, serial, reason) { ProfileId = profileId });
        if (!suppressIsConnectedChanged)
            IsConnectedChanged?.Invoke(isConnected);
    }

    private Guid? ResolveSingleCentralEventProfileId()
        => ResolveSingleCentralFeedbackProfileId();

    public DccConnectionService(
        SettingsManager settingsManager,
        Func<DccCentralType, IDccCentralClient>? clientFactory = null,
        DccMonitorOptions? monitorOptions = null,
        Func<IDccCentralClient, DccCentralType, bool>? clientMatchesOverride = null)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _clientFactory = clientFactory ?? DccClientFactory.Create;
        _clientMatchesOverride = clientMatchesOverride;

        var options = monitorOptions ?? DccMonitorOptions.Default;
        _monitorIntervalMs = options.IntervalMs;
        _monitorFailThreshold = options.FailThreshold;
        _reconnectIntervalMs = options.ReconnectIntervalMs;
        _reconnectBurstAttempts = options.ReconnectBurstAttempts;
        _reconnectIdleDelayMs = options.ReconnectIdleDelayMs;
        _reconnectMaxWindowMs = options.ReconnectMaxWindowMs;

        var initialType = GetEffectiveSnapshot().DccCentralType;
        Client = _clientFactory(initialType);
        ApplyTelemetryPreference(Client);
        SubscribeClientFeedback(Client, ResolveSingleCentralFeedbackProfileId, initialType);
    }

    private void ApplyTelemetryPreference(IDccCentralClient client)
    {
        if (client is ITelemetryPreferenceAwareClient telemetryAware)
            telemetryAware.IsTelemetryEnabled = _settingsManager.App.ShowTelemetryInStatusBar;
    }

    private Guid? ResolveSingleCentralFeedbackProfileId()
    {
        var selected = _settingsManager.GetEffectiveSelectedDccCentralProfileId();
        if (selected.HasValue)
            return selected;

        var enabledProfiles = _settingsManager.GetEffectiveEnabledDccCentralProfiles();
        return enabledProfiles.Count == 1 ? enabledProfiles[0].Id : null;
    }

    private void SubscribeClientFeedback(IDccCentralClient client, Func<Guid?> profileIdProvider, DccCentralType type)
    {
        if (client is not IRBusFeedbackSource feedbackSource)
            return;

        feedbackSource.RBusFeedbackChanged += feedback =>
        {
            var profileId = profileIdProvider();

         foreach (var d in FeedbackStateChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
           ((Action<DccFeedbackStateChange>)d)(
                    new DccFeedbackStateChange(
                        profileId,
                        type,
                        feedback.ModuleAddress,
                        feedback.PortNumber,
                        feedback.IsActive));
            }
        };
    }

    private void SubscribeClientFeedback(IDccCentralClient client, Guid? profileId, DccCentralType type)
        => SubscribeClientFeedback(client, () => profileId, type);

    private void ApplyTelemetryPreferenceToAllClients()
    {
        ApplyTelemetryPreference(Client);

        lock (_multiLock)
        {
            foreach (var conn in _multiConnections.Values)
                ApplyTelemetryPreference(conn.Client);
        }
    }

    private void DisconnectDisabledMultiConnections()
    {
        var enabledIds = _settingsManager.GetEffectiveDccCentralProfiles()
            .Where(p => p.IsEnabled)
            .Select(p => p.Id)
            .ToHashSet();

        List<PerCentralConnection> disabledConnections = new();
        lock (_multiLock)
        {
            foreach (var kvp in _multiConnections.Where(x => !enabledIds.Contains(x.Key)).ToList())
            {
                disabledConnections.Add(kvp.Value);
                _multiConnections.Remove(kvp.Key);
                _reconnectingProfileIds.Remove(kvp.Key);
            }
        }

        foreach (var conn in disabledConnections)
        {
            try
            {
                conn.StateChanged -= OnPerCentralStateChanged;
                conn.Disconnect("settings-changed");
                conn.Dispose();
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    public EffectiveSettings GetEffective() => GetEffectiveSnapshot();

    private EffectiveSettings GetEffectiveSnapshot()
    {
        _settingsLock.Wait();
        try
        {
            return _settingsManager.GetEffective();
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private async Task<EffectiveSettings> GetEffectiveSnapshotAsync(CancellationToken ct)
    {
        await _settingsLock.WaitAsync(ct);
        try
        {
            return _settingsManager.GetEffective();
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    public void EnsureClientFromEffective()
    {
        var eff = GetEffectiveSnapshot();
        var desired = eff.DccCentralType;

        if (ClientMatches(desired))
            return;

        var oldClient = Client;
        var wasConnected = oldClient.IsConnected;
        if (wasConnected)
            oldClient.Disconnect();

        StopMonitor();

        // prepnúť implementáciu klienta podľa typu centrály
        Client = _clientFactory(desired);
        ApplyTelemetryPreference(Client);
        SubscribeClientFeedback(Client, ResolveSingleCentralFeedbackProfileId, desired);

        // Dispose old client if it holds sockets/handles.
        try
        {
            (oldClient as IDisposable)?.Dispose();
        }
        catch
        {
            // best-effort
        }

        if (wasConnected)
            Raise(DccConnectionChangeKind.Disconnected, desired, false, null, "client-switch", ResolveSingleCentralEventProfileId());

        Raise(DccConnectionChangeKind.ClientChanged, desired, false, null, "client-switch", ResolveSingleCentralEventProfileId());
    }

    public async Task<(bool Ok, DccCentralType Type, uint? Serial)> ConnectAsync(CancellationToken ct = default)
    {
        var eff = await GetEffectiveSnapshotAsync(ct);
        EnsureClientFromEffective();

        var (endpoint, numericParameter, targetText) = GetConnectionTarget(eff);

        var ok = await Client.ConnectAsync(endpoint, numericParameter, ct);
        var sn = Client.SerialNumber;
        var centralName = DccCentralDisplayName.Get(eff.DccCentralType);

        if (ok)
        {
            var snText = sn.HasValue ? $", S/N: {sn.Value}" : "";
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"✅ Centrála {centralName} úspešne pripojená ({targetText}{snText})",
                DiagnosticLevel.Success);
            Raise(DccConnectionChangeKind.Connected, eff.DccCentralType, true, sn, "connect", ResolveSingleCentralEventProfileId());
        }
        else
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"❌ Nepodarilo sa pripojiť k centrále {centralName} ({targetText})",
                DiagnosticLevel.Critical);
            Raise(DccConnectionChangeKind.ConnectFailed, eff.DccCentralType, false, null, "timeout/no-response", ResolveSingleCentralEventProfileId());
        }

        // Monitor:
        // - pri úspechu štandardne
        // - pri neúspechu: NESPÚŠŤAME auto-reconnect. AutoConnect má slúžiť až po strate
        //   spojenia z predtým pripojeného stavu (connection-lost), nie ako automatické
        //   pripájanie po neúspešnom prvom connecte.
        if (ok)
        {
            StartMonitor(eff.DccCentralType);
        }
        else if (eff.AutoConnect && !SupportsAutoReconnect())
        {
            // Info message only – still no auto-connect loop on startup/failure.
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"ℹ️ Centrála {centralName} nepodporuje automatický reconnect / keepalive monitor – ostávam v stave odpojené.",
                DiagnosticLevel.Info);
        }

        return (ok, eff.DccCentralType, sn);
    }

    public void Disconnect(string reason = "user")
    {
        var eff = GetEffectiveSnapshot();
        var type = eff.DccCentralType;
        var wasConnected = Client.IsConnected;

        StopMonitor();
        Client.Disconnect();

        if (wasConnected)
            Raise(DccConnectionChangeKind.Disconnected, type, false, null, reason, ResolveSingleCentralEventProfileId());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Multi-central: ConnectAllAsync / DisconnectAll
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Connects all profiles in parallel. Each central is independent:
    /// z21 (UDP) and NanoX (COM) do not block each other.
    /// After completion the primary client (first connected network central) is
    /// promoted so existing single-central code paths continue to work.
    /// </summary>
    public async Task ConnectAllAsync(IReadOnlyList<DccCentralProfile> profiles, CancellationToken ct = default)
    {
        // Stop any previous multi connections
        DisconnectAll("settings-changed");

        var enabledProfiles = profiles.Where(p => p.IsEnabled).ToList();
        if (enabledProfiles.Count == 0) return;

        var newConns = new List<PerCentralConnection>(enabledProfiles.Count);
        lock (_multiLock)
        {
            foreach (var profile in enabledProfiles)
            {
                var client = _clientFactory(profile.Type);
                ApplyTelemetryPreference(client);
                SubscribeClientFeedback(client, profile.Id, profile.Type);
                var conn   = new PerCentralConnection(profile, client, MonitorOptions);
                conn.StateChanged += OnPerCentralStateChanged;
                _multiConnections[profile.Id] = conn;
                newConns.Add(conn);
            }
        }

        // Connect all in parallel – they are fully independent
        var tasks = newConns.Select(c => c.ConnectAsync(ct)).ToList();
        await Task.WhenAll(tasks);

        // Promote primary client for backward-compat code paths
        PromotePrimaryClientFromMulti();
    }

    /// <summary>
    /// Pripojí iba tie profily z <paramref name="allProfiles"/>, ktoré ešte nie sú aktívne pripojené.
    /// Už pripojené centrály zostanú nedotknuté. Pre timed-out centrály sa zastaví starý
    /// PerCentralConnection a spustí nový (s čerstvým reconnect oknom).
    /// </summary>
    public async Task ConnectMissingAsync(IReadOnlyList<DccCentralProfile> allProfiles, CancellationToken ct = default)
    {
        var enabledProfiles = allProfiles.Where(p => p.IsEnabled).ToList();
        if (enabledProfiles.Count == 0) return;

        // Snapshot: ktoré profily sú momentálne pripojené
        var connectedIds = ConnectedProfileIds;
        var missing = enabledProfiles.Where(p => !connectedIds.Contains(p.Id)).ToList();
        if (missing.Count == 0) return;

        var newConns = new List<PerCentralConnection>(missing.Count);
        lock (_multiLock)
        {
            foreach (var profile in missing)
            {
                // Zastaví prípadný starý (timed-out) PerCentralConnection pre tento profil
                if (_multiConnections.TryGetValue(profile.Id, out var existing))
                {
                    try
                    {
                        existing.StateChanged -= OnPerCentralStateChanged;
                        existing.Dispose();
                    }
                    catch { /* best-effort */ }
                    _multiConnections.Remove(profile.Id);
                    _reconnectingProfileIds.Remove(profile.Id);
                }

                var client = _clientFactory(profile.Type);
                ApplyTelemetryPreference(client);
                SubscribeClientFeedback(client, profile.Id, profile.Type);
                var conn   = new PerCentralConnection(profile, client, MonitorOptions);
                conn.StateChanged += OnPerCentralStateChanged;
                _multiConnections[profile.Id] = conn;
                newConns.Add(conn);
            }
        }

        // Pripájame iba chýbajúce – paralelne, nezávisle
        var tasks = newConns.Select(c => c.ConnectAsync(ct)).ToList();
        await Task.WhenAll(tasks);

        PromotePrimaryClientFromMulti();
    }

    /// <summary>Disconnects and disposes all multi-central connections.</summary>
    public void DisconnectAll(string reason = "user")
    {
        List<PerCentralConnection> snapshot;
        lock (_multiLock)
        {
            snapshot = _multiConnections.Values.ToList();
            _multiConnections.Clear();
            _reconnectingProfileIds.Clear();
        }

        foreach (var conn in snapshot)
        {
            try
            {
                conn.StateChanged -= OnPerCentralStateChanged;
                conn.Disconnect(reason);
                conn.Dispose();
            }
            catch
            {
                // best-effort
            }
        }

        // Propaguj jeden súhrnný "všetko odpojené" event pre handlery v MainWindowViewModel
        if (snapshot.Count > 0)
        {
            var primaryType = snapshot.FirstOrDefault()?.Profile.Type ?? DccCentralType.Z21Legacy;
            Raise(DccConnectionChangeKind.Disconnected, primaryType, false, null, reason);
        }
    }

    /// <summary>
    /// Selects the "primary" client from active multi-connections so that
    /// LocoDccBridge and other single-client callers keep working:
    ///   1. First connected network (non-S88) central
    ///   2. Any connected central
    ///   3. Keep existing Client if nothing connected
    /// </summary>
    private void PromotePrimaryClientFromMulti()
    {
        lock (_multiLock)
        {
            var primary =
                _multiConnections.Values
                    .FirstOrDefault(c => c.IsConnected && c.Profile.Type != DccCentralType.NanoX_S88)
                ?? _multiConnections.Values
                    .FirstOrDefault(c => c.IsConnected);

            if (primary != null)
                Client = primary.Client;
        }
    }

    /// <summary>Forwards per-central state change events to the global ConnectionStateChanged.</summary>
    private void OnPerCentralStateChanged(PerCentralConnection conn, DccConnectionChangeKind kind, string reason)
    {
        // Keep the primary client pointer up-to-date after any state change
        PromotePrimaryClientFromMulti();

        // Aktualizuj zoznam reconnecting profilov podľa udalosti
        lock (_multiLock)
        {
            switch (kind)
            {
                case DccConnectionChangeKind.Reconnecting:
                    _reconnectingProfileIds.Add(conn.ProfileId);
                    break;

                case DccConnectionChangeKind.Connected:
                    _reconnectingProfileIds.Remove(conn.ProfileId);
                    break;

                case DccConnectionChangeKind.Disconnected:
                case DccConnectionChangeKind.ConnectFailed when
                    string.Equals(reason, "auto-reconnect-timeout", StringComparison.OrdinalIgnoreCase):
                    // Pri user-disconnect alebo timeout reconnect ukončujeme
                    if (!string.Equals(reason, "connection-lost", StringComparison.OrdinalIgnoreCase))
                        _reconnectingProfileIds.Remove(conn.ProfileId);
                    break;
            }
        }

        // Propagate ConnectionStateChanged but suppress per-central IsConnectedChanged
        // (we fire the aggregate below)
        Raise(kind, conn.Profile.Type, conn.IsConnected, conn.Client.SerialNumber, reason, conn.ProfileId, suppressIsConnectedChanged: true);

        // Fire IsConnectedChanged once with the true aggregate state
        IsConnectedChanged?.Invoke(IsAnyConnected);
    }

    public async Task<DccSettingsApplyResult> ApplyDccAfterSettingsSavedAsync(EffectiveSettings before, CancellationToken ct = default)
    {
        // Runtime-only preference: aj keď sa DCC signatúra nemení, existujúce klienty
        // musia okamžite prevziať nový stav telemetrie (polling on/off).
        ApplyTelemetryPreferenceToAllClients();
        DisconnectDisabledMultiConnections();

        var after = await GetEffectiveSnapshotAsync(ct);

        if (SameDccSignature(before, after))
            return new DccSettingsApplyResult(false, false, false, false, false, after.DccCentralType, null, after);

        var wasConnected = Client.IsConnected;
        var disconnected = false;

        if (wasConnected)
        {
            Disconnect("settings-changed");
            disconnected = true;
        }

        // Prepnúť implementáciu klienta podľa typu (ak sa zmenil)
        EnsureClientFromEffective();

        var autoReconnectAttempted = false;
        var reconnectedOk = false;
        uint? serial = null;

        if (after.AutoConnect)
        {
            autoReconnectAttempted = true;
            var (ok, _, sn) = await ConnectAsync(ct);
            reconnectedOk = ok;
            serial = sn;
        }

        return new DccSettingsApplyResult(true, wasConnected, disconnected, autoReconnectAttempted, reconnectedOk, after.DccCentralType, serial, after);
    }

    public static bool SameDccSignature(EffectiveSettings a, EffectiveSettings b)
    {
        return a.DccCentralType == b.DccCentralType
               && string.Equals(a.DccCentralHost, b.DccCentralHost, StringComparison.OrdinalIgnoreCase)
               && a.DccCentralPort == b.DccCentralPort
               && string.Equals(a.DccSerialPort, b.DccSerialPort, StringComparison.OrdinalIgnoreCase)
               && a.DccBaudRate == b.DccBaudRate;
    }

    private bool ClientMatches(DccCentralType type)
    {
        if (_clientMatchesOverride != null)
            return _clientMatchesOverride(Client, type);

        return type switch
        {
            DccCentralType.Z21Legacy => Client is Z21Client,
            DccCentralType.Z21 => Client is Z21Client,
            DccCentralType.NanoX_S88 => Client is SerialDccClient,
            DccCentralType.GenericIpUdp => Client is GenericIpUdpClient,
            _ => true
        };
    }

    private bool SupportsAutoReconnect() => Client is IDccKeepAliveClient;

    private static (string Endpoint, int NumericParameter, string TargetText) GetConnectionTarget(EffectiveSettings eff)
    {
        return eff.DccCentralType switch
        {
            DccCentralType.NanoX_S88 =>
                (eff.DccSerialPort, eff.DccBaudRate, $"COM: {eff.DccSerialPort}, baudrate: {eff.DccBaudRate}"),

            _ =>
                (eff.DccCentralHost, eff.DccCentralPort, $"IP: {eff.DccCentralHost}:{eff.DccCentralPort}")
        };
    }

    private void StartMonitor(DccCentralType type)
    {

        StopMonitor();

        _monitorCts = new CancellationTokenSource();
        var ct = _monitorCts.Token;
        _monitorTask = Task.Run(() => MonitorLoopAsync(type, ct), ct);
    }

    private void StopMonitor()
    {
        try
        {
            _monitorCts?.Cancel();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _monitorCts?.Dispose();
            _monitorCts = null;
            _monitorTask = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Multi-central cleanup
        try { DisconnectAll("dispose"); } catch { /* ignore */ }

        try { StopMonitor(); } catch { /* ignore */ }
        try { Client.Disconnect(); } catch { /* ignore */ }
        try { (Client as IDisposable)?.Dispose(); } catch { /* ignore */ }
        try { _settingsLock.Dispose(); } catch { /* ignore */ }
    }

    private async Task MonitorLoopAsync(DccCentralType type, CancellationToken ct)
    {
        var fails = 0;
        var disconnectNotified = false;
        var reconnectFailNotified = false;
        var reconnectStartNotified = false;
        var reconnectTimeoutNotified = false;
        DateTime? reconnectWindowStartUtc = null;
        var reconnectWindowExpired = false;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_monitorIntervalMs, ct);
            }
            catch
            {
                return;
            }

            EffectiveSettings effNow;
            try
            {
                effNow = await GetEffectiveSnapshotAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // 1) Ak sme odpojení, stav MUSÍME vždy doručiť do UI.
            //    AutoConnect ovplyvňuje len následný reconnect.
            if (!Client.IsConnected)
            {
                if (!disconnectNotified)
                {
                    disconnectNotified = true;
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"⚠️ Strata spojenia s centrálou {DccCentralDisplayName.Get(effNow.DccCentralType)}",
                        DiagnosticLevel.Warning);
                    Raise(DccConnectionChangeKind.Disconnected, effNow.DccCentralType, false, null, "connection-lost", ResolveSingleCentralEventProfileId());
                }

                if (!effNow.AutoConnect)
                {
                    fails = 0;
                    reconnectFailNotified = false;
                    reconnectStartNotified = false;
                    reconnectTimeoutNotified = false;
                    reconnectWindowStartUtc = null;
                    reconnectWindowExpired = false;
                    continue;
                }

                if (reconnectWindowExpired)
                {
                    // limit 1 min vyčerpaný – už neskúšame, kým nepríde nové "connection-lost"
                    continue;
                }

                if (reconnectWindowStartUtc == null)
                {
                    reconnectWindowStartUtc = DateTime.UtcNow;
                    reconnectStartNotified = false;
                    reconnectTimeoutNotified = false;
                }

                if (!reconnectStartNotified)
                {
                    reconnectStartNotified = true;
                    Raise(DccConnectionChangeKind.Reconnecting, effNow.DccCentralType, false, null, "auto-reconnect", ResolveSingleCentralEventProfileId());
                }

                var elapsedMs = (int)(DateTime.UtcNow - reconnectWindowStartUtc.Value).TotalMilliseconds;
                if (elapsedMs >= _reconnectMaxWindowMs)
                {
                    reconnectWindowExpired = true;

                if (!reconnectTimeoutNotified)
                {
                    reconnectTimeoutNotified = true;
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"❌ Auto-reconnect k centrále {DccCentralDisplayName.Get(effNow.DccCentralType)} ukončený – limit 1 min vypršal",
                        DiagnosticLevel.Critical);
                    Raise(DccConnectionChangeKind.ConnectFailed, effNow.DccCentralType, false, null, "auto-reconnect-timeout", ResolveSingleCentralEventProfileId());
                }

                    continue;
                }

                EnsureClientFromEffective();
                var t = effNow.DccCentralType;

                var okReconnect = false;

                for (var i = 0; i < _reconnectBurstAttempts && !ct.IsCancellationRequested; i++)
                {
                    try
                    {
                        okReconnect = await Client.ConnectAsync(effNow.DccCentralHost, effNow.DccCentralPort, ct);
                    }
                    catch
                    {
                        okReconnect = false;
                    }

                    if (okReconnect)
                        break;

                    try
                    {
                        await Task.Delay(_reconnectIntervalMs, ct);
                    }
                    catch
                    {
                        return;
                    }
                }

                if (okReconnect)
                {
                    fails = 0;
                    disconnectNotified = false;
                    reconnectFailNotified = false;
                    reconnectStartNotified = false;
                    reconnectTimeoutNotified = false;
                    reconnectWindowStartUtc = null;
                    reconnectWindowExpired = false;

                    var sn = Client.SerialNumber;
                    var snText = sn.HasValue ? $", S/N: {sn.Value}" : "";
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"✅ Centrála {DccCentralDisplayName.Get(t)} znovu pripojená (IP: {effNow.DccCentralHost}{snText})",
                        DiagnosticLevel.Success);
                    Raise(DccConnectionChangeKind.Connected, t, true, sn, "auto-reconnect", ResolveSingleCentralEventProfileId());
                    continue;
                }

                if (!reconnectFailNotified)
                {
                    reconnectFailNotified = true;
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"⚠️ Auto-reconnect k centrále {DccCentralDisplayName.Get(t)} zlyhal (dávka pokusov vyčerpaná)",
                        DiagnosticLevel.Warning);
                    Raise(DccConnectionChangeKind.ConnectFailed, t, false, null, "auto-reconnect-failed", ResolveSingleCentralEventProfileId());
                }

                // aby to nešlo stále v 2.5s takte, spravíme dlhšiu pauzu medzi dávkami
                try
                {
                    await Task.Delay(_reconnectIdleDelayMs, ct);
                }
                catch
                {
                    return;
                }

                continue;
            }

            // 2) Sme pripojení.
            disconnectNotified = false;

            // 3) Keepalive ping (len pre klientov, ktorí to podporujú)
            if (Client is not IDccKeepAliveClient ka)
            {
                // Klient nepozná keepalive – stav linky sledujeme pollingom Client.IsConnected.
                continue;
            }

            bool okPing;
            try
            {
                okPing = await ka.PingAsync(ct);
            }
            catch
            {
                okPing = false;
            }

            if (okPing)
            {
                fails = 0;
                continue;
            }

            fails++;
            if (fails < _monitorFailThreshold)
                continue;

            // 4) Strata spojenia: lokálne odpojenie + event a potom pokračovať v slučke
            // (reconnect rieši bod 1)
            var lostType = effNow.DccCentralType;

            var wasConnected = Client.IsConnected;
            Client.Disconnect();

            if (wasConnected)
            {
                disconnectNotified = true;
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"⚠️ Strata spojenia s centrálou {DccCentralDisplayName.Get(lostType)} (keepalive timeout)",
                    DiagnosticLevel.Warning);
                Raise(DccConnectionChangeKind.Disconnected, lostType, false, null, "connection-lost", ResolveSingleCentralEventProfileId());
            }

            fails = 0;
            reconnectFailNotified = false;

            if (effNow.AutoConnect)
            {
                reconnectStartNotified = true;
                reconnectWindowStartUtc = DateTime.UtcNow;
                reconnectWindowExpired = false;
                reconnectTimeoutNotified = false;
                Raise(DccConnectionChangeKind.Reconnecting, lostType, false, null, "auto-reconnect");
            }
            else
            {
                reconnectStartNotified = false;
                reconnectWindowStartUtc = null;
                reconnectWindowExpired = false;
                reconnectTimeoutNotified = false;
            }
        }
    }
}

public readonly record struct DccSettingsApplyResult(
    bool DccChanged,
    bool WasConnected,
    bool Disconnected,
    bool AutoReconnectAttempted,
    bool ReconnectedOk,
    DccCentralType Type,
    uint? Serial,
    EffectiveSettings After
);

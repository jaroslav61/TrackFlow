using System;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models;

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
);

public sealed class DccConnectionService
{
    private readonly SettingsManager _settingsManager;

    public IDccCentralClient Client { get; private set; }

    public event Action<DccConnectionStateChange>? ConnectionStateChanged;

    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;

    private void Raise(DccConnectionChangeKind kind, DccCentralType type, bool isConnected, uint? serial, string reason)
    {
        ConnectionStateChanged?.Invoke(new DccConnectionStateChange(kind, type, isConnected, serial, reason));
    }

    public DccConnectionService(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        Client = DccClientFactory.Create(_settingsManager.GetEffective().DccCentralType);
    }

    public EffectiveSettings GetEffective() => _settingsManager.GetEffective();

    public void EnsureClientFromEffective()
    {
        var eff = _settingsManager.GetEffective();
        var desired = eff.DccCentralType;

        if (ClientMatches(desired))
            return;

        var wasConnected = Client.IsConnected;
        if (wasConnected)
            Client.Disconnect();

        StopMonitor();

        // prepnúť implementáciu klienta podľa typu centrály
        Client = DccClientFactory.Create(desired);

        if (wasConnected)
            Raise(DccConnectionChangeKind.Disconnected, desired, false, null, "client-switch");

        Raise(DccConnectionChangeKind.ClientChanged, desired, false, null, "client-switch");
    }

    public async Task<(bool Ok, DccCentralType Type, uint? Serial)> ConnectAsync(CancellationToken ct = default)
    {
        var eff = _settingsManager.GetEffective();
        EnsureClientFromEffective();

        var ok = await Client.ConnectAsync(eff.DccCentralHost, eff.DccCentralPort, ct);
        var sn = Client.SerialNumber;

        if (ok)
            Raise(DccConnectionChangeKind.Connected, eff.DccCentralType, true, sn, "connect");
        else
            Raise(DccConnectionChangeKind.ConnectFailed, eff.DccCentralType, false, null, "timeout/no-response");

        // Monitor:
        // - pri úspechu štandardne
        // - pri neúspechu: ak je AutoConnect, chceme aby sa spustil auto-reconnect cyklus
        if (ok)
        {
            StartMonitorIfSupported(eff.DccCentralType);
        }
        else if (eff.AutoConnect)
        {
            StartMonitorIfSupported(eff.DccCentralType);

            // Dôležité: pri manuálnom "Pripojiť" a neúspechu chceme prepnúť režim na "odpojené"
            // ešte pred sticky "automaticky pripájam…", aby StatusBar neostal v stave "pripojená".
            Raise(DccConnectionChangeKind.Disconnected, eff.DccCentralType, false, null, "connect-failed");

            // UX: hneď zobraz "automaticky pripájam…"
            // (monitor loop by to poslal až po intervalMs)
            Raise(DccConnectionChangeKind.Reconnecting, eff.DccCentralType, false, null, "auto-reconnect");
        }

        return (ok, eff.DccCentralType, sn);
    }

    public void Disconnect(string reason = "user")
    {
        var eff = _settingsManager.GetEffective();
        var type = eff.DccCentralType;
        var wasConnected = Client.IsConnected;

        StopMonitor();
        Client.Disconnect();

        if (wasConnected)
            Raise(DccConnectionChangeKind.Disconnected, type, false, null, reason);
    }

    public async Task<DccSettingsApplyResult> ApplyDccAfterSettingsSavedAsync(EffectiveSettings before, CancellationToken ct = default)
    {
        var after = _settingsManager.GetEffective();

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
               && a.DccCentralPort == b.DccCentralPort;
    }

    private bool ClientMatches(DccCentralType type)
    {
        return type switch
        {
            DccCentralType.Z21Legacy => Client is Z21Client,
            DccCentralType.Z21 => Client is Z21Client,
            DccCentralType.GenericIpUdp => Client is GenericIpUdpClient,
            _ => true
        };
    }

    private void StartMonitorIfSupported(DccCentralType type)
    {
        // Monitor spúšťame len pre klientov, ktorí vedia urobiť reálny ping
        if (Client is not IDccKeepAliveClient)
            return;

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

    private async Task MonitorLoopAsync(DccCentralType type, CancellationToken ct)
    {
        // Keepalive: rozumné defaulty (Z21 ping dnes robí DNS + UDP roundtrip)
        //  - interval 2.5s
        //  - 2x fail => connection-lost (~5s)
        const int intervalMs = 2500;
        const int failThreshold = 2;

        // Auto-reconnect: periodicky skúšať pripojiť
        const int reconnectIntervalMs = 2500;      // pauza medzi pokusmi v jednej dávke
        const int reconnectBurstAttempts = 3;      // počet pokusov v jednej dávke
        const int reconnectIdleDelayMs = 5000;     // pauza medzi dávkami
        const int reconnectMaxWindowMs = 60_000;   // max 1 min pokusy o reconnect

        var fails = 0;
        var reconnectFailNotified = false;
        var reconnectStartNotified = false;
        var reconnectTimeoutNotified = false;
        DateTime? reconnectWindowStartUtc = null;
        var reconnectWindowExpired = false;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(intervalMs, ct);
            }
            catch
            {
                return;
            }

            var effNow = _settingsManager.GetEffective();

            // 1) Ak sme odpojení a AutoConnect je zapnutý, skúšame reconnect (max 1 min na "dávku").
            if (!Client.IsConnected && effNow.AutoConnect)
            {
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
                    Raise(DccConnectionChangeKind.Reconnecting, effNow.DccCentralType, false, null, "auto-reconnect");
                }

                var elapsedMs = (int)(DateTime.UtcNow - reconnectWindowStartUtc.Value).TotalMilliseconds;
                if (elapsedMs >= reconnectMaxWindowMs)
                {
                    reconnectWindowExpired = true;

                    if (!reconnectTimeoutNotified)
                    {
                        reconnectTimeoutNotified = true;
                        Raise(DccConnectionChangeKind.ConnectFailed, effNow.DccCentralType, false, null, "auto-reconnect-timeout");
                    }

                    continue;
                }

                EnsureClientFromEffective();
                var t = effNow.DccCentralType;

                var okReconnect = false;

                for (var i = 0; i < reconnectBurstAttempts && !ct.IsCancellationRequested; i++)
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
                        await Task.Delay(reconnectIntervalMs, ct);
                    }
                    catch
                    {
                        return;
                    }
                }

                if (okReconnect)
                {
                    fails = 0;
                    reconnectFailNotified = false;
                    reconnectStartNotified = false;
                    reconnectTimeoutNotified = false;
                    reconnectWindowStartUtc = null;
                    reconnectWindowExpired = false;

                    var sn = Client.SerialNumber;
                    Raise(DccConnectionChangeKind.Connected, t, true, sn, "auto-reconnect");
                    continue;
                }

                if (!reconnectFailNotified)
                {
                    reconnectFailNotified = true;
                    Raise(DccConnectionChangeKind.ConnectFailed, t, false, null, "auto-reconnect-failed");
                }

                // aby to nešlo stále v 2.5s takte, spravíme dlhšiu pauzu medzi dávkami
                try
                {
                    await Task.Delay(reconnectIdleDelayMs, ct);
                }
                catch
                {
                    return;
                }

                continue;
            }

            // 2) Ak sme odpojení a AutoConnect nie je zapnutý, nič nerobíme.
            if (!Client.IsConnected)
            {
                fails = 0;
                continue;
            }

            // 3) Keepalive ping (len pre klientov, ktorí to podporujú)
            if (Client is not IDccKeepAliveClient ka)
            {
                // Klient nepozná keepalive – monitor končí ticho.
                // (Nepoužívame return v strede kvôli čitateľnosti; tu je to zámerné ukončenie.)
                return;
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
            if (fails < failThreshold)
                continue;

            // 4) Strata spojenia: lokálne odpojenie + event a potom pokračovať v slučke
            // (reconnect rieši bod 1)
            var lostType = effNow.DccCentralType;

            var wasConnected = Client.IsConnected;
            Client.Disconnect();

            if (wasConnected)
                Raise(DccConnectionChangeKind.Disconnected, lostType, false, null, "connection-lost");

            if (effNow.AutoConnect)
                Raise(DccConnectionChangeKind.Reconnecting, lostType, false, null, "auto-reconnect");

            fails = 0;
            reconnectFailNotified = false;
            reconnectWindowStartUtc = DateTime.UtcNow;
            reconnectWindowExpired = false;
            reconnectTimeoutNotified = false;
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

using System;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models;

namespace TrackFlow.Services.Dcc;

/// <summary>
/// Manages the full lifecycle of exactly ONE DCC central:
/// client, connect/disconnect and optional keepalive monitor.
/// Each instance is independent – connections run in parallel.
/// </summary>
internal sealed class PerCentralConnection : IDisposable
{
    private readonly DccMonitorOptions _monitorOptions;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private bool _disposed;

    public Guid ProfileId => Profile.Id;
    public DccCentralProfile Profile  { get; }
    public IDccCentralClient  Client  { get; }

    public bool IsConnected => Client.IsConnected;

    /// <summary>
    /// Fired after every state transition.
    /// Args: (connection, kind, reason)
    /// </summary>
    public event Action<PerCentralConnection, DccConnectionChangeKind, string>? StateChanged;

    public PerCentralConnection(DccCentralProfile profile, IDccCentralClient client, DccMonitorOptions monitorOptions)
    {
        Profile       = profile       ?? throw new ArgumentNullException(nameof(profile));
        Client        = client        ?? throw new ArgumentNullException(nameof(client));
        _monitorOptions = monitorOptions;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        var (endpoint, numericParameter, targetText) = GetConnectionTarget();
        var centralName = DccCentralDisplayName.Get(Profile.Type);

        bool ok;
        try
        {
            ok = await Client.ConnectAsync(endpoint, numericParameter, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            ok = false;
        }

        if (ok)
        {
            var snText = Client.SerialNumber.HasValue ? $", S/N: {Client.SerialNumber.Value}" : "";
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"✅ Centrála {centralName} úspešne pripojená ({targetText}{snText})",
                DiagnosticLevel.Success);
            StateChanged?.Invoke(this, DccConnectionChangeKind.Connected, "connect");

            // Monitor spúšťame vždy po úspešnom pripojení.
            // Detekcia straty spojenia musí bežať pre KAŽDÚ aktívnu centrálu;
            // AutoConnect ovplyvňuje len následný reconnect, nie samotný monitoring.
            StartMonitor();
        }
        else
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"❌ Nepodarilo sa pripojiť k centrále {centralName} ({targetText})",
                DiagnosticLevel.Critical);
            StateChanged?.Invoke(this, DccConnectionChangeKind.ConnectFailed, "timeout/no-response");

            // Uisti sa, že po neúspešnom connecte nebeží žiadny monitor.
            StopMonitor();
        }

        return ok;
    }

    public void Disconnect(string reason = "user")
    {
        StopMonitor();
        var wasConnected = Client.IsConnected;
        Client.Disconnect();
        if (wasConnected)
            StateChanged?.Invoke(this, DccConnectionChangeKind.Disconnected, reason);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Monitor (keepalive + auto-reconnect)
    // ─────────────────────────────────────────────────────────────────────────

    private void StartMonitor()
    {
        // Best-effort sync stop (Dispose / repeat-Start) – počká max 2 s na bežiaci loop.
        StopMonitorAsync().GetAwaiter().GetResult();
        _monitorCts = new CancellationTokenSource();
        var ct = _monitorCts.Token;
        _monitorTask = Task.Run(() => MonitorLoopAsync(ct), ct);
    }

    /// <summary>
    /// Bezpečne ukončí monitor a počká na dobehnutie <see cref="MonitorLoopAsync"/>
    /// (max 2 s). Bez tohto čakania by sa <see cref="_monitorCts"/> mohol disposnúť
    /// kým slučka stále visí na <c>Task.Delay(...,ct)</c> a vyhodí <see cref="ObjectDisposedException"/>
    /// namiesto <see cref="OperationCanceledException"/>; pri rýchlom Disconnect → Connect
    /// vznikol race condition kde nová iterácia bežala súbežne so starou.
    /// </summary>
    private async Task StopMonitorAsync()
    {
        var cts = Interlocked.Exchange(ref _monitorCts, null);
        var task = Interlocked.Exchange(ref _monitorTask, null);
        if (cts == null)
            return;

        try { cts.Cancel(); }
        catch { /* ignore */ }

        if (task != null)
        {
            try
            {
                // Cap čakania – ak by sa loop niečím zasekol, neblokujeme caller donekonečna.
                await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* expected */ }
            catch { /* best-effort */ }
        }

        try { cts.Dispose(); }
        catch { /* ignore */ }
    }

    private void StopMonitor()
    {
        // Synchrónny wrapper pre call-sites, ktoré nemôžu byť async (Dispose, Disconnect).
        try { StopMonitorAsync().GetAwaiter().GetResult(); }
        catch { /* best-effort */ }
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        var fails                   = 0;
        var disconnectNotified      = false;
        DateTime? reconnectStart    = null;
        var reconnectWindowExpired  = false;
        var reconnectStartNotified  = false;
        var reconnectFailNotified   = false;
        var reconnectTimeoutNotif   = false;

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(_monitorOptions.IntervalMs, ct); }
            catch { return; }

            // ── 1) Disconnected branch: always notify loss, reconnect only when enabled ──
            if (!Client.IsConnected)
            {
                if (!disconnectNotified)
                {
                    disconnectNotified = true;
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"⚠️ Strata spojenia s centrálou {DccCentralDisplayName.Get(Profile.Type)}",
                        DiagnosticLevel.Warning);
                    StateChanged?.Invoke(this, DccConnectionChangeKind.Disconnected, "connection-lost");
                }

                if (!Profile.AutoConnect)
                {
                    fails                  = 0;
                    reconnectStart         = null;
                    reconnectWindowExpired = false;
                    reconnectStartNotified = false;
                    reconnectFailNotified  = false;
                    reconnectTimeoutNotif  = false;
                    continue;
                }

                if (reconnectWindowExpired) continue;

                reconnectStart ??= DateTime.UtcNow;

                if (!reconnectStartNotified)
                {
                    reconnectStartNotified = true;
                    StateChanged?.Invoke(this, DccConnectionChangeKind.Reconnecting, "auto-reconnect");
                }

                var elapsedMs = (int)(DateTime.UtcNow - reconnectStart.Value).TotalMilliseconds;
                if (elapsedMs >= _monitorOptions.ReconnectMaxWindowMs)
                {
                    reconnectWindowExpired = true;
                    if (!reconnectTimeoutNotif)
                    {
                        reconnectTimeoutNotif = true;
                        TrackFlowDoctorService.Instance.Diagnose(
                            "DCC",
                            $"❌ Auto-reconnect k centrále {DccCentralDisplayName.Get(Profile.Type)} ukončený – limit 1 min vypršal",
                            DiagnosticLevel.Critical);
                        StateChanged?.Invoke(this, DccConnectionChangeKind.ConnectFailed, "auto-reconnect-timeout");
                    }
                    continue;
                }

                var (ep, np, _) = GetConnectionTarget();
                var okReconnect = false;

                for (var i = 0; i < _monitorOptions.ReconnectBurstAttempts && !ct.IsCancellationRequested; i++)
                {
                    try { okReconnect = await Client.ConnectAsync(ep, np, ct); }
                    catch { okReconnect = false; }
                    if (okReconnect) break;
                    try { await Task.Delay(_monitorOptions.ReconnectIntervalMs, ct); }
                    catch { return; }
                }

                if (okReconnect)
                {
                    fails                  = 0;
                    disconnectNotified     = false;
                    reconnectStart         = null;
                    reconnectWindowExpired = false;
                    reconnectTimeoutNotif  = false;
                    reconnectFailNotified  = false;
                    reconnectStartNotified = false;

                    var sn = Client.SerialNumber;
                    var snText = sn.HasValue ? $", S/N: {sn.Value}" : "";
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"✅ Centrála {DccCentralDisplayName.Get(Profile.Type)} znovu pripojená{snText}",
                        DiagnosticLevel.Success);
                    StateChanged?.Invoke(this, DccConnectionChangeKind.Connected, "auto-reconnect");
                    continue;
                }

                if (!reconnectFailNotified)
                {
                    reconnectFailNotified = true;
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"⚠️ Auto-reconnect k centrále {DccCentralDisplayName.Get(Profile.Type)} zlyhal",
                        DiagnosticLevel.Warning);
                    StateChanged?.Invoke(this, DccConnectionChangeKind.ConnectFailed, "auto-reconnect-failed");
                }

                try { await Task.Delay(_monitorOptions.ReconnectIdleDelayMs, ct); }
                catch { return; }
                continue;
            }

            // ── 2) Connected: keepalive ping (len pre IDccKeepAliveClient) ──────────
            disconnectNotified = false;

            if (Client is not IDccKeepAliveClient ka)
            {
                // Pre NanoX a iných sériových klientov: nemáme PingAsync.
                // Výpadok portu / socketu zistíme pollingom Client.IsConnected
                // v nasledujúcej iterácii vetvy 1. Stačí pokračovať.
                continue;
            }

            bool okPing;
            try { okPing = await ka.PingAsync(ct); }
            catch { okPing = false; }

            if (okPing) { fails = 0; continue; }

            fails++;
            if (fails < _monitorOptions.FailThreshold) continue;

            // ── 3) Connection lost ────────────────────────────────────────────
            var wasConnected = Client.IsConnected;
            Client.Disconnect();
            if (wasConnected)
            {
                disconnectNotified = true;
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"⚠️ Strata spojenia s centrálou {DccCentralDisplayName.Get(Profile.Type)} (keepalive timeout)",
                    DiagnosticLevel.Warning);
                StateChanged?.Invoke(this, DccConnectionChangeKind.Disconnected, "connection-lost");
            }

            fails                  = 0;
            reconnectFailNotified  = false;
            reconnectWindowExpired = false;
            reconnectTimeoutNotif  = false;

            // Reconnect je striktne oddelený od detekcie výpadku.
            // Ak AutoConnect nie je povolený, centrálu necháme v čistom červenom stave.
            if (Profile.AutoConnect)
            {
                reconnectStart         = DateTime.UtcNow;
                reconnectStartNotified = true;
                StateChanged?.Invoke(this, DccConnectionChangeKind.Reconnecting, "auto-reconnect");
            }
            else
            {
                reconnectStart         = null;
                reconnectStartNotified = false;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private (string Endpoint, int NumericParameter, string TargetText) GetConnectionTarget() =>
        Profile.Type switch
        {
            DccCentralType.NanoX_S88 =>
                (Profile.SerialPort, Profile.BaudRate, $"COM: {Profile.SerialPort}"),
            _ =>
                (Profile.Host, Profile.Port, $"IP: {Profile.Host}:{Profile.Port}")
        };

    // ─────────────────────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitor();
        try { Client.Disconnect(); } catch { /* ignore */ }
        try { (Client as IDisposable)?.Dispose(); } catch { /* ignore */ }
    }
}



using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TrackFlow.Services.Dcc;

public sealed class Z21Client : IDccCentralClient, IDccKeepAliveClient, IDccProgrammingClient, IDccTelemetry,
    ITelemetryPreferenceAwareClient, IRBusFeedbackSource, IDisposable, INotifyPropertyChanged
{
    private const int SystemStatusPollIntervalMs = 2000;
    private const int RBusPollIntervalMs = 1;
    private const int SpeedThrottleMs = 80;

    // ─────────────────────────────────────────────────────────────────────────────
    // Telemetria – LAN_SYSTEMSTATE_GETDATA (0x85) / LAN_SYSTEMSTATE_DATACHANGED (0x84)
    //
    // Komunikácia ide cez EXISTUJÚCI _sendUdp socket (žiaden druhý paralelný socket,
    // ktorý by sa bil so subscribciami z21). Receive prebieha v jednej zdieľanej
    // hlavnej slučke `MainReceiveLoopAsync`, do ktorej sa pridávajú prípadné ďalšie
    // typy odpovedí (lokomotívy, výhybky, broadcasty).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     z21 LAN_SYSTEMSTATE_GETDATA – vyžiada aktuálny systémový stav (napätie/prúd/teplota).
    ///     Štruktúra (4 B): DataLen=0x04 0x00 | HeaderID=0x85 0x00 (LE).
    ///     Centrála na to odpovedá LAN_SYSTEMSTATE_DATACHANGED (header 0x84).
    /// </summary>
    private static readonly byte[] LanGetSystemStatusPacket = { 0x04, 0x00, 0x85, 0x00 };

    private const int MaxRBusGroupIndex = 15; // group 0..15 => modules 1..160
    private static readonly byte[][] LanGetRBusPollPackets =
        Enumerable.Range(0, MaxRBusGroupIndex + 1)
            .Select(static group => new byte[] { 0x05, 0x00, 0x81, 0x00, (byte)group })
            .ToArray();

    private static readonly Z21BroadcastFlags DefaultOperationalBroadcastFlags =
        Z21BroadcastFlags.XBus |
        Z21BroadcastFlags.RBus |
        Z21BroadcastFlags.SystemState;

    private readonly Dictionary<int, byte> _lastRBusModuleMasks = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _shutdownSync = new();

    // Throttling rýchlostných paketov – max 1 paket / 80 ms
    private readonly Stopwatch _speedStopwatch = Stopwatch.StartNew();

    private double? _centralTemperature;

    // Posledný čas prijatia akéhokoľvek validného rámca cez _sendUdp – používa sa pre ľahký PingAsync.
    private long _lastFrameReceivedTicks;
    private long _lastRBusFrameReceivedTicks;
    private long _lastRBusHeartbeatTicks;
    private long _lastSpeedSentTicks; // prístup výhradne cez Interlocked.* – pozri SetLocomotiveSpeedAsync
    private Task? _mainReceiveTask;
    private string _lastDirectRBusFrameHex = string.Empty;
    private string _lastIgnoredLanX43FrameHex = string.Empty;
    private int _rBusDirectFrameReceiveCount;
    private int _rBusIgnoredLanX43Count;
    private int _rBusPollSendCount;

    private double? _mainVoltage;

    private double? _progTrackCurrent;

    private double? _progVoltage;

    private IPEndPoint? _remoteEp;

    // Perzistentný UDP socket pre komunikáciu so z21 centrálou.
    // OBOJSMERNÝ: cez _sendLock sa zo session vlákien posielajú príkazy (drive,
    // function, e-stop, accessory, polling); zároveň naň MainReceiveLoopAsync
    // počúva spontánne odpovede (LAN_SYSTEMSTATE_DATACHANGED a pod.).
    private UdpClient? _sendUdp;
    private int _shutdownRequested;
    private Task _shutdownTask = Task.CompletedTask;
    private int _shutdownVersion;

    // TCS pre čakanie na CV_RESULT po zápise — naplní DispatchSingleIncomingFrame
    private volatile TaskCompletionSource<int>? _pendingCvWriteTcs;

    // Pozadie pre telemetriu – BEŽÍ NA EXISTUJÚCOM _sendUdp sokete (žiadny druhý socket).
    // _telemetryCts riadi zároveň polling systémového stavu aj R-BUS feedbacku cez
    // jednu zdieľanú receive-slučku, ktorá parsuje LAN_SYSTEMSTATE_DATACHANGED.
    private CancellationTokenSource? _telemetryCts;
    private Task? _telemetryPollTask;

    private double? _trackCurrent;

    /// <summary>HwType vrátený centrálou cez LAN_GET_HWINFO (nastavený pri Connect).</summary>
    public Z21HardwareType HardwareType { get; private set; } = Z21HardwareType.Unknown;

    /// <summary>FW verzia (BCD, napr. 0x0140 = 1.40), nastavená pri Connect.</summary>
    public uint? FirmwareVersionRaw { get; private set; }

    private bool IsShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0;
    public bool IsConnected { get; private set; }
    public uint? SerialNumber { get; private set; }

    public async Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        await AwaitPendingShutdownAsync().ConfigureAwait(false);
        Volatile.Write(ref _shutdownRequested, 0);
        _remoteEp = null;

        // Vyriešime IP adresu
        IPAddress ip;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            if (addresses.Length == 0)
            {
                IsConnected = false;
                SerialNumber = null;
                return false;
            }

            ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
        }
        catch
        {
            IsConnected = false;
            SerialNumber = null;
            return false;
        }

        _remoteEp = new IPEndPoint(ip, port);

        // Handshake cez dočasný socket. z21 normálne odpovedá v < 50 ms;
// pri zaťažení siete môže prvý paket prísť neskôr — skúsime 3x.
        uint? serial = null;
        for (int attempt = 0; attempt < 3 && !serial.HasValue; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(300, ct).ConfigureAwait(false);
            serial = await TryGetSerialOnceAsync(_remoteEp, ct, timeoutMs: 600);
        }
        if (!serial.HasValue)
        {
            _remoteEp = null;
            IsConnected = false;
            SerialNumber = null;
            return false;
        }

        // Inicializujeme perzistentný odosielací socket
        await _sendLock.WaitAsync(ct);
        try
        {
            DisposeSendUdp();
            _sendUdp = CreateReusableUdpClient();
            _sendUdp.Connect(_remoteEp);
        }
        finally
        {
            _sendLock.Release();
        }

        SerialNumber = serial.Value;
        IsConnected = true;

        // Spustíme telemetrický polling (LAN_SYSTEMSTATE_GETDATA každé 2 s) HNEĎ.
        // HW info nie je kritická pre prevádzku – vybavíme ju na pozadí.
        StartTelemetry();

        HardwareType = Z21HardwareType.Unknown;
        FirmwareVersionRaw = null;

        var hwEndpoint = _remoteEp;
        _ = Task.Run(async () =>
        {
            try
            {
                var hwInfo = await TryGetHwInfoOnceAsync(hwEndpoint, ct, timeoutMs: 600).ConfigureAwait(false);
                if (!hwInfo.HasValue)
                    return;

                HardwareType = (Z21HardwareType)hwInfo.Value.HwType;
                FirmwareVersionRaw = hwInfo.Value.FwVersion;

                var hwName = HardwareType.ToDisplayName();
                var fwText = $"FW 0x{hwInfo.Value.FwVersion:X4}";
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"🔎 Detegovaná centrála: {hwName} ({fwText}, HwType=0x{hwInfo.Value.HwType:X8})");
            }
            catch
            {
                // HW info je best-effort – chyby nesmú zhodiť spojenie.
            }
        }, ct);

        return true;
    }

    public void Disconnect()
    {
        _ = BeginDisconnectAsync();
    }

    // LAN_X_SET_LOCO_DRIVE – 128 krokov
    // Packet (10 B): 0A 00 | 40 00 | E4 | 13 | AdrH | AdrL | SpeedByte | XOR
    // SpeedByte: bit7=smer(1=vpred), bit6..0: 0=stop(0), nouzový stop(1 – nepoužívame), krok(2-127)
    public async Task SetLocomotiveSpeedAsync(int address, int speed, bool forward, CancellationToken ct = default)
    {
        if (!IsConnected || _remoteEp == null) return;

        // Throttling – zahadzujeme príliš časté aktualizácie, ale STOP vždy prepustíme.
        // Read-modify-write je thread-safe cez Interlocked.CompareExchange – pri súbežnom
        // volaní z viacerých UI eventov (CabStrip + drag) sa zaručene neprepustia 2 pakety
        // do z21 v okne kratšom ako SpeedThrottleMs.
        var elapsed = _speedStopwatch.ElapsedMilliseconds;
        var isStop = speed <= 0;

        if (!isStop)
        {
            var lastTicks = Interlocked.Read(ref _lastSpeedSentTicks);
            if (elapsed - lastTicks < SpeedThrottleMs)
                return;
            if (Interlocked.CompareExchange(ref _lastSpeedSentTicks, elapsed, lastTicks) != lastTicks)
                return; // iný thread práve odoslal – nech to vybaví on
        }
        else
        {
            Interlocked.Exchange(ref _lastSpeedSentTicks, elapsed);
        }

        var (adrH, adrL) = DccAddressCodec.EncodeLocoAddress(address);

        byte spd;
        if (speed <= 0) spd = 0;
        else if (speed == 1) spd = 2;
        else if (speed > 126) spd = 127;
        else spd = (byte)speed;

        var speedByte = (byte)(spd | (forward ? 0x80 : 0x00));
        var xor = (byte)(0xE4 ^ 0x13 ^ adrH ^ adrL ^ speedByte);

        await SendAsync(new byte[] { 0x0A, 0x00, 0x40, 0x00, 0xE4, 0x13, adrH, adrL, speedByte, xor }, ct);
    }

    // LAN_X_SET_LOCO_FUNCTION
    // Packet (10 B): 0A 00 | 40 00 | E4 | F8 | AdrH | AdrL | DB2 | XOR
    // DB2: bit7..6 = typ (00=vyp, 01=zap, 10=toggle), bit5..0 = číslo funkcie
    public async Task SetLocomotiveFunctionAsync(int address, int functionIndex, bool active,
        CancellationToken ct = default)
    {
        if (!IsConnected || _remoteEp == null) return;

        var (adrH, adrL) = DccAddressCodec.EncodeLocoAddress(address);
        var fn = (byte)(functionIndex & 0x3F);
        var db2 = (byte)(fn | (active ? 0x40 : 0x00));
        var xor = (byte)(0xE4 ^ 0xF8 ^ adrH ^ adrL ^ db2);

        await SendAsync(new byte[] { 0x0A, 0x00, 0x40, 0x00, 0xE4, 0xF8, adrH, adrL, db2, xor }, ct);
    }

    // LAN_X_SET_STOP – núdzové zastavenie
    // Packet (6 B): 06 00 | 40 00 | 80 | 80
    // POZOR: posiela aj keď IsConnected=false (bezpečnostný prvok)
    public async Task EmergencyStopAsync(CancellationToken ct = default)
    {
        if (_remoteEp == null) return;
        await SendAsync(new byte[] { 0x06, 0x00, 0x40, 0x00, 0x80, 0x80 }, ct, true);
    }

    // LAN_X_SET_TRACK_POWER_ON – obnoví napájanie koľajiska po E-Stop
    // Packet (7 B): 07 00 | 40 00 | 21 | 81 | A0
    public async Task TrackPowerOnAsync(CancellationToken ct = default)
    {
        if (!IsConnected || _remoteEp == null) return;
        await SendAsync(CreateExitServiceModePacket(), ct);
    }

    // LAN_X_SET_TURNOUT – ovládanie výhybky / accessory decoder
    // Packet (9 B): 09 00 | 40 00 | 53 | AdrH | AdrL | Data | XOR
    // Data: bit3=activate(1=energize, 0=de-energize), bit2=queue, bit1..0=výstup(0=priamo, 1=odbočka)
    // Adresa: DCC accessory 1..2048 → interná adresa = address - 1
    public async Task SetTurnoutAsync(int address, bool branch, bool activate, CancellationToken ct = default)
    {
        if (!IsConnected || _remoteEp == null) return;

        // Z21 accessory adresa je 0-based
        var addr = address - 1;
        var adrH = (byte)((addr >> 8) & 0x07);
        var adrL = (byte)(addr & 0xFF);

        // Data byte: bit3=activate, bit2=queue(0), bit1=0, bit0=výstup(0=priamo, 1=odbočka)
        var data = (byte)((activate ? 0x08 : 0x00) | (branch ? 0x01 : 0x00));

        var xor = (byte)(0x53 ^ adrH ^ adrL ^ data);
        await SendAsync(new byte[] { 0x09, 0x00, 0x40, 0x00, 0x53, adrH, adrL, data, xor }, ct);
    }

    // LAN_X_SET_EXT_ACCESSORY – rozšírené príslušenstvo (1 adresa + číslo aspektu)
    // Packet (9 B): 09 00 | 40 00 | 54 | AdrH | AdrL | Aspect | XOR
    // Aspect: 0..31 (nižších 5 bitov)
    // Adresa: 11-bitová DCC accessory adresa (0..2047) – posiela sa priamo, bez NMRA basic offsetu.
    public async Task SetExtendedAccessoryAspectAsync(int address, int aspectNumber, CancellationToken ct = default)
    {
        if (!IsConnected || _remoteEp == null)
            return;

        // Validácia hraníc podľa Z21 LAN špec. v1.13 (sekcia 4.2.4 LAN_X_SET_EXT_ACCESSORY):
        //  • adresa: 11-bit ⇒ 0..2047
        //  • aspect: 5-bit  ⇒ 0..31
        if (address < 0 || address > 0x07FF)
            throw new ArgumentOutOfRangeException(nameof(address),
                "Extended Accessory adresa musí byť v rozsahu 0..2047 (11-bit).");
        if (aspectNumber < 0 || aspectNumber > 0x1F)
            throw new ArgumentOutOfRangeException(nameof(aspectNumber),
                "Aspect Extended Accessory musí byť v rozsahu 0..31 (5-bit).");

        var adrH = (byte)((address >> 8) & 0x07);
        var adrL = (byte)(address & 0xFF);
        var data = (byte)(aspectNumber & 0x1F);
        var xor = (byte)(0x54 ^ adrH ^ adrL ^ data);

        var packet = new byte[] { 0x09, 0x00, 0x40, 0x00, 0x54, adrH, adrL, data, xor };

        await SendAsync(packet, ct);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        if (!IsConnected || _remoteEp == null) return false;

        // Telemetria pravidelne posiela LAN_SYSTEMSTATE_GETDATA a centrála odpovedá
        // LAN_SYSTEMSTATE_DATACHANGED (~2 s). Akékoľvek prijatie validného rámca cez
        // _sendUdp dokazuje, že NAT mapovanie aj socket sú živé. Tým sa vyhneme
        // vytváraniu nového ephemeral UDP socketu pri každom keepalive (heavy ping).
        var lastTicks = Interlocked.Read(ref _lastFrameReceivedTicks);
        if (lastTicks != 0)
        {
            var ageMs = _speedStopwatch.ElapsedMilliseconds - lastTicks;
            const int FreshnessThresholdMs = 8_000; // 4× polling interval s rezervou
            if (ageMs >= 0 && ageMs < FreshnessThresholdMs)
                return true;
        }

        // Fallback (telemetria zatiaľ nedoručila žiaden rámec): pošleme LAN_GET_SERIALNUMBER
        // cez existujúci _sendUdp. Odpoveď zachytí MainReceiveLoopAsync (aktualizuje
        // _lastFrameReceivedTicks). Tu len overíme, že send neskončí chybou.
        try
        {
            await SendAsync(new byte[] { 0x04, 0x00, 0x10, 0x00 }, ct).ConfigureAwait(false);
            // Krátko počkáme na odpoveď cez MainReceiveLoopAsync.
            var deadline = _speedStopwatch.ElapsedMilliseconds + 1500;
            while (_speedStopwatch.ElapsedMilliseconds < deadline)
            {
                if (Interlocked.Read(ref _lastFrameReceivedTicks) > lastTicks)
                    return true;
                try
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> ReadCvAsync(int cvAddress, DccProgrammingTestMode programmingMode, int timeoutMs,
        int locoAddress, CancellationToken ct = default)
    {
        if (!IsConnected || _remoteEp == null)
            throw new InvalidOperationException("DCC centrála nie je pripojená.");

        if (cvAddress <= 0 || cvAddress > 1024)
            throw new ArgumentOutOfRangeException(nameof(cvAddress));

        var isPom = programmingMode == DccProgrammingTestMode.ProgramOnMain;
        if (isPom && (locoAddress < 1 || locoAddress > 9999))
            throw new ArgumentOutOfRangeException(nameof(locoAddress), "Adresa lokomotívy musí byť v rozsahu 1..9999.");

        // HW pre-check – iba pre Service Track na jednotkách, ktoré DCC vôbec negenerujú
        // (boostery, samostatné dekodéry). z21 start síce nemá fyzický PROG výstup, ale
        // Service Mode príkazy zvláda cez prepnutie hlavnej trate – preto ho NEBLOKUJEME.
        if (!isPom
            && HardwareType != Z21HardwareType.Unknown
            && !HardwareType.SupportsServiceModeProgramming())
            throw new NotSupportedException(
                $"Centrála {HardwareType.ToDisplayName()} nepodporuje service-mode CV-read " +
                "(jednotka bez vlastného DCC generátora). Použite POM (Program on Main) " +
                "s adresou lokomotívy na hlavnej trati.");

        // ─────────────────────────────────────────────────────────────────────────────
        // Dedikovaný UDP socket pre CV-read:
        // • z21 posiela LAN_X_CV_RESULT len subscribovaným endpointom (poslal aspoň
        //   1 paket za posledných 60 s). Preto sa MUSÍME najprv "predstaviť" cez
        //   LAN_GET_SERIALNUMBER → tým náš source IP:port z21 zaregistruje.
        // • LAN_SET_BROADCASTFLAGS s bitom 0x00000001 povolí programming-mode broadcasty.
        // • Receive používa CancellationToken-aware preťaženie aby sa po timeoute neviseli
        //   "unobserved" tasky.
        // ─────────────────────────────────────────────────────────────────────────────
        using var udp = CreateReusableUdpClient();
        udp.Connect(_remoteEp);
        var localPort = (udp.Client.LocalEndPoint as IPEndPoint)?.Port ?? 0;

        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"🔧 CV{cvAddress} read: otváram UDP socket localPort={localPort} → {_remoteEp}");

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        try
        {
            // 1) REGISTRÁCIA – LAN_GET_SERIALNUMBER (overí spojenie + subscribe).
            //    Z21 start občas neodpovie na prvý pokus po ExitServiceMode — retry 3x.
            var getSerial = new byte[] { 0x04, 0x00, 0x10, 0x00 };
            FrameWaitResult serialOk = default;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                await SendAndLogAsync(udp, getSerial, "LAN_GET_SERIALNUMBER");
                var retryDeadline = DateTime.UtcNow.AddMilliseconds(2000);
                serialOk = await WaitForFrameAsync(udp, ct, retryDeadline,
                    r => r.Length >= 8 && r[0] == 0x08 && r[1] == 0x00 && r[2] == 0x10 && r[3] == 0x00,
                    "LAN_GET_SERIALNUMBER_REPLY");
                if (serialOk.Matched) break;
                if (attempt < 2)
                {
                    TrackFlowDoctorService.Instance.Diagnose("DCC", $"⚠️ LAN_GET_SERIALNUMBER pokus {attempt + 1} zlyhal, opakujem...", DiagnosticLevel.Warning);
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
            }
            if (!serialOk.Matched)
                throw new TimeoutException(
                    $"Z21 neodpovedala na úvodný LAN_GET_SERIALNUMBER (3 pokusy). " +
                    "Skontrolujte sieťové pripojenie / firewall na porte 21105 UDP.");

            // 2) Povoliť programming-mode broadcasty.
            var setBroadcast = CreateSetBroadcastFlagsPacket(Z21BroadcastFlags.XBus);
            await SendAndLogAsync(udp, setBroadcast, "LAN_SET_BROADCASTFLAGS(0x00000001)").ConfigureAwait(false);

            // 3) Samotný čítací paket – buď oficiálny service-mode (0x23 0x11), alebo POM read (0xE6 0x30 …).
            byte[] packet;
            string packetDescription;
            if (isPom)
            {
                packet = CreatePomCvReadPacket(locoAddress, cvAddress);
                packetDescription = $"LAN_X_CV_POM_READ_BYTE (loco={locoAddress}, CV{cvAddress})";
            }
            else
            {
                packet = CreateServiceModeCvReadPacket(cvAddress);
                packetDescription = $"LAN_X_CV_READ (CV{cvAddress})";
            }

            await SendAndLogAsync(udp, packet, packetDescription);

            // 4) Receive loop – čakáme na LAN_X_CV_RESULT alebo NACK.
            //    Špeciálna heuristika: po príchode LAN_X_BC_PROGRAMMING_MODE (61 02) dávame
            //    už len krátky "silence timeout" 5 s – ak dovtedy nepríde výsledok ani NACK,
            //    je takmer isté že programovacia koľaj nie je aktívna (typické pre z21 start).
            const int silenceAfterProgrammingModeMs = 5000;
            var totalFramesSeen = serialOk.FramesSeen;
            var sawProgrammingModeNotification = false;
            DateTime? silenceDeadlineUtc = null;

            while (true)
            {
                var effectiveDeadline = deadline;
                if (silenceDeadlineUtc.HasValue && silenceDeadlineUtc.Value < effectiveDeadline)
                    effectiveDeadline = silenceDeadlineUtc.Value;

                var remaining = effectiveDeadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    if (sawProgrammingModeNotification)
                        throw new InvalidOperationException(
                            "Centrála prešla do programovacieho módu (LAN_X_BC_PROGRAMMING_MODE), ale dekodér do 5 s neodpovedal. " +
                            "Najčastejšia príčina: programovacia koľaj nie je u tejto centrály aktívna (napr. z21 start ju nemá). " +
                            "Alternatíva: použiť POM (Program on Main) na hlavnej trati.");

                    throw new TimeoutException(
                        $"Dekodér neodpovedal v limite {timeoutMs} ms (po LAN_X_CV_READ prišlo {totalFramesSeen} rámcov, žiadny LAN_X_CV_RESULT). " +
                        "Skontrolujte: 1) lokomotíva je na programovacej koľaji, 2) programovacia koľaj je aktivovaná, 3) dekodér je CV-readable.");
                }

                byte[] response;
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(remaining);
                    try
                    {
                        var result = await ReceiveWithCancellationAsync(udp, cts.Token).ConfigureAwait(false);
                        response = result.Buffer;
                    }
                    catch (OperationCanceledException)
                    {
                        continue; // do-while overí deadline a hodí konkrétnu hlášku
                    }
                    catch (ObjectDisposedException)
                    {
                        throw new InvalidOperationException("DCC spojenie bolo ukončené počas čítania CV.");
                    }
                }

                totalFramesSeen++;
                LogIncomingFrame(response, totalFramesSeen);

                // LAN_X_CV_RESULT (10 B): 0A 00 40 00 64 14 CV-H CV-L Value XOR
                if (response.Length >= 10
                    && response[0] == 0x0A && response[1] == 0x00
                    && response[2] == 0x40 && response[3] == 0x00
                    && response[4] == 0x64 && response[5] == 0x14)
                {
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"✅ LAN_X_CV_RESULT: CV{cvAddress} = {response[8]} (0x{response[8]:X2})",
                        DiagnosticLevel.Success);
                    return response[8];
                }

                // LAN_X_BC / NACK rámce: 07 00 40 00 61 XX YY
                if (response.Length >= 7
                    && response[0] == 0x07 && response[1] == 0x00
                    && response[2] == 0x40 && response[3] == 0x00
                    && response[4] == 0x61)
                    switch (response[5])
                    {
                        case 0x02: // LAN_X_BC_PROGRAMMING_MODE – informačné
                            if (!sawProgrammingModeNotification)
                            {
                                sawProgrammingModeNotification = true;
                                silenceDeadlineUtc = DateTime.UtcNow.AddMilliseconds(silenceAfterProgrammingModeMs);
                                TrackFlowDoctorService.Instance.Diagnose(
                                    "DCC",
                                    $"ℹ️ Centrála prešla do programming módu – čakám max {silenceAfterProgrammingModeMs} ms na odpoveď dekodéra.");
                            }

                            break;

                        case 0x12: // LAN_X_CV_NACK – dekodér neodpovedal
                            throw new InvalidOperationException(
                                "Dekodér neodpovedal (NACK). Skontrolujte, či je lokomotíva na programovacej koľaji a má kontakt.");

                        case 0x13: // LAN_X_CV_NACK_SC – short circuit
                            throw new InvalidOperationException(
                                "Skrat na programovacej koľaji (NACK SC).");
                    }
                // Iné rámce (keepalive, status) ignorujeme.
            }
        }
        finally
        {
            if (!isPom) await TryExitServiceModeAsync(udp).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Číta viacero CV v jednej service-mode session.
    /// Implementácia podľa správania TrainProgrammer (Wireshark analýza):
    /// — jeden perzistentný UDP socket pre celú session
    /// — LAN_GET_SERIALNUMBER + LAN_SET_BROADCASTFLAGS len raz na začiatku
    /// — po každom LAN_X_CV_RESULT hneď odošle ďalší LAN_X_CV_READ (bez ExitServiceMode)
    /// — LAN_X_SET_TRACK_POWER_OFF (07 00 40 00 21 24 05) posiela ako keepalive každé 2s
    /// — LAN_X_SET_TRACK_POWER_ON (ExitServiceMode) len raz na úplnom konci
    /// </summary>
    public async Task ReadMultipleCvsAsync(
        IReadOnlyList<int> cvAddresses,
        int timeoutMsPerCv,
        int interCvDelayMs,         // parameter zachovaný kvôli interface-kompatibilite, ignorovaný
        Action<int, int> onCvRead,
        Action<int, int, int>? onCvReading = null,
        CancellationToken ct = default)
    {
        if (!IsConnected || _remoteEp == null)
            throw new InvalidOperationException("DCC centrála nie je pripojená.");

        if (!HardwareType.SupportsServiceModeProgramming() && HardwareType != Z21HardwareType.Unknown)
            throw new NotSupportedException(
                $"Centrála {HardwareType.ToDisplayName()} nepodporuje service-mode CV-read.");

        using var udp = CreateReusableUdpClient();
        udp.Connect(_remoteEp);
        var localPort = (udp.Client.LocalEndPoint as IPEndPoint)?.Port ?? 0;

        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"🔧 ReadMultipleCvsAsync: localPort={localPort} → {_remoteEp}, CV count={cvAddresses.Count}");

        // Keepalive: LAN_X_SET_TRACK_POWER_OFF každé 2s (presne ako TrainProgrammer)
        const int keepaliveIntervalMs = 2000;
        var keepalivePacket = new byte[] { 0x07, 0x00, 0x40, 0x00, 0x21, 0x24, 0x05 };
        var lastKeepalive = DateTime.UtcNow;

        try
        {
            // 1) Registrácia — raz pre celú session
            var getSerial = new byte[] { 0x04, 0x00, 0x10, 0x00 };
            await SendAndLogAsync(udp, getSerial, "LAN_GET_SERIALNUMBER").ConfigureAwait(false);
            var regDeadline = DateTime.UtcNow.AddMilliseconds(timeoutMsPerCv);
            var regOk = await WaitForFrameAsync(udp, ct, regDeadline,
                r => r.Length >= 8 && r[0] == 0x08 && r[1] == 0x00 && r[2] == 0x10 && r[3] == 0x00,
                "LAN_GET_SERIALNUMBER_REPLY").ConfigureAwait(false);
            if (!regOk.Matched)
                throw new TimeoutException("Z21 neodpovedala na LAN_GET_SERIALNUMBER. Skontrolujte sieťové pripojenie.");

            // 2) Broadcast flags — raz pre celú session
            await SendAndLogAsync(udp, CreateSetBroadcastFlagsPacket(Z21BroadcastFlags.XBus),
                "LAN_SET_BROADCASTFLAGS(0x00000001)").ConfigureAwait(false);

            // 3) Čítanie CV za sebou — jeden socket, žiadny ExitServiceMode medzi CV
            for (int i = 0; i < cvAddresses.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var cvAddress = cvAddresses[i];
                onCvReading?.Invoke(cvAddress, i, cvAddresses.Count);

                await SendAndLogAsync(udp, CreateServiceModeCvReadPacket(cvAddress),
                    $"LAN_X_CV_READ (CV{cvAddress})").ConfigureAwait(false);

                lastKeepalive = DateTime.UtcNow; // reset keepalive timer po každom CV_READ

                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMsPerCv);
                const int silenceAfterProgrammingModeMs = 5000;
                var totalFramesSeen = 0;
                var sawProgrammingMode = false;
                DateTime? silenceDeadline = null;
                var gotResult = false;

                while (!gotResult)
                {
                    // Keepalive: pošli LAN_X_SET_TRACK_POWER_OFF ak uplynul interval
                    var now = DateTime.UtcNow;
                    if ((now - lastKeepalive).TotalMilliseconds >= keepaliveIntervalMs)
                    {
                        await SendAndLogAsync(udp, keepalivePacket, "LAN_X_SET_TRACK_POWER_OFF (keepalive)").ConfigureAwait(false);
                        lastKeepalive = now;
                    }

                    var effectiveDeadline = deadline;
                    if (silenceDeadline.HasValue && silenceDeadline.Value < effectiveDeadline)
                        effectiveDeadline = silenceDeadline.Value;

                    // Nastav receive timeout na minimum z: čas do deadline a čas do ďalšieho keepalive
                    var msToDeadline = (effectiveDeadline - DateTime.UtcNow).TotalMilliseconds;
                    var msToKeepalive = keepaliveIntervalMs - (DateTime.UtcNow - lastKeepalive).TotalMilliseconds;
                    var receiveTimeoutMs = Math.Max(50, Math.Min(msToDeadline, msToKeepalive));

                    if (msToDeadline <= 0)
                    {
                        if (sawProgrammingMode)
                            throw new InvalidOperationException(
                                $"CV{cvAddress}: Centrála prešla do programovacieho módu ale dekodér neodpovedal do {silenceAfterProgrammingModeMs} ms.");
                        throw new TimeoutException(
                            $"CV{cvAddress}: Dekodér neodpovedal v limite {timeoutMsPerCv} ms " +
                            $"(prišlo {totalFramesSeen} rámcov, žiadny LAN_X_CV_RESULT). " +
                            "Skontrolujte: 1) lokomotíva je na programovacej koľaji, 2) programovacia koľaj je aktivovaná.");
                    }

                    byte[] response;
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromMilliseconds(receiveTimeoutMs));
                    try
                    {
                        response = (await ReceiveWithCancellationAsync(udp, cts.Token).ConfigureAwait(false)).Buffer;
                    }
                    catch (OperationCanceledException)
                    {
                        continue; // timeout receive — skontroluj keepalive a deadline znova
                    }
                    catch (ObjectDisposedException)
                    {
                        throw new InvalidOperationException("DCC spojenie bolo ukončené počas čítania CV.");
                    }

                    totalFramesSeen++;
                    LogIncomingFrame(response, totalFramesSeen);

                    // LAN_X_CV_RESULT
                    if (response.Length >= 10
                        && response[0] == 0x0A && response[1] == 0x00
                        && response[2] == 0x40 && response[3] == 0x00
                        && response[4] == 0x64 && response[5] == 0x14)
                    {
                        var value = response[8];
                        TrackFlowDoctorService.Instance.Diagnose(
                            "DCC",
                            $"✅ LAN_X_CV_RESULT: CV{cvAddress} = {value} (0x{value:X2})",
                            DiagnosticLevel.Success);
                        onCvRead(cvAddress, value);
                        gotResult = true;
                        break;
                    }

                    // NACK / programming mode
                    if (response.Length >= 7
                        && response[0] == 0x07 && response[1] == 0x00
                        && response[2] == 0x40 && response[3] == 0x00
                        && response[4] == 0x61)
                        switch (response[5])
                        {
                            case 0x02:
                                if (!sawProgrammingMode)
                                {
                                    sawProgrammingMode = true;
                                    silenceDeadline = DateTime.UtcNow.AddMilliseconds(silenceAfterProgrammingModeMs);
                                    TrackFlowDoctorService.Instance.Diagnose(
                                        "DCC", $"ℹ️ CV{cvAddress}: Centrála prešla do programming módu.");
                                }
                                break;
                            case 0x12:
                                throw new InvalidOperationException($"CV{cvAddress}: Dekodér neodpovedal (NACK).");
                            case 0x13:
                                throw new InvalidOperationException($"CV{cvAddress}: Skrat na programovacej koľaji (NACK SC).");
                        }
                }
            }
        }
        finally
        {
            // ExitServiceMode cez hlavný _sendUdp socket
            if (_sendUdp != null)
                await TryExitServiceModeAsync(_sendUdp).ConfigureAwait(false);
        }
    }

    public async Task WriteCvAsync(int cvAddress, int value, DccProgrammingTestMode programmingMode, int timeoutMs,
        int locoAddress, CancellationToken ct = default)
    {
        if (!IsConnected || _remoteEp == null || _sendUdp == null)
            throw new InvalidOperationException("DCC centrála nie je pripojená.");

        if (cvAddress <= 0 || cvAddress > 1024)
            throw new ArgumentOutOfRangeException(nameof(cvAddress));

        if (value < 0 || value > 255)
            throw new ArgumentOutOfRangeException(nameof(value), "CV hodnota musí byť v rozsahu 0..255.");

        var isPom = programmingMode == DccProgrammingTestMode.ProgramOnMain;
        if (isPom && (locoAddress < 1 || locoAddress > 9999))
            throw new ArgumentOutOfRangeException(nameof(locoAddress), "Adresa lokomotívy musí byť v rozsahu 1..9999.");

        if (!isPom
            && HardwareType != Z21HardwareType.Unknown
            && !HardwareType.SupportsServiceModeProgramming())
            throw new NotSupportedException(
                $"Centrála {HardwareType.ToDisplayName()} nepodporuje service-mode CV-write.");

        using var udp = CreateReusableUdpClient();
        udp.Connect(_remoteEp);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        // TCS pre CV_RESULT z hlavného socketu — naplní DispatchSingleIncomingFrame
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCvWriteTcs = tcs;

        try
        {
            // Registrácia dočasného socketu
            await SendAndLogAsync(udp, new byte[] { 0x04, 0x00, 0x10, 0x00 }, "LAN_GET_SERIALNUMBER").ConfigureAwait(false);
            var serialOk = await WaitForFrameAsync(udp, ct, deadline,
                r => r.Length >= 8 && r[0] == 0x08 && r[1] == 0x00 && r[2] == 0x10 && r[3] == 0x00,
                "LAN_GET_SERIALNUMBER_REPLY").ConfigureAwait(false);
            if (!serialOk.Matched)
                throw new TimeoutException($"Z21 neodpovedala na LAN_GET_SERIALNUMBER (timeout {timeoutMs} ms).");

            await SendAndLogAsync(udp, CreateSetBroadcastFlagsPacket(Z21BroadcastFlags.XBus),
                "LAN_SET_BROADCASTFLAGS(0x00000001)").ConfigureAwait(false);

            if (!isPom)
            {
                // Vstup do service mode: pošli LAN_X_SET_TRACK_POWER_ON a počkaj na 61 01 60
                // (presne ako TrainProgrammer pred zápisom CV)
                await SendAndLogAsync(udp, CreateExitServiceModePacket(), "LAN_X_SET_TRACK_POWER_ON (enter service mode)").ConfigureAwait(false);
                var smDeadline = DateTime.UtcNow.AddMilliseconds(2000);
                var smOk = await WaitForFrameAsync(udp, ct, smDeadline,
                    r => r.Length >= 7 && r[4] == 0x61 && r[5] == 0x01,
                    "LAN_X_BC_TRACK_POWER_OFF (service mode)").ConfigureAwait(false);
                if (!smOk.Matched)
                    TrackFlowDoctorService.Instance.Diagnose("DCC",
                        "⚠️ LAN_X_BC_TRACK_POWER_OFF neprišiel po vstupe do service mode.", DiagnosticLevel.Warning);
            }

            byte[] packet;
            string packetDescription;
            if (isPom)
            {
                packet = CreatePomCvWritePacket(locoAddress, cvAddress, value);
                packetDescription = $"LAN_X_CV_POM_WRITE_BYTE (loco={locoAddress}, CV{cvAddress}={value})";
            }
            else
            {
                packet = CreateServiceModeCvWritePacket(cvAddress, value);
                packetDescription = $"LAN_X_CV_WRITE (CV{cvAddress}={value})";
            }

            await SendAndLogAsync(udp, packet, packetDescription).ConfigureAwait(false);
            // CV_WRITE aj cez hlavný _sendUdp — Z21 pošle CV_RESULT na ten socket
            // od ktorého dostala CV_WRITE; MainReceiveLoopAsync ho zachytí a naplní TCS
            if (!isPom)
                await SendAndLogAsync(_sendUdp, packet, $"{packetDescription} (via main)").ConfigureAwait(false);

            if (isPom)
            {
                await ObserveWriteResponseAsync(udp, ct, deadline, cvAddress, value, isPom: true).ConfigureAwait(false);
                return;
            }

            // Service track: čakaj na CV_RESULT z dočasného socketu ALEBO z hlavného (cez TCS)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException($"CV{cvAddress}: Vypršal čas.");
            timeoutCts.CancelAfter(remaining);

            var localTask = ObserveWriteResponseAsync(udp, timeoutCts.Token, deadline, cvAddress, value, isPom: false);

            var winner = await Task.WhenAny(localTask, tcs.Task).ConfigureAwait(false);
            timeoutCts.Cancel();

            if (tcs.Task.IsCompletedSuccessfully)
            {
                var readBack = tcs.Task.Result;
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"✅ CV{cvAddress} potvrdený (hlavný socket): readback={readBack}",
                    DiagnosticLevel.Success);
                if (readBack != value)
                    throw new InvalidOperationException(
                        $"CV{cvAddress}: Overenie zlyhalo — zapísané {value}, readback {readBack}.");
                return;
            }

            if (localTask.IsFaulted)
                throw localTask.Exception!.InnerException!;

            if (localTask.IsCompletedSuccessfully)
                return;

            throw new TimeoutException($"CV{cvAddress}: Zápis nebol potvrdený v limite {timeoutMs} ms.");
        }
        finally
        {
            _pendingCvWriteTcs = null;
            if (!isPom)
            {
                await TryExitServiceModeAsync(udp).ConfigureAwait(false);
                if (_sendUdp != null)
                    await TryExitServiceModeAsync(_sendUdp).ConfigureAwait(false);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Telemetria (IDccTelemetry) ───────────────────────────────────────────
    public bool IsTelemetrySupported => true;
    public bool IsBlackZ21 => HardwareType.IsBlackZ21();

    public double? MainVoltage
    {
        get => _mainVoltage;
        private set
        {
            if (_mainVoltage != value)
            {
                _mainVoltage = value;
                OnPropertyChanged();
            }
        }
    }

    public double? ProgVoltage
    {
        get => _progVoltage;
        private set
        {
            if (_progVoltage != value)
            {
                _progVoltage = value;
                OnPropertyChanged();
            }
        }
    }

    public double? TrackCurrent
    {
        get => _trackCurrent;
        private set
        {
            if (_trackCurrent != value)
            {
                _trackCurrent = value;
                OnPropertyChanged();
            }
        }
    }

    public double? ProgTrackCurrent
    {
        get => _progTrackCurrent;
        private set
        {
            if (_progTrackCurrent != value)
            {
                _progTrackCurrent = value;
                OnPropertyChanged();
            }
        }
    }

    public double? CentralTemperature
    {
        get => _centralTemperature;
        private set
        {
            if (_centralTemperature != value)
            {
                _centralTemperature = value;
                OnPropertyChanged();
            }
        }
    }

    public void Dispose()
    {
        // Bezpečné Dispose: BeginDisconnectAsync vracia Task, ktorý dokončí cleanup
        // receive-slučky a telemetrického pollera. Synchrónne počkáme MAX 2 s, aby
        // sa pri "stuck" sokete (pomalá sieť, vypnutý router) nezablokoval UI thread
        // pri zatváraní MainWindow.
        var shutdown = BeginDisconnectAsync();
        try
        {
            shutdown.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // cleanup je best-effort – akékoľvek interné výnimky už zachytil ObserveCleanupTaskAsync
        }

        try
        {
            _sendLock.Dispose();
        }
        catch
        {
            /* ignore */
        }
    }

    public event Action<RBusFeedbackState>? RBusFeedbackChanged;
    public bool IsTelemetryEnabled { get; set; } = true;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Iba pre testovacie účely – umožní nastaviť detegovaný HW typ bez sieťovej komunikácie.</summary>
    internal void SetHardwareTypeForTest(Z21HardwareType hardwareType)
    {
        HardwareType = hardwareType;
    }

    // Odošle packet cez perzistentný socket
    private async Task SendAsync(byte[] packet, CancellationToken ct, bool bypassConnectedCheck = false)
    {
        if (!bypassConnectedCheck && !IsConnected) return;

        await _sendLock.WaitAsync(ct);
        try
        {
            if (_sendUdp == null || _remoteEp == null) return;
            await _sendUdp.SendAsync(packet, packet.Length);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || IsShutdownRequested)
        {
            // Očakávané počas ukončovania spojenia.
        }
        catch (ObjectDisposedException) when (IsShutdownRequested)
        {
            // Socket bol lokálne zatvorený počas Disconnect().
        }
        catch (SocketException) when (ct.IsCancellationRequested || IsShutdownRequested)
        {
            // Lokálny shutdown / zrušenie odosielania.
        }
        catch (Exception ex)
        {
            if (!IsShutdownRequested)
            {
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"⚠️ Z21 send chyba: {ex.GetType().Name}: {ex.Message} – označujem centrálu ako odpojenú.",
                    DiagnosticLevel.Warning);
                IsConnected = false; // Sieťová chyba – monitor to zachytí a reconnectne
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // Samostatný krátkodobý socket pre handshake / ping – nezablokuje _sendUdp
    private static async Task<uint?> TryGetSerialOnceAsync(IPEndPoint ep, CancellationToken ct, int timeoutMs = 1500)
    {
        try
        {
            using var udp = CreateReusableUdpClient();
            udp.Connect(ep);
            var payload = new byte[] { 0x04, 0x00, 0x10, 0x00 };
            await udp.SendAsync(payload, payload.Length);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            UdpReceiveResult result;
            try
            {
                result = await ReceiveWithCancellationAsync(udp, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            var data = result.Buffer;
            if (data.Length < 8) return null;
            if (data[0] != 0x08 || data[1] != 0x00 || data[2] != 0x10 || data[3] != 0x00) return null;
            return (uint)(data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     LAN_GET_HWINFO – pýta sa centrály na typ hardvéru a verziu firmvéru.
    ///     Request:  04 00 1A 00
    ///     Response: 0C 00 1A 00 | HwType(4 B LE) | FwVersion(4 B LE)
    /// </summary>
    private static async Task<(uint HwType, uint FwVersion)?> TryGetHwInfoOnceAsync(IPEndPoint ep, CancellationToken ct, int timeoutMs = 1500)
    {
        try
        {
            using var udp = CreateReusableUdpClient();
            udp.Connect(ep);
            var payload = new byte[] { 0x04, 0x00, 0x1A, 0x00 };
            await udp.SendAsync(payload, payload.Length);

            // Niekedy príde najprv serial-broadcast alebo iný rámec – skúsime pár pokusov.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            try
            {
                while (!cts.IsCancellationRequested)
                {
                    UdpReceiveResult result;
                    try
                    {
                        result = await ReceiveWithCancellationAsync(udp, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }

                    var data = result.Buffer;
                    if (data.Length >= 12
                        && data[0] == 0x0C && data[1] == 0x00
                        && data[2] == 0x1A && data[3] == 0x00)
                    {
                        var hw = (uint)(data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24));
                        var fw = (uint)(data[8] | (data[9] << 8) | (data[10] << 16) | (data[11] << 24));
                        return (hw, fw);
                    }
                    // iný rámec – pokračujeme
                }
            }
            catch (OperationCanceledException)
            {
                /* timeout */
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task SendAndLogAsync(UdpClient udp, byte[] packet, string description)
    {
        await udp.SendAsync(packet, packet.Length);
        Debug.WriteLine($"TX: {description}");
        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"📤 {description}: {BytesToHex(packet)}");
    }

    private static async Task ObserveWriteResponseAsync(
        UdpClient udp,
        CancellationToken ct,
        DateTime deadlineUtc,
        int cvAddress,
        int value,
        bool isPom)
    {
        var idleAfterProgrammingModeMs = isPom ? 400 : 3000;  // v prípade zlýhania zápisu do registrov CV zvýšiť hodnotu
        var hardObservationDeadline = DateTime.UtcNow.AddMilliseconds(isPom ? 1_000 : 2_000);
        var effectiveDeadline = hardObservationDeadline < deadlineUtc ? hardObservationDeadline : deadlineUtc;
        DateTime? silenceDeadlineUtc = null;

        while (true)
        {
            var currentDeadline = silenceDeadlineUtc.HasValue && silenceDeadlineUtc.Value < effectiveDeadline
                ? silenceDeadlineUtc.Value
                : effectiveDeadline;

            var remaining = currentDeadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return;

            byte[] response;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(remaining);

            try
            {
                var result = await ReceiveWithCancellationAsync(udp, cts.Token).ConfigureAwait(false);
                response = result.Buffer;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            LogIncomingFrame(response, 0);
            Debug.WriteLine($"WRITE RX: {BytesToHex(response)}");

            if (response.Length >= 7
                && response[0] == 0x07 && response[1] == 0x00
                && response[2] == 0x40 && response[3] == 0x00
                && response[4] == 0x61)
                switch (response[5])
                {
                    case 0x02:
                        silenceDeadlineUtc = DateTime.UtcNow.AddMilliseconds(idleAfterProgrammingModeMs);
                        continue;
                    case 0x12:
                        throw new InvalidOperationException($"Dekodér odmietol zápis CV{cvAddress}={value} (NACK).");
                    case 0x13:
                        throw new InvalidOperationException("Skrat na programovacej koľaji (NACK SC).");
                }

            if (response.Length >= 10
                && response[0] == 0x0A && response[1] == 0x00
                && response[2] == 0x40 && response[3] == 0x00
                && response[4] == 0x64 && response[5] == 0x14)
                return;
        }
    }

    private static async Task TryExitServiceModeAsync(UdpClient udp)
    {
        try
        {
            await Task.Delay(100).ConfigureAwait(false);
            await SendAndLogAsync(udp, CreateExitServiceModePacket(), "LAN_X_SET_TRACK_POWER_ON (exit service mode)")
                .ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Socket už neexistuje – cleanup je len best effort.
        }
        catch (Exception ex)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"⚠️ Nepodarilo sa ukončiť service mode / obnoviť napájanie trate: {ex.Message}",
                DiagnosticLevel.Warning);
        }
    }

    private static UdpClient CreateReusableUdpClient(int localPort = 0)
    {
        var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
        return udp;
    }

    private static void LogIncomingFrame(byte[] frame, int seq)
    {
        Debug.WriteLine($"RX: {BytesToHex(frame)}");
        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"📥 #{seq} ({frame.Length} B): {BytesToHex(frame)}");
    }

    private static async Task<UdpReceiveResult> ReceiveWithCancellationAsync(UdpClient udp, CancellationToken ct)
    {
        var receiveTask = udp.ReceiveAsync();

        try
        {
            return await receiveTask.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _ = receiveTask.ContinueWith(
                static t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            throw;
        }
    }

    private static string BytesToHex(byte[] data)
    {
        if (data == null || data.Length == 0) return "(empty)";
        var sb = new StringBuilder(data.Length * 3);
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[i].ToString("X2"));
        }

        return sb.ToString();
    }

    internal static byte[] CreateSetBroadcastFlagsPacket(Z21BroadcastFlags flags)
    {
        var value = (uint)flags;
        return new byte[]
        {
            0x08, 0x00, 0x50, 0x00,
            (byte)(value & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 24) & 0xFF)
        };
    }

    private static async Task<FrameWaitResult> WaitForFrameAsync(
        UdpClient udp,
        CancellationToken ct,
        DateTime deadlineUtc,
        Func<byte[], bool> isMatch,
        string description)
    {
        var framesSeen = 0;
        while (true)
        {
            var remaining = deadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"⏱️ Timeout pri čakaní na {description} (prijatých {framesSeen} cudzích rámcov).",
                    DiagnosticLevel.Warning);
                return new FrameWaitResult(false, framesSeen);
            }

            byte[] frame;
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(remaining);
                try
                {
                    var result = await ReceiveWithCancellationAsync(udp, cts.Token).ConfigureAwait(false);
                    frame = result.Buffer;
                }
                catch (OperationCanceledException)
                {
                    continue; // deadline check at top of loop
                }
            }

            framesSeen++;
            LogIncomingFrame(frame, framesSeen);

            if (isMatch(frame))
            {
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"✅ Prijatý {description}.",
                    DiagnosticLevel.Success);
                return new FrameWaitResult(true, framesSeen);
            }
        }
    }

    /// <summary>
    ///     Vytvorí OFICIÁLNY z21 paket LAN_X_CV_READ pre service-mode (programovacia koľaj).
    ///     Štruktúra (9 B):
    ///     z21 Header (4 B):  DataLen (LE) | HeaderID (LE)   = 0x09,0x00 | 0x40,0x00
    ///     X-Bus dáta (4 B):  XHeader=0x23 | DB0=0x11 | CV-Adresa (BE, zero-based)
    ///     XOR (1 B):         XOR cez 4 X-Bus bajty
    ///     Pre CV1 (cvNumber=1) vznikne: 0x09,0x00,0x40,0x00,0x23,0x11,0x00,0x00,0x32
    ///     Toto je paket, na ktorý centrála odpovedá LAN_X_CV_RESULT (0A 00 40 00 64 14 CV-H CV-L Value XOR).
    /// </summary>
    public static byte[] CreateServiceModeCvReadPacket(int cvNumber)
    {
        if (cvNumber < 1 || cvNumber > 1024)
            throw new ArgumentOutOfRangeException(nameof(cvNumber), "CV číslo musí byť v rozsahu 1..1024.");

        var cvIndex = cvNumber - 1;
        var cvHigh = (byte)((cvIndex >> 8) & 0x03); // CV adresa max 10-bit (0..1023)
        var cvLow = (byte)(cvIndex & 0xFF);

        var xor = (byte)(0x23 ^ 0x11 ^ cvHigh ^ cvLow);
        return new byte[] { 0x09, 0x00, 0x40, 0x00, 0x23, 0x11, cvHigh, cvLow, xor };
    }

    public static byte[] CreateServiceModeCvWritePacket(int cvNumber, int value)
    {
        if (cvNumber < 1 || cvNumber > 1024)
            throw new ArgumentOutOfRangeException(nameof(cvNumber), "CV číslo musí byť v rozsahu 1..1024.");
        if (value < 0 || value > 255)
            throw new ArgumentOutOfRangeException(nameof(value), "CV hodnota musí byť v rozsahu 0..255.");

        var cvIndex = cvNumber - 1;
        var cvHigh = (byte)((cvIndex >> 8) & 0x03);
        var cvLow = (byte)(cvIndex & 0xFF);
        var cvValue = (byte)value;

        var xor = (byte)(0x24 ^ 0x12 ^ cvHigh ^ cvLow ^ cvValue);
        return new byte[] { 0x0A, 0x00, 0x40, 0x00, 0x24, 0x12, cvHigh, cvLow, cvValue, xor };
    }

    /// <summary>
    ///     Paket na ukončenie service-mode programovania a návrat centrály do normálneho
    ///     prevádzkového režimu (Track Power On).
    /// </summary>
    public static byte[] CreateExitServiceModePacket()
    {
        return new byte[] { 0x07, 0x00, 0x40, 0x00, 0x21, 0x81, 0xA0 };
    }

    /// <summary>
    ///     Vytvorí OFICIÁLNY z21 paket LAN_X_CV_POM_READ_BYTE pre čítanie CV cez POM (Program on Main).
    ///     POM read funguje na hlavnej trati – vyžaduje RailCom-kompatibilný dekodér aj centrálu.
    ///     Štruktúra (12 B):
    ///     z21 Header (4 B):  DataLen | HeaderID = 0x0C,0x00 | 0x40,0x00
    ///     X-Bus dáta (7 B):
    ///     XHeader = 0xE6
    ///     DB0     = 0x30
    ///     AddrMSB = (locoAddr > 127 ? 0xC0 : 0x00) | ((locoAddr >> 8) &amp; 0x3F)
    ///     AddrLSB = locoAddr &amp; 0xFF
    ///     OptCvH  = 0xE4 | ((cv-1) >> 8) &amp; 0x03    ← 0xE4 = "POM read byte" príkaz
    ///     CvLo    = (cv-1) &amp; 0xFF
    ///     Data    = 0x00 (pre read ignorované)
    ///     XOR (1 B):  XOR cez všetkých 7 X-Bus bajtov
    ///     Príklad pre loco=3, CV=1:  0C 00 40 00 E6 30 00 03 E4 00 00 31
    ///     Odpoveď: rovnaký LAN_X_CV_RESULT ako pri service-mode (0A 00 40 00 64 14 CV-H CV-L Value XOR).
    /// </summary>
    public static byte[] CreatePomCvReadPacket(int locoAddress, int cvNumber)
    {
        if (locoAddress < 1 || locoAddress > 9999)
            throw new ArgumentOutOfRangeException(nameof(locoAddress), "Adresa lokomotívy musí byť v rozsahu 1..9999.");
        if (cvNumber < 1 || cvNumber > 1024)
            throw new ArgumentOutOfRangeException(nameof(cvNumber), "CV číslo musí byť v rozsahu 1..1024.");

        // DCC adresa lokomotívy je zakódovaná podľa NMRA / Z21:
        // krátka (1..127) → AddrMSB=0x00; dlhá (128+) → 0xC0 | (addr >> 8 & 0x3F).
        var (addrMsb, addrLsb) = DccAddressCodec.EncodeLocoAddress(locoAddress);

        // CV adresa – zero-based, rozdelená na 2 horné bity (do OptCvH) a 8 dolných (do CvLo).
        var cvIndex = cvNumber - 1;
        var optCvH = (byte)(0xE4 | ((cvIndex >> 8) & 0x03)); // 0xE4 = POM read byte príkaz
        var cvLo = (byte)(cvIndex & 0xFF);
        byte data = 0x00; // pri reade sa data byte ignoruje

        byte[] xBus = { 0xE6, 0x30, addrMsb, addrLsb, optCvH, cvLo, data };
        byte xor = 0;
        for (var i = 0; i < xBus.Length; i++) xor ^= xBus[i];

        var packet = new byte[4 + xBus.Length + 1];
        packet[0] = 0x0C;
        packet[1] = 0x00;
        packet[2] = 0x40;
        packet[3] = 0x00;
        Buffer.BlockCopy(xBus, 0, packet, 4, xBus.Length);
        packet[4 + xBus.Length] = xor;
        return packet;
    }

    public static byte[] CreatePomCvWritePacket(int locoAddress, int cvNumber, int value)
    {
        if (locoAddress < 1 || locoAddress > 9999)
            throw new ArgumentOutOfRangeException(nameof(locoAddress), "Adresa lokomotívy musí byť v rozsahu 1..9999.");
        if (cvNumber < 1 || cvNumber > 1024)
            throw new ArgumentOutOfRangeException(nameof(cvNumber), "CV číslo musí byť v rozsahu 1..1024.");
        if (value < 0 || value > 255)
            throw new ArgumentOutOfRangeException(nameof(value), "CV hodnota musí byť v rozsahu 0..255.");

        var (addrMsb, addrLsb) = DccAddressCodec.EncodeLocoAddress(locoAddress);

        var cvIndex = cvNumber - 1;
        var optCvH = (byte)(0xEC | ((cvIndex >> 8) & 0x03));
        var cvLo = (byte)(cvIndex & 0xFF);
        var data = (byte)value;

        byte[] xBus = { 0xE6, 0x30, addrMsb, addrLsb, optCvH, cvLo, data };
        byte xor = 0;
        for (var i = 0; i < xBus.Length; i++) xor ^= xBus[i];

        var packet = new byte[4 + xBus.Length + 1];
        packet[0] = 0x0C;
        packet[1] = 0x00;
        packet[2] = 0x40;
        packet[3] = 0x00;
        Buffer.BlockCopy(xBus, 0, packet, 4, xBus.Length);
        packet[4 + xBus.Length] = xor;
        return packet;
    }

    /// <summary>
    ///     Vytvorí UDP paket so štruktúrou 0xE6 0x30 (XPressNet "Direct CV access" / POM-style).
    ///     POZN.: Toto NIE JE správny paket pre LAN_X_CV_READ na service-mode programovacej koľaji –
    ///     na ten použi <see cref="CreateServiceModeCvReadPacket" />. Metóda je ponechaná pre kompatibilitu
    ///     a budúce POM operácie.
    ///     Štruktúra (11 B):
    ///     z21 Header (4 B):  DataLen (LE) | HeaderID (LE)   = 0x0C,0x00 | 0x40,0x00
    ///     X-Bus dáta (6 B):  XHeader=0xE6 | DB0=0x30 | CV-Adresa (BE, zero-based) | 0x00 | 0x00
    ///     XOR (1 B):         XOR cez všetkých 6 X-Bus bajtov
    ///     Pre CV1 vznikne: 0x0C,0x00,0x40,0x00,0xE6,0x30,0x00,0x00,0x00,0x00,0xD6
    /// </summary>
    public static byte[] CreateReadCv1Packet()
    {
        return CreateReadCvPacket(1);
    }

    /// <summary>
    ///     Univerzálna verzia – vytvorí LAN_X_CV_READ paket pre ľubovoľné CV (1..1024).
    ///     CV adresa sa do paketu ukladá ako zero-based (cvNumber - 1) v Big Endian poradí.
    /// </summary>
    public static byte[] CreateReadCvPacket(int cvNumber)
    {
        if (cvNumber < 1 || cvNumber > 1024)
            throw new ArgumentOutOfRangeException(nameof(cvNumber), "CV číslo musí byť v rozsahu 1..1024.");

        // CV adresa – zero-based, Big Endian
        var cvIndex = cvNumber - 1;
        var cvHigh = (byte)((cvIndex >> 8) & 0xFF);
        var cvLow = (byte)(cvIndex & 0xFF);

        // X-Bus časť paketu: XHeader, DB0, CV-H, CV-L, 0x00, 0x00 (6 B)
        byte[] xBus = { 0xE6, 0x30, cvHigh, cvLow, 0x00, 0x00 };

        // XOR checksum cez všetky X-Bus bajty
        byte xor = 0;
        for (var i = 0; i < xBus.Length; i++)
            xor ^= xBus[i];

        // Zloženie kompletného paketu (4 B header + 6 B X-Bus + 1 B XOR = 11 B)
        var packet = new byte[4 + xBus.Length + 1];
        packet[0] = 0x0C; // DataLen LSB
        packet[1] = 0x00; // DataLen MSB
        packet[2] = 0x40; // HeaderID LSB (LAN_X_...)
        packet[3] = 0x00; // HeaderID MSB
        Buffer.BlockCopy(xBus, 0, packet, 4, xBus.Length);
        packet[4 + xBus.Length] = xor;

        return packet;
    }

    private void DisposeSendUdp()
    {
        var udp = Interlocked.Exchange(ref _sendUdp, null);
        try
        {
            udp?.Close();
        }
        catch
        {
        }

        try
        {
            udp?.Dispose();
        }
        catch
        {
        }
    }

    private Task AwaitPendingShutdownAsync()
    {
        lock (_shutdownSync)
        {
            return _shutdownTask;
        }
    }

    private Task BeginDisconnectAsync(bool waitForCompletion = false)
    {
        Task shutdownTask;

        lock (_shutdownSync)
        {
            if (IsShutdownRequested)
            {
                shutdownTask = _shutdownTask;
            }
            else
            {
                Volatile.Write(ref _shutdownRequested, 1);
                var version = ++_shutdownVersion;

                IsConnected = false;
                SerialNumber = null;
                _remoteEp = null;

                var (pollTask, receiveTask) = CancelTelemetry();
                DisposeSendUdp();

                shutdownTask = CompleteDisconnectAsync(version, receiveTask, pollTask);
                _shutdownTask = shutdownTask;
            }
        }

        if (waitForCompletion)
            shutdownTask.GetAwaiter().GetResult();

        return shutdownTask;
    }

    private async Task CompleteDisconnectAsync(int shutdownVersion, Task? receiveTask, Task? pollTask)
    {
        try
        {
            await ObserveCleanupTaskAsync(receiveTask).ConfigureAwait(false);
            await ObserveCleanupTaskAsync(pollTask).ConfigureAwait(false);
        }
        finally
        {
            lock (_shutdownSync)
            {
                if (_shutdownVersion == shutdownVersion)
                {
                    Volatile.Write(ref _shutdownRequested, 0);
                    _shutdownTask = Task.CompletedTask;
                }
            }
        }
    }

    private static async Task ObserveCleanupTaskAsync(Task? task)
    {
        if (task == null)
            return;

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Očakávaný koniec pri shutdown-e.
        }
        catch (ObjectDisposedException)
        {
            // Socket / token už bol uvoľnený.
        }
        catch
        {
            // Cleanup je best-effort; Disconnect nesmie spadnúť ani blokovať UI.
        }
    }

    private void StartTelemetry()
    {
        CancelTelemetry();
        if (_sendUdp == null || _remoteEp == null) return;

        // CTS vytvoríme PRED spustením fire-and-forget taskov – aby vedeli rešpektovať
        // shutdown a aby sa pri rýchlom Connect/Disconnect/Connect nikdy neposlal
        // register/poll z predošlého cyklu na nový socket (alebo naopak na už zatvorený).
        _telemetryCts = new CancellationTokenSource();
        var ct = _telemetryCts.Token;

        // ── Registrácia odberu systémových správ ────────────────────────────────
        // LAN_SET_BROADCASTFLAGS (header 0x50) – bez tohto z21 nepošle
        // LAN_SYSTEMSTATE_DATACHANGED na náš endpoint.
        // Príznaky (32-bit LE):
        //   0x00000001 – X-Bus broadcasty (drive/switch/programming)
        //   0x00000010 – R-Bus / S88 feedback zmeny (LAN_RMBUS_DATACHANGED)
        //   0x00000100 – LAN_SYSTEMSTATE_DATACHANGED (napätie / prúd / teplota)
        // Posielame všetky tri cez typovaný enum `Z21BroadcastFlags` → telemetria, X-bus aj spätná väzba.
        _ = Task.Run(async () =>
        {
            try
            {
                var registerTelemetryPacket = CreateSetBroadcastFlagsPacket(DefaultOperationalBroadcastFlags);
                await SendAsync(registerTelemetryPacket, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                /* shutdown */
            }
            catch
            {
                // best-effort only
            }
        }, ct);

        // Hlavná zdieľaná receive-slučka na _sendUdp.
        _mainReceiveTask = Task.Run(() => MainReceiveLoopAsync(ct), ct);
        // Periodický polling LAN_SYSTEMSTATE_GETDATA každé 2 s cez _sendUdp.
        _telemetryPollTask = Task.Run(() => TelemetryPollLoopAsync(ct), ct);

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var packet in LanGetRBusPollPackets)
                    await SendAsync(packet, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                /* shutdown */
            }
            catch
            {
                // best-effort only
            }
        }, ct);
    }

    private void StopTelemetry()
    {
        // Nepoužívame .GetAwaiter().GetResult() – Dispose/StopTelemetry sa môže volať
        // z UI vlákna a synchrónne čakanie na receive-loop môže spôsobiť deadlock.
        var shutdown = BeginDisconnectAsync();
        try
        {
            shutdown.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            /* cleanup je best-effort */
        }
    }

    private (Task? PollTask, Task? ReceiveTask) CancelTelemetry()
    {
        var cts = _telemetryCts;
        var pollTask = _telemetryPollTask;
        var receiveTask = _mainReceiveTask;

        _telemetryCts = null;
        _telemetryPollTask = null;
        _mainReceiveTask = null;
        _lastRBusModuleMasks.Clear();
        Interlocked.Exchange(ref _lastRBusFrameReceivedTicks, 0);
        Interlocked.Exchange(ref _lastRBusHeartbeatTicks, 0);
        Interlocked.Exchange(ref _rBusDirectFrameReceiveCount, 0);
        Interlocked.Exchange(ref _rBusIgnoredLanX43Count, 0);
        Interlocked.Exchange(ref _rBusPollSendCount, 0);
        _lastDirectRBusFrameHex = string.Empty;
        _lastIgnoredLanX43FrameHex = string.Empty;

        try
        {
            cts?.Cancel();
        }
        catch
        {
        }

        try
        {
            cts?.Dispose();
        }
        catch
        {
        }

        // Reset – pri odpojení UI ukáže prázdne hodnoty.
        MainVoltage = null;
        ProgVoltage = null;
        TrackCurrent = null;
        ProgTrackCurrent = null;
        CentralTemperature = null;

        return (pollTask, receiveTask);
    }

    private async Task TelemetryPollLoopAsync(CancellationToken ct)
    {
        var nextSystemStatusAtUtc = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            // Stavová telemetria do status baru môže byť vypnutá, ale R-BUS obsadenosť musí
            // bežať stále – je to funkčná časť detekcie blokov, nie len vizuálna telemetria.
            // Preto IsTelemetryEnabled gate-uje iba LAN_SYSTEMSTATE_GETDATA, nie R-BUS polling.
            if (IsTelemetryEnabled && DateTime.UtcNow >= nextSystemStatusAtUtc)
            {
                try
                {
                    await SendAsync(LanGetSystemStatusPacket, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && !IsShutdownRequested)
                {
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"⚠️ Z21 telemetria send chyba (LAN_SYSTEMSTATE_GETDATA): {ex.GetType().Name}: {ex.Message}",
                        DiagnosticLevel.Warning);
                }

                nextSystemStatusAtUtc = DateTime.UtcNow.AddMilliseconds(SystemStatusPollIntervalMs);
            }

            try
            {
                foreach (var packet in LanGetRBusPollPackets)
                    await SendAsync(packet, ct).ConfigureAwait(false);
                Interlocked.Increment(ref _rBusPollSendCount);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception) when (!ct.IsCancellationRequested && !IsShutdownRequested)
            {
                // R-BUS polling errors are intentionally hidden from Doctor.
            }

            try
            {
                await Task.Delay(RBusPollIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    ///     Hlavná zdieľaná receive-slučka nad _sendUdp.
    ///     Sem patrí parsovanie VŠETKÝCH spontánnych odpovedí z21 (telemetria,
    ///     v budúcnosti aj lokomotívy / výhybky / broadcasty).
    /// </summary>
    private async Task MainReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var udp = _sendUdp;
            if (udp == null) return;

            UdpReceiveResult result;
            try
            {
                result = await ReceiveWithCancellationAsync(udp, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException) when (ct.IsCancellationRequested || IsShutdownRequested)
            {
                return;
            }
            catch (SocketException ex)
            {
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"⚠️ MainReceiveLoop socket chyba: {ex.SocketErrorCode}: {ex.Message}",
                    DiagnosticLevel.Warning);

                // Sieťová chyba – krátka pauza a ďalšie kolo.
                try
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && !IsShutdownRequested)
            {
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"⚠️ MainReceiveLoop chyba: {ex.GetType().Name}: {ex.Message}",
                    DiagnosticLevel.Warning);

                // Sieťová chyba – krátka pauza a ďalšie kolo.
                try
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            DispatchIncomingFrame(result.Buffer);
        }
    }

    /// <summary>
    ///     Centrálny dispečer prichádzajúcich rámcov z _sendUdp.
    ///     Tu sa pridávajú podmienky pre nové typy odpovedí.
    /// </summary>
    private void DispatchIncomingFrame(byte[] frame)
    {
        if (frame == null || frame.Length < 4) return;

        // Akýkoľvek validný rámec aktualizuje keepalive timestamp – využíva ho PingAsync.
        Interlocked.Exchange(ref _lastFrameReceivedTicks, _speedStopwatch.ElapsedMilliseconds);

        foreach (var singleFrame in SplitCombinedFrames(frame))
            DispatchSingleIncomingFrame(singleFrame);
    }

    internal static IReadOnlyList<byte[]> SplitCombinedFrames(byte[] payload)
    {
        if (payload == null || payload.Length < 4)
            return Array.Empty<byte[]>();

        var frames = new List<byte[]>();
        var offset = 0;

        while (offset + 4 <= payload.Length)
        {
            var frameLength = payload[offset] | (payload[offset + 1] << 8);
            if (frameLength < 4 || offset + frameLength > payload.Length)
                break;

            var singleFrame = new byte[frameLength];
            Buffer.BlockCopy(payload, offset, singleFrame, 0, frameLength);
            frames.Add(singleFrame);
            offset += frameLength;
        }

        if (frames.Count > 0)
            return frames;

        var fallback = new byte[payload.Length];
        Buffer.BlockCopy(payload, 0, fallback, 0, payload.Length);
        return new[] { fallback };
    }

    private void DispatchSingleIncomingFrame(byte[] frame)
    {
        if (frame == null || frame.Length < 4)
            return;

        // LAN_SYSTEMSTATE_DATACHANGED – 20 B paket, header 0x14 0x00 0x84 0x00 (LE)
        if (frame.Length >= 4 && frame[0] == 0x14 && frame[1] == 0x00 && frame[2] == 0x84 && frame[3] == 0x00)
        {
            TryParseSystemStateDatachanged(frame);
            return;
        }

        // R-Bus / S88 spätná väzba.
        // Za spoľahlivý feedback považujeme iba priamy LAN_RMBUS_DATACHANGED (0x80 0x00).
        // LAN_X rámce s XHeader 0x43 sa v reálnych logoch ukázali ako X-Bus / accessory broadcast,
        // nie ako stabilná occupancy telemetria – spôsobovali falošné prepínanie blokov počas
        // odosielania návestí po pripojení centrály.
        if (IsDirectRBusDataChangedFrame(frame))
        {
            Interlocked.Exchange(ref _lastRBusFrameReceivedTicks, _speedStopwatch.ElapsedMilliseconds);
            Interlocked.Increment(ref _rBusDirectFrameReceiveCount);
            _lastDirectRBusFrameHex = BytesToHex(frame);
            TryParseRBusDataChanged(frame);
            return;
        }

        if (IsLanX0x43Frame(frame))
        {
            _lastIgnoredLanX43FrameHex = BytesToHex(frame);
            Interlocked.Increment(ref _rBusIgnoredLanX43Count);
        }

        // LAN_X_CV_RESULT — potvrdenie zápisu CV; naplní TCS pre WriteCvAsync
        if (frame.Length >= 10
            && frame[0] == 0x0A && frame[1] == 0x00
            && frame[2] == 0x40 && frame[3] == 0x00
            && frame[4] == 0x64 && frame[5] == 0x14)
        {
            _pendingCvWriteTcs?.TrySetResult(frame[8]);
            return;
        }

        // Sem v budúcnosti pribudnú ďalšie ramce (loco, turnout, broadcasts).
    }

    /// <summary>
    ///     Rozparsuje LAN_SYSTEMSTATE_DATACHANGED (header 0x14 0x00 0x84 0x00) a aktualizuje
    ///     telemetrické vlastnosti.
    ///     Štruktúra paketu podľa oficiálnej Z21 LAN špecifikácie v1.13 (20 B):
    ///     [0..3]   header 0x14 0x00 0x84 0x00
    ///     [4..5]   MainCurrent          (mA, signed LE)   – aktuálny prúd hlavnej koľaje
    ///     [6..7]   ProgCurrent          (mA, signed LE)   – prúd programovacej koľaje
    ///     [8..9]   FilteredMainCurrent  (mA, signed LE)   – vyhladený prúd → krajší pre UI
    ///     [10..11] Temperature          (°C, signed LE)   – teplota centrály
    ///     [12..13] SupplyVoltage        (mV, unsigned LE) – napätie napájania
    ///     [14..15] VCCVoltage           (mV, unsigned LE) – napätie hlavnej koľaje  ← MAIN
    ///     [16]     CentralState         (bit-field)
    ///     [17]     CentralStateEx
    ///     [18]     (reserved)
    ///     [19]     CapabilityFlags
    /// </summary>
    internal void TryParseSystemStateDatachanged(byte[] data)
    {
        if (data == null || data.Length < 16) return;
        if (data[0] != 0x14 || data[1] != 0x00 || data[2] != 0x84 || data[3] != 0x00) return;

        // Prúdy + teplota – signed 16-bit LE.
        var progCurrentMa = (short)(data[6] | (data[7] << 8));
        var filteredMainMa = (short)(data[8] | (data[9] << 8));
        var tempC = (short)(data[10] | (data[11] << 8));

        // Napätia – unsigned 16-bit LE (môžu byť tesne pod 32768 mV, znamienko je zbytočné).
        var supplyMv = (ushort)(data[12] | (data[13] << 8));
        var vccMv = data.Length >= 16
            ? (ushort)(data[14] | (data[15] << 8))
            : (ushort)0;

        MainVoltage = vccMv / 1000.0; // VCCVoltage = napätie koľaje
        ProgVoltage = supplyMv / 1000.0; // SupplyVoltage ako proxy
        TrackCurrent = filteredMainMa / 1000.0; // vyhladený prúd – krajšie pre UI
        ProgTrackCurrent = progCurrentMa / 1000.0;
        CentralTemperature = tempC;
    }

    /// <summary>
    ///     Rozparsuje feedback rámec R-Bus / S88 a publikuje zmeny jednotlivých
    ///     1-based vstupov spätnoväzbových modulov.
    ///     Štruktúra LAN_RMBUS_DATACHANGED podľa Z21 LAN špec. v1.13 (sekcia 7.1.2):
    ///     [0..1] dĺžka rámca (LE, vždy 0x0F 0x00 = 15 B)
    ///     [2..3] header = 0x80 0x00
    ///     [4]    GroupIndex (0-based, každý group = 10 modulov)
    ///     • 0  → moduly   1..10
    ///     • 1  → moduly  11..20
    ///     • 15 → moduly 151..160
    ///     [5..14] 10 bajtov masiek, jeden bajt = jeden modul, bit0 = vstup 1, bit7 = vstup 8.
    ///     UI a konfigurácia TrackFlow používajú 1-based ModuleAddress aj PortNumber.
    /// </summary>
    internal void TryParseRBusDataChanged(byte[] data)
    {
        if (data == null || data.Length < 6)
            return;

        if (!IsDirectRBusDataChangedFrame(data))
            return;

        var declaredLength = data[0] | (data[1] << 8);
        if (declaredLength < 6 || data.Length < declaredLength)
            return;

        // Z21 protokol: GroupIndex N ⇒ moduly (N*10+1)..(N*10+10).
        // Pôvodná formula `data[4] + 1` posielala pre group 1 modul 2 namiesto 11,
        // čo by pri >10 R-BUS / S88 moduloch kolidovalo s adresami z group 0.
        var firstDirectModuleAddress = data[4] * 10 + 1;
        var rBusDataEndExclusive = Math.Min(declaredLength, 15);
        for (var byteIndex = 5; byteIndex < rBusDataEndExclusive; byteIndex++)
            PublishRBusModuleState(firstDirectModuleAddress + (byteIndex - 5), data[byteIndex]);
    }

    private static bool IsDirectRBusDataChangedFrame(byte[] frame)
    {
        return frame.Length >= 6
               && frame[2] == 0x80
               && frame[3] == 0x00;
    }

    private static bool IsLanX0x43Frame(byte[] frame)
    {
        return frame.Length >= 5
               && frame[2] == 0x40
               && frame[3] == 0x00
               && frame[4] == 0x43;
    }

    private void PublishRBusModuleState(int moduleAddress, byte mask)
    {
        var hadPreviousMask = _lastRBusModuleMasks.TryGetValue(moduleAddress, out var previousMask);
        if (hadPreviousMask && previousMask == mask)
            return;

        // Pri PRVOM rámci z modulu publikujeme len bity, ktoré sú reálne aktívne (mask & 1).
        // UI default pre indikátor je "neaktívny" (cont_ind_d.png), takže nemusíme posielať
        // 8 udalostí so 6 nulami – ušetríme záplavu Doctor logov pri štarte centrály
        // a zachováme správnu semantiku "0 = neaktívne / žiadna zmena oproti defaultu".
        var changedMask = hadPreviousMask
            ? (byte)(previousMask ^ mask)
            : mask;

        _lastRBusModuleMasks[moduleAddress] = mask;


        for (var bit = 0; bit < 8; bit++)
        {
            if ((changedMask & (1 << bit)) == 0)
                continue;

            var portNumber = bit + 1;
            var isActive = (mask & (1 << bit)) != 0;
            RBusFeedbackChanged?.Invoke(new RBusFeedbackState(moduleAddress, portNumber, isActive));
        }
    }

    private readonly record struct FrameWaitResult(bool Matched, int FramesSeen);
}
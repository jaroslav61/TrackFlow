using System;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace TrackFlow.Services.Dcc;

/// <summary>
/// Generický klient pre sériovú (COM) DCC centrálu (XpressNet).
/// Implementuje otvorenie COM portu a XpressNet service-mode čítanie CV.
/// </summary>
public sealed class SerialDccClient : IDccCentralClient, IDccProgrammingClient, IDccTelemetry, IDisposable
{
    // ── IDccTelemetry: XpressNet telemetriu napätia / prúdu nepodporuje ──
    public bool IsTelemetrySupported => false;
    public bool IsBlackZ21 => false;
    public double? MainVoltage => null;
    public double? ProgVoltage => null;
    public double? TrackCurrent => null;
    public double? ProgTrackCurrent => null;
    public double? CentralTemperature => null;
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { /* no-op: NanoX nikdy nevyvolá zmenu telemetrických hodnôt */ }
        remove { /* no-op */ }
    }

    private const int InitialProgrammingResponseDelayMs = 200;
    private const int BusyRetryDelayMs = 100;

    // Celkový timeout service-mode CV readu (Paged Mode v2 + SMRR retry cyklus):
    //  - Po odoslaní 22 14 cv XOR centrála pošle 61 02 (busy / measuring).
    //  - PC musí EXPLICITNE poslať Service Mode Results Request 21 10 31 (SMRR),
    //    inak centrála nikdy nevyšle dátový rámec.
    //  - Centrála odpovedá buď 61 12 (programmer busy → opakovať SMRR), 61 13
    //    (No ACK), 63 10 V VV XOR (Paged Mode result) alebo 63 14 V VV XOR
    //    (Direct Mode result).
    //  - Starší firmvér môže vrátiť 61 82 (instruction not supported) – v tom
    //    prípade prejdeme späť na pasívne čakanie spontánneho 63 1x rámca.
    //  - 8 s pokrýva typický SMRR retry cyklus (~3× 200 ms + relé + ACK + prenos).
    private const int DefaultPassiveReadTimeoutMs = 8_000;
    private const int MinEffectiveTimeoutMs = DefaultPassiveReadTimeoutMs;
    private const int MaxEffectiveTimeoutMs = 30_000;

    private readonly Func<string, int, ISerialDccPort> _portFactory;
    private ISerialDccPort? _serialPort;

    /// <summary>
    /// Skutočný stav spojenia: odráža fyzický stav portu.
    /// Keď sa USB-to-serial adaptér odpojí, OS nastaví SerialPort.IsOpen = false
    /// a monitor to automaticky zachytí.
    /// </summary>
    public bool IsConnected => _serialPort?.IsOpen ?? false;
    public uint? SerialNumber => null;

    // Interná príznaková premenná pre logiku Connect/Disconnect
    private bool _wasConnected;

    public SerialDccClient()
        : this((portName, baudRate) => new SerialPortAdapter(portName, baudRate))
    {
    }

    internal SerialDccClient(Func<string, int, ISerialDccPort> portFactory)
    {
        _portFactory = portFactory ?? throw new ArgumentNullException(nameof(portFactory));
    }

    public async Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var portName = host.Trim();
        if (string.IsNullOrWhiteSpace(portName))
            return false;

        if (port <= 0)
            return false;

        Disconnect();

        try
        {
            var serialPort = _portFactory(portName, port);
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                serialPort.Open();
            }, ct).ConfigureAwait(false);

            _serialPort = serialPort;
            _wasConnected = true;
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"✅ NanoX-S88 pripojená cez {portName} @ {port} Bd.",
                DiagnosticLevel.Success);
            return true;
        }
        catch (OperationCanceledException)
        {
            Disconnect();
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"❌ NanoX-S88: port {portName} @ {port} Bd je obsadený alebo neprístupný.",
                DiagnosticLevel.Warning);
            Disconnect();
            return false;
        }
        catch (IOException)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"❌ NanoX-S88: I/O chyba pri otváraní {portName} @ {port} Bd.",
                DiagnosticLevel.Warning);
            Disconnect();
            return false;
        }
        catch (ArgumentException)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"❌ NanoX-S88: neplatný sériový port {portName} alebo baudrate {port}.",
                DiagnosticLevel.Warning);
            Disconnect();
            return false;
        }
        catch (InvalidOperationException)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"❌ NanoX-S88: port {portName} sa nepodarilo otvoriť (invalid operation).",
                DiagnosticLevel.Warning);
            Disconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        if (_wasConnected)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                "ℹ️ NanoX-S88: zatváram sériové spojenie.",
                DiagnosticLevel.Info);
        }

        _wasConnected = false;

        SafeCloseAndDispose(_serialPort);
        _serialPort = null;
    }

    public async Task<int> ReadCvAsync(int cvAddress, DccProgrammingTestMode programmingMode, int timeoutMs, int locoAddress, CancellationToken ct = default)
    {
        if (!IsConnected || _serialPort == null || !_serialPort.IsOpen)
            throw new InvalidOperationException("NanoX-S88 nie je pripojená.");

        if (programmingMode != DccProgrammingTestMode.ServiceTrack)
            throw new NotSupportedException("NanoX-S88 aktuálne podporuje iba Service Mode čítanie CV cez XpressNet.");

        if (cvAddress < 1 || cvAddress > 256)
            throw new ArgumentOutOfRangeException(nameof(cvAddress), "CV číslo musí byť v rozsahu 1..256 pre XpressNet Paged Mode v2.");

        // 0. ABSOLÚTNE VYČISTENIE LINKY PRED ŠTARTOM (Prevencia zablokovania z minulého testu)
        try { _serialPort.DiscardInBuffer(); } catch { }

        var effectiveTimeoutMs = Math.Clamp(timeoutMs, MinEffectiveTimeoutMs, MaxEffectiveTimeoutMs);
        var packet = CreateServiceModeCvReadPacket(cvAddress);

        // 1. Lenz XpressNet handshake
        await SendCommandStationVersionHandshakeAsync(_serialPort, ct).ConfigureAwait(false);

        // 2. Track Power ON (Resume Operations)
        await SendTrackPowerOnAsync(_serialPort, ct).ConfigureAwait(false);

        // 2b. Drén Resume Operations broadcastov (61 01) – NanoX-S88 ich po Track Power ON pošle 3–4×.
        //     Bez vyčistenia sa nám prvý CV-read frame zamení s týmto echom a "zožerú" 200–600 ms timeoutu.
        try { _serialPort.DiscardInBuffer(); } catch { }
        await DelayWithTimeoutAsync(200, CancellationToken.None, ct, cvAddress, effectiveTimeoutMs).ConfigureAwait(false);
        try { _serialPort.DiscardInBuffer(); } catch { }

        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"📤 NanoX XpressNet CV read request: CV{cvAddress}, mode={programmingMode}, timeout={effectiveTimeoutMs} ms (zadané {timeoutMs} ms), packet={BytesToHex(packet)}",
            DiagnosticLevel.Info);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(effectiveTimeoutMs);

        var frameSeq = 0;
        var statusFrameCount = 0;
        var saw6102 = false;
        var smrrSupported = true;   // optimisticky; ak NanoX vráti 61 82, prepneme na pasívne čakanie
        var smrrAttempts = 0;
        const int MaxSmrrAttempts = 15; // ~3 s pri 200 ms pauze
        const int SmrrRetryDelayMs = 200;

        const int MaxStartAttempts = 3;
        const int Post6113DrainDelayMs = 300;
        const int InterByteReadTimeoutMs = 1000;

        // Lokálny helper na poslanie SMRR (21 10 31)
        async Task SendSmrrAsync()
        {
            if (!smrrSupported) return;
            var smrr = CreateServiceModeResultsRequestPacket();
            await _serialPort.WriteAsync(smrr, timeoutCts.Token).ConfigureAwait(false);
            smrrAttempts++;
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"📤 NanoX-S88 SMRR (pokus {smrrAttempts}/{MaxSmrrAttempts}): {BytesToHex(smrr)}",
                DiagnosticLevel.Info);
        }

        // Vnútrofunkčný parser surových bajtov s fixným časovým limitom na prvý bajt.
        // Používa sa aj po prijatí CV výsledku, keď už čakáme len na ukončovací status
        // 61 00 / 61 01 a nechceme visieť až do celého programovacieho timeoutu.
        async Task<byte[]> ReadRawResponseAsync(
            CancellationToken timeoutToken,
            CancellationToken outerToken,
            int headerTimeoutMs = InterByteReadTimeoutMs,
            int interByteTimeoutMs = InterByteReadTimeoutMs)
        {
            try
            {
                using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken);
                headerCts.CancelAfter(headerTimeoutMs);
                byte header = await _serialPort.ReadByteAsync(headerCts.Token).ConfigureAwait(false);

                using var interByteCts = new CancellationTokenSource(interByteTimeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken, interByteCts.Token);

                byte execution = await _serialPort.ReadByteAsync(linkedCts.Token).ConfigureAwait(false);

                var expectedLength = GetExpectedResponseLength(header, execution);
                byte[] response = new byte[expectedLength];
                response[0] = header;
                response[1] = execution;

                for (var i = 2; i < expectedLength; i++)
                    response[i] = await _serialPort.ReadByteAsync(linkedCts.Token).ConfigureAwait(false);

                return response;
            }
            catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
            {
                throw new TimeoutException($"NanoX-S88 neodpovedala na Paged Mode meranie CV{cvAddress} v limite.");
            }
        }

        byte[]? pendingResponse = null;
        int? pagedValue = null;
        const int PostResultDrainHeaderTimeoutMs = 400;

        try
        {
            // Štartovací cyklus s Retry pre prípadné No ACK (61 13) alebo Centrála Busy (61 81)
            for (var attempt = 1; attempt <= MaxStartAttempts; attempt++)
            {
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"📤 NanoX XpressNet CV read start (pokus {attempt}/{MaxStartAttempts}): CV{cvAddress}, packet={BytesToHex(packet)}",
                    DiagnosticLevel.Info);
                await _serialPort.WriteAsync(packet, timeoutCts.Token).ConfigureAwait(false);

                await DelayWithTimeoutAsync(InitialProgrammingResponseDelayMs, timeoutCts.Token, ct, cvAddress, effectiveTimeoutMs).ConfigureAwait(false);

                var firstResponse = await ReadRawResponseAsync(timeoutCts.Token, ct).ConfigureAwait(false);

                frameSeq++;
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"📥 NanoX XpressNet raw response #{frameSeq}: {BytesToHex(firstResponse)}",
                    DiagnosticLevel.Info);
                ValidateChecksum(firstResponse);

                // Ak hneď na začiatku povie, že je obsadená, vyčistíme zbernicu a urobíme dlhšiu pauzu
                if (firstResponse[0] == 0x61 && firstResponse[1] == 0x81)
                {
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"⚠️ NanoX-S88 hlási 61 81 (Zaneprázdnená) pri štarte. Čistím buffer, čakám a skúšam znova...",
                        DiagnosticLevel.Warning);
                    try { _serialPort.DiscardInBuffer(); } catch { }
                    await DelayWithTimeoutAsync(500, timeoutCts.Token, ct, cvAddress, effectiveTimeoutMs).ConfigureAwait(false);
                    continue;
                }

                if (firstResponse[0] == 0x61 && firstResponse[1] == 0x13)
                {
                    if (attempt >= MaxStartAttempts)
                    {
                        throw new InvalidOperationException(
                            $"NanoX-S88 odmietla príkaz na čítanie CV{cvAddress} (rámec 61 13 No ACK po {MaxStartAttempts} pokusoch).");
                    }
                    try { _serialPort.DiscardInBuffer(); } catch { }
                    await DelayWithTimeoutAsync(Post6113DrainDelayMs, timeoutCts.Token, ct, cvAddress, effectiveTimeoutMs).ConfigureAwait(false);
                    continue;
                }

                pendingResponse = firstResponse;
                break;
            }

            // Hlavná spracovateľská slučka
            while (true)
                {
                    byte[] response;
                    try
                    {
                        if (pendingResponse != null)
                    {
                        response = pendingResponse;
                        pendingResponse = null;
                        }
                        else
                        {
                            response = await ReadRawResponseAsync(
                                timeoutCts.Token,
                                ct,
                                pagedValue.HasValue ? PostResultDrainHeaderTimeoutMs : InterByteReadTimeoutMs,
                                InterByteReadTimeoutMs).ConfigureAwait(false);
                        }
                    }
                    catch (TimeoutException) when (pagedValue.HasValue)
                    {
                        TrackFlowDoctorService.Instance.Diagnose(
                            "DCC",
                            $"ℹ️ NanoX-S88: po prijatí CV výsledku neprišiel do {PostResultDrainHeaderTimeoutMs} ms žiadny ďalší rámec; vraciam CV{cvAddress}={pagedValue.Value}.",
                            DiagnosticLevel.Info);
                        return pagedValue.Value;
                    }
                    catch (TimeoutException) when (statusFrameCount > 0 || saw6102)
                    {
                        throw new TimeoutException(
                            $"NanoX-S88 vstúpila do service mode (stavové rámce 61 02 Busy), " +
                            $"ale dátový výsledok CV{cvAddress} (rámec 63 10 / 63 14) neprišiel v celkovom limite.");
                }

                frameSeq++;

                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"📥 NanoX XpressNet raw response #{frameSeq}: {BytesToHex(response)}",
                    DiagnosticLevel.Info);
                ValidateChecksum(response);

                // X. GLOBÁLNE 3-bajtové stavové / systémové rámce XpressNetu (01 XX XX)
                //    Tieto rámce nesú globálny stav trate/centrály (napr. skrat, vypnutie napájania,
                //    prechod do ostrej prevádzky) a môžu prísť kedykoľvek počas service-mode čítania.
                //    Nemajú nič spoločné s výsledkom CV readu, preto ich iba zalogujeme a pokračujeme
                //    v čakaní na skutočný výsledkový rámec 63 10 / 63 14.
                if (IsXpressNetSystemStatusFrame(response))
                {
                    statusFrameCount++;
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"ℹ️ NanoX-S88: ignorujem systémový XpressNet rámec počas čítania CV{cvAddress}: {BytesToHex(response)}",
                        DiagnosticLevel.Info);
                    continue;
                }

                // A. FINÁLNY ÚSPECH – DÁTOVÝ RÁMEC s hodnotou CV
                //    Formát (Lenz XpressNet v3.6, 5 bajtov): 0x63 ID CV V XOR
                //    63 10 = Paged Mode v2 result, 63 14 = Direct Mode result.
                //    response[2] = CV číslo (echo), response[3] = HODNOTA CV.
                if (response[0] == 0x63 && (response[1] == 0x10 || response[1] == 0x14) && response.Length >= 5)
                {
                    var modeLabel = response[1] == 0x10 ? "Paged Mode v2" : "Direct Mode";
                    pagedValue = response[3];
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"✅ NanoX-S88 ({modeLabel}): CV{cvAddress} (echo {response[2]}) = {pagedValue.Value} (0x{pagedValue.Value:X2})",
                        DiagnosticLevel.Success);
                    continue;
                }

                // B. NACK CHYBA POČAS BEHU – dekodér neodpovedal
                if (response[0] == 0x61 && response[1] == 0x13)
                {
                    throw new TimeoutException($"Dekodér neodpovedal pri čítaní CV{cvAddress} (NACK 0x61 0x13).");
                }

                // C. INFORMAČNÉ STATUSY (vrátane 61 00, 61 01, 61 02, 61 12, 61 81, 61 82)
                if (response[0] == 0x61 && IsKnownInformationalStatus(response[1]))
                {
                    statusFrameCount++;

                    if (pagedValue.HasValue && (response[1] == 0x00 || response[1] == 0x01))
                    {
                        TrackFlowDoctorService.Instance.Diagnose(
                            "DCC",
                            $"✅ NanoX-S88: prijal som ukončovací status {BytesToHex(response)} po CV výsledku; vraciam CV{cvAddress}={pagedValue.Value}.",
                            DiagnosticLevel.Success);
                        return pagedValue.Value;
                    }

                    // 61 02 – meranie beží na koľaji. Po krátkej pauze si vyžiadame výsledok cez SMRR.
                    if (response[1] == 0x02)
                    {
                        saw6102 = true;
                        TrackFlowDoctorService.Instance.Diagnose(
                            "DCC",
                            "ℹ️ NanoX-S88: 61 02 (busy / measuring) – po pauze pošlem Service Mode Results Request.",
                            DiagnosticLevel.Info);
                        await DelayWithTimeoutAsync(BusyRetryDelayMs, timeoutCts.Token, ct, cvAddress, effectiveTimeoutMs).ConfigureAwait(false);
                        if (smrrSupported && smrrAttempts < MaxSmrrAttempts)
                            await SendSmrrAsync().ConfigureAwait(false);
                        continue;
                    }

                    // 61 12 – programmer busy (odpoveď na SMRR): meranie ešte beží, retry SMRR
                    if (response[1] == 0x12)
                    {
                        TrackFlowDoctorService.Instance.Diagnose(
                            "DCC",
                            "ℹ️ NanoX-S88: 61 12 (programmer busy) – meranie ešte beží, opakujem SMRR.",
                            DiagnosticLevel.Info);
                        await DelayWithTimeoutAsync(SmrrRetryDelayMs, timeoutCts.Token, ct, cvAddress, effectiveTimeoutMs).ConfigureAwait(false);
                        if (smrrSupported && smrrAttempts < MaxSmrrAttempts)
                        {
                            await SendSmrrAsync().ConfigureAwait(false);
                        }
                        else if (smrrAttempts >= MaxSmrrAttempts)
                        {
                            throw new TimeoutException(
                                $"NanoX-S88 neukončila meranie CV{cvAddress} ani po {MaxSmrrAttempts} SMRR pokusoch (programmer trvale busy).");
                        }
                        continue;
                    }

                    // 61 82 – Instruction not supported. Najpravdepodobnejšie SMRR neakceptovaný
                    // → prepneme na pasívne čakanie spontánneho 63 1x rámca.
                    if (response[1] == 0x82)
                    {
                        if (smrrSupported)
                        {
                            smrrSupported = false;
                            TrackFlowDoctorService.Instance.Diagnose(
                                "DCC",
                                "⚠️ NanoX-S88: 61 82 (instruction not supported) – firmvér nepodporuje SMRR. Prepínam na pasívne čakanie spontánneho 63 1x rámca.",
                                DiagnosticLevel.Warning);
                        }
                        else
                        {
                            TrackFlowDoctorService.Instance.Diagnose(
                                "DCC",
                                $"ℹ️ NanoX-S88: 61 82 (instruction not supported) pri čítaní CV{cvAddress} – ignorujem, pokračujem v pasívnom čakaní.",
                                DiagnosticLevel.Info);
                        }
                        continue;
                    }

                    // 61 81 – Command Station Busy uprostred slučky → defenzívne prerušíme
                    if (response[1] == 0x81)
                    {
                        TrackFlowDoctorService.Instance.Diagnose(
                            "DCC",
                            "⚠️ NanoX-S88: Centrála hlási 61 81 (Command Station Busy) počas slučky. Čakám na uvoľnenie a skúšam ďalej.",
                            DiagnosticLevel.Warning);
                        try { _serialPort.DiscardInBuffer(); } catch { }
                        await DelayWithTimeoutAsync(BusyRetryDelayMs, timeoutCts.Token, ct, cvAddress, effectiveTimeoutMs).ConfigureAwait(false);
                        continue;
                    }

                    var statusMeaning = DescribeStatusFrame(response[1]);
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"ℹ️ NanoX-S88: {statusMeaning} pri čítaní CV{cvAddress}: {BytesToHex(response)}",
                        DiagnosticLevel.Info);
                    continue;
                }

                throw new InvalidOperationException($"Neočakávaná odpoveď NanoX-S88: {BytesToHex(response)}");
            }
        }
        finally
        {
            try { _serialPort?.DiscardInBuffer(); } catch { }
            try { await Task.Delay(250, CancellationToken.None).ConfigureAwait(false); } catch { }
            await TryExitServiceModeAsync(_serialPort).ConfigureAwait(false);
            try { await Task.Delay(300, CancellationToken.None).ConfigureAwait(false); } catch { }
            try { _serialPort?.DiscardInBuffer(); } catch { }
        }
    }

    public async Task WriteCvAsync(int cvAddress, int value, DccProgrammingTestMode programmingMode, int timeoutMs, int locoAddress, CancellationToken ct = default)
    {
        if (!IsConnected || _serialPort == null || !_serialPort.IsOpen)
            throw new InvalidOperationException("NanoX-S88 nie je pripojená.");

        if (programmingMode != DccProgrammingTestMode.ServiceTrack)
            throw new NotSupportedException("NanoX-S88 aktuálne podporuje iba Service Mode zápis CV cez XpressNet.");

        if (cvAddress < 1 || cvAddress > 256)
            throw new ArgumentOutOfRangeException(nameof(cvAddress), "CV číslo musí byť v rozsahu 1..256 pre XpressNet Paged Mode v2.");

        if (value < 0 || value > 255)
            throw new ArgumentOutOfRangeException(nameof(value), "CV hodnota musí byť v rozsahu 0..255.");

        try { _serialPort.DiscardInBuffer(); } catch { }

        var effectiveTimeoutMs = Math.Clamp(timeoutMs, MinEffectiveTimeoutMs, MaxEffectiveTimeoutMs);
        var packet = CreateServiceModeCvWritePacket(cvAddress, value);

        await SendCommandStationVersionHandshakeAsync(_serialPort, ct).ConfigureAwait(false);
        await SendTrackPowerOnAsync(_serialPort, ct).ConfigureAwait(false);

        try { _serialPort.DiscardInBuffer(); } catch { }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(effectiveTimeoutMs);

        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"📤 NanoX XpressNet CV write request: CV{cvAddress}={value}, mode={programmingMode}, timeout={effectiveTimeoutMs} ms, packet={BytesToHex(packet)}",
            DiagnosticLevel.Info);

        await _serialPort.WriteAsync(packet, timeoutCts.Token).ConfigureAwait(false);
        await DelayWithTimeoutAsync(700, timeoutCts.Token, ct, cvAddress, effectiveTimeoutMs).ConfigureAwait(false);

        try
        {
            await _serialPort.WriteAsync(CreateExitServiceModePacket(), timeoutCts.Token).ConfigureAwait(false);
            await Task.Delay(300, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"NanoX-S88 neukončila service mode po zápise CV{cvAddress} v limite.");
        }

        try { _serialPort.DiscardInBuffer(); } catch { }

        var readBack = await ReadCvAsync(cvAddress, programmingMode, effectiveTimeoutMs, locoAddress, ct).ConfigureAwait(false);
        if (readBack != value)
        {
            throw new InvalidOperationException(
                $"Overenie zápisu zlyhalo: po zápise CV{cvAddress}={value} NanoX-S88 prečítala hodnotu {readBack}.");
        }
    }

    public async Task ReadMultipleCvsAsync(
        IReadOnlyList<int> cvAddresses,
        int timeoutMsPerCv,
        int interCvDelayMs,
        Action<int, int> onCvRead,
        Action<int, int, int>? onCvReading = null,
        CancellationToken ct = default)
    {
        for (int i = 0; i < cvAddresses.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
                await Task.Delay(interCvDelayMs, ct).ConfigureAwait(false);
            int cv = cvAddresses[i];
            onCvReading?.Invoke(cv, i, cvAddresses.Count);
            int value = await ReadCvAsync(cv, DccProgrammingTestMode.ServiceTrack, timeoutMsPerCv, 0, ct).ConfigureAwait(false);
            onCvRead(cv, value);
        }
    }


    private static int GetExpectedResponseLength(byte header, byte execution)
    {
        return (header, execution) switch
        {
            (0x63, 0x10) => 5,
            (0x63, 0x14) => 5,
            (0x63, 0x21) => 5,
            (0x01, _) => 3,
            (0x61, _) => 3,
            _ => 4
        };
    }

    /// <summary>
    /// NanoX-S88 / Lenz LI100F v2 service-mode CV read packet v Paged Mode v2 formáte
    /// (0x22 0x14 CV XOR). Pre CV1: 22 14 01 37 (0x22 ^ 0x14 ^ 0x01 = 0x37).
    /// Pôvodný Direct Mode formát (0x22 0x11 …) NanoX-S88 stabilne odmietal rámcom
    /// 61 13 (No ACK), preto sme prešli na Paged Mode v2, ktorý je pre staršiu
    /// Lenz LI100F v2 špecifikáciu spoľahlivejší.
    /// Tento paket preukázateľne fyzicky spúšťa meranie na programovacej koľaji
    /// (busy rámce 61 02 63 a pohyb dekodéra). Posiela sa po handshake + Track Power ON.
    /// </summary>
    public static byte[] CreateServiceModeCvReadPacket(int cvAddress)
    {
        if (cvAddress < 1 || cvAddress > 256)
            throw new ArgumentOutOfRangeException(nameof(cvAddress), "CV číslo musí byť v rozsahu 1..256 pre XpressNet Paged Mode v2.");

        const byte header = 0x22;
        const byte execution = 0x14;
        byte cvOneBased = (byte)cvAddress;
        byte checksum = (byte)(header ^ execution ^ cvOneBased);
        return new[] { header, execution, cvOneBased, checksum };
    }

    public static byte[] CreateServiceModeCvWritePacket(int cvAddress, int value)
    {
        if (cvAddress < 1 || cvAddress > 256)
            throw new ArgumentOutOfRangeException(nameof(cvAddress), "CV číslo musí byť v rozsahu 1..256 pre XpressNet Paged Mode v2.");
        if (value < 0 || value > 255)
            throw new ArgumentOutOfRangeException(nameof(value), "CV hodnota musí byť v rozsahu 0..255.");

        const byte header = 0x23;
        const byte execution = 0x16;
        byte cvOneBased = (byte)cvAddress;
        byte cvValue = (byte)value;
        byte checksum = (byte)(header ^ execution ^ cvOneBased ^ cvValue);
        return new[] { header, execution, cvOneBased, cvValue, checksum };
    }

    public static byte[] CreateExitServiceModePacket() => new byte[] { 0x21, 0x81, 0xA0 };

    /// <summary>
    /// Lenz XpressNet 'Resume Operations Request' (0x21 0x81 0xA0).
    /// Zapína napájanie koľaje (Track Power ON). NanoX-S88 sa po inicializácii
    /// nachádza v stave Track Power OFF a bez tohto príkazu centrála okamžite
    /// odmietne každý Service Mode programovací paket rámcom 61 13 (No ACK).
    /// Paket je binárne identický s 'Exit Service Mode' – ide o ten istý
    /// XpressNet 'Resume Operations' príkaz použitý v dvoch kontextoch.
    /// </summary>
    public static byte[] CreateResumeOperationsPacket() => new byte[] { 0x21, 0x81, 0xA0 };

    /// <summary>
    /// Lenz XpressNet 'Command Station Software Version Request' (0x21 0x21 0x00).
    /// Slúži ako úvodný handshake po otvorení sériovej linky – mnohé Lenz
    /// kompatibilné centrály (vrátane Paco NanoX-S88) ignorujú prvý príkaz
    /// po naviazaní spojenia, pokiaľ im nie je najprv "predstavený" PC ovládač.
    /// </summary>
    public static byte[] CreateCommandStationVersionRequestPacket() => new byte[] { 0x21, 0x21, 0x00 };

    /// <summary>
    /// Lenz XpressNet 'Service Mode Results Request' (0x21 0x10 0x31).
    /// Po prijatí 61 02 (programmer busy / measuring) si PC týmto paketom
    /// vyžiada výsledok CV merania. Bez neho centrála NanoX-S88 nikdy nepošle
    /// dátový rámec (63 10 / 63 14). Možné odpovede:
    ///   61 12 73 – programmer busy (meranie ešte beží → opakovať SMRR)
    ///   61 13 72 – No ACK (dekodér neodpovedal)
    ///   61 82 E3 – Instruction not supported (starší firmvér NanoX-S88;
    ///              prejdeme späť na pasívne čakanie)
    ///   63 10 V VV XOR – Paged Mode result (CV value = V)
    ///   63 14 V VV XOR – Direct Mode result (CV value = V)
    /// </summary>
    public static byte[] CreateServiceModeResultsRequestPacket() => new byte[] { 0x21, 0x10, 0x31 };

    private static bool IsKnownInformationalStatus(byte execution)
    {
        // Stavové XpressNet rámce z hlavičky 0x61, ktoré sú len informačné a neukončujú čítanie CV:
        //  0x00 - service mode entered / track power off
        //  0x01 - normal operations resumed
        //  0x02 - service mode entered (busy / measuring)
        //  0x12 - programmer busy (SMRR – meranie ešte beží, opakovať SMRR)
        //  0x80 - transfer error
        //  0x81 - command station busy
        //  0x82 - instruction not supported by command station (napr. SMRR nepodporovaný)
        return execution is 0x00 or 0x01 or 0x02 or 0x12 or 0x80 or 0x81 or 0x82;
    }

    private static string DescribeStatusFrame(byte execution) => execution switch
    {
        0x00 => "stavový rámec 61 00 (service mode / power off)",
        0x01 => "stavový rámec 61 01 (normal operations resumed)",
        0x02 => "stavový rámec 61 02 (busy / measuring)",
        0x12 => "stavový rámec 61 12 (programmer busy – SMRR retry)",
        0x80 => "stavový rámec 61 80 (transfer error)",
        0x81 => "stavový rámec 61 81 (command station busy)",
        0x82 => "stavový rámec 61 82 (instruction not supported)",
        _ => $"stavový rámec 61 {execution:X2}"
    };

    private static async Task DelayWithTimeoutAsync(int delayMs, CancellationToken timeoutToken, CancellationToken outerToken, int cvAddress, int timeoutMs)
    {
        try
        {
            await Task.Delay(delayMs, timeoutToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
        {
            throw new TimeoutException($"NanoX-S88 nevrátila odpoveď na čítanie CV{cvAddress} do {timeoutMs} ms.");
        }
    }


    private static void ValidateChecksum(byte[] response)
    {
        // Ak má rámec 2 bajty (napr. čisté 61 00 bez checksumu), neoverujeme ho a púšťame ďalej
        if (response.Length < 3)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"ℹ️ NanoX-S88: Skrátený rámec {BytesToHex(response)} bez checksumu – preskakujem validáciu.",
                DiagnosticLevel.Info);
            return;
        }

        byte expected = 0;
        for (var i = 0; i < response.Length - 1; i++)
            expected ^= response[i];

        var actual = response[^1];
        if (actual != expected)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"⚠️ NanoX-S88: checksum mismatch pre rámec {BytesToHex(response)} (očakávané {expected:X2}, prijaté {actual:X2}).",
                DiagnosticLevel.Warning);
            throw new InvalidOperationException($"Neplatný checksum odpovede NanoX-S88. Očakávané {expected:X2}, prijaté {actual:X2}.");
        }

        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"ℹ️ NanoX-S88: checksum OK pre rámec {BytesToHex(response)}.",
            DiagnosticLevel.Info);
    }

    private static async Task TryExitServiceModeAsync(ISerialDccPort? serialPort)
    {
        if (serialPort == null || !serialPort.IsOpen)
            return;

        try
        {
            var packet = CreateExitServiceModePacket();
            // Stabilizačná pauza je už súčasťou volajúceho finally bloku (200 ms).
            await serialPort.WriteAsync(packet, CancellationToken.None).ConfigureAwait(false);
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"📤 NanoX XpressNet exit service mode: {BytesToHex(packet)}",
                DiagnosticLevel.Info);
        }
        catch (Exception ex)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"⚠️ NanoX-S88: nepodarilo sa ukončiť service mode: {ex.Message}",
                DiagnosticLevel.Warning);
        }
    }

    private bool IsXpressNetSystemStatusFrame(byte[] response)
    {
        if (response == null || response.Length != 3)
            return false;

        // Rámce globálneho statusu trate podľa XpressNet špecifikácie (vždy 3 bajty vrátane XOR):
        // 01 01 00 - Skrat (Track Short Circuit)
        // 01 02 03 - Vypnuté napájanie (Track Power OFF)
        // 01 04 05 - Normálna prevádzka (Normal Operation)
        // 01 05 04 - Servisný režim zapnutý (Service Mode Active)
        return response[0] == 0x01;
    }

    private static async Task SendTrackPowerOnAsync(ISerialDccPort serialPort, CancellationToken ct)
    {
        // Pôvodne 200 ms – na fyzickej linke dekodér v mašine potrebuje viac času,
        // aby sa po zapnutí prúdu ustálil a vygeneroval silnejší ACK impulz.
        const int TrackPowerStabilizationMs = 500;

        var packet = CreateResumeOperationsPacket();
        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"📤 NanoX XpressNet Track Power ON (Resume Operations): {BytesToHex(packet)}",
            DiagnosticLevel.Info);

        await serialPort.WriteAsync(packet, ct).ConfigureAwait(false);
        await Task.Delay(TrackPowerStabilizationMs, ct).ConfigureAwait(false);

        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"ℹ️ NanoX-S88: Track Power ON odoslané, stabilizujem {TrackPowerStabilizationMs} ms pred CV readom.",
            DiagnosticLevel.Info);
    }

    /// <summary>
    /// Lenz XpressNet úvodný handshake: pošle 'Command Station Software Version Request'
    /// (0x21 0x21 0x00) a 150 ms pasívne číta prípadnú odpoveď centrály.
    /// Odpoveď sa iba zaloguje (typicky 0x63 0x21 main_ver sub_ver xor alebo 0x62 0x21 xor)
    /// – pre handshake nemá zmysel ju validovať, kľúčové je samotné prebudenie linky.
    /// Žiadne výnimky sa hore nepropagujú – handshake je best-effort.
    /// </summary>
    private static async Task SendCommandStationVersionHandshakeAsync(
        ISerialDccPort serialPort,
        CancellationToken ct)
    {
        const int HandshakeResponseWindowMs = 150;
        const int HandshakeFrameReadTimeoutMs = 750;

        try
        {
            var packet = CreateCommandStationVersionRequestPacket();
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"📤 NanoX XpressNet handshake (Command Station Version Request): {BytesToHex(packet)}",
                DiagnosticLevel.Info);
            await serialPort.WriteAsync(packet, ct).ConfigureAwait(false);

            // Dáme centrále krátke okno, aby odpoveď prišla celá do vstupného buffera.
            // Potom prečítame jeden kompletný XpressNet frame. Pôvodné byte-by-byte
            // drainovanie vedelo prečítať len prvý bajt 0x63 a zvyšok 21 36 00 74
            // nechalo v porte; ten sa potom mylne interpretoval ako CV read odpoveď.
            await Task.Delay(HandshakeResponseWindowMs, ct).ConfigureAwait(false);

            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(HandshakeFrameReadTimeoutMs);

            try
            {
                var response = await ReadHandshakeFrameAsync(serialPort, handshakeCts.Token).ConfigureAwait(false);
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"📥 NanoX XpressNet handshake response ({response.Length} B): {BytesToHex(response)}",
                    DiagnosticLevel.Info);

                TryValidateHandshakeChecksum(response);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Očakávané – centrála nemusí vždy odpovedať na handshake.
                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    "ℹ️ NanoX-S88: handshake odoslaný, centrála v okne neodpovedala (OK, linka prebudená).",
                    DiagnosticLevel.Info);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"⚠️ NanoX-S88: handshake (Command Station Version Request) zlyhal: {ex.Message}",
                DiagnosticLevel.Warning);
        }
    }

            private static async Task<byte[]> ReadHandshakeFrameAsync(
                ISerialDccPort serialPort,
                CancellationToken ct)
            {
                var header = await serialPort.ReadByteAsync(ct).ConfigureAwait(false);
                var execution = await serialPort.ReadByteAsync(ct).ConfigureAwait(false);
                var frameLength = GetExpectedResponseLength(header, execution);

                var response = new byte[frameLength];
                response[0] = header;
                response[1] = execution;
                for (var i = 2; i < frameLength; i++)
                    response[i] = await serialPort.ReadByteAsync(ct).ConfigureAwait(false);

                return response;
            }

            private static void TryValidateHandshakeChecksum(byte[] response)
            {
                if (response.Length < 3)
                    return;

                byte expected = 0;
                for (var i = 0; i < response.Length - 1; i++)
                    expected ^= response[i];

                var actual = response[^1];
                if (actual == expected)
                {
                    TrackFlowDoctorService.Instance.Diagnose(
                        "DCC",
                        $"ℹ️ NanoX-S88: handshake checksum OK pre rámec {BytesToHex(response)}.",
                        DiagnosticLevel.Info);
                    return;
                }

                TrackFlowDoctorService.Instance.Diagnose(
                    "DCC",
                    $"⚠️ NanoX-S88: handshake checksum nesedí pre rámec {BytesToHex(response)} (očakávané {expected:X2}, prijaté {actual:X2}) – pokračujem, handshake je best-effort.",
                    DiagnosticLevel.Warning);
            }

    private static string BytesToHex(byte[] data)
    {
        if (data.Length == 0)
            return "(empty)";

        return BitConverter.ToString(data).Replace('-', ' ');
    }

    private static void SafeCloseAndDispose(ISerialDccPort? serialPort)
    {
        if (serialPort == null)
            return;

        try { serialPort.Close(); }
        catch (InvalidOperationException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        try { serialPort.Dispose(); }
        catch (InvalidOperationException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public Task SetLocomotiveSpeedAsync(int address, int speed, bool forward, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SetLocomotiveFunctionAsync(int address, int functionIndex, bool active, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task EmergencyStopAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public Task TrackPowerOnAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SetTurnoutAsync(int address, bool branch, bool activate, CancellationToken ct = default)
        => Task.CompletedTask;

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            Disconnect();
            System.Diagnostics.Debug.WriteLine("SerialDccClient: sériový port bol bezpečne uvoľnený cez Dispose.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SerialDccClient: chyba pri uvoľňovaní sériového portu: {ex.Message}");
        }
    }

    internal interface ISerialDccPort : IDisposable
    {
        bool IsOpen { get; }
        void Open();
        void Close();
        Task WriteAsync(byte[] data, CancellationToken ct);
        Task<byte> ReadByteAsync(CancellationToken ct);
        void DiscardInBuffer();
    }

    private sealed class SerialPortAdapter : ISerialDccPort
    {
        private readonly SerialPort _serialPort;

        public SerialPortAdapter(string portName, int baudRate)
        {
            _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true,
                NewLine = "\r\n"
            };
        }

        public bool IsOpen => _serialPort.IsOpen;

        public void Open() => _serialPort.Open();

        public void Close() => _serialPort.Close();

        public void DiscardInBuffer()
        {
            try { _serialPort.DiscardInBuffer(); }
            catch (InvalidOperationException) { /* port closed */ }
        }

        public async Task WriteAsync(byte[] data, CancellationToken ct)
        {
            await _serialPort.BaseStream.WriteAsync(data.AsMemory(0, data.Length), ct).ConfigureAwait(false);
            await _serialPort.BaseStream.FlushAsync(ct).ConfigureAwait(false);
        }

        public async Task<byte> ReadByteAsync(CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var value = _serialPort.ReadByte();
                        if (value < 0)
                            throw new EndOfStreamException("Serial port returned no data.");

                        return (byte)value;
                    }
                    catch (TimeoutException)
                    {
                        // SerialPort.ReadByte používa interný timeout (500 ms).
                        // Po každom timeoute dáme šancu cancellation tokenu ukončiť operáciu.
                    }
                }
            }, ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                    System.Diagnostics.Debug.WriteLine("SerialPortAdapter: COM port bol korektne zatvorený pred Dispose.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SerialPortAdapter: chyba pri zatváraní COM portu: {ex.Message}");
            }
            finally
            {
                try { _serialPort.Dispose(); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SerialPortAdapter: chyba pri Dispose COM portu: {ex.Message}");
                }
            }
        }
    }
}


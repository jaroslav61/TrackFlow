using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Services.Dcc;
using Xunit;

namespace TrackFlow.Tests;

public sealed class SerialDccClientTests
{
    [Fact]
    public void CreateServiceModeCvReadPacket_ForCv1_ReturnsExpectedXpressNetBytes()
    {
        var packet = SerialDccClient.CreateServiceModeCvReadPacket(1);

        // NanoX-S88 / Lenz LI100F v2 service-mode CV read packet (Paged Mode v2): 0x22 0x14 CV XOR.
        // Pre CV1: 0x22 ^ 0x14 ^ 0x01 = 0x37.
        Assert.Equal(new byte[] { 0x22, 0x14, 0x01, 0x37 }, packet);
    }

    [Fact]
    public void CreateCommandStationVersionRequestPacket_ReturnsExpectedLenzHandshake()
    {
        var packet = SerialDccClient.CreateCommandStationVersionRequestPacket();

        // Lenz XpressNet "Command Station Software Version Request": 0x21 0x21 0x00
        // (checksum 0x21 ^ 0x21 = 0x00).
        Assert.Equal(new byte[] { 0x21, 0x21, 0x00 }, packet);
    }

    [Fact]
    public void CreateResumeOperationsPacket_ReturnsExpectedTrackPowerOnBytes()
    {
        var packet = SerialDccClient.CreateResumeOperationsPacket();

        // Lenz XpressNet "Resume Operations Request" (Track Power ON): 0x21 0x81 0xA0
        // (checksum 0x21 ^ 0x81 = 0xA0).
        Assert.Equal(new byte[] { 0x21, 0x81, 0xA0 }, packet);
    }

    [Fact]
    public void CreateServiceModeResultsRequestPacket_ReturnsExpectedSmrrBytes()
    {
        var packet = SerialDccClient.CreateServiceModeResultsRequestPacket();

        // Lenz XpressNet "Service Mode Results Request" (SMRR): 0x21 0x10 0x31
        // (checksum 0x21 ^ 0x10 = 0x31).
        Assert.Equal(new byte[] { 0x21, 0x10, 0x31 }, packet);
    }

    [Fact]
    public async Task ReadCvAsync_ForSuccessfulProgrammingReply_ReturnsCvValue()
    {
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker, // handshake drain – centrála neodpovedá
            (byte)0x63, (byte)0x14, (byte)0x01, (byte)0x04, (byte)0x72
        });
        var client = new SerialDccClient((_, _) => serial);

        var connected = await client.ConnectAsync("COM7", 19200);
        var value = await client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 3000, 0);

        Assert.True(connected);
        Assert.Equal(4, value);
        // Writes: [0]=handshake (21 21 00), [1]=track power on (21 81 A0),
        // [2]=CV read (22 11 01 32), [^1]=exit (21 81 A0).
        Assert.Equal(4, serial.AllWrites.Count);
        Assert.Equal(new byte[] { 0x21, 0x21, 0x00 }, serial.AllWrites[0].ToArray());
        Assert.Equal(new byte[] { 0x21, 0x81, 0xA0 }, serial.AllWrites[1].ToArray());
        Assert.Equal(new byte[] { 0x22, 0x14, 0x01, 0x37 }, serial.AllWrites[2].ToArray());
        Assert.Equal(new byte[] { 0x21, 0x81, 0xA0 }, serial.AllWrites[^1].ToArray());
    }

    [Fact]
    public async Task ReadCvAsync_SendsHandshakeBeforeCvReadPacket_AndDrainsHandshakeResponse()
    {
        // Handshake odpoveď (typický Lenz format 0x63 0x21 main sub xor) sa má prečítať
        // pred CV read paketom. Potom prichádza CV read stream.
        var serial = new FakeNanoPort(new object[]
        {
            // 5-bajtová Lenz "version" odpoveď, ktorú handshake drainuje.
            // Checksum: 0x63 ^ 0x21 ^ 0x36 ^ 0x00 = 0x74.
            (byte)0x63, (byte)0x21, (byte)0x36, (byte)0x00, (byte)0x74,
            (byte)0x63, (byte)0x14, (byte)0x01, (byte)0x04, (byte)0x72 // skutočná CV odpoveď
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);
        var value = await client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 3000, 0);

        Assert.Equal(4, value);
        Assert.Equal(4, serial.AllWrites.Count);
        // Poradie writes: handshake → track power on → CV read → exit.
        Assert.Equal(new byte[] { 0x21, 0x21, 0x00 }, serial.AllWrites[0].ToArray());
        Assert.Equal(new byte[] { 0x21, 0x81, 0xA0 }, serial.AllWrites[1].ToArray());
        Assert.Equal(new byte[] { 0x22, 0x14, 0x01, 0x37 }, serial.AllWrites[2].ToArray());
        Assert.Equal(new byte[] { 0x21, 0x81, 0xA0 }, serial.AllWrites[^1].ToArray());
    }

    [Fact]
    public async Task ReadCvAsync_WhenPagedModeResultArrivesAfterBusyAndSmrr_ReturnsCvValue()
    {
        // Nový kontrakt podľa Lenz LI100F v2: 61 00 je iba informačný status (Service Mode entered).
        // Skutočná hodnota CV príde ako dátový rámec 63 10 V VV XOR (Paged Mode result) až po
        // tom, ako PC explicitne pošle SMRR (21 10 31) v reakcii na 61 02 (busy).
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker,                       // handshake okno
            (byte)0x61, (byte)0x02, (byte)0x63,               // busy → pošle sa SMRR
            (byte)0x63, (byte)0x10, (byte)0x01, (byte)0x04, (byte)0x76    // Paged Mode v2 result, CV=4
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);

        var value = await client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 5000, 0);

        Assert.Equal(4, value);
        Assert.Contains(serial.AllWrites, w => w.Count == 3 && w[0] == 0x21 && w[1] == 0x10 && w[2] == 0x31);
        Assert.Equal(1, serial.AllWrites.Count(w => w.Count == 4 && w[0] == 0x22 && w[1] == 0x14 && w[2] == 0x01 && w[3] == 0x37));
        Assert.Equal(new byte[] { 0x21, 0x81, 0xA0 }, serial.AllWrites[^1].ToArray());
    }

    [Fact]
    public async Task ReadCvAsync_IgnoresInformational610263Frame_AndWaitsForFinalResult()
    {
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker,
            (byte)0x61, (byte)0x02, (byte)0x63,
            (byte)0x63, (byte)0x14, (byte)0x01, (byte)0x04, (byte)0x72
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);

        var value = await client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 3000, 0);

        Assert.Equal(4, value);
        // CV read paket sa pošle iba raz; medzi 61 02 a finálnym 63 14 sa zmestí SMRR.
        Assert.Equal(1, serial.AllWrites.Count(w => w.Count == 4 && w[0] == 0x22 && w[1] == 0x14 && w[2] == 0x01 && w[3] == 0x37));
        Assert.Equal(new byte[] { 0x21, 0x21, 0x00 }, serial.AllWrites[0].ToArray());
        Assert.Equal(new byte[] { 0x21, 0x81, 0xA0 }, serial.AllWrites[1].ToArray());
        Assert.Equal(new byte[] { 0x21, 0x81, 0xA0 }, serial.AllWrites[^1].ToArray());
    }

    [Fact]
    public async Task ReadCvAsync_When6100ArrivesAsFirstFrame_TreatsItAsStatusAndContinuesReading()
    {
        // Nový kontrakt: rámec 61 00 NIE JE finálny výsledok – je to len informačný status
        // "Service Mode entered". Kód musí pokračovať v čítaní, kým nepríde 63 1x dátový rámec.
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker,
            (byte)0x61, (byte)0x00, (byte)0x61,               // status: service mode entered
            (byte)0x63, (byte)0x14, (byte)0x01, (byte)0x07, (byte)0x71    // Direct Mode result, CV=7
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);

        var value = await client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 3000, 0);

        Assert.Equal(7, value);
    }

    [Fact]
    public async Task ReadCvAsync_When6100ArrivesAfterCvResult_ReturnsValueAndConsumesTerminalStatus()
    {
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker,
            (byte)0x61, (byte)0x02, (byte)0x63,
            (byte)0x63, (byte)0x14, (byte)0x01, (byte)0x03, (byte)0x75,
            (byte)0x61, (byte)0x00, (byte)0x61
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);

        var value = await client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 3000, 0);

        Assert.Equal(3, value);
        Assert.Equal(new byte[] { 0x21, 0x81, 0xA0 }, serial.AllWrites[^1].ToArray());
    }

    [Fact]
    public async Task ReadCvAsync_WhenFirstAttemptIsBusy6181_RetriesAndEventuallyReturnsValue()
    {
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker,
            (byte)0x61, (byte)0x81, (byte)0xE0,
            (byte)0x61, (byte)0x02, (byte)0x63,
            (byte)0x63, (byte)0x10, (byte)0x01, (byte)0x05, (byte)0x77
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);

        var value = await client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 5000, 0);

        Assert.Equal(5, value);
        Assert.Equal(2, serial.AllWrites.Count(w => w.Count == 4 && w[0] == 0x22 && w[1] == 0x14 && w[2] == 0x01 && w[3] == 0x37));
    }

    [Fact]
    public async Task ReadCvAsync_RepeatedInformationalFramesWithoutResult_EndsWithTimeout()
    {
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker,
            (byte)0x61, (byte)0x02, (byte)0x63,
            (byte)0x61, (byte)0x02, (byte)0x63,
            (byte)0x61, (byte)0x02, (byte)0x63,
            FakeNanoPort.TimeoutMarker
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 5000, 0));
        Assert.Contains("service mode", ex.Message);
        // Po novom kontrakte sa po každom 61 02 pošle SMRR (21 10 31).
        Assert.Contains(serial.AllWrites, w => w.Count == 3 && w[0] == 0x21 && w[1] == 0x10 && w[2] == 0x31);
        // CV read paket sa pošle len raz.
        Assert.Equal(1, serial.AllWrites.Count(w => w.Count == 4 && w[0] == 0x22 && w[1] == 0x14 && w[2] == 0x01 && w[3] == 0x37));
        // Posledný write je exit service mode.
        Assert.Equal(new byte[] { 0x21, 0x81, 0xA0 }, serial.AllWrites[^1].ToArray());
    }

    [Fact]
    public async Task ReadCvAsync_AfterBusyFrames_SendsServiceModeResultsRequest_AndDoesNotRepeatCvRead()
    {
        // Nový kontrakt: po 61 02 sa MUSÍ poslať SMRR (21 10 31), aby centrála vyšla
        // s dátovým rámcom. CV read sa neopakuje.
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker,
            (byte)0x61, (byte)0x02, (byte)0x63,
            (byte)0x61, (byte)0x02, (byte)0x63,
            (byte)0x61, (byte)0x02, (byte)0x63,
            FakeNanoPort.TimeoutMarker
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 5000, 0));

        Assert.Equal(1, serial.AllWrites.Count(w => w.Count == 4 && w[0] == 0x22 && w[1] == 0x14 && w[2] == 0x01 && w[3] == 0x37));
        Assert.Contains(serial.AllWrites, w => w.Count == 3 && w[0] == 0x21 && w[1] == 0x10 && w[2] == 0x31);
    }

    [Fact]
    public async Task ReadCvAsync_WhenProgrammerBusy6112_RetriesSmrr_UntilCvResultArrives()
    {
        // 61 12 = programmer busy (odpoveď na SMRR – meranie ešte beží, treba opakovať SMRR).
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker,
            (byte)0x61, (byte)0x02, (byte)0x63,                // busy → SMRR
            (byte)0x61, (byte)0x12, (byte)0x73,                // programmer busy → retry SMRR
            (byte)0x61, (byte)0x12, (byte)0x73,                // programmer busy → retry SMRR
            (byte)0x63, (byte)0x10, (byte)0x01, (byte)0x09, (byte)0x7B    // Paged Mode v2 result, CV=9
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);

        var value = await client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 5000, 0);

        Assert.Equal(9, value);
        var smrrCount = serial.AllWrites.Count(w => w.Count == 3 && w[0] == 0x21 && w[1] == 0x10 && w[2] == 0x31);
        Assert.True(smrrCount >= 3, $"Očakávané aspoň 3 SMRR pakety, dostal som {smrrCount}.");
    }

    [Fact]
    public async Task ReadCvAsync_When6182NotSupported_FallsBackToPassiveWaiting()
    {
        // Starší firmvér NanoX-S88 môže vrátiť 61 82 (instruction not supported) na SMRR.
        // V tom prípade kód musí prepnúť na pasívne čakanie a stále zachytiť spontánny 63 1x.
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker,
            (byte)0x61, (byte)0x02, (byte)0x63,                // busy → SMRR
            (byte)0x61, (byte)0x82, (byte)0xE3,                // SMRR neakceptovaný → fallback
            (byte)0x63, (byte)0x14, (byte)0x01, (byte)0x05, (byte)0x73    // spontánny Direct Mode result
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);

        var value = await client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 5000, 0);

        Assert.Equal(5, value);
    }

    [Fact]
    public async Task ReadCvAsync_WhenSpontaneousResultArrivesAfterBusyFrames_ReturnsCvValue()
    {
        // Šťastný scenár: po niekoľkých 61 02 busy rámcoch centrála spontánne pošle 63 14 …
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker, // handshake okno
            (byte)0x61, (byte)0x02, (byte)0x63,
            (byte)0x61, (byte)0x02, (byte)0x63,
            (byte)0x61, (byte)0x02, (byte)0x63,
            (byte)0x63, (byte)0x14, (byte)0x01, (byte)0x04, (byte)0x72
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);

        var value = await client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 5000, 0);

        Assert.Equal(4, value);
        // CV read paket sa pošle iba raz, posledný write je exit.
        Assert.Equal(1, serial.AllWrites.Count(w => w.Count == 4 && w[0] == 0x22 && w[1] == 0x14 && w[2] == 0x01 && w[3] == 0x37));
        Assert.Equal(new byte[] { 0x21, 0x81, 0xA0 }, serial.AllWrites[^1].ToArray());
    }

    [Fact]
    public async Task ReadCvAsync_WhenGlobal01StatusFramesArrive_IgnoresThemAndWaitsForPagedModeResult()
    {
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker,
            (byte)0x61, (byte)0x02, (byte)0x63,                // busy → SMRR
            (byte)0x01, (byte)0x01, (byte)0x00,                // skrat / globálny stav trate
            (byte)0x01, (byte)0x02, (byte)0x03,                // vypnutie napájania / systémový stav
            (byte)0x01, (byte)0x04, (byte)0x05,                // prechod do ostrej prevádzky
            (byte)0x63, (byte)0x10, (byte)0x01, (byte)0x0A, (byte)0x78
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);

        var value = await client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 5000, 0);

        Assert.Equal(10, value);
        Assert.Contains(serial.AllWrites, w => w.Count == 3 && w[0] == 0x21 && w[1] == 0x10 && w[2] == 0x31);
    }

    [Fact]
    public async Task ReadCvAsync_ForNackReply_ThrowsClearNoAckException()
    {
        var checksum = (byte)(0x61 ^ 0x13);
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker, // handshake okno
            // 3 pokusy – všetky zlyhajú s 61 13 (No ACK)
            (byte)0x61, (byte)0x13, checksum,
            (byte)0x61, (byte)0x13, checksum,
            (byte)0x61, (byte)0x13, checksum,
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM3", 19200);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 3000, 0));
        Assert.Contains("61 13", ex.Message);
        Assert.Contains("3 pokus", ex.Message);
        // Štartovací paket sa po 61 13 retry posiela 3×.
        Assert.Equal(3, serial.AllWrites.Count(w => w.Count == 4 && w[0] == 0x22 && w[1] == 0x14));
        // DiscardInBuffer sa volá viackrát (init, 2b. drén pred CV read, medzi retry pokusmi, finally).
        // Presný počet nie je zmluvný – stačí, že sa volal viackrát.
        Assert.True(serial.DiscardInBufferCallCount >= 2,
            $"Očakávané aspoň 2 DiscardInBuffer volania, dostal som {serial.DiscardInBufferCallCount}.");
    }

    [Fact]
    public async Task ReadCvAsync_WhenFirstStartPacketGets6113_RetriesAndReturnsValueOnSecondAttempt()
    {
        // Po 61 13 sa štartovací paket aktívne ZOPAKUJE (max 3 pokusy)
        // s drainom buffra a 300 ms pauzou. Druhý pokus prejde a centrála vráti 63 14.
        var nackChecksum = (byte)(0x61 ^ 0x13);
        var serial = new FakeNanoPort(new object[]
        {
            FakeNanoPort.TimeoutMarker,                        // handshake okno bez odpovede
            (byte)0x61, (byte)0x13, nackChecksum,              // 1. pokus → No ACK
            (byte)0x63, (byte)0x14, (byte)0x01, (byte)0x04, (byte)0x72     // 2. pokus → úspech
        });
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM7", 19200);

        var value = await client.ReadCvAsync(1, DccProgrammingTestMode.ServiceTrack, 3000, 0);

        Assert.Equal(4, value);
        // CV read paket sa pošle 2× (1. pokus → 61 13 → retry, 2. pokus → úspech).
        Assert.Equal(2, serial.AllWrites.Count(w => w.Count == 4 && w[0] == 0x22 && w[1] == 0x14 && w[2] == 0x01 && w[3] == 0x37));
        // Posledný write je exit service mode.
        Assert.Equal(new byte[] { 0x21, 0x81, 0xA0 }, serial.AllWrites[^1].ToArray());
    }

    [Fact]
    public async Task ReadCvAsync_ForPomMode_ThrowsNotSupportedException()
    {
        var serial = new FakeNanoPort(Array.Empty<byte>());
        var client = new SerialDccClient((_, _) => serial);

        await client.ConnectAsync("COM3", 19200);

        await Assert.ThrowsAsync<NotSupportedException>(() => client.ReadCvAsync(1, DccProgrammingTestMode.ProgramOnMain, 3000, 3));
    }

    private sealed class FakeNanoPort : SerialDccClient.ISerialDccPort
    {
        private readonly Queue<object> _steps;

        public static object TimeoutMarker { get; } = new TimeoutStep();

        public FakeNanoPort(IEnumerable<byte> bytes)
        {
            _steps = new Queue<object>();
            foreach (var b in bytes)
                _steps.Enqueue(b);
        }

        public FakeNanoPort(IEnumerable<object> steps)
        {
            _steps = new Queue<object>(steps);
        }

        public bool IsOpen { get; private set; }

        public List<List<byte>> AllWrites { get; } = new();

        public void Open() => IsOpen = true;

        public void Close() => IsOpen = false;

        public int DiscardInBufferCallCount { get; private set; }

        public void DiscardInBuffer() => DiscardInBufferCallCount++;

        public Task WriteAsync(byte[] data, CancellationToken ct)
        {
            AllWrites.Add(new List<byte>(data));
            return Task.CompletedTask;
        }

        public Task<byte> ReadByteAsync(CancellationToken ct)
        {
            if (_steps.Count == 0)
                throw new OperationCanceledException(ct);

            var step = _steps.Dequeue();
            if (ReferenceEquals(step, TimeoutMarker))
                throw new OperationCanceledException(ct);

            return Task.FromResult((byte)step);
        }

        public void Dispose()
        {
        }

        private sealed class TimeoutStep { }
    }
}















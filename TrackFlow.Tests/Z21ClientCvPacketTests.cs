using System;
using TrackFlow.Services.Dcc;
using Xunit;

namespace TrackFlow.Tests;

public sealed class Z21ClientCvPacketTests
{
    [Fact]
    public void CreateReadCvPacket_ProCv1_VratiPresnyByteSequence()
    {
        var expected = new byte[]
        {
            0x0C, 0x00, 0x40, 0x00,
            0xE6, 0x30, 0x00, 0x00, 0x00, 0x00,
            0xD6
        };

        var actual = Z21Client.CreateReadCvPacket(1);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CreateReadCv1Packet_ZhodujeSaSUniverzalnouMetodou()
    {
        var universal = Z21Client.CreateReadCvPacket(1);
        var cv1 = Z21Client.CreateReadCv1Packet();

        Assert.Equal(universal, cv1);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 0, 1)]
    [InlineData(256, 0, 0xFF)]
    [InlineData(257, 1, 0x00)]
    [InlineData(1024, 0x03, 0xFF)]
    public void CreateReadCvPacket_ZakodujeAdresuAkoZeroBasedBigEndian(int cvNumber, byte expectedHigh, byte expectedLow)
    {
        var packet = Z21Client.CreateReadCvPacket(cvNumber);

        Assert.Equal(11, packet.Length);
        Assert.Equal(0x0C, packet[0]);
        Assert.Equal(0x00, packet[1]);
        Assert.Equal(0x40, packet[2]);
        Assert.Equal(0x00, packet[3]);
        Assert.Equal(0xE6, packet[4]);
        Assert.Equal(0x30, packet[5]);
        Assert.Equal(expectedHigh, packet[6]);
        Assert.Equal(expectedLow, packet[7]);
        Assert.Equal(0x00, packet[8]);
        Assert.Equal(0x00, packet[9]);

        byte expectedXor = (byte)(0xE6 ^ 0x30 ^ expectedHigh ^ expectedLow ^ 0x00 ^ 0x00);
        Assert.Equal(expectedXor, packet[10]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1025)]
    public void CreateReadCvPacket_MimoRozsahu_HodiVynimku(int cvNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Z21Client.CreateReadCvPacket(cvNumber));
    }

    // ──────────────────────────────────────────────────────────────────────────────────
    // OFICIÁLNY LAN_X_CV_READ paket (service-mode) – formát 0x23 0x11, 9 bajtov.
    // Toto je paket, ktorý reálne posiela ReadCvAsync a na ktorý z21 odpovedá CV_RESULT.
    // ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateServiceModeCvReadPacket_ProCv1_VratiOficialnyByteSequence()
    {
        var expected = new byte[]
        {
            0x09, 0x00, 0x40, 0x00,
            0x23, 0x11, 0x00, 0x00,
            0x32
        };

        var actual = Z21Client.CreateServiceModeCvReadPacket(1);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1, 0x00, 0x00)]
    [InlineData(2, 0x00, 0x01)]
    [InlineData(256, 0x00, 0xFF)]
    [InlineData(257, 0x01, 0x00)]
    [InlineData(1024, 0x03, 0xFF)]
    public void CreateServiceModeCvReadPacket_ZakodujeAdresuAkoZeroBasedBigEndian(int cvNumber, byte expectedHigh, byte expectedLow)
    {
        var packet = Z21Client.CreateServiceModeCvReadPacket(cvNumber);

        Assert.Equal(9, packet.Length);
        Assert.Equal(0x09, packet[0]);
        Assert.Equal(0x00, packet[1]);
        Assert.Equal(0x40, packet[2]);
        Assert.Equal(0x00, packet[3]);
        Assert.Equal(0x23, packet[4]);
        Assert.Equal(0x11, packet[5]);
        Assert.Equal(expectedHigh, packet[6]);
        Assert.Equal(expectedLow, packet[7]);

        byte expectedXor = (byte)(0x23 ^ 0x11 ^ expectedHigh ^ expectedLow);
        Assert.Equal(expectedXor, packet[8]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1025)]
    public void CreateServiceModeCvReadPacket_MimoRozsahu_HodiVynimku(int cvNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Z21Client.CreateServiceModeCvReadPacket(cvNumber));
    }

    [Fact]
    public void CreateExitServiceModePacket_VratiFixnyTrackPowerOnPacket()
    {
        var expected = new byte[]
        {
            0x07, 0x00, 0x40, 0x00,
            0x21, 0x81, 0xA0
        };

        var actual = Z21Client.CreateExitServiceModePacket();

        Assert.Equal(expected, actual);
    }

    // ──────────────────────────────────────────────────────────────────────────────────
    // LAN_X_CV_POM_READ_BYTE – POM (Program on Main) čítanie CV. 12-bajtový paket
    // 0x0C 0x00 0x40 0x00 0xE6 0x30 AddrMSB AddrLSB OptCvH CvLo 0x00 XOR
    // OptCvH = 0xE4 | ((cv-1) >> 8) & 0x03 (0xE4 = POM read byte príkaz).
    // ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreatePomCvReadPacket_ProLoco3CV1_VratiOcakavanyByteSequence()
    {
        // loco=3 (short addr) → AddrMSB=0x00, AddrLSB=0x03
        // CV=1 (idx=0)        → OptCvH=0xE4, CvLo=0x00
        // XOR = 0xE6 ^ 0x30 ^ 0x00 ^ 0x03 ^ 0xE4 ^ 0x00 ^ 0x00 = 0x31
        var expected = new byte[]
        {
            0x0C, 0x00, 0x40, 0x00,
            0xE6, 0x30, 0x00, 0x03, 0xE4, 0x00, 0x00,
            0x31
        };

        var actual = Z21Client.CreatePomCvReadPacket(locoAddress: 3, cvNumber: 1);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CreatePomCvReadPacket_ProDluhuAdresu1234_PouzijePrefix0xC0()
    {
        // loco=1234 = 0x04D2 (long addr) → AddrMSB = 0xC0 | 0x04 = 0xC4, AddrLSB = 0xD2
        // CV=1                            → OptCvH=0xE4, CvLo=0x00
        var packet = Z21Client.CreatePomCvReadPacket(locoAddress: 1234, cvNumber: 1);

        Assert.Equal(12, packet.Length);
        Assert.Equal(0xC4, packet[6]); // AddrMSB
        Assert.Equal(0xD2, packet[7]); // AddrLSB
        Assert.Equal(0xE4, packet[8]); // OptCvH (CV1 → horné 2 bity = 00)
        Assert.Equal(0x00, packet[9]); // CvLo

        byte expectedXor = (byte)(0xE6 ^ 0x30 ^ 0xC4 ^ 0xD2 ^ 0xE4 ^ 0x00 ^ 0x00);
        Assert.Equal(expectedXor, packet[11]);
    }

    [Fact]
    public void CreatePomCvReadPacket_ProCv1024_KodujeHorneBityDoOptCvH()
    {
        // CV=1024 (idx=1023=0x3FF) → horné 2 bity = 0x03, dolných 8 = 0xFF
        // OptCvH = 0xE4 | 0x03 = 0xE7
        var packet = Z21Client.CreatePomCvReadPacket(locoAddress: 3, cvNumber: 1024);

        Assert.Equal(0xE7, packet[8]); // OptCvH
        Assert.Equal(0xFF, packet[9]); // CvLo
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(10000, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 1025)]
    public void CreatePomCvReadPacket_MimoRozsahu_HodiVynimku(int loco, int cv)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Z21Client.CreatePomCvReadPacket(loco, cv));
    }

    [Fact]
    public void CreateSetBroadcastFlagsPacket_XBus_VratiLittleEndianMasku()
    {
        var packet = Z21Client.CreateSetBroadcastFlagsPacket(Z21BroadcastFlags.XBus);

        Assert.Equal(new byte[] { 0x08, 0x00, 0x50, 0x00, 0x01, 0x00, 0x00, 0x00 }, packet);
    }

    [Fact]
    public void CreateSetBroadcastFlagsPacket_DefaultOperationalFlags_Vrati0x00000111()
    {
        var packet = Z21Client.CreateSetBroadcastFlagsPacket(
            Z21BroadcastFlags.XBus |
            Z21BroadcastFlags.RBus |
            Z21BroadcastFlags.SystemState);

        Assert.Equal(new byte[] { 0x08, 0x00, 0x50, 0x00, 0x11, 0x01, 0x00, 0x00 }, packet);
    }
}





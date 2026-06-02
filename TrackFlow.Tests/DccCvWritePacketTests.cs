using System;
using TrackFlow.Services.Dcc;
using Xunit;

namespace TrackFlow.Tests;

public sealed class DccCvWritePacketTests
{
    [Fact]
    public void Z21_CreateServiceModeCvWritePacket_PreCv29VratiOcakavanuSekvenciu()
    {
        var packet = Z21Client.CreateServiceModeCvWritePacket(29, 0x26);

        var expected = new byte[]
        {
            0x0A, 0x00, 0x40, 0x00,
            0x24, 0x12, 0x00, 0x1C, 0x26,
            (byte)(0x24 ^ 0x12 ^ 0x00 ^ 0x1C ^ 0x26)
        };

        Assert.Equal(expected, packet);
    }

    [Fact]
    public void Z21_CreatePomCvWritePacket_PreDlhuAdresuKodujeLongAddressAjWriteOpcode()
    {
        var packet = Z21Client.CreatePomCvWritePacket(1234, 29, 0x26);

        Assert.Equal(12, packet.Length);
        Assert.Equal(0xE6, packet[4]);
        Assert.Equal(0x30, packet[5]);
        Assert.Equal(0xC4, packet[6]);
        Assert.Equal(0xD2, packet[7]);
        Assert.Equal(0xEC, packet[8]);
        Assert.Equal(0x1C, packet[9]);
        Assert.Equal(0x26, packet[10]);
        Assert.Equal((byte)(0xE6 ^ 0x30 ^ 0xC4 ^ 0xD2 ^ 0xEC ^ 0x1C ^ 0x26), packet[11]);
    }

    [Fact]
    public void NanoX_CreateServiceModeCvWritePacket_PreCv29VratiPagedModeWritePacket()
    {
        var packet = SerialDccClient.CreateServiceModeCvWritePacket(29, 0x26);

        var expected = new byte[]
        {
            0x23, 0x16, 0x1D, 0x26,
            (byte)(0x23 ^ 0x16 ^ 0x1D ^ 0x26)
        };

        Assert.Equal(expected, packet);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1025, 1)]
    [InlineData(1, -1)]
    [InlineData(1, 256)]
    public void Z21_CreateServiceModeCvWritePacket_MimoRozsahuHodiVynimku(int cv, int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Z21Client.CreateServiceModeCvWritePacket(cv, value));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(257, 1)]
    [InlineData(1, -1)]
    [InlineData(1, 256)]
    public void NanoX_CreateServiceModeCvWritePacket_MimoRozsahuHodiVynimku(int cv, int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SerialDccClient.CreateServiceModeCvWritePacket(cv, value));
    }
}


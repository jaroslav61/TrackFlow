using System.Collections.Generic;
using TrackFlow.Services.Dcc;
using Xunit;

namespace TrackFlow.Tests;

public sealed class Z21ClientRBusFeedbackTests
{
    [Fact]
    public void TryParseRBusDataChanged_LanX0x43Frame_IsIgnoredForOccupancy()
    {
        using var client = new Z21Client();
        var received = new List<RBusFeedbackState>();
        client.RBusFeedbackChanged += received.Add;

        // Reálne 0x43 broadcasty zo z21 sa ukázali ako X-Bus/accessory broadcast,
        // nie stabilná occupancy telemetria – nesmú preklápať bloky ako R-BUS.
        client.TryParseRBusDataChanged(new byte[] { 0x09, 0x00, 0x40, 0x00, 0x43, 0x00, 0x70, 0x02, 0x31 });

        Assert.Empty(received);
    }

    [Fact]
    public void TryParseRBusDataChanged_DirectRBusFrame_MapsBitsToOneBasedPorts()
    {
        using var client = new Z21Client();
        var received = new List<RBusFeedbackState>();
        client.RBusFeedbackChanged += received.Add;

        // Priamy LAN_RMBUS_DATACHANGED: GroupIndex=0 => prvý modul je 1,
        // maska 0b1100_0000 => porty 7 a 8 aktívne.
        // Po oprave initial-mask publikujeme iba reálne aktívne bity (mask & 1),
        // nie všetkých 8 portov – ušetríme záplavu false-positive eventov pri štarte.
        client.TryParseRBusDataChanged(new byte[] { 0x06, 0x00, 0x80, 0x00, 0x00, 0xC0 });

        Assert.Equal(2, received.Count);
        Assert.Contains(received, x => x.ModuleAddress == 1 && x.PortNumber == 7 && x.IsActive);
        Assert.Contains(received, x => x.ModuleAddress == 1 && x.PortNumber == 8 && x.IsActive);
        Assert.DoesNotContain(received, x => !x.IsActive);
    }

    [Fact]
    public void TryParseRBusDataChanged_RegularLanXPacket_IsIgnored()
    {
        using var client = new Z21Client();
        var received = new List<RBusFeedbackState>();
        client.RBusFeedbackChanged += received.Add;

        // Bežný LAN_X CV-read paket (nie feedback) – nesmie sa interpretovať ako R-BUS.
        client.TryParseRBusDataChanged(new byte[] { 0x09, 0x00, 0x40, 0x00, 0x23, 0x11, 0x00, 0x00, 0x32 });

        Assert.Empty(received);
    }

    [Fact]
    public void TryParseRBusDataChanged_RepeatedIdenticalFrame_DoesNotRepublishSameStates()
    {
        using var client = new Z21Client();
        var received = new List<RBusFeedbackState>();
        client.RBusFeedbackChanged += received.Add;

        var frame = new byte[] { 0x06, 0x00, 0x80, 0x00, 0x00, 0xC0 };

        client.TryParseRBusDataChanged(frame);
        client.TryParseRBusDataChanged(frame);

        // Po oprave initial-mask: prvý rámec publikuje 2 aktívne porty (7, 8),
        // druhý identický rámec nepridá nič (mask sa nemení).
        Assert.Equal(2, received.Count);
    }

    [Fact]
    public void TryParseRBusDataChanged_SecondFrame_PublishesOnlyChangedPorts()
    {
        using var client = new Z21Client();
        var received = new List<RBusFeedbackState>();
        client.RBusFeedbackChanged += received.Add;

        client.TryParseRBusDataChanged(new byte[] { 0x06, 0x00, 0x80, 0x00, 0x00, 0xC0 });
        received.Clear();

        client.TryParseRBusDataChanged(new byte[] { 0x06, 0x00, 0x80, 0x00, 0x00, 0x80 });

        var changed = Assert.Single(received);
        Assert.Equal(1, changed.ModuleAddress);
        Assert.Equal(7, changed.PortNumber);
        Assert.False(changed.IsActive);
    }
}


using System.Collections.Generic;
using TrackFlow.Services.Dcc;
using Xunit;

namespace TrackFlow.Tests;

/// <summary>
/// Overuje opravu bugu 4.1 z TECH_AUDIT_2026-05-31.md – mapovanie GroupIndex
/// z LAN_RMBUS_DATACHANGED (Z21 LAN špec. v1.13, sekcia 7.1.2):
///  • GroupIndex 0 → moduly  1..10
///  • GroupIndex 1 → moduly 11..20
/// Pôvodná formula `data[4] + 1` posielala pre group=1 modul 2, čím by sa pri
/// fyzickej konfigurácii s &gt;10 R-BUS/S88 modulmi kolidovali adresy z oboch skupín.
/// </summary>
public sealed class Z21ClientRBusGroupTests
{
    [Fact]
    public void Parses_Group0_StartsAtModule1()
    {
        using var client = new Z21Client();
        var received = new List<RBusFeedbackState>();
        client.RBusFeedbackChanged += received.Add;

        // GroupIndex=0, prvý bajt masky = 0x01 → modul 1, port 1 aktívny.
        client.TryParseRBusDataChanged(new byte[] { 0x06, 0x00, 0x80, 0x00, 0x00, 0x01 });

        var single = Assert.Single(received);
        Assert.Equal(1, single.ModuleAddress);
        Assert.Equal(1, single.PortNumber);
        Assert.True(single.IsActive);
    }

    [Fact]
    public void Parses_Group1_StartsAtModule11()
    {
        using var client = new Z21Client();
        var received = new List<RBusFeedbackState>();
        client.RBusFeedbackChanged += received.Add;

        // GroupIndex=1, prvý bajt masky = 0x01 → modul 11 (nie modul 2!), port 1 aktívny.
        client.TryParseRBusDataChanged(new byte[] { 0x06, 0x00, 0x80, 0x00, 0x01, 0x01 });

        var single = Assert.Single(received);
        Assert.Equal(11, single.ModuleAddress);
        Assert.Equal(1, single.PortNumber);
        Assert.True(single.IsActive);
    }

    [Fact]
    public void Parses_Group1_DoesNotCollideWith_Group0()
    {
        using var client = new Z21Client();
        var received = new List<RBusFeedbackState>();
        client.RBusFeedbackChanged += received.Add;

        // Najprv group 0 – modul 2 aktivuje port 1.
        client.TryParseRBusDataChanged(new byte[] { 0x07, 0x00, 0x80, 0x00, 0x00, 0x00, 0x01 });
        // Potom group 1 – PRVÝ modul v tejto skupine (11) aktivuje port 1.
        client.TryParseRBusDataChanged(new byte[] { 0x06, 0x00, 0x80, 0x00, 0x01, 0x01 });

        Assert.Contains(received, x => x.ModuleAddress == 2  && x.PortNumber == 1 && x.IsActive);
        Assert.Contains(received, x => x.ModuleAddress == 11 && x.PortNumber == 1 && x.IsActive);
        // Kontrola: žiadny event nesmie ísť na modul 2 z group=1.
        Assert.DoesNotContain(received, x => x.ModuleAddress == 2 && !x.IsActive);
    }

    [Fact]
    public void InitialFrame_PublishesOnlyActiveBits()
    {
        using var client = new Z21Client();
        var received = new List<RBusFeedbackState>();
        client.RBusFeedbackChanged += received.Add;

        // Bug 4.2: initial mask 0xFF spôsoboval publikáciu 8 eventov pre každý prvý
        // rámec modulu (vrátane neaktívnych bitov), čo zaplavovalo Doctor log
        // a triggerovalo zbytočné prepočty obsadenosti.
        // Maska 0b0000_1010 → očakávame iba 2 udalosti (porty 2 a 4, obe aktívne).
        client.TryParseRBusDataChanged(new byte[] { 0x06, 0x00, 0x80, 0x00, 0x00, 0x0A });

        Assert.Equal(2, received.Count);
        Assert.Contains(received, x => x.ModuleAddress == 1 && x.PortNumber == 2 && x.IsActive);
        Assert.Contains(received, x => x.ModuleAddress == 1 && x.PortNumber == 4 && x.IsActive);
        Assert.DoesNotContain(received, x => !x.IsActive);
    }
}


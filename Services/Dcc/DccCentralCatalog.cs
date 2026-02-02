using System;
using System.Collections.Generic;
using TrackFlow.Models;

namespace TrackFlow.Services.Dcc;

public static class DccCentralCatalog
{
    public sealed record CentralItem(
        string Name,
        DccCentralType Type,
        bool IsImplemented,
        int? DefaultPort = null
    );

    public sealed record CentralGroup(
        string Name,
        IReadOnlyList<CentralItem> Items
    );

    // V TrackFlow je dnes reálne implementované:
    // - z21 (štartovacia, UDP) = enum Z21Legacy
    // - Generic IP/UDP skeleton
    private static readonly HashSet<DccCentralType> Implemented = new()
    {
        DccCentralType.Z21Legacy,
        DccCentralType.GenericIpUdp
    };

    // Default porty – len tam, kde je to bezpečné a stabilné.
    private static readonly Dictionary<DccCentralType, int> DefaultPorts = new()
    {
        // z21 UDP port (Roco/Fleischmann)
        { DccCentralType.Z21Legacy, 21105 },

        // Pozn.: "Z21" (plná) môže mať v praxi rovnaký port, ale keďže v TrackFlow dnes
        // reálne implementuješ z21 (= Z21Legacy), nedávam sem default pre Z21,
        // aby sa to omylom nepretláčalo ako "fungujúca" voľba.
        // { DccCentralType.Z21, 21105 },
    };

    public static IReadOnlyList<CentralGroup> GetGroups()
    {
        // Presne podľa tvojho UiHelpers.java: značky + položky.
        // Skupiny bez položiek ostávajú ako prázdne (len header).
        return new List<CentralGroup>
        {
            new("Maerklin", new List<CentralItem>
            {
                Item("Maerklin Central Station 3", DccCentralType.MaerklinCs3),
                Item("Maerklin Central Station 2", DccCentralType.MaerklinCs2),
                Item("Maerklin Central Station 1", DccCentralType.MaerklinCs1),
                Item("Maerklin 6050/6051", DccCentralType.Maerklin6050_6051),
                Item("Maerklin 6023", DccCentralType.Maerklin6023),
            }),

            new("Lenz", new List<CentralItem>
            {
                Item("Lenz Digital Plus / USB", DccCentralType.LenzDigitalPlusUsb),
                Item("Lenz Digital Plus / LAN", DccCentralType.LenzDigitalPlusLan),
                Item("Lenz Digital Plus / LZV200", DccCentralType.LenzLzv200),
                Item("Lenz Digital Plus / LI101F", DccCentralType.LenzLi101F),
                Item("Lenz Decoder-Programmer", DccCentralType.LenzDecoderProgrammer),
            }),

            new("Roco", new List<CentralItem>
            {
                // Existujú dve rozdielne centrály:
                //  - "Z21" (plná)
                //  - "z21" (štartovacia)
                // V UI musia byť rozlíšené presne veľkosťou písmena.
                Item("Roco/Fleischmann Z21", DccCentralType.Z21),
                Item("Roco/Fleischmann z21", DccCentralType.Z21Legacy),

                Item("Roco/Fleischmann Multizentrale", DccCentralType.RocoMultizentrale),
                Item("Rocomation 10785", DccCentralType.Rocomation10785),
            }),

            new("Fleischmann", new List<CentralItem>
            {
                Item("Fleischmann Multizentrale", DccCentralType.FleischmannMultizentrale),

                // „legacy“ tu nechávam, lebo je to súčasť historického názvu rozhrania 6050/6051
                Item("Fleischmann 6050/6051 (legacy)", DccCentralType.Fleischmann6050_6051Legacy),
            }),

            new("ESU", new List<CentralItem>
            {
                Item("ESU LokProgrammer / LokSound", DccCentralType.EsuLokProgrammerLokSound),
            }),

            new("Uhlenbrock", new List<CentralItem>
            {
                Item("Uhlenbrock Intellibox / UR-Decoder", DccCentralType.UhlenbrockIntelliboxUrDecoder),
            }),

            new("Digitrax", new List<CentralItem>
            {
                Item("Digitrax DCC Systems", DccCentralType.DigitraxDccSystems),
            }),

            new("LocoNet", new List<CentralItem>
            {
                Item("LocoNet (JMRI / Digitrax)", DccCentralType.LocoNetJmriDigitrax),
            }),

            new("Tams", new List<CentralItem>
            {
                Item("Tams Master Control", DccCentralType.TamsMasterControl),
            }),

            new("Piko", new List<CentralItem>
            {
                Item("Piko SmartControl", DccCentralType.PikoSmartControl),
            }),

            new("Zimo", new List<CentralItem>
            {
                Item("ZIMO DEC/MX", DccCentralType.ZimoDecMx),
            }),

            new("Doehler & Haass / MTTM", Array.Empty<CentralItem>()),
            new("Trix", Array.Empty<CentralItem>()),
            new("Bachmann", Array.Empty<CentralItem>()),

            new("Iný...", new List<CentralItem>
            {
                Item("Generic IP/UDP", DccCentralType.GenericIpUdp),
            }),
        };
    }

    public static bool IsImplemented(DccCentralType type) => Implemented.Contains(type);

    public static int? GetDefaultPort(DccCentralType type)
        => DefaultPorts.TryGetValue(type, out var p) ? p : null;

    private static CentralItem Item(string name, DccCentralType type)
        => new(name, type, IsImplemented(type), GetDefaultPort(type));
}

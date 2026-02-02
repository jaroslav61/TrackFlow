namespace TrackFlow.Models;

public enum DccCentralType
{
    // Roco/Fleischmann
    Z21 = 0,
    Z21Legacy = 1,
    RocoMultizentrale = 2,
    Rocomation10785 = 3,
    FleischmannMultizentrale = 4,
    Fleischmann6050_6051Legacy = 5,

    // Märklin (v Java: Maerklin)
    MaerklinCs3 = 20,
    MaerklinCs2 = 21,
    MaerklinCs1 = 22,
    Maerklin6050_6051 = 23,
    Maerklin6023 = 24,

    // Lenz
    LenzDigitalPlusUsb = 40,
    LenzDigitalPlusLan = 41,
    LenzLzv200 = 42,
    LenzLi101F = 43,
    LenzDecoderProgrammer = 44,

    // ESU
    EsuLokProgrammerLokSound = 60,

    // Uhlenbrock
    UhlenbrockIntelliboxUrDecoder = 70,

    // Digitrax / LocoNet
    DigitraxDccSystems = 80,
    LocoNetJmriDigitrax = 81,

    // Tams / Piko / Zimo
    TamsMasterControl = 90,
    PikoSmartControl = 91,
    ZimoDecMx = 92,

    // Generické
    GenericIpUdp = 200
}


using System;

namespace TrackFlow.Services.Dcc;

/// <summary>
/// z21 LAN_SET_BROADCASTFLAGS (32-bit little-endian maska).
/// Hodnoty podľa Z21 LAN špecifikácie; TrackFlow aktuálne používa len podmnožinu,
/// ale enum ponecháva priestor aj pre ďalšie feedback zdroje bez ďalších magic numbers.
/// </summary>
[Flags]
public enum Z21BroadcastFlags : uint
{
    None = 0x00000000,

    /// <summary>X-Bus broadcasty (drive / turnout / programming).</summary>
    XBus = 0x00000001,

    /// <summary>LocoNet detector správy.</summary>
    LocoNetDetector = 0x00000002,

    /// <summary>R-Bus / S88 feedback zmeny (LAN_RMBUS_DATACHANGED).</summary>
    RBus = 0x00000010,

    /// <summary>LAN_SYSTEMSTATE_DATACHANGED (napätie / prúd / teplota).</summary>
    SystemState = 0x00000100,

    /// <summary>RailCom feedback zmeny.</summary>
    RailComDataChanged = 0x00010000,

    /// <summary>LocoNet duplex príjem pre Z21.</summary>
    LocoNetZ21Rx = 0x00040000,
}


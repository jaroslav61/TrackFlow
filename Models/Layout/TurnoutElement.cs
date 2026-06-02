using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrackFlow.Models.Layout;

/// <summary>Poloha výhybky.</summary>
public enum TurnoutState
{
    Straight,    // priamo
    Diverge,     // odbočka (pre 2-cestné výhybky)
    DivergeLeft, // odbočka doľava (pre 3-cestné výhybky)
    DivergeRight,// odbočka doprava (pre 3-cestné výhybky)
    Cross,       // krížom (pre výhybky Doubleslip)
    Unknown,     // neznámy (po štarte, pred prvým dotazom)
}

/// <summary>Typ pohonu výhybky.</summary>
public enum TurnoutDriveType
{
    Digital,    // DCC accessory decoder
    Servo,      // PWM servo (cez rozširovací modul)
    Relay,      // Relé (cez GPIO/modul)
    Manual      // Manuálne ovládanie
}

/// <summary>
/// Výhybka.
/// Ovládaná cez Z21 LAN_X_SET_TURNOUT príkazom.
/// </summary>
public sealed class TurnoutElement : LayoutElement
{
    [JsonIgnore]
    public override LayoutElementType ElementType => LayoutElementType.Turnout;

    private int _dccAddress;
    
    /// <summary>DCC adresa výhybky (accessory decoder, 1..2048).</summary>
    public int DccAddress
    {
        get => _dccAddress;
        set
        {
            if (value < 0 || value > 2048)
                throw new ArgumentOutOfRangeException(nameof(DccAddress), 
                    "DCC adresa musí byť v rozsahu 0-2048 (0 = nepridelená)");
            _dccAddress = value;
        }
    }

    /// <summary>Aktuálna poloha výhybky.</summary>
    public TurnoutState State { get; set; } = TurnoutState.Unknown;

    /// <summary>Či je výhybka trojcestná (Y-výhybka).</summary>
    public bool IsThreeWay { get; set; }

    private int _dccAddress2;
    
    /// <summary>DCC adresa druhého pohonu (pri trojcestnej výhybke).</summary>
    public int DccAddress2
    {
        get => _dccAddress2;
        set
        {
            if (value < 0 || value > 2048)
                throw new ArgumentOutOfRangeException(nameof(DccAddress2), 
                    "DCC adresa 2 musí byť v rozsahu 0-2048 (0 = nepridelená)");
            _dccAddress2 = value;
        }
    }

    // ── Všeobecné ────────────────────────────────────────────────────────────
    
    /// <summary>Dlhší popis výhybky.</summary>
    public string Description { get; set; } = "";

    /// <summary>Default stav výhybky pri inicializácii.</summary>
    public TurnoutState InitialState { get; set; } = TurnoutState.Straight;

    /// <summary>Dĺžka výhybky v centimetroch.</summary>
    public int TurnoutLength { get; set; } = 15;

    // ── Pripojenie ───────────────────────────────────────────────────────────
    
    /// <summary>Typ DCC systému (null = bez pripojenia). Legacy – pre nové dáta sa použije DccCentralProfileId.</summary>
    public DccCentralType? DccSystemType { get; set; }

    /// <summary>
    /// ID konkrétneho profilu DCC centrály v efektívnom zozname profilov
    /// (projektový override alebo <see cref="AppSettingsData.DccCentralProfiles"/>).
    /// null = „Bez pripojenia“. Pri zápise sa zároveň aktualizuje aj <see cref="DccSystemType"/>.
    /// </summary>
    public System.Guid? DccCentralProfileId { get; set; }

    /// <summary>Dĺžka impulzu v ms (štandardne 100ms).</summary>
    public int PulseLength { get; set; } = 100;

    /// <summary>Použiť predvolenú dĺžku impulzu z DCC systému.</summary>
    public bool UseDefaultPulse { get; set; } = true;

    /// <summary>Invertovaná logika (pre dekodéry s opačnou logikou).</summary>
    public bool ReverseLogic { get; set; }

    // ── Indikátory ───────────────────────────────────────────────────────────
    
    /// <summary>Zoznam ID indikátorov ktoré monitorujú obsadenie pri tejto výhybke.</summary>
    public List<string> DetectorLinkIds { get; set; } = new();

    // ── Podmienky ────────────────────────────────────────────────────────────
    
    /// <summary>Maximálna rýchlosť cez výhybku (km/h).</summary>
    public int MaxSpeed { get; set; } = 60;

    /// <summary>Obmedzená rýchlosť cez výhybku v odbočke (km/h).</summary>
    public int LimitedSpeed { get; set; } = 40;

    /// <summary>Vyžiadať žltý signál pred výhybkou.</summary>
    public bool RequestYellow { get; set; }
}

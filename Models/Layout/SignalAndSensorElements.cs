using System;
using System.Text.Json.Serialization;

namespace TrackFlow.Models.Layout;

/// <summary>Stav signálu / návesti.</summary>
public enum SignalAspect
{
    // Zastarané názvy - NEPOUŽÍVAŤ (len pre načítanie starých projektov; SignalElement.Aspect ich normalizuje)
    Red,        // stoj
    Green,      // voľno
    Yellow,     // horná žltá - trvalá
    White,      // posun dovolený
    Off,        // vypnutý

    // TAB2 explicitné názvy (POUŽÍVAŤ VÝLUČNE TIETO)
    Stop,
    Proceed,
    Caution,
    SlowProceed,
    SlowCaution,
    SlowExpect40,
    ShuntingPermitted
}

/// <summary>
/// Signál / návesť.
/// Ovládaná cez accessory decoder (rovnaký príkaz ako výhybka).
/// </summary>
public sealed class SignalElement : LayoutElement
{
    [JsonIgnore]
    public override LayoutElementType ElementType => LayoutElementType.Signal;

    /// <summary>DCC adresa signálu (accessory decoder, 1..2048).</summary>
    private int _dccAddress;
    public int DccAddress
    {
        get => _dccAddress;
        set
        {
            if (value < 0 || value > 2048)
                throw new ArgumentOutOfRangeException(nameof(DccAddress),
                    "DCC adresa musí byť v rozsahu 0-2048 (0 = nepriradená)");
            _dccAddress = value;
        }
    }

    private SignalAspect _aspect = SignalAspect.Stop;

    /// <summary>Aktuálny aspect signálu.</summary>
    public SignalAspect Aspect
    {
        get => _aspect;
        set => _aspect = value switch
        {
            SignalAspect.Red => SignalAspect.Stop,
            SignalAspect.Green => SignalAspect.Proceed,
            SignalAspect.Yellow => SignalAspect.Caution,
            SignalAspect.White => SignalAspect.ShuntingPermitted,
            _ => value
        };
    }

    /// <summary>
    /// Režim ovládania dekodéra.
    /// true = základný (emulácia 4 adries), false = rozšírený (1 adresa + číslo aspektu).
    /// </summary>
    public bool IsBasicMode { get; set; } = true;

    /// <summary>Či signál chráni vstup do bloku (pre automatiku ciest).</summary>
    public string? ProtectsBlockId { get; set; }

    /// <summary>ID návestnej sústavy použitej týmto návestidlom.</summary>
    public string? SignalSystemId { get; set; }

    /// <summary>Profil markeru v rámci sústavy (napr. "2-aspect", "3-aspect", ...).</summary>
    public string? SignalProfile { get; set; }
}

/// <summary>
/// Senzor obsadenosti (S88, RailCom, IR-bariéra...).
/// Pasívny – len prijíma stav, neposiela príkazy.
/// </summary>
public sealed class SensorElement : LayoutElement
{
    [JsonIgnore]
    public override LayoutElementType ElementType => LayoutElementType.Sensor;

    /// <summary>Adresa senzora v rámci S88 / RailCom zbernice.</summary>
    private int _sensorAddress;
    public int SensorAddress
    {
        get => _sensorAddress;
        set
        {
            if (value < 0 || value > 2048)
                throw new ArgumentOutOfRangeException(nameof(SensorAddress),
                    "Adresa senzora musí byť v rozsahu 0-2048 (0 = nepriradená)");
            _sensorAddress = value;
        }
    }

    /// <summary>Aktuálny stav – true = obsadený.</summary>
    public bool IsActive { get; set; }

    /// <summary>ID bloku ktorý tento senzor monitoruje.</summary>
    public string? MonitorsBlockId { get; set; }
}


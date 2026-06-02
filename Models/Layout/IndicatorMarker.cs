using System;

namespace TrackFlow.Models.Layout;

/// <summary>
/// Marker v rámci indikátora - pre riadenie vlaku (Distance, Braking, Stop, Action)
/// </summary>
public class IndicatorMarker
{
    /// <summary>
    /// Unikátny identifikátor markeru
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Typ markeru (Distance, Braking, Stop, Action)
    /// </summary>
    public MarkerType Type { get; set; }
    
    /// <summary>
    /// Smer jazdy (Forward/Backward)
    /// </summary>
    public MarkerDirection Direction { get; set; }
    
    /// <summary>
    /// Pozícia v cm RELATÍVNE K ZAČIATKU INDIKÁTORA (nie bloku!)
    /// Rozsah: 0 až (EndCm - StartCm) indikátora
    /// </summary>
    public int PositionCm { get; set; }
    
    /// <summary>
    /// Koniec markeru v cm (dojazd) - relatívne k indikátoru
    /// </summary>
    public int EndPositionCm { get; set; }
    
    /// <summary>
    /// Hodnota rýchlosti (km/h) - pre Distance a Braking markery
    /// </summary>
    public int? SpeedValue { get; set; }
    
    /// <summary>
    /// Pozícia zastavenia - pre Stop marker
    /// Možnosti: "Begin", "Middle", "End"
    /// </summary>
    public string? StopPosition { get; set; }
}

/// <summary>
/// Typ markeru pre riadenie vlaku
/// </summary>
public enum MarkerType
{
    /// <summary>
    /// Vzdialenostný marker (Distance) - modrá farba
    /// </summary>
    Distance,
    
    /// <summary>
    /// Brzdný marker (Braking) - zelená farba
    /// </summary>
    Braking,
    
    /// <summary>
    /// Stop marker (Stop) - červená farba
    /// </summary>
    Stop,
    
    /// <summary>
    /// Akčný marker (Action) - šedá farba
    /// </summary>
    Action
}

/// <summary>
/// Smer jazdy pre marker
/// </summary>
public enum MarkerDirection
{
    /// <summary>
    /// Smer dopředu (A → B)
    /// </summary>
    Forward,
    
    /// <summary>
    /// Smer dozadu (B → A)
    /// </summary>
    Backward
}


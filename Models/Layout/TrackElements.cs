using System.Text.Json.Serialization;

namespace TrackFlow.Models.Layout;

/// <summary>
/// Priamy úsek trate / blok.
/// Môže mať priradenú lokomotívu (v prevádzkovom režime).
/// </summary>
public sealed class TrackSegmentElement : LayoutElement
{
    [JsonIgnore]
    public override LayoutElementType ElementType => LayoutElementType.TrackSegment;

    /// <summary>Dĺžka úseku v mm (reálna mierka).</summary>
    public double LengthMm { get; set; } = 168; // štandard H0 priamy úsek

    /// <summary>Či je blok obsadený (aktualizuje sa zo senzorov / DCC).</summary>
    public bool IsOccupied { get; set; }

    /// <summary>ID lokomotívy priradenej k tomuto bloku (prevádzka).</summary>
    public string? AssignedLocoId { get; set; }

    /// <summary>Či je blok uzamknutý (súčasť aktívnej cesty).</summary>
    public bool IsLocked { get; set; }
}

/// <summary>Oblúk trate.</summary>
public sealed class CurveElement : LayoutElement
{
    [JsonIgnore]
    public override LayoutElementType ElementType => LayoutElementType.Curve;

    /// <summary>Polomer oblúka v mm.</summary>
    public double RadiusMm { get; set; } = 360;

    /// <summary>Uhol oblúka v stupňoch.</summary>
    public double AngleDeg { get; set; } = 30;
}

/// <summary>Nárazník / koniec slepej koľaje.</summary>
public sealed class BumperElement : LayoutElement
{
    [JsonIgnore]
    public override LayoutElementType ElementType => LayoutElementType.Bumper;
}


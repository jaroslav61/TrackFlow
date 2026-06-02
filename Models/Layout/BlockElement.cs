using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TrackFlow.Models.Layout;

/// <summary>Typ signálov na vstupe/výstupe bloku.</summary>
public enum BlockSignalType
{
    Dvojstavove,
    Trojstavove,
    Stvorstavove,
    Patstavove,
}

/// <summary>Pozícia zastavenia vlaku v bloku.</summary>
public enum BlockStopPosition
{
    CeloVlaku,
    StredVlaku,
    KoncaVlaku,
}

/// <summary>Smer pohybu z bloku pre výber príslušného návestidla.</summary>
public enum NavigationDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>
/// Blok trate – zaberá 1×4 bunky v editore.
/// Obsahuje všetky konfiguračné parametre bloku.
/// </summary>
public sealed class BlockElement : LayoutElement
{
    [JsonIgnore]
    public override LayoutElementType ElementType => LayoutElementType.Block;

    // ── Všeobecné ─────────────────────────────────────────────────────────
    // Physical length of the block in centimeters.
    // Default 0 = unknown / not specified by user.
    public int  LengthCm         { get; set; } = 0;
    public int  BlockLengthCells { get; set; } = 4;  // Počet buniek v editore (default 4, min 1, max 20)
    public bool RequestYellow    { get; set; }
    public int  MaxSpeedKmh      { get; set; } = 120;
    public int  ResSpeedKmh      { get; set; } = 40;
    public bool AllowBackward    { get; set; } = true;
    public bool AllowForward     { get; set; } = true;
    public bool CriticalSection  { get; set; }
    public int  MaxTrainLengthCm { get; set; }

    // ── Indikátory ────────────────────────────────────────────────────────
    public List<BlockIndicator> Indicators { get; set; } = new();

    // ── Markery – smer ◄ ──────────────────────────────────────────────────
    public int BwdDistanceCm { get; set; }
    public int BwdBrakingCm  { get; set; }
    public int BwdStopCm     { get; set; }
    public int BwdActionCm   { get; set; }
    public int BwdDistanceEndCm { get; set; }
    public int BwdBrakingEndCm  { get; set; }
    public int BwdStopEndCm     { get; set; }
    public int BwdActionEndCm   { get; set; }
    
    // Väzby markerov na indikátory (Backward)
    public Guid? BwdDistanceIndicatorId { get; set; }
    public Guid? BwdBrakingIndicatorId  { get; set; }
    public Guid? BwdStopIndicatorId     { get; set; }
    public Guid? BwdActionIndicatorId   { get; set; }

    // ── Markery – smer ► ──────────────────────────────────────────────────
    public int FwdDistanceCm { get; set; }
    public int FwdBrakingCm  { get; set; }
    public int FwdStopCm     { get; set; }
    public int FwdActionCm   { get; set; }
    public int FwdDistanceEndCm { get; set; }
    public int FwdBrakingEndCm  { get; set; }
    public int FwdStopEndCm     { get; set; }
    public int FwdActionEndCm   { get; set; }
    
    // Väzby markerov na indikátory (Forward)
    public Guid? FwdDistanceIndicatorId { get; set; }
    public Guid? FwdBrakingIndicatorId  { get; set; }
    public Guid? FwdStopIndicatorId     { get; set; }
    public Guid? FwdActionIndicatorId   { get; set; }

    // ── Signál / pozícia zastavenia ───────────────────────────────────────
    public BlockSignalType   SignalType    { get; set; } = BlockSignalType.Dvojstavove;
    public BlockStopPosition StopPosition { get; set; } = BlockStopPosition.CeloVlaku;

    // ── Smerové priradenie návestidiel (odjazd z bloku) ───────────────────
    public string? SignalLeftId  { get; set; }
    public string? SignalRightId { get; set; }
    public string? SignalUpId    { get; set; }
    public string? SignalDownId  { get; set; }

    // ── Posun ─────────────────────────────────────────────────────────────
    public bool AllowShunting { get; set; }

    // ── Runtime (nie serializované) ───────────────────────────────────────
    [JsonIgnore] public bool    IsOccupied        { get; set; }
    [JsonIgnore] public string? AssignedLocoId    { get; set; }
    [JsonIgnore] public bool    IsLocked          { get; set; }
    [JsonIgnore] public string? ReservedLocoId    { get; set; }

    /// <summary>Smer, ktorým bola lokomotíva priradená k bloku (true = dopredu / forward).</summary>
    [JsonIgnore] public bool    AssignedLocoIsForward { get; set; } = true;

    /// <summary>Smer ghost rezervácie (true = dopredu / forward).</summary>
    [JsonIgnore] public bool    ReservedLocoIsForward { get; set; } = true;

    /// <summary>Prechodový stav počas drag-over: true ak je nad blokom ťahaná lokomotíva.</summary>
    [JsonIgnore] public bool    IsDragOverActive  { get; set; }

    /// <summary>Smer indikovaný počas drag-over (true = pravá/dolná polovica = forward).</summary>
    [JsonIgnore] public bool    DragOverIsForward { get; set; }

    /// <summary>Príznak, či je shadow (ghost rezervácia) pre tento blok už nastavená. Zabráni opakovanej rezervácii.</summary>
    [JsonIgnore] public bool    IsShadowSet { get; set; }

    /// <summary>
    /// Prechodový stav: Vlak opúšťa blok (tail clearing). Blok je vizuálne prázdny (ikona zmizla),
    /// ale logicky ešte obsadený pre účely návestidiel - čaká sa na úplné uvoľnenie.
    /// </summary>
    [JsonIgnore] public bool    IsTailClearing { get; set; }

    /// <summary>Vráti ID návestidla priradeného pre daný smer odjazdu z bloku.</summary>
    public string? GetSignalForDirection(NavigationDirection dir)
        => dir switch
        {
            NavigationDirection.Left => SignalLeftId,
            NavigationDirection.Right => SignalRightId,
            NavigationDirection.Up => SignalUpId,
            NavigationDirection.Down => SignalDownId,
            _ => null
        };

    /// <summary>
    /// Vráti konkrétny signal element z poskytnutej kolekcie podľa smeru odjazdu.
    /// </summary>
    public SignalElement? GetSignalForDirection(NavigationDirection dir, IEnumerable<SignalElement>? signals)
    {
        var signalId = GetSignalForDirection(dir);
        if (string.IsNullOrWhiteSpace(signalId) || signals == null)
            return null;

        var resolved = signals.FirstOrDefault(s => string.Equals(s.Id, signalId, StringComparison.OrdinalIgnoreCase));
        return resolved;
    }
}

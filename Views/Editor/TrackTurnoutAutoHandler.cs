using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Editor;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;

namespace TrackFlow.Views.Editor;

/// <summary>
/// Handler pre automatické vytváranie výhybiek pri dotykoch koľají.
/// 
/// LOGIKA:
/// Keď sa nová koľaj dotýka existujúcej koľaje pod iným uhlom (nie križuje),
/// automaticky sa vloží výhybka na miesto dotyku.
/// 
/// PRÍKLAD:
///   - Horizontálna koľaj 0° + nová koľaj 45° vychádzajúca z nej → Turnout_R @ 90°
///   - Horizontálna koľaj 0° + nová koľaj 45° končiaca v nej → Turnout_R @ 270°
/// </summary>
public class TrackTurnoutAutoHandler
{
    private const double CellSize = 24.0;
    private readonly LayoutEditorViewModel _vm;
    private readonly TrackCrossing45Handler? _crossing45Handler;

    /// <summary>
    /// Lookup tabuľka pre automatické vkladanie výhybiek.
    /// Kľúč: (existingAngle, newAngle, isStart) → (markerKey, rotation)
    /// isStart = true znamená, že nová koľaj ZAČÍNA na existujúcej
    /// isStart = false znamená, že nová koľaj KONČÍ na existujúcej
    /// </summary>
    private static readonly Dictionary<(int existing, int newAngle, bool isStart), (string markerKey, int rotation)> TurnoutLookup = new()
    {
        // ══════════════════════════════════════════════════════════════
        // SKUPINA 1: Horizontálna koľaj (0° alebo 180°) + Šikmá koľaj (45°, 135°, 225°, 315°)
        // ══════════════════════════════════════════════════════════════
        
        // Horizontálna 0° + Šikmá 45° (vpravo hore)
        [(0, 45, true)]  = ("Turnout_R", 90),   // Začína vpravo → výhybka vpravo @ 90°
        [(0, 45, false)] = ("Turnout_R", 270),  // Končí vľavo → výhybka vpravo @ 270°
        
        // Horizontálna 0° + Šikmá 135° (vľavo hore)
        [(0, 135, true)]  = ("Turnout_L", 270),   // Začína vľavo → výhybka vľavo @ 90°
        [(0, 135, false)] = ("Turnout_L", 90),  // Končí vpravo → výhybka vľavo @ 270°
        
        // Horizontálna 0° + Šikmá 225° (225°=45°+180° → rovnaký typ ako 45° = Turnout_R)
        [(0, 225, true)]  = ("Turnout_R", 270),
        [(0, 225, false)] = ("Turnout_R", 90),
        
        // Horizontálna 0° + Šikmá 315° (315°=135°+180° → rovnaký typ ako 135° = Turnout_L)
        [(0, 315, true)]  = ("Turnout_L", 90),
        [(0, 315, false)] = ("Turnout_L", 270),
        
        // Horizontálna 180° + Šikmá 45° (vpravo hore)
        [(180, 45, true)]  = ("Turnout_R", 90),
        [(180, 45, false)] = ("Turnout_R", 270),
        
        // Horizontálna 180° + Šikmá 135° (vľavo hore)
        [(180, 135, true)]  = ("Turnout_L", 270),
        [(180, 135, false)] = ("Turnout_L", 90),
        
        // Horizontálna 180° + Šikmá 225° (225°=45°+180° → Turnout_R)
        [(180, 225, true)]  = ("Turnout_R", 270),
        [(180, 225, false)] = ("Turnout_R", 90),
        
        // Horizontálna 180° + Šikmá 315° (315°=135°+180° → Turnout_L)
        [(180, 315, true)]  = ("Turnout_L", 90),
        [(180, 315, false)] = ("Turnout_L", 270),
        
        // ══════════════════════════════════════════════════════════════
        // SKUPINA 2: Vertikálna koľaj (90° alebo 270°) + Šikmá koľaj (45°, 135°, 225°, 315°)
        // ══════════════════════════════════════════════════════════════
        
        // Vertikálna 90° + Šikmá 45°
        [(90, 45, true)]  = ("Turnout_L", 180),
        [(90, 45, false)] = ("Turnout_L", 0),
        
        // Vertikálna 90° + Šikmá 135°
        [(90, 135, true)]  = ("Turnout_R", 180),
        [(90, 135, false)] = ("Turnout_R", 0),
        
        // Vertikálna 90° + Šikmá 225° (225°=45°+180° → Turnout_R)
        [(90, 225, true)]  = ("Turnout_L", 0),
        [(90, 225, false)] = ("Turnout_L", 180),
        
        // Vertikálna 90° + Šikmá 315° (315°=135°+180° → Turnout_L)
        [(90, 315, true)]  = ("Turnout_R", 0),
        [(90, 315, false)] = ("Turnout_R", 180),
        
        // Vertikálna 270° + Šikmá 45°
        [(270, 45, true)]  = ("Turnout_L", 180),
        [(270, 45, false)] = ("Turnout_L", 0),
        
        // Vertikálna 270° + Šikmá 135°
        [(270, 135, true)]  = ("Turnout_R", 180),
        [(270, 135, false)] = ("Turnout_R", 0),
        
        // Vertikálna 270° + Šikmá 225° (225°=45°+180° → Turnout_R)
        [(270, 225, true)]  = ("Turnout_L", 0),
        [(270, 225, false)] = ("Turnout_L", 180),
        
        // Vertikálna 270° + Šikmá 315° (315°=135°+180° → Turnout_L)
        [(270, 315, true)]  = ("Turnout_R", 0),
        [(270, 315, false)] = ("Turnout_R", 180),
        
        // ══════════════════════════════════════════════════════════════
        // SKUPINA 3: Šikmá koľaj + Horizontálna/Vertikálna (opačné poradie)
        // ══════════════════════════════════════════════════════════════
        
        // Šikmá 45° + Horizontálna 0°
        [(45, 0, true)]  = ("Turnout_L", 135),
        [(45, 0, false)] = ("Turnout_L", 315),
        
        // Šikmá 45° + Horizontálna 180°
        [(45, 180, true)]  = ("Turnout_L", 315),  
        [(45, 180, false)] = ("Turnout_L", 135), 
        
        // Šikmá 45° + Vertikálna 90°
        [(45, 90, true)]  = ("Turnout_R", 135),
        [(45, 90, false)] = ("Turnout_R", 315),
        
        // Šikmá 45° + Vertikálna 270°
        [(45, 270, true)]  = ("Turnout_R", 315),
        [(45, 270, false)] = ("Turnout_R", 135),
        
        // Šikmá 135° + Horizontálna 0°
        [(135, 0, true)]  = ("Turnout_R", 45),
        [(135, 0, false)] = ("Turnout_R", 225),
        
        // Šikmá 135° + Horizontálna 180°
        [(135, 180, true)]  = ("Turnout_R", 225),
        [(135, 180, false)] = ("Turnout_R", 45),
        
        // Šikmá 135° + Vertikálna 90°
        [(135, 90, true)]  = ("Turnout_L", 225),
        [(135, 90, false)] = ("Turnout_L", 45),
        
        // Šikmá 135° + Vertikálna 270°
        [(135, 270, true)]  = ("Turnout_L", 45),
        [(135, 270, false)] = ("Turnout_L", 225),
        
        // Šikmá 225° + Horizontálna 0° (225°=45°+180° → Turnout_R)
        [(225, 0, true)]  = ("Turnout_L", 135),
        [(225, 0, false)] = ("Turnout_L", 315),
        
        // Šikmá 225° + Horizontálna 180° (225°=45°+180° → Turnout_R)
        [(225, 180, true)]  = ("Turnout_L", 315),
        [(225, 180, false)] = ("Turnout_L", 135),
        
        // Šikmá 225° + Vertikálna 90° (225°=45°+180° → Turnout_R)
        [(225, 90, true)]  = ("Turnout_R", 135),
        [(225, 90, false)] = ("Turnout_R", 315),
        
        // Šikmá 225° + Vertikálna 270° (225°=45°+180° → Turnout_R)
        [(225, 270, true)]  = ("Turnout_R", 315),
        [(225, 270, false)] = ("Turnout_R", 135),
        
        // Šikmá 315° + Horizontálna 0° (315°=135°+180° → Turnout_L)
        [(315, 0, true)]  = ("Turnout_R", 45),
        [(315, 0, false)] = ("Turnout_R", 225),
        
        // Šikmá 315° + Horizontálna 180° (315°=135°+180° → Turnout_L)
        [(315, 180, true)]  = ("Turnout_R", 225),
        [(315, 180, false)] = ("Turnout_R", 45),
        
        // Šikmá 315° + Vertikálna 90° (315°=135°+180° → Turnout_L)
        [(315, 90, true)]  = ("Turnout_L", 225),
        [(315, 90, false)] = ("Turnout_L", 45),
        
        // Šikmá 315° + Vertikálna 270° (315°=135°+180° → Turnout_L)
        [(315, 270, true)]  = ("Turnout_L", 45),
        [(315, 270, false)] = ("Turnout_L", 225),
    };

    public TrackTurnoutAutoHandler(LayoutEditorViewModel viewModel, TrackCrossing45Handler? crossing45Handler = null)
    {
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _crossing45Handler = crossing45Handler;
    }

    private static double CellToPixelX(int cellX) => cellX * CellSize;
    private static double CellToPixelY(int cellY) => cellY * CellSize;

    private const double Tolerance = 1.5;

    // 8 smerov pre prehľadávanie susedov
    private static readonly int[] Dx = { -1, 1, 0, 0, -1, 1, -1, 1 };
    private static readonly int[] Dy = { 0, 0, -1, 1, -1, -1, 1, 1 };

    /// <summary>
    /// Skontroluje bunky novej línie a vytvorí výhybky tam, kde sa dotýka existujúcej koľaje.
    /// NOVÁ STRATÉGIA: Kontroluje všetkých 8 susedov prvej a poslednej bunky.
    /// </summary>
    public void CheckAndCreateTurnouts(List<(int cellX, int cellY)> lineCells, int trackRotation)
    {
        if (lineCells.Count < 2)
            return;

        Debug.WriteLine($"");
        Debug.WriteLine($"╔═══════════════════════════════════════════════════════════════");
        Debug.WriteLine($"║ TURNOUT AUTO CHECK START");
        Debug.WriteLine($"╠═══════════════════════════════════════════════════════════════");
        Debug.WriteLine($"║ New line: trackRotation={trackRotation}, cells count={lineCells.Count}");
        Debug.WriteLine($"║ First cell: ({lineCells[0].cellX},{lineCells[0].cellY})");
        Debug.WriteLine($"║ Last cell: ({lineCells[^1].cellX},{lineCells[^1].cellY})");
        Debug.WriteLine($"╚═══════════════════════════════════════════════════════════════");

        // Množina buniek novej línie - pre rýchle overenie
        var lineCellSet = new HashSet<(int, int)>(lineCells);

        // Kontrola PRVEJ bunky (isStart = true)
        CheckTurnoutAtEndpoint(lineCells[0].cellX, lineCells[0].cellY,
                               trackRotation, isStart: true, lineCellSet);

        // Kontrola POSLEDNEJ bunky (isStart = false)
        CheckTurnoutAtEndpoint(lineCells[^1].cellX, lineCells[^1].cellY,
                               trackRotation, isStart: false, lineCellSet);

        Debug.WriteLine($"");
        Debug.WriteLine($"║ TURNOUT AUTO CHECK END");
        Debug.WriteLine($"╚═══════════════════════════════════════════════════════════════");
        Debug.WriteLine($"");
    }

    /// <summary>
    /// Skontroluje endpoint (prvú alebo poslednú bunku) novej línie.
    /// Hľadá existujúce TrackSegmenty v susedných bunkách (všetkých 8 smerov).
    /// </summary>
    private void CheckTurnoutAtEndpoint(int cellX, int cellY, int trackRotation,
                                        bool isStart, HashSet<(int, int)> lineCellSet)
    {
        double pixelX = CellToPixelX(cellX);
        double pixelY = CellToPixelY(cellY);
        string position = isStart ? "START" : "END";

        Debug.WriteLine($"");
        Debug.WriteLine($"  → Checking {position}: endpoint ({cellX},{cellY})");

        // Preskočiť ak na tejto pozícii už je oblúk, kríženie alebo výhybka
        if (HasCurveOrCrossingOrTurnoutAt(pixelX, pixelY))
        {
            Debug.WriteLine($"    ℹ Already has curve/crossing/turnout at ({cellX},{cellY}) → SKIP");
            return;
        }

        // STRATÉGIA 1: Na samotnej bunke je existujúci TrackSegment s INOU rotáciou
        // (nastáva keď nová línia končí NA bunke existujúcej koľaje)
        var trackAtEndpoint = _vm.Elements.FirstOrDefault(el =>
            el.MarkerKey == "TrackSegment" &&
            Math.Abs(el.X - pixelX) < Tolerance &&
            Math.Abs(el.Y - pixelY) < Tolerance);

        if (trackAtEndpoint != null)
        {
            int existingRot = (int)Math.Round(trackAtEndpoint.Rotation);
            int normalizedExisting = NormalizeAngle(existingRot);
            int normalizedNew = NormalizeAngle(trackRotation);

            // Ak sú uhly RÔZNE a nie sú opačné (0°/180° je tá istá koľaj)
            if (normalizedExisting != normalizedNew &&
                normalizedExisting != NormalizeAngle(normalizedNew + 180))
            {
                Debug.WriteLine($"    • Found track AT endpoint: rot={existingRot}°, new rot={trackRotation}°");

                // POZOR: Cross45 vzniká LEN pri KRÍŽENÍ (rovnaká pozícia), nie pri DOTYKOCH
                // Tu sme na endpoint bunke, kde je už existujúci track - to je DOTYK, nie kríženie
                // Preto NEVYKONÁVAME Cross45 kontrolu tu - tá patrí do crossing handlera

                if (ShouldCreateTurnout(existingRot, trackRotation, isStart, out string mk, out int rot))
                {
                    Debug.WriteLine($"    • ✓ ShouldCreateTurnout = TRUE (at endpoint)");
                    Debug.WriteLine($"    •   MarkerKey: {mk}, Rotation: {rot}°");
                    CreateTurnoutAt(pixelX, pixelY, mk, rot);
                    return;
                }
            }
        }

        // STRATÉGIA 2: Kontrolujeme všetkých 8 susedov pre existujúce TrackSegmenty
        Debug.WriteLine($"    • Checking 8 neighbors of ({cellX},{cellY})...");

        for (int i = 0; i < 8; i++)
        {
            int adjX = cellX + Dx[i];
            int adjY = cellY + Dy[i];

            // Preskočiť bunky, ktoré sú súčasťou novej línie
            if (lineCellSet.Contains((adjX, adjY)))
                continue;

            // Vypočítame smer k susednej bunke
            int directionToNeighbor = GetDirectionToCell(cellX, cellY, adjX, adjY);
            
            // KĽÚČOVÁ KONTROLA: Výhybka sa vytvorí len ak susedná bunka je v smere,
            // kam nová koľaj smeruje (trackRotation) ALEBO v opačnom smere (začiatok línie)
            int normalizedDir = NormalizeAngle(directionToNeighbor);
            int normalizedTrack = NormalizeAngle(trackRotation);
            int reverseTrack = NormalizeAngle(trackRotation + 180);
            
            // Výhybka vzniká len ak:
            // - isStart=true a sused je v opačnom smere koľaje (koľaj začína od suseda)
            // - isStart=false a sused je v smere koľaje (koľaj končí pri susedovi)
            bool isInCorrectDirection = false;
            if (isStart)
            {
                // Pri začiatku línie: sused musí byť v OPAČNOM smere (odkiaľ koľaj prichádza)
                isInCorrectDirection = (normalizedDir == reverseTrack);
            }
            else
            {
                // Pri konci línie: sused musí byť v SMERE koľaje (kam koľaj smeruje)
                isInCorrectDirection = (normalizedDir == normalizedTrack);
            }
            
            if (!isInCorrectDirection)
            {
                Debug.WriteLine($"    • Neighbor ({adjX},{adjY}) dir={directionToNeighbor}° - NOT in track direction → SKIP");
                continue;
            }

            double adjPixelX = CellToPixelX(adjX);
            double adjPixelY = CellToPixelY(adjY);

            var existingTrack = _vm.Elements.FirstOrDefault(el =>
                el.MarkerKey == "TrackSegment" &&
                Math.Abs(el.X - adjPixelX) < Tolerance &&
                Math.Abs(el.Y - adjPixelY) < Tolerance);

            if (existingTrack == null)
                continue;

            int existingRotation = (int)Math.Round(existingTrack.Rotation);
            Debug.WriteLine($"    • Neighbor ({adjX},{adjY}): existing rot={existingRotation}°, new rot={trackRotation}° - IN DIRECTION");

            // POZOR: Cross45 vzniká LEN pri KRÍŽENÍ (rovnaká pozícia), nie pri DOTYKOCH
            // Tu kontrolujeme SUSEDNÉ bunky - to sú DOTYKY, nie kríženia
            // Preto NEVYKONÁVAME Cross45 kontrolu - tá patrí do crossing handlera

            if (ShouldCreateTurnout(existingRotation, trackRotation, isStart, out string markerKey, out int rotation))
            {
                Debug.WriteLine($"    • ✓ ShouldCreateTurnout = TRUE");
                Debug.WriteLine($"    •   MarkerKey: {markerKey}, Rotation: {rotation}°");

                if (!HasTurnoutAt(pixelX, pixelY))
                {
                    Debug.WriteLine($"    • ✓ No existing turnout → CREATING at ({cellX},{cellY})...");
                    CreateTurnoutAt(pixelX, pixelY, markerKey, rotation);
                }
                else
                {
                    Debug.WriteLine($"    • ✗ Turnout already exists → SKIP");
                }
                return; // Len jedna výhybka na endpoint
            }
            else
            {
                Debug.WriteLine($"    • ✗ Not in lookup for ({existingRotation}, {trackRotation}, {isStart})");
            }
        }

        Debug.WriteLine($"    ℹ No valid turnout combination found for {position}");
    }
    
    /// <summary>Vypočíta smer (uhol) od bunky (fromX, fromY) k bunke (toX, toY).</summary>
    private int GetDirectionToCell(int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX;
        int dy = toY - fromY;
        
        // Mapovanie relatívnej pozície na uhol
        return (dx, dy) switch
        {
            (1, 0)   => 180,  // Vpravo
            (-1, 0)  => 0,    // Vľavo
            (0, -1)  => 90,   // Hore
            (0, 1)   => 270,  // Dole
            (1, -1)  => 135,  // Vpravo hore
            (-1, -1) => 45,   // Vľavo hore
            (-1, 1)  => 315,  // Vľavo dole
            (1, 1)   => 225,  // Vpravo dole
            _        => -1    // Neznámy smer
        };
    }

    /// <summary>
    /// Kontroluje, či má vzniknúť výhybka na základe uhlov a pozície.
    /// </summary>
    private bool ShouldCreateTurnout(int existingRotation, int newRotation, bool isStart,
                                     out string markerKey, out int rotation)
    {
        markerKey = "";
        rotation = 0;

        int existing = NormalizeAngle(existingRotation);
        int newAngle = NormalizeAngle(newRotation);

        var key = (existing, newAngle, isStart);
        if (TurnoutLookup.TryGetValue(key, out var result))
        {
            markerKey = result.markerKey;
            rotation = result.rotation;
            return true;
        }

        return false;
    }

    private static int NormalizeAngle(int angle)
    {
        angle = angle % 360;
        if (angle < 0) angle += 360;
        return angle;
    }

    /// <summary>
    /// Kontroluje, či už existuje výhybka na danej pozícii.
    /// </summary>
    private bool HasTurnoutAt(double snapX, double snapY)
    {
        return _vm.Elements.Any(el =>
            (el.MarkerKey == "Turnout_L" || el.MarkerKey == "Turnout_R" ||
             el.MarkerKey == "TurnoutCurve_L" || el.MarkerKey == "TurnoutCurve_R" ||
             el.MarkerKey == "Turnout_Y" || el.MarkerKey == "Turnout_3W") &&
            Math.Abs(el.X - snapX) < Tolerance &&
            Math.Abs(el.Y - snapY) < Tolerance);
    }

    /// <summary>
    /// Kontroluje, či na pozícii už je oblúk, kríženie alebo výhybka (nesmieme prepísať).
    /// </summary>
    private bool HasCurveOrCrossingOrTurnoutAt(double snapX, double snapY)
    {
        return _vm.Elements.Any(el =>
            (el.MarkerKey == "Curve_45" || el.MarkerKey == "Curve_90" ||
             el.MarkerKey == "Cross90" || el.MarkerKey == "Cross45" ||
             el.MarkerKey == "Turnout_L" || el.MarkerKey == "Turnout_R" ||
             el.MarkerKey == "TurnoutCurve_L" || el.MarkerKey == "TurnoutCurve_R" ||
             el.MarkerKey == "Turnout_Y" || el.MarkerKey == "Turnout_3W" ||
             el.MarkerKey == "Bridge90" || el.MarkerKey == "Bridge45L" || el.MarkerKey == "Bridge45R") &&
            Math.Abs(el.X - snapX) < Tolerance &&
            Math.Abs(el.Y - snapY) < Tolerance);
    }

    /// <summary>
    /// Vytvorí výhybku na danej pozícii. Odstráni TrackSegmenty aj Bumpery.
    /// FIX: Používa TurnoutElement namiesto TrackSegmentElement.
    /// FIX: Odstraňuje aj Bumper elementy.
    /// </summary>
    private void CreateTurnoutAt(double snapX, double snapY, string markerKey, int rotation)
    {
        Debug.WriteLine($"");
        Debug.WriteLine($"  ┌─────────────────────────────────────────────────────");
        Debug.WriteLine($"  │ CreateTurnoutAt called");
        Debug.WriteLine($"  │ Position: ({snapX:F2}, {snapY:F2})");
        Debug.WriteLine($"  │ MarkerKey: {markerKey}");
        Debug.WriteLine($"  │ Rotation: {rotation}°");
        Debug.WriteLine($"  └─────────────────────────────────────────────────────");

        if (HasTurnoutAt(snapX, snapY))
        {
            Debug.WriteLine($"  ⚠ Turnout already exists at this position - skipping");
            return;
        }

        // Nájdeme existujúci element - použijeme jeho presnú pozíciu
        var existingElement = _vm.Elements.FirstOrDefault(el =>
            (el.MarkerKey == "TrackSegment" || el.MarkerKey == "Bumper") &&
            Math.Abs(el.X - snapX) < Tolerance &&
            Math.Abs(el.Y - snapY) < Tolerance);

        double posX = existingElement?.X ?? snapX;
        double posY = existingElement?.Y ?? snapY;

        Debug.WriteLine($"  → Using position: ({posX:F2}, {posY:F2})");
        Debug.WriteLine($"  → Creating {markerKey} element (TurnoutElement)...");

        // FIX: Použiť TurnoutElement namiesto TrackSegmentElement
        var turnout = new TurnoutElement
        {
            MarkerKey = markerKey,
            Label = GetTurnoutAutoName(markerKey),  // Automatický názov s poradovým číslom
            X = posX,
            Y = posY,
            Rotation = rotation
        };

        // FIX: Odstránime TrackSegmenty AJ Bumpery na tejto pozícii
        var elementsToRemove = _vm.Elements
            .Where(el => (el.MarkerKey == "TrackSegment" || el.MarkerKey == "Bumper") &&
                        Math.Abs(el.X - snapX) < Tolerance &&
                        Math.Abs(el.Y - snapY) < Tolerance)
            .ToList();

        Debug.WriteLine($"  → Removing {elementsToRemove.Count} elements (TrackSegments + Bumpers)");

        foreach (var el in elementsToRemove)
        {
            Debug.WriteLine($"    • Removing {el.MarkerKey} at ({el.X:F2}, {el.Y:F2}), rot={el.Rotation:F1}°");
            _vm.Elements.Remove(el);
        }

        Debug.WriteLine($"  → Adding {markerKey} to Elements collection");
        _vm.Elements.Add(turnout);

        Debug.WriteLine($"  ✅ {markerKey} created successfully!");
        Debug.WriteLine($"");
    }

    /// <summary>
    /// Odstráni všetky automaticky vytvorené výhybky z layoutu.
    /// </summary>
    public void RemoveAllAutoTurnouts()
    {
        var turnouts = _vm.Elements
            .Where(el => el.MarkerKey == "Turnout_L" || el.MarkerKey == "Turnout_R")
            .ToList();
        foreach (var turnout in turnouts)
            _vm.Elements.Remove(turnout);
    }

    /// <summary>Generuje automatický názov pre výhybku podľa MarkerKey a poradového čísla.</summary>
    private string GetTurnoutAutoName(string markerKey)
    {
        var (prefix, counter) = markerKey switch
        {
            "Turnout_L"      => ("VĽ", GetNextTurnoutCount("Turnout_L")),      // Výhybka ľavá
            "Turnout_R"      => ("VP", GetNextTurnoutCount("Turnout_R")),      // Výhybka pravá
            "TurnoutL90"     => ("OVĽ", GetNextTurnoutCount("TurnoutL90")),    // Obojsmerná výhybka ľavá 90°
            "TurnoutR90"     => ("OVP", GetNextTurnoutCount("TurnoutR90")),    // Obojsmerná výhybka pravá 90°
            "TurnoutCurve_L" => ("OVĹO", GetNextTurnoutCount("TurnoutCurve_L")),// Obojsmerná výhybka ľavá oblúk
            "TurnoutCurve_R" => ("OVPO", GetNextTurnoutCount("TurnoutCurve_R")),// Obojsmerná výhybka pravá oblúk
            "Turnout_Y"      => ("YV", GetNextTurnoutCount("Turnout_Y")),      // Y výhybka
            "Turnout_3W"     => ("3W", GetNextTurnoutCount("Turnout_3W")),     // 3-cestná výhybka
            "DoubleSlip"     => ("KV", GetNextTurnoutCount("DoubleSlip")),     // Krížová výhybka
            _                => ("V", GetNextTurnoutCount(markerKey))           // Všeobecná výhybka
        };
        
        return $"{prefix} {counter}";
    }

    /// <summary>Získa poradové číslo pre konkrétny typ výhybky.</summary>
    private int GetNextTurnoutCount(string markerKey)
    {
        // Získame prefix názvu pre daný MarkerKey
        string prefix = markerKey switch
        {
            "Turnout_L"      => "VĽ",
            "Turnout_R"      => "VP",
            "TurnoutL90"     => "OVĽ",
            "TurnoutR90"     => "OVP",
            "TurnoutCurve_L" => "OVĹO",
            "TurnoutCurve_R" => "OVPO",
            "Turnout_Y"      => "YV",
            "Turnout_3W"     => "3W",
            "DoubleSlip"     => "KV",
            _                => "V"
        };
        
        // Nájdeme najvyššie poradové číslo z existujúcich názvov s týmto prefixom
        int maxNumber = 0;
        foreach (var element in _vm.Elements)
        {
            if (element.Label != null && element.Label.StartsWith(prefix + " "))
            {
                // Pokúsime sa extrahovať číslo z názvu (napr. "VĽ 5" -> 5)
                var parts = element.Label.Split(' ');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int num))
                {
                    if (num > maxNumber)
                        maxNumber = num;
                }
            }
        }
        
        return maxNumber + 1;
    }
}







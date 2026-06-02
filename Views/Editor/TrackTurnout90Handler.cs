using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Editor;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;

namespace TrackFlow.Views.Editor;

/// <summary>
/// Handler pre automatické vytváranie kolmých výhybiek (TurnoutL90/TurnoutR90) pri napájaní kolmých koľají.
/// 
/// LOGIKA:
/// Keď sa nová koľaj napája kolmo (90°) na existujúcu koľaj, automaticky sa vloží výhybka 90°.
/// 
/// PRÍKLAD:
///   - Horizontálna koľaj 0° + nová koľaj 90° vychádzajúca z nej → TurnoutR90 @ 90°
///   - Horizontálna koľaj 0° + nová koľaj 270° končiaca v nej → TurnoutR90 @ 270°
/// </summary>
public class TrackTurnout90Handler
{
    private const double CellSize = 24.0;
    private readonly LayoutEditorViewModel _vm;

    /// <summary>
    /// Lookup tabuľka pre automatické vkladanie kolmých výhybiek.
    /// Kľúč: (existingAngle, newAngle, isStart) → (markerKey, rotation)
    /// isStart = true znamená, že nová koľaj ZAČÍNA na existujúcej
    /// isStart = false znamená, že nová koľaj KONČÍ na existujúcej
    /// </summary>
    private static readonly Dictionary<(int existing, int newAngle, bool isStart), (string markerKey, int rotation)> Turnout90Lookup = new()
    {
        // ══════════════════════════════════════════════════════════════
        // SKUPINA 1: Horizontálna koľaj (0° alebo 180°) + Vertikálna koľaj (90° alebo 270°)
        // ══════════════════════════════════════════════════════════════
        
        // Horizontálna 0° + Vertikálna 90° (hore)
        [(0, 90, true)]   = ("TurnoutR90", 90),    // Začína hore → VP90
        [(0, 90, false)]  = ("TurnoutR90", 270),   // Končí zdola → VP270
        
        // Horizontálna 0° + Vertikálna 270° (dole)
        [(0, 270, true)]  = ("TurnoutR90", 270),   // Začína dole → VP270
        [(0, 270, false)] = ("TurnoutR90", 90),    // Končí zhora → VP90
        
        // Horizontálna 180° + Vertikálna 90° (hore)
        [(180, 90, true)]   = ("TurnoutR90", 90),   // Začína hore → VP90
        [(180, 90, false)]  = ("TurnoutR90", 270),  // Končí zdola → VP270
        
        // Horizontálna 180° + Vertikálna 270° (dole)
        [(180, 270, true)]  = ("TurnoutR90", 270),  // Začína dole → VP270
        [(180, 270, false)] = ("TurnoutR90", 90),   // Končí zhora → VP90
        
        // ══════════════════════════════════════════════════════════════
        // SKUPINA 2: Vertikálna koľaj (90° alebo 270°) + Horizontálna koľaj (0° alebo 180°)
        // ══════════════════════════════════════════════════════════════
        
        // Vertikálna 90° + Horizontálna 0° (vpravo)
        [(90, 0, true)]    = ("TurnoutR90", 0),     // Začína vpravo → VP0
        [(90, 0, false)]   = ("TurnoutR90", 180),   // Končí zľava → VP180
        
        // Vertikálna 90° + Horizontálna 180° (vľavo)
        [(90, 180, true)]  = ("TurnoutR90", 180),   // Začína vľavo → VP180
        [(90, 180, false)] = ("TurnoutR90", 0),     // Končí zprava → VP0
        
        // Vertikálna 270° + Horizontálna 0° (vpravo)
        [(270, 0, true)]   = ("TurnoutR90", 0),     // Začína vpravo → VP0
        [(270, 0, false)]  = ("TurnoutR90", 180),   // Končí zľava → VP180
        
        // Vertikálna 270° + Horizontálna 180° (vľavo)
        [(270, 180, true)]  = ("TurnoutR90", 180),  // Začína vľavo → VP180
        [(270, 180, false)] = ("TurnoutR90", 0),    // Končí zprava → VP0
    };

    public TrackTurnout90Handler(LayoutEditorViewModel viewModel)
    {
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private static double CellToPixelX(int cellX) => cellX * CellSize;
    private static double CellToPixelY(int cellY) => cellY * CellSize;

    private const double Tolerance = 1.5;

    // 8 smerov pre prehľadávanie susedov
    private static readonly int[] Dx = { -1, 1, 0, 0, -1, 1, -1, 1 };
    private static readonly int[] Dy = { 0, 0, -1, 1, -1, -1, 1, 1 };

    /// <summary>
    /// Skontroluje bunky novej línie a vytvorí kolmé výhybky tam, kde sa kolmo napája na existujúcu koľaj.
    /// </summary>
    public void CheckAndCreateTurnout90(List<(int cellX, int cellY)> lineCells, int trackRotation)
    {
        if (lineCells.Count < 2)
            return;

        Debug.WriteLine($"");
        Debug.WriteLine($"╔═══════════════════════════════════════════════════════════════");
        Debug.WriteLine($"║ TURNOUT90 AUTO CHECK START");
        Debug.WriteLine($"╠═══════════════════════════════════════════════════════════════");
        Debug.WriteLine($"║ New line: trackRotation={trackRotation}, cells count={lineCells.Count}");
        Debug.WriteLine($"║ First cell: ({lineCells[0].cellX},{lineCells[0].cellY})");
        Debug.WriteLine($"║ Last cell: ({lineCells[^1].cellX},{lineCells[^1].cellY})");
        Debug.WriteLine($"╚═══════════════════════════════════════════════════════════════");

        // Množina buniek novej línie - pre rýchle overenie
        var lineCellSet = new HashSet<(int, int)>(lineCells);

        // Kontrola PRVEJ bunky (isStart = true)
        CheckTurnout90AtEndpoint(lineCells[0].cellX, lineCells[0].cellY,
                               trackRotation, isStart: true, lineCellSet);

        // Kontrola POSLEDNEJ bunky (isStart = false)
        CheckTurnout90AtEndpoint(lineCells[^1].cellX, lineCells[^1].cellY,
                               trackRotation, isStart: false, lineCellSet);

        Debug.WriteLine($"");
        Debug.WriteLine($"║ TURNOUT90 AUTO CHECK END");
        Debug.WriteLine($"╚═══════════════════════════════════════════════════════════════");
        Debug.WriteLine($"");
    }

    /// <summary>
    /// Skontroluje endpoint (prvú alebo poslednú bunku) novej línie.
    /// Hľadá existujúce TrackSegmenty v susedných bunkách (všetkých 8 smerov).
    /// </summary>
    private void CheckTurnout90AtEndpoint(int cellX, int cellY, int trackRotation,
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

        // STRATÉGIA 1: Na samotnej bunke je existujúci TrackSegment s KOLMOU rotáciou
        var trackAtEndpoint = _vm.Elements.FirstOrDefault(el =>
            el.MarkerKey == "TrackSegment" &&
            Math.Abs(el.X - pixelX) < Tolerance &&
            Math.Abs(el.Y - pixelY) < Tolerance);

        if (trackAtEndpoint != null)
        {
            int existingRot = (int)Math.Round(trackAtEndpoint.Rotation);
            int normalizedExisting = NormalizeAngle(existingRot);
            int normalizedNew = NormalizeAngle(trackRotation);

            // Kontrola kolmosti (90° rozdiel)
            if (ArePerpendicularTracks(normalizedExisting, normalizedNew))
            {
                Debug.WriteLine($"    • Found PERPENDICULAR track AT endpoint: rot={existingRot}°, new rot={trackRotation}°");

                if (ShouldCreateTurnout90(existingRot, trackRotation, isStart, out string mk, out int rot))
                {
                    Debug.WriteLine($"    • ✓ ShouldCreateTurnout90 = TRUE (at endpoint)");
                    Debug.WriteLine($"    •   MarkerKey: {mk}, Rotation: {rot}°");
                    CreateTurnout90At(pixelX, pixelY, mk, rot);
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
            
            // Výhybka 90° vzniká len ak susedná bunka je v smere kolmo na track
            int normalizedDir = NormalizeAngle(directionToNeighbor);
            int normalizedTrack = NormalizeAngle(trackRotation);
            int reverseTrack = NormalizeAngle(trackRotation + 180);
            
            // Pre kolmé výhybky: sused musí byť v smere koľaje alebo v opačnom smere
            bool isInCorrectDirection = false;
            if (isStart)
            {
                isInCorrectDirection = (normalizedDir == reverseTrack);
            }
            else
            {
                isInCorrectDirection = (normalizedDir == normalizedTrack);
            }
            
            if (!isInCorrectDirection)
                continue;

            double adjPixelX = CellToPixelX(adjX);
            double adjPixelY = CellToPixelY(adjY);

            var existingTrack = _vm.Elements.FirstOrDefault(el =>
                el.MarkerKey == "TrackSegment" &&
                Math.Abs(el.X - adjPixelX) < Tolerance &&
                Math.Abs(el.Y - adjPixelY) < Tolerance);

            if (existingTrack == null)
                continue;

            int existingRotation = (int)Math.Round(existingTrack.Rotation);
            
            // Kontrola kolmosti
            if (!ArePerpendicularTracks(existingRotation, trackRotation))
                continue;

            Debug.WriteLine($"    • Neighbor ({adjX},{adjY}): existing rot={existingRotation}°, new rot={trackRotation}° - PERPENDICULAR");

            if (ShouldCreateTurnout90(existingRotation, trackRotation, isStart, out string markerKey, out int rotation))
            {
                Debug.WriteLine($"    • ✓ ShouldCreateTurnout90 = TRUE");
                Debug.WriteLine($"    •   MarkerKey: {markerKey}, Rotation: {rotation}°");

                if (!HasTurnout90At(pixelX, pixelY))
                {
                    Debug.WriteLine($"    • ✓ No existing turnout90 → CREATING at ({cellX},{cellY})...");
                    CreateTurnout90At(pixelX, pixelY, markerKey, rotation);
                }
                else
                {
                    Debug.WriteLine($"    • ✗ Turnout90 already exists → SKIP");
                }
                return;
            }
        }

        Debug.WriteLine($"    ℹ No valid turnout90 combination found for {position}");
    }
    
    /// <summary>Vypočíta smer (uhol) od bunky (fromX, fromY) k bunke (toX, toY).</summary>
    private int GetDirectionToCell(int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX;
        int dy = toY - fromY;
        
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
            _        => -1
        };
    }

    /// <summary>
    /// Kontroluje, či sú dva track segmenty kolmé (90° rozdiel).
    /// </summary>
    private bool ArePerpendicularTracks(int rotation1, int rotation2)
    {
        int r1 = NormalizeAngle(rotation1);
        int r2 = NormalizeAngle(rotation2);
        int diff = Math.Abs(r1 - r2);
        if (diff > 180) diff = 360 - diff;
        return diff == 90;
    }

    /// <summary>
    /// Kontroluje, či má vzniknúť kolmá výhybka na základe uhlov a pozície.
    /// </summary>
    private bool ShouldCreateTurnout90(int existingRotation, int newRotation, bool isStart,
                                     out string markerKey, out int rotation)
    {
        markerKey = "";
        rotation = 0;

        int existing = NormalizeAngle(existingRotation);
        int newAngle = NormalizeAngle(newRotation);

        var key = (existing, newAngle, isStart);
        if (Turnout90Lookup.TryGetValue(key, out var result))
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
    /// Kontroluje, či už existuje kolmá výhybka na danej pozícii.
    /// </summary>
    private bool HasTurnout90At(double snapX, double snapY)
    {
        return _vm.Elements.Any(el =>
            (el.MarkerKey == "TurnoutL90" || el.MarkerKey == "TurnoutR90") &&
            Math.Abs(el.X - snapX) < Tolerance &&
            Math.Abs(el.Y - snapY) < Tolerance);
    }

    /// <summary>
    /// Kontroluje, či na pozícii už je oblúk, kríženie alebo výhybka.
    /// </summary>
    private bool HasCurveOrCrossingOrTurnoutAt(double snapX, double snapY)
    {
        return _vm.Elements.Any(el =>
            (el.MarkerKey == "Curve_45" || el.MarkerKey == "Curve_90" ||
             el.MarkerKey == "Cross90" || el.MarkerKey == "Cross45" ||
             el.MarkerKey == "Turnout_L" || el.MarkerKey == "Turnout_R" ||
             el.MarkerKey == "TurnoutL90" || el.MarkerKey == "TurnoutR90" ||
             el.MarkerKey == "TurnoutCurve_L" || el.MarkerKey == "TurnoutCurve_R" ||
             el.MarkerKey == "Turnout_Y" || el.MarkerKey == "Turnout_3W" ||
             el.MarkerKey == "Bridge90" || el.MarkerKey == "Bridge45L" || el.MarkerKey == "Bridge45R") &&
            Math.Abs(el.X - snapX) < Tolerance &&
            Math.Abs(el.Y - snapY) < Tolerance);
    }

    /// <summary>
    /// Vytvorí kolmú výhybku na danej pozícii. Odstráni TrackSegmenty aj Bumpery.
    /// </summary>
    private void CreateTurnout90At(double snapX, double snapY, string markerKey, int rotation)
    {
        Debug.WriteLine($"");
        Debug.WriteLine($"  ┌─────────────────────────────────────────────────────");
        Debug.WriteLine($"  │ CreateTurnout90At called");
        Debug.WriteLine($"  │ Position: ({snapX:F2}, {snapY:F2})");
        Debug.WriteLine($"  │ MarkerKey: {markerKey}");
        Debug.WriteLine($"  │ Rotation: {rotation}°");
        Debug.WriteLine($"  └─────────────────────────────────────────────────────");

        if (HasTurnout90At(snapX, snapY))
        {
            Debug.WriteLine($"  ⚠ Turnout90 already exists at this position - skipping");
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

        var turnout = new TurnoutElement
        {
            MarkerKey = markerKey,
            Label = GetTurnoutAutoName(markerKey),  // Automatický názov s poradovým číslom
            X = posX,
            Y = posY,
            Rotation = rotation
        };

        // Odstránime TrackSegmenty aj Bumpery na tejto pozícii
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
    /// Odstráni všetky automaticky vytvorené kolmé výhybky z layoutu.
    /// </summary>
    public void RemoveAllTurnout90()
    {
        var turnouts = _vm.Elements
            .Where(el => el.MarkerKey == "TurnoutL90" || el.MarkerKey == "TurnoutR90")
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


using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Editor;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;

namespace TrackFlow.Views.Editor;

/// <summary>
/// Handler pre automatické vytváranie križovatiek 45° pri krížení koľají.
/// 
/// LOGIKA:
/// 1. Vertikálna koľaj (90° alebo 270°) križovaná koľajou pod uhlom 135° alebo 315°
/// 2. Šikmá koľaj (45°, 135°, 225°, 315°) križovaná kolmou koľajou
/// 
/// PRÍKLAD:
///   - Vertikálna koľaj (90°) + šikmá koľaj (135°) → Cross45
///   - Šikmá koľaj (45°) + kolmá koľaj (135°) → Cross45
/// </summary>
public class TrackCrossing45Handler
{
    private const double CellSize = 24.0;  // Rovnaká ako LayoutEditorViewModel.CellSize
    private readonly LayoutEditorViewModel _vm;

    /// <summary>
    /// Lookup tabuľka pre detekciu Cross45 prípadov.
    /// Kľúč: (existingAngle, newAngle) → True ak má vzniknúť Cross45
    /// </summary>
    private static readonly Dictionary<(int existing, int new_angle), (bool shouldCreate, int rotation)> Cross45Lookup = new()
    {
        // ══════════════════════════════════════════════════════════════
        // SKUPINA 1: Vertikálna koľaj (90° alebo 270°) + šikmá koľaj (135° alebo 315°)
        // ══════════════════════════════════════════════════════════════
        
        // Vertikálna 90° krížená šikmou
        [(90, 45)]   = (true, 0),   // Vertikálna 90° + šikmá 45° → Cross45 @ 0°
        [(90, 135)]  = (true, 45),  // Vertikálna 90° + šikmá 135° → Cross45 @ 45°
        [(90, 225)]  = (true, 0),   // Vertikálna 90° + šikmá 225° → Cross45 @  0°
        [(90, 315)]  = (true, 45),  // Vertikálna 90° + šikmá 315° → Cross45 @ 45°
        
        // Vertikálna 270° krížená šikmou
        [(270, 135)] = (true, 45),   // Vertikálna 270° + šikmá 135° → Cross45 @ 45°
        [(270, 45)]  = (true, 0),    // Vertikálna 270° + šikmá 45° → Cross45 @ 0°
        [(270, 225)] = (true, 0),    // Vertikálna 270° + šikmá 225° → Cross45 @ 0°
        [(270, 315)] = (true, 225),  // Vertikálna 270° + šikmá 315° → Cross45 @ 90°
        
        // Šikmá koľaj krížená vertikálnou (opačné poradie)
        [(135, 90)]  = (true, 45),   // Šikmá 135° + vertikálna 90° → Cross45 @ 45°
        [(315, 90)]  = (true, 45),   // Šikmá 315° + vertikálna 90° → Cross45 @ 45°
        [(135, 270)] = (true, 225),  // Šikmá 135° + vertikálna 270° → Cross45 @ 225°
        [(315, 270)] = (true, 225),   // Šikmá 315° + vertikálna 270° → Cross45 @ 90°
        
        // ══════════════════════════════════════════════════════════════
        // SKUPINA 2: Šikmá koľaj + kolmá koľaj
        // ══════════════════════════════════════════════════════════════
   
        // Šikmá 225° + kolmá (rozdiel 90°)
        [(225, 90)] = (true, 0),      // Šikmá 225° + kolmá 90° → Cross45 @ 0°
        [(225, 270)] = (true, 0),     // Šikmá 225° + kolmá 270° → Cross45 @ 0°
     
        // Šikmá 315° + kolmá (rozdiel 90°)
        [(315, 0)]  = (true, 90),     // Šikmá 315° + kolmá 0° → Cross45 @ 90°
        [(315, 180)] = (true, 270),   // Šikmá 315° + kolmá 180° → Cross45 @ 270°
    
        // ══════════════════════════════════════════════════════════════
        // SKUPINA 3: Horizontálna koľaj (0° alebo 180°) + šikmá koľaj (45° alebo 225°)
        // ══════════════════════════════════════════════════════════════
        
        // Horizontálna 0° krížená šikmou
        [(0, 45)]    = (true, 135),   // Horizontálna 0° + šikmá 45° → Cross45 @ 135°
        [(0, 225)]   = (true, 135),   // Horizontálna 0° + šikmá 225° → Cross45 @ 135°
        [(0, 135)]   = (true, 90),    // Horizontálna 0° + šikmá 135° → Cross45 @ 90°
        [(0, 315)]   = (true, 90),    // Horizontálna 0° + šikmá 315° → Cross45 @ 90°
        
        // Horizontálna 180° krížená šikmou
        [(180, 45)]  = (true, 135),   // Horizontálna 180° + šikmá 45° → Cross45 @ 135°
        [(180, 135)] = (true, 90),    // Horizontálna 180° + šikmá 135° → Cross45 @ 90°
        [(180, 315)] = (true, 270),   // Horizontálna 180° + šikmá 315° → Cross45 @ 270°
        [(180, 225)] = (true, 315),   // Horizontálna 180° + šikmá 225° → Cross45 @ 315°
        
        // Šikmá koľaj krížená horizontálnou (opačné poradie)
        [(45, 0)]    = (true, 135),   // Šikmá 45° + horizontálna 0° → Cross45 @ 135°
        [(225, 0)]   = (true, 135),   // Šikmá 225° + horizontálna 0° → Cross45 @ 135°
        [(45, 180)]  = (true, 315),   // Šikmá 45° + horizontálna 180° → Cross45 @ 315°
        [(225, 180)] = (true, 315),   // Šikmá 225° + horizontálna 180° → Cross45 @ 315°
    };

    public TrackCrossing45Handler(LayoutEditorViewModel viewModel)
    {
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    /// <summary>
    /// Vizuálna bunka (cx,cy) → pixel pozícia TrackSegmentu.
    /// TrackSegment vložený na bunke cx má X = cx * CellSize.
    /// </summary>
    private static double CellToPixelX(int cellX) => cellX * CellSize;
    private static double CellToPixelY(int cellY) => cellY * CellSize;

    /// <summary>Tolerancia pre porovnanie pozícií (±1.5px).</summary>
    private const double Tolerance = 1.5;

    /// <summary>
    /// Skontroluje bunky novej línie a vytvorí Cross45 tam, kde sa križujú koľaje pod uhlom 45°.
    /// </summary>
    /// <param name="lineCells">Zoznam buniek novej línie koľají</param>
    /// <param name="trackRotation">Rotácia novej koľaje (0, 45, 90, 135, 180, 225, 270, 315)</param>
    public void CheckAndCreateCrossings45(List<(int cellX, int cellY)> lineCells, int trackRotation)
    {
        if (lineCells.Count == 0)
            return;

        Debug.WriteLine($"=== CROSSING 45° CHECK START ===");
        Debug.WriteLine($"  New line: trackRotation={trackRotation}, cells count={lineCells.Count}");
        Debug.WriteLine($"  Cells: {string.Join(", ", lineCells.Select(c => $"({c.cellX},{c.cellY})"))}");

        foreach (var (cellX, cellY) in lineCells)
        {
            double expectedX = CellToPixelX(cellX);
            double expectedY = CellToPixelY(cellY);

            var tracksHere = _vm.Elements
                .Where(el => el.MarkerKey == "TrackSegment" &&
                            Math.Abs(el.X - expectedX) < Tolerance &&
                            Math.Abs(el.Y - expectedY) < Tolerance)
                .ToList();

            if (tracksHere.Count > 0)
            {
                Debug.WriteLine($"  Cell ({cellX},{cellY}), snapPos=({expectedX},{expectedY}): found {tracksHere.Count} tracks:");
                foreach (var t in tracksHere)
                {
                    bool shouldCreate = ShouldCreateCross45(t.Rotation, trackRotation, out int rotation);
                    Debug.WriteLine($"    - X={t.X}, Y={t.Y}, Rot={t.Rotation}, shouldCreate={shouldCreate}, targetRotation={rotation}");
                }
            }

            CheckCrossingAtCell(cellX, cellY, trackRotation);
        }

        Debug.WriteLine($"=== CROSSING 45° CHECK END ===");
    }

    /// <summary>
    /// Kontroluje, či má vzniknúť Cross45 na základe uhlov dvoch koľají.
    /// </summary>
    public bool ShouldCreateCross45(double existingRotation, double newRotation, out int targetRotation)
    {
        targetRotation = 0;

        int existing = NormalizeAngle((int)Math.Round(existingRotation));
        int newAngle = NormalizeAngle((int)Math.Round(newRotation));

        var key = (existing, newAngle);
        if (Cross45Lookup.TryGetValue(key, out var result))
        {
            targetRotation = result.rotation;
            return result.shouldCreate;
        }

        return false;
    }

    /// <summary>Normalizuje uhol na rozsah 0-360.</summary>
    private static int NormalizeAngle(int angle)
    {
        angle = angle % 360;
        if (angle < 0) angle += 360;
        return angle;
    }

    /// <summary>
    /// Kontroluje kríženie na konkrétnej bunke.
    /// </summary>
    private void CheckCrossingAtCell(int cellX, int cellY, int newTrackRotation)
    {
        double expectedX = CellToPixelX(cellX);
        double expectedY = CellToPixelY(cellY);

        Debug.WriteLine($"    ┌─ CheckCrossingAtCell: ({cellX},{cellY})");

        // Hľadáme TrackSegmenty na PRESNEJ snap pozícii novej koľaje
        var tracksAtPosition = _vm.Elements
            .Where(el => el.MarkerKey == "TrackSegment" &&
                        Math.Abs(el.X - expectedX) < Tolerance &&
                        Math.Abs(el.Y - expectedY) < Tolerance)
            .ToList();

        Debug.WriteLine($"    │  Found {tracksAtPosition.Count} existing TrackSegments");

        if (tracksAtPosition.Count == 0)
        {
            Debug.WriteLine($"    └─ No tracks → SKIP");
            return;
        }

        foreach (var existingTrack in tracksAtPosition)
        {
            Debug.WriteLine($"    │  Existing track rot={existingTrack.Rotation:F1}°, new rot={newTrackRotation}°");
            
            if (ShouldCreateCross45(existingTrack.Rotation, newTrackRotation, out int targetRotation))
            {
                Debug.WriteLine($"    │  ✓ ShouldCreateCross45 = TRUE, targetRotation={targetRotation}°");
                
                if (!HasCross45At(expectedX, expectedY))
                {
                    Debug.WriteLine($"    │  ✓ No existing Cross45 → CREATING...");
                    CreateCross45At(expectedX, expectedY, targetRotation);
                }
                else
                {
                    Debug.WriteLine($"    │  ✗ Cross45 already exists → SKIP");
                }
                return;
            }
            else
            {
                Debug.WriteLine($"    │  ✗ ShouldCreateCross45 = FALSE (not in lookup table)");
            }
        }
        
        Debug.WriteLine($"    └─ No valid combination found");
    }

    /// <summary>
    /// Kontroluje, či už existuje Cross45 na danej pozícii.
    /// </summary>
    private bool HasCross45At(double snapX, double snapY)
    {
        return _vm.Elements.Any(el =>
            el.MarkerKey == "Cross45" &&
            Math.Abs(el.X - snapX) < Tolerance &&
            Math.Abs(el.Y - snapY) < Tolerance);
    }

    /// <summary>
    /// Vytvorí Cross45 marker na danej pozícii.
    /// </summary>
    private void CreateCross45At(double snapX, double snapY, int baseRotation)
    {
        Debug.WriteLine($"");
        Debug.WriteLine($"  ┌─────────────────────────────────────────────────────");
        Debug.WriteLine($"  │ CreateCross45At called");
        Debug.WriteLine($"  │ Position: ({snapX:F2}, {snapY:F2})");
        Debug.WriteLine($"  │ Rotation: {baseRotation}°");
        Debug.WriteLine($"  └─────────────────────────────────────────────────────");

        if (HasCross45At(snapX, snapY))
        {
            Debug.WriteLine($"  ⚠ Cross45 already exists at this position - skipping");
            return;
        }

        // Nájdeme existujúci TrackSegment na presnej pozícii - použijeme jeho X,Y
        var existingTrack = _vm.Elements.FirstOrDefault(el =>
            el.MarkerKey == "TrackSegment" &&
            Math.Abs(el.X - snapX) < Tolerance &&
            Math.Abs(el.Y - snapY) < Tolerance);

        double posX = existingTrack?.X ?? snapX;
        double posY = existingTrack?.Y ?? snapY;

        Debug.WriteLine($"  → Using position: ({posX:F2}, {posY:F2})");
        Debug.WriteLine($"  → Creating Cross45 element...");

        var cross45 = new TrackSegmentElement
        {
            MarkerKey = "Cross45",
            Label = "Cross45",
            X = posX,
            Y = posY,
            Rotation = baseRotation
        };

        // Odstránime len TrackSegmenty na PRESNEJ snap pozícii
        var tracksToRemove = _vm.Elements
            .Where(el => el.MarkerKey == "TrackSegment" &&
                        Math.Abs(el.X - snapX) < Tolerance &&
                        Math.Abs(el.Y - snapY) < Tolerance)
            .ToList();

        Debug.WriteLine($"  → Removing {tracksToRemove.Count} TrackSegments");
        
        foreach (var track in tracksToRemove)
        {
            Debug.WriteLine($"    • Removing TrackSegment at ({track.X:F2}, {track.Y:F2}), rot={track.Rotation:F1}°");
            _vm.Elements.Remove(track);
        }

        Debug.WriteLine($"  → Adding Cross45 to Elements collection");
        _vm.Elements.Add(cross45);
        
        Debug.WriteLine($"  ✅ Cross45 created successfully!");
        Debug.WriteLine($"");
    }

    /// <summary>
    /// Odstráni všetky Cross45 markery z layoutu.
    /// </summary>
    public void RemoveAllCross45()
    {
        var cross45Elements = _vm.Elements
            .Where(el => el.MarkerKey == "Cross45")
            .ToList();
        foreach (var cross in cross45Elements)
            _vm.Elements.Remove(cross);
    }
}



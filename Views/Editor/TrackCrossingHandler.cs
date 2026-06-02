using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Editor;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;

namespace TrackFlow.Views.Editor;

/// <summary>
/// Handler pre automatické vytváranie križovatiek pri krížení kolmých koľají.
/// 
/// KĽÚČOVÝ INSIGHT: 
///   LayoutEditorView.Cell = ViewModel.CellSize = 24 (snap mriežka = vizuálna bunka)
///   cellX z auto-insert = Math.Floor(pixelX / 24)
///   PlaceElementAt(cx*24 + 12, cy*24 + 12) → snappedX = Math.Floor((cx*24+12)/24)*24 = cx*24
///   Takže TrackSegment na bunke (cx,cy) má X = cx * 24, Y = cy * 24
/// </summary>
public class TrackCrossingHandler
{
    private const double CellSize = 24.0;  // Rovnaká ako LayoutEditorViewModel.CellSize
    private readonly LayoutEditorViewModel _vm;

    public TrackCrossingHandler(LayoutEditorViewModel viewModel)
    {
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
    
    /// <summary>
    /// Vizuálna bunka (cx,cy) → pixel pozícia TrackSegmentu.
    /// TrackSegment vložený na bunke cx má X = cx * CellSize.
    /// </summary>
    private static double CellToPixelX(int cellX) => cellX * CellSize;
    private static double CellToPixelY(int cellY) => cellY * CellSize;

    /// <summary>Tolerancia pre porovnanie pozícií (±1px).</summary>
    private const double Tolerance = 1.5;

    public void CheckAndCreateCrossings(List<(int cellX, int cellY)> lineCells, int trackRotation)
    {
        if (lineCells == null || lineCells.Count == 0)
            return;

        Debug.WriteLine($"=== CROSSING CHECK START ===");
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
                    bool perp = ArePerpendicularTracks(t.Rotation, trackRotation);
                    Debug.WriteLine($"    - X={t.X}, Y={t.Y}, Rot={t.Rotation}, perp={perp}");
                }
            }
            
            CheckCrossingAtCell(cellX, cellY, trackRotation);
        }
        
        Debug.WriteLine($"=== CROSSING CHECK END ===");
    }

    /// <summary>
    /// Kontrola pre dotyk koľají - keď nová koľaj začína alebo končí na diagonálnej koľaji.
    /// Volá sa pre PRVÚ a POSLEDNÚ bunku pri kreslení koľaje.
    /// </summary>
    public void CheckTouchCrossing(int cellX, int cellY, int newTrackRotation)
    {
        int normalizedNewRot = ((newTrackRotation % 360) + 360) % 360;
        
        // Kontrolujeme len ak je nová koľaj diagonálna (45°, 135°, 225°, 315°)
        if (!IsDiagonalAngle(normalizedNewRot))
            return;

        Debug.WriteLine($"=== TOUCH CROSSING CHECK ===");
        Debug.WriteLine($"  Cell ({cellX},{cellY}), newRotation={normalizedNewRot}");
        
        double expectedX = CellToPixelX(cellX);
        double expectedY = CellToPixelY(cellY);
        
        // Hľadáme existujúce TrackSegmenty na tejto pozícii
        var tracksHere = _vm.Elements
            .Where(el => el.MarkerKey == "TrackSegment" &&
                        Math.Abs(el.X - expectedX) < Tolerance &&
                        Math.Abs(el.Y - expectedY) < Tolerance)
            .ToList();
        
        if (tracksHere.Count == 0)
        {
            Debug.WriteLine($"  No existing tracks at ({cellX},{cellY})");
            return;
        }

        foreach (var existingTrack in tracksHere)
        {
            int existingRot = ((int)Math.Round(existingTrack.Rotation) % 360 + 360) % 360;
            
            // Kontrolujeme či existujúca koľaj je tiež diagonálna
            if (IsDiagonalAngle(existingRot))
            {
                Debug.WriteLine($"  Found diagonal track at ({cellX},{cellY}): existingRot={existingRot}");
                
                // Vytvoríme Cross90 ak tam ešte nie je
                if (!HasCross90At(expectedX, expectedY))
                {
                    Debug.WriteLine($"  >>> CREATING Touch Cross90 at ({expectedX},{expectedY})");
                    CreateCross90At(expectedX, expectedY, existingRot);
                }
                return;
            }
        }
    }
    
    private static bool IsDiagonalAngle(int angle)
    {
        return angle == 45 || angle == 135 || angle == 225 || angle == 315;
    }

    public void CheckAndCreateCrossings()
    {
        // Nepoužíva sa momentálne
    }
    
    public void RemoveAllCross90()
    {
        var cross90Elements = _vm.Elements
            .Where(el => el.MarkerKey == "Cross90")
            .ToList();
        foreach (var cross in cross90Elements)
            _vm.Elements.Remove(cross);
    }
    
    private static bool ArePerpendicularTracks(double rotation1, double rotation2)
    {
        int r1 = ((int)Math.Round(rotation1) % 360 + 360) % 360;
        int r2 = ((int)Math.Round(rotation2) % 360 + 360) % 360;
        int diff = Math.Abs(r1 - r2);
        if (diff > 180) diff = 360 - diff;
        return diff == 90;
    }

    private void CheckCrossingAtCell(int cellX, int cellY, int newTrackRotation)
    {
        double expectedX = CellToPixelX(cellX);
        double expectedY = CellToPixelY(cellY);
        
        // Hľadáme TrackSegmenty na PRESNEJ snap pozícii novej koľaje
        var tracksAtPosition = _vm.Elements
            .Where(el => el.MarkerKey == "TrackSegment" &&
                        Math.Abs(el.X - expectedX) < Tolerance &&
                        Math.Abs(el.Y - expectedY) < Tolerance)
            .ToList();

        if (tracksAtPosition.Count == 0)
            return;

        foreach (var existingTrack in tracksAtPosition)
        {
            if (ArePerpendicularTracks(existingTrack.Rotation, newTrackRotation))
            {
                if (!HasCross90At(expectedX, expectedY))
                {
                    int existingRotation = ((int)Math.Round(existingTrack.Rotation) % 360 + 360) % 360;
                    Debug.WriteLine($"  >>> CREATING Cross90 at snapPos=({expectedX},{expectedY}) - existing rot={existingRotation}, new rot={newTrackRotation}");
                    CreateCross90At(expectedX, expectedY, existingRotation);
                }
                return;
            }
        }
    }
    
    private bool HasCross90At(double snapX, double snapY)
    {
        return _vm.Elements.Any(el => 
            el.MarkerKey == "Cross90" &&
            Math.Abs(el.X - snapX) < Tolerance &&
            Math.Abs(el.Y - snapY) < Tolerance);
    }

    private void CreateCross90At(double snapX, double snapY, int baseRotation)
    {
        if (HasCross90At(snapX, snapY))
            return;
        
        // Nájdeme existujúci TrackSegment na presnej pozícii - použijeme jeho X,Y
        var existingTrack = _vm.Elements.FirstOrDefault(el =>
            el.MarkerKey == "TrackSegment" &&
            Math.Abs(el.X - snapX) < Tolerance &&
            Math.Abs(el.Y - snapY) < Tolerance);
        
        double posX = existingTrack?.X ?? snapX;
        double posY = existingTrack?.Y ?? snapY;
        
        Debug.WriteLine($"  >>> Cross90 final position: ({posX},{posY}), rot={baseRotation}");
        
        var cross90 = new TrackSegmentElement
        {
            MarkerKey = "Cross90",
            Label = "Cross90",
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
        
        Debug.WriteLine($"  >>> Removing {tracksToRemove.Count} TrackSegments at ({snapX},{snapY})");

        foreach (var track in tracksToRemove)
            _vm.Elements.Remove(track);

        _vm.Elements.Add(cross90);
    }
}

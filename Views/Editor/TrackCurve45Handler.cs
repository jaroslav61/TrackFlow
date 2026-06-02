using System.Collections.Generic;

namespace TrackFlow.Views.Editor;

/// <summary>
/// Handler pre automatické vytváranie 45° oblúkov pri napájaní koľají.
/// Odpoveda za výpočet správnej rotácie oblúku Curve_45 na základe uhlov susedných koľají.
/// </summary>
public class TrackCurve45Handler
{
    /// <summary>
    /// LOOKUP TABUĽKA pre oblúky (Curve_45).
    /// Kombinácie kde nová koľaj pokračuje plynulo (diffFromAdj == 45°).
    /// Formát: (adjacentAngle, trackAngle) → rotácia oblúku
    /// </summary>
    private static readonly Dictionary<(int adjAngle, int trackAngle), int> CurveLookup = new()
    {
        // Hard-coded kombinácie pre oblúky (overené používateľom 2026-04-09)
        [(0, 45)]     = 45,   // 0° → 45° = Oblúk @ 45°
        [(0, 135)]    = 0,    // 0° → 135° = Oblúk @ 0°  
        [(0, 225)]    = 225,  // 0° → 225° = Oblúk @ 225°  
        [(0, 315)]    = 180,  // 0° → 315° = Oblúk @ 180°
        [(45, 0)]     = 225,  // 45° → 0° = Oblúk @ 225°
        [(45, 180)]   = 45,   // 45° → 180° = Oblúk @ 45°
        [(90, 45)]    = 270,  // 90° → 45° = Oblúk @ 270°
        [(90, 135)]   = 135,  // 90° → 135° = Oblúk @ 135°
        [(90, 225)]   = 90,   // 90° → 225° = Oblúk @ 90°
        [(90, 315)]   = 315,  // 90° → 315° = Oblúk @ 315°
        [(135, 0)]    = 0,    // 135° → 0° = Oblúk @ 0°  
        [(135, 180)]  = 180,  // 135° → 180° = Oblúk @ 180°
        [(225, 0)]    = 225,  // 225° → 0° = Oblúk @ 225°
        [(225, 180)]  = 45,   // 225° → 180° = Oblúk @ 45° 
        [(180, 45)]   = 45,   // 180° → 45° = Oblúk @ 45°
        [(180, 135)]  = 0,    // 180° → 135° = Oblúk @ 0°
        [(180, 225)]  = 225,  // 180° → 225° = Oblúk @ 225°
        [(180, 315)]  = 180,  // 180° → 315° = Oblúk @ 180°
        [(270, 225)]  = 90,   // 270° → 225° = Oblúk @ 90°
        [(270, 45)]   = 270,  // 270° → 45° = Oblúk @ 270°
        [(315, 0)]    = 0,    // 315° → 0° = Oblúk @ 0°
        [(315, 180)]  = 180,  // 315° → 180° = Oblúk @ 180°
    };

    /// <summary>Vypočíta potrebnú rotáciu oblúku 45° pre napojenie dvoch koľají s rôznymi uhlami.</summary>
    public int CalculateCurve45Rotation(int fromAngle, int toAngle)
    {
        // Normalizujeme uhly na rozsah 0-360
        fromAngle = ((fromAngle % 360) + 360) % 360;
        toAngle = ((toAngle % 360) + 360) % 360;
        
        
        // Vyhľadáme v tabuľke
        var key = (fromAngle, toAngle);
        if (CurveLookup.TryGetValue(key, out var rotation))
        {
            return rotation;
        }
        
        return -1; // Nie je v tabuľke - nebudeme vkladať oblúk
    }
}



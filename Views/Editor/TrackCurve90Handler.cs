using System.Collections.Generic;

namespace TrackFlow.Views.Editor;

/// <summary>
/// Handler pre automatické vytváranie 90° oblúkov pri napájaní kolmých koľají.
/// Odpoveda za výpočet správnej rotácie oblúku Curve_90 na základe uhlov susedných koľají a pozície bumpera.
/// </summary>
public class TrackCurve90Handler
{
    /// <summary>
    /// LOOKUP TABUĽKA pre oblúky 90° (Curve_90).
    /// Používa sa pri napájaní kolmých koľají (0°-90°, 90°-180° atď.)
    /// Formát: (adjacentAngle, trackAngle, bumperPosition) → rotácia oblúku
    /// bumperPosition: "HB"=horný, "DB"=dolný, "PB"=pravý, "LB"=ľavý
    /// </summary>
    private static readonly Dictionary<(int adjAngle, int trackAngle, string bumperPos), int> Curve90Lookup = new()
    {
        // 0° → 90° (vodorovná doprava → kolmo nahor)
        [(0, 90, "PB")]   = 90,  // Pravý bumper = Oblúk @ 90°
        [(0, 90, "LB")]   = 0,    // Ľavý bumper = Oblúk @ 0°
        
        // 0° → 270° (vodorovná doprava → kolmo nadol)
        [(0, 270, "PB")]  = 180,  // Pravý bumper = Oblúk @ 180°
        [(0, 270, "LB")]  = 270,  // Ľavý bumper = Oblúk @ 270°
      
        // 180° → 90° (vodorovná doľava → kolmo nahor)
        [(180, 90, "PB")] = 0,   // Pravý bumper = Oblúk @ 90°
        [(180, 90, "LB")] = 90,    // Ľavý bumper = Oblúk @ 0°
        
        // 180° → 270° (vodorovná doľava → kolmo nadol)
        [(180, 270, "LB")] = 180, // Pravý bumper = Oblúk @ 180°
        [(180, 270, "PB")] = 270, // Ľavý bumper = Oblúk @ 270°
 
        // 90° → 0° (kolmo nahor → vodorovná doprava)
        [(90, 0, "HB")]    = 0,   // Horný bumper = Oblúk @ 0°
        [(90, 0, "DB")]    = 270, // Dolný bumper = Oblúk @ 270°
        
        // 90° → 180° (kolmo nahor → vodorovná doľava)
        [(90, 180, "HB")]  = 90,  // Horný bumper = Oblúk @ 90°
        [(90, 180, "DB")]  = 180, // Dolný bumper = Oblúk @ 180°
        
        // 270° → 0° (kolmo nadol → vodorovná doprava)
        [(270, 0, "DB")]   = 0,   // Horný bumper = Oblúk @ 0°
        [(270, 0, "HB")]   = 270, // Dolný bumper = Oblúk @ 270°
        
        // 270° → 180° (kolmo nadol → vodorovná doľava)
        [(270, 180, "DB")] = 90,  // Horný bumper = Oblúk @ 90°
        [(270, 180, "HB")] = 180, // Dolný bumper = Oblúk @ 180°
        
        // Šikmé napojenia (uhlopriečne oblúky s radius=24)
        // 45° → 135° (šikmá doprava hore → šikmá doľava hore)
        [(45, 135, "HB")]  = 135,
        [(45, 135, "DB")]  = 45,
        
        // 135° → 45° (šikmá doľava hore → šikmá doprava hore)
        [(135, 45, "HB")]  = 315,
        [(135, 45, "DB")]  = 45,
        
        // 135° → 225° (šikmá doľava hore → šikmá doľava dole)
        [(135, 225, "HB")] = 225,
        [(135, 225, "DB")] = 135,
        
        // 225° → 135° (šikmá doľava dole → šikmá doľava hore)
        [(225, 135, "HB")] = 45,
        [(225, 135, "DB")] = 135,
        
        // 225° → 315° (šikmá doľava dole → šikmá doprava dole)
        [(225, 315, "HB")] = 315,
        [(225, 315, "DB")] = 225,
        
        // 315° → 225° (šikmá doprava dole → šikmá doľava dole)
        [(315, 225, "HB")] = 135,
        [(315, 225, "DB")] = 225,
        
        // 315° → 45° (šikmá doprava dole → šikmá doprava hore)
        [(315, 45, "HB")]  = 45,
        [(315, 45, "DB")]  = 315,
        
        // 45° → 315° (šikmá doprava hore → šikmá doprava dole)
        [(45, 315, "HB")]  = 315,
        [(45, 315, "DB")]  = 345,
    };

    /// <summary>Vypočíta potrebnú rotáciu oblúku 90° pre napojenie kolmých koľají.</summary>
    public int CalculateCurve90Rotation(int fromAngle, int toAngle, string bumperPosition)
    {
        // Normalizujeme uhly na rozsah 0-360
        fromAngle = ((fromAngle % 360) + 360) % 360;
        toAngle = ((toAngle % 360) + 360) % 360;
        
        // Vyhľadáme v tabuľke
        var key = (fromAngle, toAngle, bumperPosition);
        if (Curve90Lookup.TryGetValue(key, out var rotation))
        {
            return rotation;
        }
        
        return -1; // Nie je v tabuľke - nebudeme vkladať oblúk
    }
}



namespace TrackFlow.Models.Layout;

/// <summary>Typ prvku koľajiska.</summary>
public enum LayoutElementType
{
    TrackSegment,   // priamy úsek
    Curve,          // oblúk 90°
    CurveNarrow,    // oblúk 45°
    Turnout,        // výhybka štandard (L/R)
    TurnoutCurve,   // oblúková výhybka (L/R)
    TurnoutY,       // Y-výhybka
    Turnout3W,      // 3-cestná výhybka
    TurnoutL90,     // výhybka ľavá 90°
    TurnoutR90,     // výhybka pravá 90°
    Cross90,        // križovatka 90°
    Cross45,        // križovatka 45°
    DoubleSlip,     // križovatková výhybka
    Bridge90,       // most 90°
    Bridge45L,      // most 45° ľavý
    Bridge45R,      // most 45° pravý
    Signal,         // návesť / signál
    Sensor,         // senzor obsadenosti
    Bumper,         // zarážadlo
    Block,          // blok (1x4 bunky)
    Route,          // cesta (route)
    Text,           // textový popis
}


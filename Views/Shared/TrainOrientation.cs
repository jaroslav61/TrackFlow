namespace TrackFlow.Views.Shared;

/// <summary>
/// Orientácia vykresľovaného vlaku v bloku. Kombinuje os bloku (horizontálna / vertikálna)
/// a smer hlavy lokomotívy (Forward = vpravo / dole, Backward = vľavo / hore).
/// </summary>
/// <remarks>
/// Nahrádza dvojicu booleanov <c>(isVertical, isForwardDir)</c>. Všetky 4 prípady sú
/// explicitné a kompilátor automaticky vylučuje nezmyselné kombinácie.
/// </remarks>
public enum TrainOrientation
{
    /// <summary>Horizontálny blok, hlava lokomotívy vpravo.</summary>
    HForward,
    /// <summary>Horizontálny blok, hlava lokomotívy vľavo.</summary>
    HBackward,
    /// <summary>Vertikálny blok, hlava lokomotívy dole.</summary>
    VDown,
    /// <summary>Vertikálny blok, hlava lokomotívy hore.</summary>
    VUp,
}

public static class TrainOrientationExtensions
{
    /// <summary>True ak je blok vertikálny (VDown / VUp).</summary>
    public static bool IsVertical(this TrainOrientation o) =>
        o == TrainOrientation.VDown || o == TrainOrientation.VUp;

    /// <summary>
    /// True ak hlava lokomotívy smeruje v "pozitívnom" smere osi bloku
    /// (horizontálny: vpravo; vertikálny: dole).
    /// </summary>
    public static bool IsForward(this TrainOrientation o) =>
        o == TrainOrientation.HForward || o == TrainOrientation.VDown;

    /// <summary>Vytvorí orientáciu z dvoch booleanov (zachová spätnú kompatibilitu).</summary>
    public static TrainOrientation From(bool isVertical, bool isForwardDir) =>
        (isVertical, isForwardDir) switch
        {
            (false, true)  => TrainOrientation.HForward,
            (false, false) => TrainOrientation.HBackward,
            (true,  true)  => TrainOrientation.VDown,
            (true,  false) => TrainOrientation.VUp,
        };
}


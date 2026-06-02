using TrackFlow.Models.Layout;

namespace TrackFlow.Extensions;

/// <summary>Rozšírenia pre preklad aspektov návestidiel do slovenčiny.</summary>
public static class SignalAspectExtensions
{
    /// <summary>
    /// Vráti slovenský názov aspektu návestidla.
    /// </summary>
    public static string ToSlovakName(this SignalAspect aspect) => aspect switch
    {
        SignalAspect.Stop           => "Stoj",
        SignalAspect.Proceed        => "Voľno",
        SignalAspect.Green          => "Voľno (zelená)",
        SignalAspect.Caution        => "Výstraha",
        SignalAspect.Yellow         => "Výstraha (žltá)",
        SignalAspect.SlowProceed    => "40 a Voľno",
        SignalAspect.SlowCaution    => "40 a Výstraha",
        SignalAspect.SlowExpect40   => "Očakávaj 40",
        SignalAspect.ShuntingPermitted => "Posun dovolený",
        SignalAspect.White          => "Posun dovolený (biela)",
        SignalAspect.Off            => "Vypnuté",
        _                           => aspect.ToString()
    };
}


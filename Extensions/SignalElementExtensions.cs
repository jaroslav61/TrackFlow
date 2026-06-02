using TrackFlow.Models.Layout;

namespace TrackFlow.Extensions;

/// <summary>
/// Extension metódy pre SignalElement.
/// </summary>
public static class SignalElementExtensions
{
    /// <summary>
    /// Vráti true, ak má návestidlo platnú DCC adresu (> 0).
    /// Návestidlá s neplatnou adresou nemôžu byť ovládané cez DCC centrálou.
    /// </summary>
    public static bool HasValidDccAddress(this SignalElement signal)
    {
        return signal != null && signal.DccAddress > 0;
    }
}


using System;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services.Signal;

/// <summary>
/// Stateless helper s čistou logikou výpočtu/klasifikácie návestidlových aspektov,
/// extrahovaný 1:1 z <see cref="TrackFlow.ViewModels.Operation.OperationViewModel"/>
/// (mechanický presun bez zmeny správania).
///
/// POZNÁMKA: Niektoré z týchto helperov majú ekvivalent v
/// <see cref="TrackFlow.Services.SignalController"/> (napr. <c>IsRestrictedAspect</c>).
/// Duplicita je v tejto fáze refaktoru ZÁMERNÁ – konsolidácia bude riešená až po
/// stabilizácii rozbíjania OperationViewModel.
/// </summary>
internal static class SignalAspectLogic
{
    public static bool IsRestrictedAspect(SignalAspect aspect)
        => aspect is SignalAspect.Caution or SignalAspect.SlowProceed or SignalAspect.SlowExpect40 or SignalAspect.SlowCaution;

    public static bool IsStopAspect(SignalAspect aspect)
        => aspect == SignalAspect.Stop;

    public static SignalAspect GetPermissiveAspect(SignalElement signal)
    {
        return signal.SignalProfile?.Contains("shunt", StringComparison.OrdinalIgnoreCase) == true
            ? SignalAspect.ShuntingPermitted
            : SignalAspect.Proceed;
    }

    public static SignalAspect SynthesizeLookAheadAspect(
        SignalElement signal,
        SignalAspect baseAspect,
        bool isStartSegment,
        bool isDiverged,
        bool warnNextStop)
    {
        if (isStartSegment && isDiverged && warnNextStop)
            return SignalAspect.SlowCaution;       // odbočka + výstraha na Stop

        if (isStartSegment && isDiverged)
            return SignalAspect.SlowProceed;       // odbočka bez výstrahy

        if (warnNextStop)
            return SignalAspect.Caution;           // rovno/vnútorný segment + výstraha

        if (baseAspect == SignalAspect.ShuntingPermitted || baseAspect == SignalAspect.White)
            return SignalAspect.ShuntingPermitted;

        return GetPermissiveAspect(signal);        // rovno bez výstrahy => Voľno
    }
}


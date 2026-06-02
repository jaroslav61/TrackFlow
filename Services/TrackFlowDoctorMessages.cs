using System;

namespace TrackFlow.Services;

/// <summary>
/// Centrálne textové šablóny pre Doctor okno.
///
/// Ak chceš upraviť slovník alebo štýl hlášok v Doktorovi,
/// začni práve v tejto triede.
/// </summary>
public static class TrackFlowDoctorMessages
{
    public static string NormalizeRouteLabel(string routeLabel)
    {
        if (string.IsNullOrWhiteSpace(routeLabel) || routeLabel == "-")
            return "žiadna cesta";

        var value = routeLabel.Trim();
        if (value.StartsWith("cesta [", StringComparison.OrdinalIgnoreCase))
            return "neznáma cesta";

        var looksTechnical = !value.Contains('→')
            && !value.Contains(' ')
            && (value.Contains('_') || value.Contains('-') || value.Length > 12 || char.IsLower(value[0]));

        return looksTechnical ? "neznáma cesta" : value;
    }

    public static string FormatReservationEngineSummary(string route, string current, string next, string action)
        => $"Rezervačné okno: {NormalizeRouteLabel(route)} | {current} → {next} | akcia: {action}";

    public static string FormatRouteSummary(string route, string state, string train)
        => $"Cesta {NormalizeRouteLabel(route)}: {state} (vlak {train})";

    public static string FormatBlockSummary(string block, string state, string train, string route)
        => $"Blok {block}: {state} | vlak {train} | cesta {NormalizeRouteLabel(route)}";

    public static string FormatTurnoutSummary(string turnout, string state, string position, string requestedBy, string owner)
    {
        var route = NormalizeRouteLabel(requestedBy);
        var ownerRoute = NormalizeRouteLabel(owner);
        var normalizedPosition = string.IsNullOrWhiteSpace(position) || position == "-" ? "neznáma poloha" : position;

        return state switch
        {
            "pripravená" => $"Výhybka {turnout} pripravená na smer {normalizedPosition} pre cestu {route}",
            "prestavená" => $"Výhybka {turnout} prestavená na smer {normalizedPosition} pre cestu {route}",
            "uvoľnená" => $"Výhybka {turnout} uvoľnená, aktuálna poloha {normalizedPosition}",
            "odmietnutá" => $"Výhybka {turnout} sa nedá prestaviť na smer {normalizedPosition} — vlastní ju {ownerRoute}",
            "sticky-odmietnutá" => $"Výhybka {turnout} čaká na uvoľnenie — prednosť má {ownerRoute}",
            "deadlock-yield-uvoľnená" => $"Výhybka {turnout} uvoľnená kvôli vyriešeniu patovej situácie, poloha {normalizedPosition}",
            _ => $"Výhybka {turnout}: {state} | poloha {normalizedPosition}"
        };
    }

    public static string FormatSignalSummary(string signal, string state, string aspect, string route, string train)
    {
        var routeLabel = NormalizeRouteLabel(route);

        return state switch
        {
            "stoj-pred-segmentom" => $"Návestidlo {signal} na STOJ pred ďalším úsekom (cesta {routeLabel})",
            "stoj-po-prejazde" => $"Návestidlo {signal} zhodené na STOJ po prejazde úseku (cesta {routeLabel})",
            "stoj-po-tail-clear" => $"Návestidlo {signal} zhodené na STOJ po uvoľnení chvosta vlaku",
            _ => $"Návestidlo {signal}: {state} | návesť {aspect} | vlak {train}"
        };
    }

    public static string FormatCleanupSummary(string route, string state, string reservations, string turnouts)
        => $"Uvoľnenie cesty {NormalizeRouteLabel(route)}: {state} | rezervácie {reservations} | výhybky {turnouts}";

    public static string FormatWaitSummary(string route, string resource, string state, string reason, string attempt)
        => $"Čakanie: cesta {NormalizeRouteLabel(route)} | prvok/blok {resource} | stav {state} | dôvod {reason} | pokus {attempt}";

    public static string FormatArbitrationSummary(string type, string resource, string winner)
        => $"Arbitráž: {type} {resource} → víťaz {NormalizeRouteLabel(winner)}";

    public static string FormatDeadlockSummary(string route, string waitsFor, string blockedBy, string state)
        => $"Patová situácia: cesta {NormalizeRouteLabel(route)} | čaká na {waitsFor} | blokuje {NormalizeRouteLabel(blockedBy)} | stav {state}";
}


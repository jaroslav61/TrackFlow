using System;
using System.Collections.Generic;
using Serilog;

namespace TrackFlow.Models.Layout;

/// <summary>
/// Celý model koľajiska – kolekcia prvkov + metadata.
/// Uložený v TrackFlowProject.Layout.
/// </summary>
public sealed class TrackLayout
{
    /// <summary>Verzia schémy modelu (pre migrácie).</summary>
    public int SchemaVersion { get; set; } = 3;

    /// <summary>Šírka plátna v pixeloch.</summary>
    public double CanvasWidth { get; set; } = 2000;

    /// <summary>Výška plátna v pixeloch.</summary>
    public double CanvasHeight { get; set; } = 1200;

    /// <summary>Aktuálny zoom faktor plátna.</summary>
    public double ZoomFactor { get; set; } = 1.0;

    /// <summary>Posun plátna X (ScrollViewer offset).</summary>
    public double PanX { get; set; }

    /// <summary>Posun plátna Y (ScrollViewer offset).</summary>
    public double PanY { get; set; }

    /// <summary>Všetky prvky koľajiska.</summary>
    public List<LayoutElement> Elements { get; set; } = new();

    /// <summary>Definované cesty (routes) pre automatiku.</summary>
    public List<RouteDefinition> Routes { get; set; } = new();

    /// <summary>Dostupné návestné sústavy v projekte.</summary>
    public List<SignalSystemDefinition> SignalSystems { get; set; } = new()
    {
        new SignalSystemDefinition
        {
            Id = SignalSystemDefinition.DefaultSystemId,
            Name = "Slovenská základná sústava",
            Kind = SignalingSystemKind.Slovak,
            SupportedHeadCounts = new() { 2, 3, 4, 5 }
        }
    };

    /// <summary>Jazdné plány (sekvencia ciest pre vlak).</summary>
    public List<TrainPlan> Plans { get; set; } = new();
}

/// <summary>Canonical smerové stringy pre route JSON.</summary>
public static class RouteDirection
{
    public const string Left = "Left";
    public const string Right = "Right";
    public const string Up = "Up";
    public const string Down = "Down";
    public const string LegacyForward = "Forward";
    public const string LegacyBackward = "Backward";

    public static bool IsValid(string? direction)
        => string.Equals(direction, Left, StringComparison.Ordinal)
           || string.Equals(direction, Right, StringComparison.Ordinal)
           || string.Equals(direction, Up, StringComparison.Ordinal)
           || string.Equals(direction, Down, StringComparison.Ordinal);

    public static string NormalizeOrDefault(string? direction, string defaultDirection, string context)
    {
        if (string.Equals(direction, LegacyForward, StringComparison.OrdinalIgnoreCase))
            return Right;

        if (string.Equals(direction, LegacyBackward, StringComparison.OrdinalIgnoreCase))
            return Left;

        if (IsValid(direction))
            return direction!;

        Log.Warning("Invalid route direction '{Direction}' in {Context}. Using default '{DefaultDirection}'.",
            direction ?? "<null>", context, defaultDirection);
        return defaultDirection;
    }
}

/// <summary>
/// Definícia cesty – zoznam blokov a výhybiek ktoré ju tvoria.
/// </summary>
public sealed class RouteDefinition
{
    private string _fromBlockDirection = RouteDirection.Right;
    private string _toBlockDirection = RouteDirection.Right;
    private string _startNavigationDirection = RouteDirection.Right;
    private string _safetyFallbackAspect = "Stop";
    private RouteDefinitionKind _kind = RouteDefinitionKind.UserDefinedRoute;

    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// <summary>ID štartovacieho bloku.</summary>
    public string FromBlockId { get; set; } = string.Empty;

    /// <summary>ID cieľového bloku.</summary>
    public string ToBlockId { get; set; } = string.Empty;

    /// <summary>
    /// Smer jazdy v počiatočnom bloku (Left/Right/Up/Down).
    /// </summary>
    public string FromBlockDirection
    {
        get => _fromBlockDirection;
        set => _fromBlockDirection = RouteDirection.NormalizeOrDefault(value, RouteDirection.Right, nameof(FromBlockDirection));
    }

    /// <summary>
    /// Smer jazdy v cieľovom bloku (Left/Right/Up/Down).
    /// </summary>
    public string ToBlockDirection
    {
        get => _toBlockDirection;
        set => _toBlockDirection = RouteDirection.NormalizeOrDefault(value, RouteDirection.Right, nameof(ToBlockDirection));
    }

    /// <summary>Smer odjazdu zo štartového bloku (Left/Right/Up/Down).</summary>
    public string StartNavigationDirection
    {
        get => _startNavigationDirection;
        set => _startNavigationDirection = RouteDirection.NormalizeOrDefault(value, RouteDirection.Right, nameof(StartNavigationDirection));
    }

    /// <summary>Zoznam návestidiel naviazaných na cestu (ID signal elementov).</summary>
    public List<string> RouteSignalIds { get; set; } = new();

    /// <summary>
    /// Typ definície: používateľská vlaková cesta alebo elementárna auto-generovaná cesta.
    /// </summary>
    public RouteDefinitionKind Kind
    {
        get => _kind;
        set => _kind = value;
    }

    /// <summary>
    /// Bezpečnostný fallback aspekt pri nejednoznačnosti (ukladaný ako string).
    /// Aktuálne povolená hodnota: Stop. Legacy hodnota Red sa pri načítaní normalizuje na Stop.
    /// </summary>
    public string SafetyFallbackAspect
    {
        get => _safetyFallbackAspect;
        set
        {
            if (string.Equals(value, "Stop", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Red", StringComparison.OrdinalIgnoreCase))
            {
                _safetyFallbackAspect = "Stop";
                return;
            }

            Log.Warning("Invalid SafetyFallbackAspect '{Aspect}' in route '{RouteId}'. Using default 'Stop'.", value ?? "<null>", Id);
            _safetyFallbackAspect = "Stop";
        }
    }

    /// <summary>Zoznam ID výhybiek a ich požadovaných polôh pri aktivácii cesty.</summary>
    public List<RouteTurnoutSetting> TurnoutSettings { get; set; } = new();

    /// <summary>Zoznam ID blokov ktoré cesta prechádza (vrátane start a end).</summary>
    public List<string> BlockIds { get; set; } = new();

    /// <summary>Zoznam ID všetkých prvkov koľajiska na ceste medzi blokmi (TrackSegment, Curve, Turnout, ...). Bez štart/cieľ blokov.</summary>
    public List<string> PathElementIds { get; set; } = new();

    /// <summary>Či bola cesta automaticky vygenerovaná.</summary>
    public bool IsAutoGenerated
    {
        get => Kind == RouteDefinitionKind.AutoGeneratedPath;
        set => Kind = value ? RouteDefinitionKind.AutoGeneratedPath : RouteDefinitionKind.UserDefinedRoute;
    }

    /// <summary>Či je cesta povolená (aktívna).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Farba zvýraznenia cesty (hex, napr. "#00D4AA").</summary>
    public string Color { get; set; } = "#00D4AA";

    /// <summary>Maximálna rýchlosť na ceste v km/h.</summary>
    public int MaxSpeed { get; set; } = 60;
}

/// <summary>Rozlíšenie medzi elementárnou auto-cestou a používateľskou vlakovou cestou.</summary>
public enum RouteDefinitionKind
{
    AutoGeneratedPath,
    UserDefinedRoute
}

/// <summary>Požadovaná poloha výhybky pre konkrétnu cestu.</summary>
public sealed class RouteTurnoutSetting
{
    public string TurnoutId { get; set; } = string.Empty;
    public TurnoutState RequiredState { get; set; }
}

/// <summary>Jazdný plán – pomenovaná sekvencia ciest pre jeden vlak.</summary>
public sealed class TrainPlan
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// <summary>ID lokomotívy priradenej k plánu.</summary>
    public string? LocoId { get; set; }

    /// <summary>Názov lokomotívy (pre zobrazenie).</summary>
    public string LocoName { get; set; } = string.Empty;

    /// <summary>Kroky plánu v poradí.</summary>
    public List<PlanStep> Steps { get; set; } = new();

    /// <summary>Opakovať plán v slučke.</summary>
    public bool IsLoop { get; set; }
}

/// <summary>Jeden krok jazdného plánu.</summary>
public sealed class PlanStep
{
    /// <summary>ID RouteDefinition ktorá sa má aktivovať.</summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>Čas čakania v stanici pred odchodom (sekundy).</summary>
    public int DwellTimeSeconds { get; set; } = 30;

    /// <summary>Rýchlosť na tomto úseku (km/h, 0 = použiť z cesty).</summary>
    public int SpeedKmh { get; set; }
}

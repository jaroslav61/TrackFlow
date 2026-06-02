using System.Collections.Generic;

namespace TrackFlow.Models.Layout;

/// <summary>
/// Typ návestnej sústavy. Rozšíriteľné o ďalšie medzinárodné sústavy.
/// </summary>
public enum SignalingSystemKind
{
    Slovak,
    Generic
}

/// <summary>
/// Definícia jedného návestného obrazca (aspektu) v rámci profilu sústavy.
/// </summary>
public sealed class SignalAspectDefinition
{
    /// <summary>Unikátne ID aspektu (napr. "Stop", "Caution", "Go", "Shunt").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Zobrazený názov aspektu v UI (napr. "Stoj!", "Výstraha", "Voľno").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Mapovanie na runtime enum aspektu signálu.</summary>
    public SignalAspect Aspect { get; set; }

    /// <summary>
    /// Názov marker assetu pre tento aspekt
    /// (napr. "signal_sk_3h_stop.png"). Prázdny = použiť farbu z Color.
    /// </summary>
    public string MarkerAssetName { get; set; } = string.Empty;

    /// <summary>Hex farba pre náhľad aspektu v UI (napr. "#E53935").</summary>
    public string Color { get; set; } = "#888888";
}

/// <summary>
/// Profil návestidla – počet svetelných hláv a zoznam platných aspektov.
/// </summary>
public sealed class SignalProfileDefinition
{
    /// <summary>Unikátne ID profilu (napr. "2-aspect", "3-aspect", "4-aspect", "5-aspect").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Ľudsky čitateľný popis profilu.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Počet svetelných hláv návestidla.</summary>
    public int HeadCount { get; set; }

    /// <summary>Zoznam aspektov dostupných pre tento profil (v poradí od najrestriktívnejšieho).</summary>
    public List<SignalAspectDefinition> Aspects { get; set; } = new();
}

/// <summary>
/// Definícia návestnej sústavy dostupnej v projekte.
/// </summary>
public sealed class SignalSystemDefinition
{
    public const string DefaultSystemId = "SK_DEFAULT";

    /// <summary>Unikátne ID sústavy v projekte.</summary>
    public string Id { get; set; } = DefaultSystemId;

    /// <summary>Zobrazený názov sústavy.</summary>
    public string Name { get; set; } = "Slovenská základná sústava";

    /// <summary>Typ sústavy pre runtime logiku.</summary>
    public SignalingSystemKind Kind { get; set; } = SignalingSystemKind.Slovak;

    /// <summary>Podporovaný počet svetelných hláv (2/3/4/5).</summary>
    public List<int> SupportedHeadCounts { get; set; } = new() { 2, 3, 4, 5 };

    /// <summary>Profily návestidiel definované pre túto sústavu.</summary>
    public List<SignalProfileDefinition> Profiles { get; set; } = new();
}


using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrackFlow.Models.Layout;

/// <summary>Typ indikátora v bloku.</summary>
public enum BlockIndicatorType
{
    Contact,    // Kontaktný indikátor
    Flagman,    // Flagman
    Virtual     // Virtuálny kontakt
}

/// <summary>
/// Indikátor v bloku - segment s detekciou obsadenia.
/// Každý blok môže mať viacero indikátorov rozdelených binárne.
/// </summary>
public sealed class BlockIndicator
{
    /// <summary>Unikátny identifikátor indikátora.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>Názov indikátora (napr. "Kontaktný indikátor Blok 1-1").</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Typ indikátora (Contact/Flagman/Virtual).</summary>
    public BlockIndicatorType Type { get; set; }
    
    /// <summary>Začiatok indikátora v cm od začiatku bloku.</summary>
    public int StartCm { get; set; }
    
    /// <summary>Koniec indikátora v cm od začiatku bloku.</summary>
    public int EndCm { get; set; }
    
    /// <summary>Adresa portu pre DCC/hardware komunikáciu.</summary>
    public string PortAddress { get; set; } = string.Empty;
    
    /// <summary>Popis indikátora (voliteľný).</summary>
    public string? Description { get; set; }
    
    // ── Pripojenie (pre Kontaktný indikátor) ───────────────────────────────
    /// <summary>Adresa modulu (pre Kontaktný indikátor).</summary>
    public int ModuleAddress { get; set; }
    
    /// <summary>Číslo portu na module (pre Kontaktný indikátor).</summary>
    public int PortNumber { get; set; }

    /// <summary>
    /// ID konkrétneho profilu DCC centrály v efektívnom zozname profilov
    /// (projektový override alebo AppSettingsData.DccCentralProfiles).
    /// null = „Bez pripojenia“.
    /// </summary>
    public Guid? DccCentralProfileId { get; set; }
    
    // ── Spúšťač (pre Flagman) ──────────────────────────────────────────────
    /// <summary>Typ spúšťača (pre Flagman).</summary>
    public string? TriggerType { get; set; }
    
    /// <summary>Podmienka spúšťača (pre Flagman).</summary>
    public string? TriggerCondition { get; set; }
    
    // ── Podmienky (pre Flagman a Virtuálny kontakt) ────────────────────────
    /// <summary>Výraz podmienky (pre Flagman a Virtuálny kontakt).</summary>
    public string? ConditionExpression { get; set; }
    
    // ── Operácie (pre všetkých) ────────────────────────────────────────────
    /// <summary>Skript operácií.</summary>
    public string? OperationsScript { get; set; }
    
    // ── Reset (pre všetkých) ───────────────────────────────────────────────
    /// <summary>Automatický reset indikátora.</summary>
    public bool AutoReset { get; set; } = true;
    
    /// <summary>Oneskorenie resetu v milisekundách.</summary>
    public int ResetDelayMs { get; set; } = 1000;
    
    /// <summary>Je indikátor aktuálne vybraný v editore?</summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// Runtime stav indikátora (true = aktívny / obsadený).
    /// Pre kontaktné indikátory sa nastavuje zo živej spätnej väzby S88/R-Bus.
    /// </summary>
    [JsonIgnore]
    public bool IsActive { get; set; }
    
    /// <summary>
    /// Markery patriace tomuto indikátoru.
    /// Každý marker má pozíciu RELATÍVNE k indikátoru (nie k bloku).
    /// </summary>
    public List<IndicatorMarker> Markers { get; set; } = new();
    
    /// <summary>Šírka indikátora v cm.</summary>
    public int WidthCm => EndCm - StartCm;
}


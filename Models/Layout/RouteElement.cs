using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrackFlow.Models.Layout;

/// <summary>
/// Prvok cesty (route) - definuje trasu vlaku cez koľajisko.
/// </summary>
public class RouteElement : LayoutElement
{
    [JsonIgnore]
    public override LayoutElementType ElementType => LayoutElementType.Route;

    /// <summary>Názov cesty.</summary>
    public string RouteName { get; set; } = string.Empty;

    /// <summary>Popis cesty.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Vyžiadať žltú (warning aspect).</summary>
    public bool RequestYellow { get; set; }

    /// <summary>Maximálna rýchlosť v km/h.</summary>
    public int MaxSpeed { get; set; } = 60;

    /// <summary>Obmedzená rýchlosť v km/h.</summary>
    public int LimitedSpeed { get; set; } = 40;

    /// <summary>Zoznam ID indikátorov priradených k ceste.</summary>
    public List<string> IndicatorIds { get; set; } = new();

    /// <summary>ID počiatočného bloku/bodu.</summary>
    public string? StartBlockId { get; set; }

    /// <summary>ID cieľového bloku/bodu.</summary>
    public string? EndBlockId { get; set; }

    /// <summary>Zoznam prvkov tvoriacich cestu (bloky, výhybky v správnej polohe).</summary>
    public List<RouteStep> Steps { get; set; } = new();

    /// <summary>ID vybratej RouteDefinition zo Správcu ciest, ktorú marker v operation režime spúšťa.</summary>
    public string? SelectedRouteDefinitionId { get; set; }
}

/// <summary>Jeden krok v ceste - prvok a jeho požadovaný stav.</summary>
public class RouteStep
{
    /// <summary>ID prvku (výhybka, blok, ...).</summary>
    public string ElementId { get; set; } = string.Empty;

    /// <summary>Požadovaný stav prvku (napr. "Straight", "Diverge").</summary>
    public string RequiredState { get; set; } = string.Empty;
}


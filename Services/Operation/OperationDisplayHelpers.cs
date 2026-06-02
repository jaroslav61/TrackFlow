using System;
using System.Linq;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services.Operation;

/// <summary>
/// Stateless helper s čistou logikou krátkych "display name" textov pre blok / návestidlo
/// a rezolúcie štart/cieľ block-id z <see cref="RouteDefinition"/>. Extrahované 1:1 z
/// <see cref="TrackFlow.ViewModels.Operation.OperationViewModel"/> – mechanický presun
/// bez zmeny správania.
///
/// POZNÁMKA: <c>SignalDisplayName</c> má identický ekvivalent v
/// <see cref="TrackFlow.Services.SignalController"/> (private static) a tiež je tu samostatne
/// definovaný helper <see cref="TrackFlow.Services.LayoutElementDisplayHelper"/> s odlišnou
/// (širšou) sémantikou. Konsolidácia je v aktuálnej fáze refaktoru ZÁMERNE odložená.
/// </summary>
internal static class OperationDisplayHelpers
{
    public static string BlockDisplayName(BlockElement block) =>
        !string.IsNullOrWhiteSpace(block.Label) ? block.Label : block.Id;

    public static string TurnoutStateDisplayName(TurnoutState state)
        => state switch
        {
            TurnoutState.Straight => "priamo",
            TurnoutState.Diverge => "do odbočky",
            TurnoutState.DivergeLeft => "do odbočky vľavo",
            TurnoutState.DivergeRight => "do odbočky vpravo",
            TurnoutState.Cross => "krížom",
            TurnoutState.Unknown => "neznámy",
            _ => state.ToString()
        };

    public static string SignalDisplayName(SignalElement signal) =>
        !string.IsNullOrWhiteSpace(signal.Label) ? signal.Label : signal.Id;

    
    public static string ResolveBlockDisplayName(TrackLayout layout, string? blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId))
            return "Neznámy blok";

        var block = layout.Elements.OfType<BlockElement>()
            .FirstOrDefault(b => string.Equals(b.Id, blockId, StringComparison.OrdinalIgnoreCase));
        return block != null ? BlockDisplayName(block) : blockId;
    }

    public static string ResolveRouteStartBlockId(RouteDefinition route)
        => !string.IsNullOrWhiteSpace(route.FromBlockId)
            ? route.FromBlockId
            : route.BlockIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? string.Empty;

    public static string ResolveRouteEndBlockId(RouteDefinition route)
        => !string.IsNullOrWhiteSpace(route.ToBlockId)
            ? route.ToBlockId
            : route.BlockIds.LastOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? string.Empty;
}


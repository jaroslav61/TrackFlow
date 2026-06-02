using System;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services.Operation;

/// <summary>
/// Stateless helper s čistou logikou rezolúcie aktuálneho bloku a fyzickej orientácie
/// lokomotívy z layoutu. Extrahované 1:1 z
/// <see cref="TrackFlow.ViewModels.Operation.OperationViewModel"/> – mechanický presun
/// bez zmeny správania.
/// </summary>
internal static class LocoStateResolver
{
    /// <summary>
    /// Rezolúcia fyzickej orientácie vybranej lokomotívy podľa <see cref="BlockElement.AssignedLocoIsForward"/>.
    /// Ak sa pre lokomotívu nenájde žiadny obsadený blok, vracia <c>true</c> (kompatibilné s pôvodnou OVM logikou).
    /// </summary>
    public static bool ResolveSelectedLocoPhysicalOrientation(IEnumerable<LayoutElement> elements, Locomotive loco)
    {
        var sourceBlock = elements
            .OfType<BlockElement>()
            .FirstOrDefault(b => string.Equals(b.AssignedLocoId, loco.Code, StringComparison.OrdinalIgnoreCase));

        if (sourceBlock != null)
            return sourceBlock.AssignedLocoIsForward;

        return true;
    }

    /// <summary>
    /// Rezolúcia aktuálneho block-id lokomotívy. Najskôr sa pokúsi použiť
    /// <see cref="Locomotive.AssignedBlockId"/>, ale iba ak je konzistentné s obsadením v layoute
    /// (môže byť stale – napr. v unit testoch). Ak nesedí, fallback na vyhľadanie
    /// bloku s <c>AssignedLocoId == loco.Code</c>.
    /// </summary>
    public static string? ResolveLocoCurrentBlockId(IEnumerable<LayoutElement> elements, Locomotive loco)
    {
        if (loco == null || string.IsNullOrWhiteSpace(loco.Code))
            return null;

        // AssignedBlockId môže byť stale (napr. v unit testoch). Použi ho iba ak sa zhoduje s obsadením layoutu.
        if (!string.IsNullOrWhiteSpace(loco.AssignedBlockId))
        {
            var assigned = elements
                .OfType<BlockElement>()
                .FirstOrDefault(b => string.Equals(b.Id, loco.AssignedBlockId, StringComparison.OrdinalIgnoreCase));
            if (assigned != null && string.Equals(assigned.AssignedLocoId, loco.Code, StringComparison.OrdinalIgnoreCase))
                return assigned.Id;
        }

        return elements
            .OfType<BlockElement>()
            .FirstOrDefault(b => string.Equals(b.AssignedLocoId, loco.Code, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }
}


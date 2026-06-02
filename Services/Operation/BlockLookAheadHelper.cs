using System;
using System.Collections.Generic;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services.Operation;

/// <summary>
/// Stateless helpers pre look-ahead analýzu blokov v driving order.
/// Mechanická 1:1 extrakcia z OperationViewModel (behavior-preserving).
/// </summary>
internal static class BlockLookAheadHelper
{
    /// <summary>
    /// Detekuje, či je ďalší blok v ceste (za targetBlockId) rezervovaný pre danú lokomotívu.
    /// </summary>
    public static bool IsNextBlockReservedForLoco(
        IReadOnlyList<string> drivingOrder,
        string currentBlockId,
        string locoCode,
        IReadOnlyDictionary<string, BlockElement> blocks)
    {
        if (drivingOrder == null || drivingOrder.Count < 2 || string.IsNullOrWhiteSpace(currentBlockId) || string.IsNullOrWhiteSpace(locoCode))
            return false;

        // Nájdi aktuálny blok v driving order (smer jazdy)
        var currentIndex = -1;
        for (int i = 0; i < drivingOrder.Count; i++)
        {
            if (string.Equals(drivingOrder[i], currentBlockId, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0 || currentIndex >= drivingOrder.Count - 1)
            return false;

        // Ďalší blok v DRIVING ORDER (smer jazdy)
        var nextBlockId = drivingOrder[currentIndex + 1];
        if (string.IsNullOrWhiteSpace(nextBlockId))
            return false;

        // Kontrola rezervácie ďalšieho bloku
        if (!blocks.TryGetValue(nextBlockId, out var nextBlock))
            return false;

        // Blok je rezervovaný pre túto lokomotívu (Shadow rezervácia)
        var isReserved = nextBlock.IsShadowSet
            && string.Equals(nextBlock.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase);

        return isReserved;
    }

    /// <summary>
    /// ÚLOHA 1: Look-ahead - vráti nasledujúce bloky v ceste pre danú lokomotívu.
    /// Zohľadňuje stavy IsOccupied, IsReserved a IsShadowSet.
    /// </summary>
    public static List<BlockLookAheadInfo> GetLookAheadBlocks(
        string locoCode,
        string currentBlockId,
        IReadOnlyList<string> drivingOrder,
        IReadOnlyDictionary<string, BlockElement> blocks,
        int lookAheadCount = 3)
    {
        var result = new List<BlockLookAheadInfo>();
        if (drivingOrder == null || drivingOrder.Count == 0 || string.IsNullOrWhiteSpace(currentBlockId))
            return result;

        var currentIndex = -1;
        for (int i = 0; i < drivingOrder.Count; i++)
        {
            if (string.Equals(drivingOrder[i], currentBlockId, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0)
            return result;

        // Pozri sa až do lookAheadCount blokov vpred
        for (int offset = 1; offset <= lookAheadCount && (currentIndex + offset) < drivingOrder.Count; offset++)
        {
            var blockId = drivingOrder[currentIndex + offset];
            if (string.IsNullOrWhiteSpace(blockId))
                continue;

            if (!blocks.TryGetValue(blockId, out var block))
                continue;

            // Kontrola stavu bloku
            var isReservedForLoco = block.IsShadowSet
                && string.Equals(block.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase);
            var isOccupiedByOther = block.IsOccupied
                && !string.Equals(block.AssignedLocoId, locoCode, StringComparison.OrdinalIgnoreCase);
            var isBlocked = !isReservedForLoco && (block.IsOccupied || block.IsLocked);

            result.Add(new BlockLookAheadInfo(
                block,
                offset,
                isReservedForLoco,
                isOccupiedByOther,
                isBlocked
            ));
        }

        return result;
    }
}

internal readonly record struct BlockLookAheadInfo(
    BlockElement Block,
    int Distance,           // Počet blokov od aktuálneho (1, 2, 3...)
    bool IsReservedForLoco,
    bool IsOccupiedByOther,
    bool IsBlocked          // Obsadený alebo zamknutý, nerezerováný pre túto loko
);


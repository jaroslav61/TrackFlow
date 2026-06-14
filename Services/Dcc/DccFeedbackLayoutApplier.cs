using System;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services.Dcc;

internal static class DccFeedbackLayoutApplier
{
    private readonly record struct FeedbackBindingKey(Guid? ProfileId, int ModuleAddress, int PortNumber);
    private sealed record FeedbackMatch(BlockElement Block, BlockIndicator Indicator);

    private static readonly Dictionary<FeedbackBindingKey, string> _bindingOwners = new();

    public static IReadOnlyList<BlockElement> ApplyFeedback(TrackLayout? layout, DccFeedbackStateChange change)
    {
        if (layout == null)
            return Array.Empty<BlockElement>();

        var allBlocks = layout.Elements.OfType<BlockElement>().ToList();
        var matches = allBlocks
            .SelectMany(block => block.Indicators
                .Where(static indicator => indicator.Type == BlockIndicatorType.Contact)
                .Where(indicator => MatchesIndicator(indicator, change))
                .Select(indicator => new FeedbackMatch(block, indicator)))
            .ToList();

        if (matches.Count == 0)
            return Array.Empty<BlockElement>();

        var targetMatches = ResolveTargetMatches(change, matches);
        var affectedBlocks = matches
            .Select(static match => match.Block)
            .Distinct()
            .ToList();

        var changedBlocks = new List<BlockElement>();
        foreach (var block in affectedBlocks)
        {
            bool blockChanged = false;

            foreach (var indicator in block.Indicators.Where(static indicator => indicator.Type == BlockIndicatorType.Contact))
            {
                var shouldApply = targetMatches.Any(match => ReferenceEquals(match.Indicator, indicator));
                if (!shouldApply)
                    continue;

                if (indicator.IsActive != change.IsActive)
                {
                    indicator.IsActive = change.IsActive;
                    blockChanged = true;
                }
            }

            bool occupiedByContacts = block.Indicators
                .Where(static i => i.Type == BlockIndicatorType.Contact)
                .Any(static i => i.IsActive);

            if (block.IsOccupied != occupiedByContacts)
            {
                block.IsOccupied = occupiedByContacts;
                blockChanged = true;
            }

            if (blockChanged)
                changedBlocks.Add(block);
        }

        return changedBlocks;
    }

    public static IReadOnlyList<BlockElement> ClearFeedbackState(
        TrackLayout? layout,
        Guid? profileId = null,
        bool clearAll = false)
    {
        if (layout == null)
            return Array.Empty<BlockElement>();

        var changedBlocks = new List<BlockElement>();

        foreach (var block in layout.Elements.OfType<BlockElement>())
        {
            bool blockChanged = false;

            foreach (var indicator in block.Indicators.Where(static indicator => indicator.Type == BlockIndicatorType.Contact))
            {
                if (!ShouldClearIndicator(indicator, profileId, clearAll))
                    continue;

                if (!indicator.IsActive)
                    continue;

                indicator.IsActive = false;
                blockChanged = true;
            }

            bool occupiedByContacts = block.Indicators
                .Where(static i => i.Type == BlockIndicatorType.Contact)
                .Any(static i => i.IsActive);

            if (block.IsOccupied != occupiedByContacts)
            {
                block.IsOccupied = occupiedByContacts;
                blockChanged = true;
            }

            if (blockChanged)
                changedBlocks.Add(block);
        }

        ClearBindingOwners(profileId, clearAll);
        return changedBlocks;
    }

    public static IReadOnlyList<BlockElement> SynchronizeOccupancyFromIndicators(TrackLayout? layout)
    {
        if (layout == null)
            return Array.Empty<BlockElement>();

        var changedBlocks = new List<BlockElement>();

        foreach (var block in layout.Elements.OfType<BlockElement>())
        {
            var occupiedByContacts = block.Indicators
                .Where(static indicator => indicator.Type == BlockIndicatorType.Contact)
                .Any(static indicator => indicator.IsActive);

            // Tento helper slúži na recovery po safety/mode resetoch, ktoré vynulovali
            // block.IsOccupied bez toho, aby sa zmazal runtime stav aktívneho kontaktu.
            // Zámerne teda robíme iba false -> true resync. Uvoľnenie obsadenia musí
            // naďalej prísť explicitne cez feedback/release pipeline, nie tichým sweepom.
            if (!occupiedByContacts || block.IsOccupied)
                continue;

            block.IsOccupied = true;
            changedBlocks.Add(block);
        }

        return changedBlocks;
    }

    private static IReadOnlyList<FeedbackMatch> ResolveTargetMatches(
        DccFeedbackStateChange change,
        IReadOnlyList<FeedbackMatch> matches)
    {
        var distinctBlocks = matches
            .Select(static match => match.Block)
            .Distinct()
            .ToList();

        if (distinctBlocks.Count <= 1)
            return matches;

        var key = new FeedbackBindingKey(change.ProfileId, change.ModuleAddress, change.PortNumber);

        if (!change.IsActive)
        {
            _bindingOwners.Remove(key);

            var activeMatches = matches.Where(static match => match.Indicator.IsActive).ToList();
            if (activeMatches.Count > 0)
            {
                return activeMatches;
            }

            return Array.Empty<FeedbackMatch>();
        }

        if (_bindingOwners.TryGetValue(key, out var ownerBlockId))
        {
            var ownerMatches = matches
                .Where(match => string.Equals(match.Block.Id, ownerBlockId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (ownerMatches.Count > 0)
                return ownerMatches;

            _bindingOwners.Remove(key);
        }

        var ownerBlock = ChooseOwnerBlock(distinctBlocks);
        if (ownerBlock == null)
        {
            return Array.Empty<FeedbackMatch>();
        }

        _bindingOwners[key] = ownerBlock.Id;

        return matches
            .Where(match => string.Equals(match.Block.Id, ownerBlock.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static BlockElement? ChooseOwnerBlock(IReadOnlyList<BlockElement> blocks)
    {
        var withOtherActiveContact = blocks
            .Where(HasOtherActiveContact)
            .ToList();
        if (withOtherActiveContact.Count == 1)
            return withOtherActiveContact[0];

        var occupiedBlocks = blocks
            .Where(static block => block.IsOccupied)
            .ToList();
        if (occupiedBlocks.Count == 1)
            return occupiedBlocks[0];

        var assignedBlocks = blocks
            .Where(block => !string.IsNullOrWhiteSpace(block.AssignedLocoId) || !string.IsNullOrWhiteSpace(block.ReservedLocoId))
            .ToList();
        if (assignedBlocks.Count == 1)
            return assignedBlocks[0];

        return null;
    }

    private static bool HasOtherActiveContact(BlockElement block)
        => block.Indicators
            .Where(static indicator => indicator.Type == BlockIndicatorType.Contact)
            .Count(static indicator => indicator.IsActive) > 0;

    private static string FormatBlocks(IEnumerable<BlockElement> blocks)
        => string.Join(", ",
            blocks.Select(block => string.IsNullOrWhiteSpace(block.Label)
                ? block.Id
                : $"{block.Label}({block.Id})"));

    private static bool MatchesIndicator(BlockIndicator indicator, DccFeedbackStateChange change)
    {
        if (indicator.ModuleAddress != change.ModuleAddress || indicator.PortNumber != change.PortNumber)
            return false;

        if (change.ProfileId.HasValue)
            return indicator.DccCentralProfileId == change.ProfileId;

        return indicator.DccCentralProfileId == null;
    }

    private static bool ShouldClearIndicator(BlockIndicator indicator, Guid? profileId, bool clearAll)
    {
        if (clearAll)
            return true;

        if (profileId.HasValue)
            return indicator.DccCentralProfileId == profileId;

        return indicator.DccCentralProfileId == null;
    }

    private static void ClearBindingOwners(Guid? profileId, bool clearAll)
    {
        if (_bindingOwners.Count == 0)
            return;

        if (clearAll)
        {
            _bindingOwners.Clear();
            return;
        }

        var keysToRemove = _bindingOwners.Keys
            .Where(key => key.ProfileId == profileId)
            .ToList();

        foreach (var key in keysToRemove)
            _bindingOwners.Remove(key);
    }
}


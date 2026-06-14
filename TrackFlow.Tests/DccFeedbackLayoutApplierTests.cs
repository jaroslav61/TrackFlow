using System;
using System.Linq;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services.Dcc;
using Xunit;

namespace TrackFlow.Tests;

public sealed class DccFeedbackLayoutApplierTests
{
    [Fact]
    public void ApplyFeedback_MatchingIndicator_UpdatesIndicatorAndBlockOccupancy()
    {
        var profileId = Guid.NewGuid();
        var block = new BlockElement
        {
            Id = "B1",
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = profileId,
                    ModuleAddress = 1,
                    PortNumber = 7,
                    IsActive = false
                },
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = profileId,
                    ModuleAddress = 1,
                    PortNumber = 8,
                    IsActive = false
                }
            }
        };

        var layout = new TrackLayout();
        layout.Elements.Add(block);

        var changed = DccFeedbackLayoutApplier.ApplyFeedback(
            layout,
            new DccFeedbackStateChange(profileId, DccCentralType.Z21Legacy, 1, 7, true));

        Assert.Single(changed);
        Assert.True(block.Indicators.Single(i => i.PortNumber == 7).IsActive);
        Assert.False(block.Indicators.Single(i => i.PortNumber == 8).IsActive);
        Assert.True(block.IsOccupied);
    }

    [Fact]
    public void ApplyFeedback_LastActiveContactClearsBlockOccupancy()
    {
        var profileId = Guid.NewGuid();
        var block = new BlockElement
        {
            Id = "B2",
            IsOccupied = true,
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = profileId,
                    ModuleAddress = 1,
                    PortNumber = 7,
                    IsActive = true
                }
            }
        };

        var layout = new TrackLayout();
        layout.Elements.Add(block);

        var changed = DccFeedbackLayoutApplier.ApplyFeedback(
            layout,
            new DccFeedbackStateChange(profileId, DccCentralType.Z21Legacy, 1, 7, false));

        Assert.Single(changed);
        Assert.False(block.Indicators[0].IsActive);
        Assert.False(block.IsOccupied);
    }

    [Fact]
    public void ApplyFeedback_DifferentProfile_DoesNotTouchIndicator()
    {
        var block = new BlockElement
        {
            Id = "B3",
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = Guid.NewGuid(),
                    ModuleAddress = 1,
                    PortNumber = 7,
                    IsActive = false
                }
            }
        };

        var layout = new TrackLayout();
        layout.Elements.Add(block);

        var changed = DccFeedbackLayoutApplier.ApplyFeedback(
            layout,
            new DccFeedbackStateChange(Guid.NewGuid(), DccCentralType.Z21Legacy, 1, 7, true));

        Assert.Empty(changed);
        Assert.False(block.Indicators[0].IsActive);
        Assert.False(block.IsOccupied);
    }

    [Fact]
    public void ApplyFeedback_DuplicateBinding_PrefersBlockWithAnotherActiveContact()
    {
        var profileId = Guid.NewGuid();
        var primaryBlock = new BlockElement
        {
            Id = "B4",
            Label = "Blok 4",
            IsOccupied = true,
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = profileId,
                    ModuleAddress = 1,
                    PortNumber = 7,
                    IsActive = true
                },
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = profileId,
                    ModuleAddress = 1,
                    PortNumber = 8,
                    IsActive = false
                }
            }
        };
        var duplicateBlock = new BlockElement
        {
            Id = "B1",
            Label = "Blok 1",
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = profileId,
                    ModuleAddress = 1,
                    PortNumber = 8,
                    IsActive = false
                }
            }
        };

        var layout = new TrackLayout();
        layout.Elements.Add(primaryBlock);
        layout.Elements.Add(duplicateBlock);

        var changed = DccFeedbackLayoutApplier.ApplyFeedback(
            layout,
            new DccFeedbackStateChange(profileId, DccCentralType.Z21Legacy, 1, 8, true));

        Assert.Single(changed);
        Assert.True(primaryBlock.Indicators.Single(i => i.PortNumber == 8).IsActive);
        Assert.False(duplicateBlock.Indicators.Single().IsActive);
        Assert.True(primaryBlock.IsOccupied);
        Assert.False(duplicateBlock.IsOccupied);
    }

    [Fact]
    public void ApplyFeedback_DuplicateBinding_ReleaseClearsAllPreviouslyActiveDuplicates()
    {
        var profileId = Guid.NewGuid();
        var blockA = new BlockElement
        {
            Id = "BA",
            IsOccupied = true,
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = profileId,
                    ModuleAddress = 1,
                    PortNumber = 8,
                    IsActive = true
                }
            }
        };
        var blockB = new BlockElement
        {
            Id = "BB",
            IsOccupied = true,
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = profileId,
                    ModuleAddress = 1,
                    PortNumber = 8,
                    IsActive = true
                }
            }
        };

        var layout = new TrackLayout();
        layout.Elements.Add(blockA);
        layout.Elements.Add(blockB);

        var changed = DccFeedbackLayoutApplier.ApplyFeedback(
            layout,
            new DccFeedbackStateChange(profileId, DccCentralType.Z21Legacy, 1, 8, false));

        Assert.Equal(2, changed.Count);
        Assert.False(blockA.Indicators.Single().IsActive);
        Assert.False(blockB.Indicators.Single().IsActive);
        Assert.False(blockA.IsOccupied);
        Assert.False(blockB.IsOccupied);
    }

    [Fact]
    public void ClearFeedbackState_ProfileMatch_ClearsActiveContactsAndBlockOccupancy()
    {
        var profileId = Guid.NewGuid();
        var block = new BlockElement
        {
            Id = "B-CLEAR-1",
            IsOccupied = true,
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = profileId,
                    ModuleAddress = 11,
                    PortNumber = 1,
                    IsActive = true
                }
            }
        };

        var layout = new TrackLayout();
        layout.Elements.Add(block);

        var changed = DccFeedbackLayoutApplier.ClearFeedbackState(layout, profileId);

        Assert.Single(changed);
        Assert.False(block.Indicators.Single().IsActive);
        Assert.False(block.IsOccupied);
    }

    [Fact]
    public void ClearFeedbackState_OtherProfile_DoesNotTouchForeignIndicator()
    {
        var targetProfileId = Guid.NewGuid();
        var foreignProfileId = Guid.NewGuid();
        var targetBlock = new BlockElement
        {
            Id = "B-CLEAR-2A",
            IsOccupied = true,
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = targetProfileId,
                    ModuleAddress = 1,
                    PortNumber = 1,
                    IsActive = true
                }
            }
        };
        var foreignBlock = new BlockElement
        {
            Id = "B-CLEAR-2B",
            IsOccupied = true,
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = foreignProfileId,
                    ModuleAddress = 2,
                    PortNumber = 1,
                    IsActive = true
                }
            }
        };

        var layout = new TrackLayout();
        layout.Elements.Add(targetBlock);
        layout.Elements.Add(foreignBlock);

        var changed = DccFeedbackLayoutApplier.ClearFeedbackState(layout, targetProfileId);

        var changedBlock = Assert.Single(changed);
        Assert.Same(targetBlock, changedBlock);
        Assert.False(targetBlock.Indicators.Single().IsActive);
        Assert.False(targetBlock.IsOccupied);
        Assert.True(foreignBlock.Indicators.Single().IsActive);
        Assert.True(foreignBlock.IsOccupied);
    }

    [Fact]
    public void ClearFeedbackState_ClearAll_ClearsStaleOccupancyAcrossAllProfiles()
    {
        var blockOld = new BlockElement
        {
            Id = "YY",
            IsOccupied = true,
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = Guid.NewGuid(),
                    ModuleAddress = 1,
                    PortNumber = 1,
                    IsActive = true
                }
            }
        };

        var blockNew = new BlockElement
        {
            Id = "XX",
            IsOccupied = true,
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = Guid.NewGuid(),
                    ModuleAddress = 2,
                    PortNumber = 1,
                    IsActive = true
                }
            }
        };

        var layout = new TrackLayout();
        layout.Elements.Add(blockOld);
        layout.Elements.Add(blockNew);

        var changed = DccFeedbackLayoutApplier.ClearFeedbackState(layout, clearAll: true);

        Assert.Equal(2, changed.Count);
        Assert.False(blockOld.Indicators.Single().IsActive);
        Assert.False(blockOld.IsOccupied);
        Assert.False(blockNew.Indicators.Single().IsActive);
        Assert.False(blockNew.IsOccupied);
    }

    [Fact]
    public void SynchronizeOccupancyFromIndicators_ActiveContactRestoresBlockOccupancy()
    {
        var block = new BlockElement
        {
            Id = "B-RESYNC-1",
            IsOccupied = false,
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    ModuleAddress = 1,
                    PortNumber = 7,
                    IsActive = true
                }
            }
        };

        var layout = new TrackLayout();
        layout.Elements.Add(block);

        var changed = DccFeedbackLayoutApplier.SynchronizeOccupancyFromIndicators(layout);

        var changedBlock = Assert.Single(changed);
        Assert.Same(block, changedBlock);
        Assert.True(block.IsOccupied);
    }
}


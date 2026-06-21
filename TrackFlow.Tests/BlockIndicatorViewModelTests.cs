using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Editor;
using Xunit;

namespace TrackFlow.Tests;

public sealed class BlockIndicatorViewModelTests
{
    [Fact]
    public void ContactIndicator_DefaultState_UsesDesaturatedIcon()
    {
        var indicator = new BlockIndicator
        {
            Type = BlockIndicatorType.Contact,
            IsActive = false
        };

        var vm = new BlockIndicatorViewModel(indicator, blocklengthMm: 1000);

        Assert.Equal("avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png", vm.IconPath);
    }

    [Fact]
    public void ContactIndicator_ActiveState_UsesColoredIcon()
    {
        var indicator = new BlockIndicator
        {
            Type = BlockIndicatorType.Contact,
            IsActive = true
        };

        var vm = new BlockIndicatorViewModel(indicator, blocklengthMm: 1000);

        Assert.Equal("avares://TrackFlow/Assets/Appicons/16/cont_ind.png", vm.IconPath);
    }
}


using System.Linq;
using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Editor;
using Xunit;

namespace TrackFlow.Tests;

public class BlockPropertiesViewModelDirectionalPersistenceTests
{
    [Fact]
    public void SaveCommand_UloziSmerovePriradeniaDoBloku_AZnovuSaNacitaju()
    {
        var block = new BlockElement { MarkerKey = "Block", Id = "blk_1" };
        var signals = new[]
        {
            new SignalElement { Id = "sig_left", Label = "L", MarkerKey = "Signal" },
            new SignalElement { Id = "sig_right", Label = "R", MarkerKey = "Signal" },
            new SignalElement { Id = "sig_up", Label = "U", MarkerKey = "Signal" },
            new SignalElement { Id = "sig_down", Label = "D", MarkerKey = "Signal" }
        };

        var vm = new BlockPropertiesViewModel(block, signals);
        vm.SelectedSignalLeft = vm.DirectionalSignalItems.First(x => x.Id == "sig_left");
        vm.SelectedSignalRight = vm.DirectionalSignalItems.First(x => x.Id == "sig_right");
        vm.SelectedSignalUp = vm.DirectionalSignalItems.First(x => x.Id == "sig_up");
        vm.SelectedSignalDown = vm.DirectionalSignalItems.First(x => x.Id == "sig_down");

        vm.SaveCommand.Execute(null);

        Assert.Equal("sig_left", block.SignalLeftId);
        Assert.Equal("sig_right", block.SignalRightId);
        Assert.Equal("sig_up", block.SignalUpId);
        Assert.Equal("sig_down", block.SignalDownId);

        var reopened = new BlockPropertiesViewModel(block, signals);
        Assert.Equal("sig_left", reopened.SelectedSignalLeft?.Id);
        Assert.Equal("sig_right", reopened.SelectedSignalRight?.Id);
        Assert.Equal("sig_up", reopened.SelectedSignalUp?.Id);
        Assert.Equal("sig_down", reopened.SelectedSignalDown?.Id);
    }

    [Fact]
    public void LoadFromBlock_JeCaseInsensitive_PreSignalId()
    {
        var block = new BlockElement
        {
            MarkerKey = "Block",
            SignalRightId = "SIG_RIGHT"
        };

        var signals = new[]
        {
            new SignalElement { Id = "sig_right", Label = "R", MarkerKey = "Signal" }
        };

        var vm = new BlockPropertiesViewModel(block, signals);

        Assert.Equal("sig_right", vm.SelectedSignalRight?.Id);
    }
}


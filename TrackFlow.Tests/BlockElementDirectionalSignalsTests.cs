using System.Linq;
using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Editor;
using Xunit;

namespace TrackFlow.Tests;

public class BlockElementDirectionalSignalsTests
{
    [Fact]
    public void GetSignalForDirection_ReturnsAssignedIds()
    {
        var block = new BlockElement
        {
            SignalLeftId = "sig-left",
            SignalRightId = "sig-right",
            SignalUpId = "sig-up",
            SignalDownId = "sig-down"
        };

        Assert.Equal("sig-left", block.GetSignalForDirection(NavigationDirection.Left));
        Assert.Equal("sig-right", block.GetSignalForDirection(NavigationDirection.Right));
        Assert.Equal("sig-up", block.GetSignalForDirection(NavigationDirection.Up));
        Assert.Equal("sig-down", block.GetSignalForDirection(NavigationDirection.Down));
    }

    [Fact]
    public void GetSignalForDirection_ResolvesSignalElementFromCollection()
    {
        var block = new BlockElement { SignalRightId = "s2" };
        var signals = new[]
        {
            new SignalElement { Id = "s1", Label = "S1" },
            new SignalElement { Id = "s2", Label = "S2" }
        };

        var resolved = block.GetSignalForDirection(NavigationDirection.Right, signals);

        Assert.NotNull(resolved);
        Assert.Equal("s2", resolved!.Id);
    }

    [Theory]
    [InlineData(90, false)]  // pätka vľavo -> svetlá smerujú doprava (OK pre Right)
    [InlineData(270, true)]  // pätka vpravo -> svetlá smerujú doľava (warning pre Right)
    public void BlockPropertiesViewModel_ValidatesDirectionalSignalOrientation(double rotation, bool expectWarning)
    {
        var signal = new SignalElement
        {
            Id = "sig-right",
            Label = "R1",
            Rotation = rotation
        };

        var block = new BlockElement();
        var vm = new BlockPropertiesViewModel(block, new[] { signal });

        var selected = vm.DirectionalSignalItems.FirstOrDefault(x => x.Id == "sig-right");
        vm.SelectedSignalRight = selected;

        Assert.Equal(expectWarning, vm.HasSignalDirectionWarning);
    }
}


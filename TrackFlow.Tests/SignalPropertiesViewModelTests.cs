using System.Collections.Generic;
using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Editor;
using Xunit;

namespace TrackFlow.Tests;

public class SignalPropertiesViewModelTests
{
    [Fact]
    public void DecoderMode_DefaultsToBasic()
    {
        var vm = CreateVm();

        Assert.True(vm.IsBasicMode);
        Assert.False(vm.IsExtendedMode);
    }

    [Fact]
    public void DecoderMode_ChangingBasic_RaisesPropertyChangedForExtended()
    {
        var vm = CreateVm();
        var changed = new List<string>();

        vm.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
                changed.Add(e.PropertyName!);
        };

        vm.IsBasicMode = false;

        Assert.False(vm.IsBasicMode);
        Assert.True(vm.IsExtendedMode);
        Assert.Contains(nameof(SignalPropertiesViewModel.IsBasicMode), changed);
        Assert.Contains(nameof(SignalPropertiesViewModel.IsExtendedMode), changed);
    }

    [Fact]
    public void DecoderMode_LoadsFromSignalElement()
    {
        var signal = new SignalElement { MarkerKey = "Signal", IsBasicMode = false };
        var vm = new SignalPropertiesViewModel(signal, new List<BlockElement>(), new List<SignalSystemDefinition>());

        Assert.False(vm.IsBasicMode);
        Assert.True(vm.IsExtendedMode);
    }

    [Fact]
    public void Save_PersistsDecoderModeToSignalElement()
    {
        var signal = new SignalElement { MarkerKey = "Signal", IsBasicMode = true };
        var vm = new SignalPropertiesViewModel(signal, new List<BlockElement>(), new List<SignalSystemDefinition>());

        vm.IsBasicMode = false;
        vm.SaveCommand.Execute(null);

        Assert.False(signal.IsBasicMode);
    }

    private static SignalPropertiesViewModel CreateVm()
    {
        var signal = new SignalElement { MarkerKey = "Signal" };
        var blocks = System.Array.Empty<BlockElement>();
        var systems = System.Array.Empty<SignalSystemDefinition>();
        return new SignalPropertiesViewModel(signal, blocks, systems);
    }
}


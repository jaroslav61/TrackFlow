using System;
using System.Linq;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.ViewModels.Editor;
using Xunit;

namespace TrackFlow.Tests;

public sealed class IndicatorPropertiesViewModelTests
{
    [Fact]
    public void Constructor_LoadsConfiguredDccCentrals_AndPreselectsIndicatorProfile()
    {
        var disabledProfile = new DccCentralProfile
        {
            Id = Guid.NewGuid(),
            IsEnabled = false,
            Type = DccCentralType.Z21Legacy,
            Host = "192.168.0.10",
            Port = 21105
        };

        var selectedProfile = new DccCentralProfile
        {
            Id = Guid.NewGuid(),
            Type = DccCentralType.NanoX_S88,
            SerialPort = "COM7",
            BaudRate = 19200
        };

        var manager = new SettingsManager();
        manager.LoadApp();
        manager.NewProject();
        manager.App.DccCentralProfiles.Add(disabledProfile);
        manager.App.DccCentralProfiles.Add(selectedProfile);

        var indicator = new BlockIndicator
        {
            Type = BlockIndicatorType.Contact,
            DccCentralProfileId = selectedProfile.Id,
            ModuleAddress = 8,
            PortNumber = 2
        };

        var vm = new IndicatorPropertiesViewModel(indicator, manager);

        Assert.Equal(2, vm.DccSystems.Count);
        Assert.Equal("Bez pripojenia", vm.DccSystems[0].Name);
        Assert.DoesNotContain(vm.DccSystems, x => x.ProfileId == disabledProfile.Id);
        Assert.Equal(selectedProfile.Id, vm.SelectedDccSystem?.ProfileId);
        Assert.Equal(8, vm.ModuleAddress);
        Assert.Equal(2, vm.PortNumber);
    }

    [Fact]
    public void SaveCommand_PersistsSelectedCentral_ModuleAndPort_ToIndicatorModel()
    {
        var selectedProfile = new DccCentralProfile
        {
            Id = Guid.NewGuid(),
            Type = DccCentralType.NanoX_S88,
            SerialPort = "COM5",
            BaudRate = 19200
        };

        var manager = new SettingsManager();
        manager.LoadApp();
        manager.NewProject();
        manager.App.DccCentralProfiles.Add(selectedProfile);

        var indicator = new BlockIndicator
        {
            Type = BlockIndicatorType.Contact
        };

        var vm = new IndicatorPropertiesViewModel(indicator, manager);
        vm.SelectedDccSystem = Assert.Single(vm.DccSystems, x => x.ProfileId == selectedProfile.Id);
        vm.ModuleAddress = 12;
        vm.PortNumber = 3;

        vm.SaveCommand.Execute(null);

        Assert.Equal(selectedProfile.Id, indicator.DccCentralProfileId);
        Assert.Equal(12, indicator.ModuleAddress);
        Assert.Equal(3, indicator.PortNumber);
    }

    [Theory]
    [InlineData(false, "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png")]
    [InlineData(true, "avares://TrackFlow/Assets/Appicons/16/cont_ind.png")]
    public void ContactIndicator_TestIconPath_UsesExpectedAssetUri(bool isActive, string expectedIcon)
    {
        var indicator = new BlockIndicator
        {
            Type = BlockIndicatorType.Contact,
            IsActive = isActive
        };

        using var vm = new IndicatorPropertiesViewModel(indicator);

        Assert.Equal(isActive, vm.IsTestIndicatorActive);
        Assert.Equal(expectedIcon, vm.TestIconPath);
    }

    [Fact]
    public void ContactIndicator_DuplicateBinding_ShowsWarning_AndDisablesSave()
    {
        var profileId = Guid.NewGuid();
        var manager = new SettingsManager();
        manager.LoadApp();
        manager.NewProject();
        manager.App.DccCentralProfiles.Add(new DccCentralProfile
        {
            Id = profileId,
            IsEnabled = true,
            Type = DccCentralType.Z21,
            Host = "192.168.0.111",
            Port = 21105
        });

        var existingBlock = new BlockElement
        {
            Id = "B1",
            Label = "Blok 1",
            Indicators =
            {
                new BlockIndicator
                {
                    Id = Guid.NewGuid(),
                    Name = "Kontaktný indikátor Blok 1-1",
                    Type = BlockIndicatorType.Contact,
                    DccCentralProfileId = profileId,
                    ModuleAddress = 1,
                    PortNumber = 8
                }
            }
        };
        manager.CurrentProject!.Layout.Elements.Add(existingBlock);

        var editedIndicator = new BlockIndicator
        {
            Id = Guid.NewGuid(),
            Type = BlockIndicatorType.Contact
        };

        var vm = new IndicatorPropertiesViewModel(editedIndicator, manager);
        vm.SelectedDccSystem = Assert.Single(vm.DccSystems, x => x.ProfileId == profileId);
        vm.ModuleAddress = 1;
        vm.PortNumber = 8;

        Assert.True(vm.HasContactBindingWarning);
        Assert.False(vm.CanSave);
        Assert.Contains("Blok 1", vm.ContactBindingWarning);

        vm.SaveCommand.Execute(null);

        Assert.Null(editedIndicator.DccCentralProfileId);
        Assert.Equal(0, editedIndicator.ModuleAddress);
        Assert.Equal(0, editedIndicator.PortNumber);
    }
}



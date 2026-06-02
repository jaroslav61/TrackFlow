using System;
using System.Linq;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.ViewModels.Library;
using Xunit;

namespace TrackFlow.Tests;

public sealed class LocomotivesWindowViewModelDccCentralFilteringTests
{
    [Fact]
    public void Constructor_LoadsOnlyEnabledDigitalSystems_AndMarksAvailabilityAccordingly()
    {
        var disabledProfile = new DccCentralProfile
        {
            Id = Guid.NewGuid(),
            IsEnabled = false,
            Type = DccCentralType.Z21Legacy,
            Host = "192.168.0.10",
            Port = 21105
        };

        var enabledProfile = new DccCentralProfile
        {
            Id = Guid.NewGuid(),
            IsEnabled = true,
            Type = DccCentralType.NanoX_S88,
            SerialPort = "COM4",
            BaudRate = 19200
        };

        var settings = new SettingsManager();
        settings.LoadApp();
        settings.App.DccCentralProfiles.Clear();
        settings.App.DccCentralProfiles.Add(disabledProfile);
        settings.App.DccCentralProfiles.Add(enabledProfile);

        var vm = new LocomotivesWindowViewModel(settings);

        Assert.True(vm.HasConfiguredDigitalSystems);
        Assert.Equal(2, vm.DigitalSystems.Count);
        Assert.Equal(Guid.Empty, vm.DigitalSystems[0].Id);
        Assert.DoesNotContain(vm.DigitalSystems, x => x.Id == disabledProfile.Id);
        Assert.Contains(vm.DigitalSystems, x => x.Id == enabledProfile.Id);
    }

    [Fact]
    public void Constructor_WhenOnlyDisabledProfilesExist_HasNoConfiguredDigitalSystems()
    {
        var settings = new SettingsManager();
        settings.LoadApp();
        settings.App.DccCentralProfiles.Clear();
        settings.App.DccCentralProfiles.Add(new DccCentralProfile
        {
            Id = Guid.NewGuid(),
            IsEnabled = false,
            Type = DccCentralType.Z21Legacy,
            Host = "192.168.0.10",
            Port = 21105
        });

        var vm = new LocomotivesWindowViewModel(settings);

        Assert.False(vm.HasConfiguredDigitalSystems);
        var onlyItem = Assert.Single(vm.DigitalSystems);
        Assert.Equal(Guid.Empty, onlyItem.Id);
        Assert.Equal("Bez pripojenia", onlyItem.DisplayText);
    }
}


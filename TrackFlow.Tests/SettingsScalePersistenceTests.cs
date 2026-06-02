using System;
using System.IO;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.ViewModels.Settings;
using Xunit;

namespace TrackFlow.Tests;

public sealed class SettingsScalePersistenceTests
{
    [Fact]
    public void Save_And_Load_Preserves_DefaultScale()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();

            var vm = new SettingsViewModel(manager)
            {
                UseProjectForScale = false,
                Scale = "TT"
            };

            Assert.True(vm.Save());

            var reloadedManager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            reloadedManager.LoadApp();

            Assert.Equal("TT", reloadedManager.App.DefaultScale);

            var reloadedVm = new SettingsViewModel(reloadedManager);
            Assert.Equal("TT", reloadedVm.Scale);
        }
        finally
        {
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad-value")]
    public void Load_Normalizes_BlankOrInvalid_DefaultScale_ToH0(string? persistedScale)
    {
        var manager = new SettingsManager();
        manager.App.DefaultScale = persistedScale!;

        var vm = new SettingsViewModel(manager);

        Assert.Equal("H0", vm.Scale);
        Assert.Contains(vm.ScaleItems, x => x.Code == "H0");
    }

    [Fact]
    public void Save_And_Load_Preserves_ProjectScaleOverride()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");
        var projectPath = Path.Combine(Path.GetTempPath(), $"trackflow-project-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();
            manager.NewProject();
            Assert.True(manager.SaveProjectAs(projectPath));

            var vm = new SettingsViewModel(manager)
            {
                UseProjectForScale = true,
                Scale = "N"
            };

            Assert.True(vm.Save());

            var reloadedManager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            reloadedManager.LoadApp();
            reloadedManager.OpenProject(projectPath);

            var reloadedVm = new SettingsViewModel(reloadedManager);
            Assert.True(reloadedVm.UseProjectForScale);
            Assert.Equal("N", reloadedVm.Scale);
        }
        finally
        {
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
            if (File.Exists(projectPath))
                File.Delete(projectPath);
        }
    }

    [Fact]
    public void Changing_Scale_Marks_Project_Dirty_When_Project_Is_Open()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), $"trackflow-project-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager();
            manager.LoadApp();
            manager.NewProject();
            Assert.True(manager.SaveProjectAs(projectPath));

            Assert.NotNull(manager.CurrentProject);
            Assert.False(manager.CurrentProject!.IsDirty);

            var vm = new SettingsViewModel(manager);

            // act
            vm.Scale = "TT";

            // assert
            Assert.True(manager.CurrentProject!.IsDirty);
        }
        finally
        {
            if (File.Exists(projectPath))
                File.Delete(projectPath);
        }
    }

    [Fact]
    public void Changing_Scale_Marks_Project_Dirty_For_New_Unsaved_Project()
    {
        var manager = new SettingsManager();
        manager.LoadApp();
        manager.NewProject();

        Assert.NotNull(manager.CurrentProject);
        Assert.Null(manager.CurrentProjectPath);
        Assert.False(manager.CurrentProject!.IsDirty);

        var vm = new SettingsViewModel(manager);
        vm.Scale = "N";

        Assert.True(manager.CurrentProject!.IsDirty);
    }

    [Theory]
    [InlineData(0.25, 1.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(3.0, 3.0)]
    [InlineData(5.0, 5.0)]
    [InlineData(9.0, 5.0)]
    public void ProjectSettings_Normalizes_SimulationSpeedFactor_ToAllowedRange(double value, double expected)
    {
        var settings = new ProjectSettingsData { SimulationSpeedFactor = value };

        Assert.Equal(expected, settings.SimulationSpeedFactor, precision: 3);
    }

    [Fact]
    public void Changing_SimulationSpeedFactor_Updates_ProjectSettings_Immediately()
    {
        var manager = new SettingsManager();
        manager.LoadApp();
        manager.NewProject();

        Assert.NotNull(manager.CurrentProject);
        Assert.False(manager.CurrentProject!.IsDirty);

        var vm = new SettingsViewModel(manager);
        vm.SimulationSpeedFactor = 4.2;

        Assert.Equal(4.2, manager.CurrentProject.Settings.SimulationSpeedFactor, precision: 3);
        Assert.True(manager.CurrentProject.IsDirty);
    }

    [Fact]
    public void Save_And_Load_Preserves_ProjectSimulationSpeedFactor()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");
        var projectPath = Path.Combine(Path.GetTempPath(), $"trackflow-project-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();
            manager.NewProject();
            Assert.True(manager.SaveProjectAs(projectPath));

            var vm = new SettingsViewModel(manager)
            {
                SimulationSpeedFactor = 4.5
            };

            Assert.True(vm.Save());

            var reloadedManager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            reloadedManager.LoadApp();
            reloadedManager.OpenProject(projectPath);

            var reloadedVm = new SettingsViewModel(reloadedManager);
            Assert.Equal(4.5, reloadedManager.CurrentProject!.Settings.SimulationSpeedFactor, precision: 3);
            Assert.Equal(4.5, reloadedVm.SimulationSpeedFactor, precision: 3);
        }
        finally
        {
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
            if (File.Exists(projectPath))
                File.Delete(projectPath);
        }
    }

    [Fact]
    public void Selecting_NanoX_S88_Switches_ViewModel_ToSerialSettings()
    {
        var manager = new SettingsManager();
        manager.LoadApp();

        var vm = new SettingsViewModel(manager)
        {
            DccCentralType = DccCentralType.NanoX_S88
        };

        Assert.True(vm.UsesSerialConnectionSettings);
        Assert.False(vm.UsesNetworkConnectionSettings);
        Assert.Contains(19200, vm.AvailableBaudRates);
    }

    [Fact]
    public void Save_And_Load_Preserves_DefaultNanoXSerialSettings()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();

            var vm = new SettingsViewModel(manager)
            {
                UseProjectForDcc = false,
                DccCentralType = DccCentralType.NanoX_S88,
                DccCentralSerialPort = "COM7",
                DccCentralBaudRate = 19200
            };

            Assert.True(vm.Save());

            var reloadedManager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            reloadedManager.LoadApp();

            Assert.Equal(DccCentralType.NanoX_S88, reloadedManager.App.DefaultDccCentralType);
            Assert.Equal("COM7", reloadedManager.App.DefaultDccSerialPort);
            Assert.Equal(19200, reloadedManager.App.DefaultDccBaudRate);

            var reloadedVm = new SettingsViewModel(reloadedManager);
            Assert.Equal(DccCentralType.NanoX_S88, reloadedVm.DccCentralType);
            Assert.Equal("COM7", reloadedVm.DccCentralSerialPort);
            Assert.Equal(19200, reloadedVm.DccCentralBaudRate);
            Assert.True(reloadedVm.UsesSerialConnectionSettings);
        }
        finally
        {
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
        }
    }
}



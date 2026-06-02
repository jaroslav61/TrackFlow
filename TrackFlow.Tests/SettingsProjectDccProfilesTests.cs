using System;
using System.IO;
using System.Threading.Tasks;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.ViewModels.Settings;
using Xunit;

namespace TrackFlow.Tests;

public sealed class SettingsProjectDccProfilesTests
{
    [Fact]
    public void Load_UsesProjectLegacyDccOverrides_WhenProjectFieldsArePresent()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");
        var projectPath = Path.Combine(Path.GetTempPath(), $"trackflow-project-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();
            manager.NewProject();
            Assert.True(manager.SaveProjectAs(projectPath));

            manager.Project!.DccCentralType = DccCentralType.NanoX_S88;
            manager.Project.DccSerialPort = "COM4";
            manager.Project.DccBaudRate = 19200;
            manager.Project.AutoConnect = true;

            var vm = new SettingsViewModel(manager);

            Assert.True(vm.UseProjectForDcc);
            Assert.Equal(DccCentralType.NanoX_S88, vm.DccCentralType);
            Assert.Equal("COM4", vm.DccCentralSerialPort);
            Assert.Equal(19200, vm.DccCentralBaudRate);
            Assert.True(vm.AutoConnect);
            Assert.Empty(vm.ConfiguredCentrals);
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
    public void Save_PersistsConfiguredProfilesToAppSettings_AndSelectedProfile()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();
            manager.NewProject();

            var z21 = new DccCentralProfile
            {
                Id = Guid.NewGuid(),
                Type = DccCentralType.Z21Legacy,
                Host = "192.168.0.50",
                Port = 21105
            };

            var nanoX = new DccCentralProfile
            {
                Id = Guid.NewGuid(),
                Type = DccCentralType.NanoX_S88,
                SerialPort = "COM7",
                BaudRate = 19200,
                AutoConnect = true
            };

            var vm = new SettingsViewModel(manager);
            vm.ConfiguredCentrals.Add(new ConfiguredDccCentralItem(z21, 1));
            vm.ConfiguredCentrals.Add(new ConfiguredDccCentralItem(nanoX, 2));
            vm.SelectedConfiguredCentral = vm.ConfiguredCentrals[1];

            Assert.True(vm.Save());

            Assert.Equal(2, manager.App.DccCentralProfiles.Count);
            Assert.Equal(z21.Id, manager.App.DccCentralProfiles[0].Id);
            Assert.Equal(nanoX.Id, manager.App.DccCentralProfiles[1].Id);
            Assert.Equal(nanoX.Id, manager.App.SelectedDccCentralProfileId);
        }
        finally
        {
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
        }
    }

    [Fact]
    public void Reload_RestoresConfiguredProfilesFromAppSettings_AndSelection()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");

        try
        {
            var selectedId = Guid.NewGuid();
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();
            manager.App.DccCentralProfiles.Add(new DccCentralProfile
            {
                Id = Guid.NewGuid(),
                Type = DccCentralType.Z21Legacy,
                Host = "192.168.0.10",
                Port = 21105
            });
            manager.App.DccCentralProfiles.Add(new DccCentralProfile
            {
                Id = selectedId,
                Type = DccCentralType.NanoX_S88,
                SerialPort = "COM8",
                BaudRate = 19200
            });
            manager.App.SelectedDccCentralProfileId = selectedId;
            Assert.True(manager.SaveApp());

            var reloadedManager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            reloadedManager.LoadApp();
            var vm = new SettingsViewModel(reloadedManager);

            Assert.Equal(2, vm.ConfiguredCentrals.Count);
            Assert.NotNull(vm.SelectedConfiguredCentral);
            Assert.Equal(selectedId, vm.SelectedConfiguredCentral!.Profile.Id);
            Assert.Equal("COM8", vm.SelectedConfiguredCentral.Profile.SerialPort);
        }
        finally
        {
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
        }
    }

    [Fact]
    public void Load_UsesProjectScopedProfilesAndSelection_WhenProjectProfileOverrideIsPresent()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");
        var projectPath = Path.Combine(Path.GetTempPath(), $"trackflow-project-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();
            manager.NewProject();
            Assert.True(manager.SaveProjectAs(projectPath));

            manager.App.DccCentralProfiles.Add(new DccCentralProfile
            {
                Id = Guid.NewGuid(),
                Type = DccCentralType.Z21Legacy,
                Host = "192.168.0.10",
                Port = 21105
            });
            manager.App.SelectedDccCentralProfileId = manager.App.DccCentralProfiles[0].Id;

            var projectSelectedId = Guid.NewGuid();
            manager.Project!.DccCentralProfiles = new()
            {
                new DccCentralProfile
                {
                    Id = Guid.NewGuid(),
                    Type = DccCentralType.Z21,
                    Host = "192.168.0.50",
                    Port = 21105
                },
                new DccCentralProfile
                {
                    Id = projectSelectedId,
                    Type = DccCentralType.NanoX_S88,
                    SerialPort = "COM12",
                    BaudRate = 19200
                }
            };
            manager.Project.SelectedDccCentralProfileId = projectSelectedId;

            var vm = new SettingsViewModel(manager);

            Assert.True(vm.UseProjectForDcc);
            Assert.Equal(2, vm.ConfiguredCentrals.Count);
            Assert.NotNull(vm.SelectedConfiguredCentral);
            Assert.Equal(projectSelectedId, vm.SelectedConfiguredCentral!.Profile.Id);
            Assert.Equal("COM12", vm.SelectedConfiguredCentral.Profile.SerialPort);
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
    public void Save_WithUseProjectForDcc_StoresLegacyProjectOverride_AndProfilesInProjectSettings()
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
                UseProjectForDcc = true,
                DccCentralType = DccCentralType.NanoX_S88,
                DccCentralSerialPort = "COM9",
                DccCentralBaudRate = 19200,
                AutoConnect = true
            };

            var profile = new DccCentralProfile
            {
                Id = Guid.NewGuid(),
                Type = DccCentralType.Z21Legacy,
                Host = "192.168.0.111",
                Port = 21105
            };
            vm.ConfiguredCentrals.Add(new ConfiguredDccCentralItem(profile, 1));
            vm.SelectedConfiguredCentral = vm.ConfiguredCentrals[0];
            vm.DccCentralType = DccCentralType.NanoX_S88;
            vm.DccCentralSerialPort = "COM9";
            vm.DccCentralBaudRate = 19200;
            vm.AutoConnect = true;

            Assert.True(vm.Save());

            var projectSettings = manager.Project;
            Assert.NotNull(projectSettings);
            Assert.Equal(DccCentralType.NanoX_S88, projectSettings!.DccCentralType);
            Assert.Equal("COM9", projectSettings.DccSerialPort);
            Assert.Equal(19200, projectSettings.DccBaudRate);
            Assert.Null(projectSettings.AutoConnect);
            var projectProfiles = projectSettings.DccCentralProfiles;
            Assert.NotNull(projectProfiles);
            Assert.Single(projectProfiles!);
            Assert.Equal(profile.Id, projectProfiles[0].Id);
            Assert.Equal(profile.Id, projectSettings.SelectedDccCentralProfileId);

            Assert.Empty(manager.App.DccCentralProfiles);
            Assert.Null(manager.App.SelectedDccCentralProfileId);
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
    public void Save_WithUseProjectForDccFalse_ClearsProjectScopedProfilesAndPersistsToApp()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");
        var projectPath = Path.Combine(Path.GetTempPath(), $"trackflow-project-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();
            manager.NewProject();
            Assert.True(manager.SaveProjectAs(projectPath));

            manager.Project!.DccCentralProfiles = new()
            {
                new DccCentralProfile
                {
                    Id = Guid.NewGuid(),
                    Type = DccCentralType.Z21Legacy,
                    Host = "192.168.0.201",
                    Port = 21105
                }
            };
            manager.Project.SelectedDccCentralProfileId = manager.Project.DccCentralProfiles[0].Id;

            var newGlobal = new DccCentralProfile
            {
                Id = Guid.NewGuid(),
                Type = DccCentralType.NanoX_S88,
                SerialPort = "COM15",
                BaudRate = 19200,
                AutoConnect = true
            };

            var vm = new SettingsViewModel(manager)
            {
                UseProjectForDcc = false
            };
            vm.ConfiguredCentrals.Clear();
            vm.ConfiguredCentrals.Add(new ConfiguredDccCentralItem(newGlobal, 1));
            vm.SelectedConfiguredCentral = vm.ConfiguredCentrals[0];

            Assert.True(vm.Save());

            Assert.Null(manager.Project.DccCentralProfiles);
            Assert.Null(manager.Project.SelectedDccCentralProfileId);
            Assert.Single(manager.App.DccCentralProfiles);
            Assert.Equal(newGlobal.Id, manager.App.DccCentralProfiles[0].Id);
            Assert.Equal(newGlobal.Id, manager.App.SelectedDccCentralProfileId);
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
    public void CurrentProjectName_ReflectsOpenedProjectFileName()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");
        var projectPath = Path.Combine(Path.GetTempPath(), $"trackflow-project-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();
            manager.NewProject();
            Assert.True(manager.SaveProjectAs(projectPath));

            var vm = new SettingsViewModel(manager);

            Assert.Equal(Path.GetFileName(projectPath), vm.CurrentProjectName);
            Assert.True(vm.HasProject);
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
    public async Task EditCentralCommand_PreservesExistingMainTrackCurrentLimitOverride_WhenDialogResultDoesNotExposeIt()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();

            var existingProfile = new DccCentralProfile
            {
                Id = Guid.NewGuid(),
                Type = DccCentralType.Z21Legacy,
                Host = "192.168.0.50",
                Port = 21105,
                MainTrackCurrentLimitAmperes = 4.2d,
                AutoConnect = false,
                StartupBehavior = StartupFunctionBehavior.SendAllFunctions
            };

            var vm = new SettingsViewModel(manager);
            vm.ConfiguredCentrals.Add(new ConfiguredDccCentralItem(existingProfile, 1));
            vm.SelectedConfiguredCentral = vm.ConfiguredCentrals[0];
            vm.SetCentralEditDialogFactory(_ => Task.FromResult<DccCentralProfile?>(new DccCentralProfile
            {
                Type = DccCentralType.Z21Legacy,
                Host = "192.168.0.77",
                Port = 21105,
                SerialPort = string.Empty,
                BaudRate = 19200,
                AutoConnect = true,
                StartupBehavior = StartupFunctionBehavior.AssumeOffState
            }));

            await vm.EditCentralCommand.ExecuteAsync(null);

            Assert.Equal("192.168.0.77", existingProfile.Host);
            Assert.True(existingProfile.AutoConnect);
            Assert.Equal(StartupFunctionBehavior.AssumeOffState, existingProfile.StartupBehavior);
            Assert.Equal(4.2d, existingProfile.MainTrackCurrentLimitAmperes);
        }
        finally
        {
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
        }
    }

    [Fact]
    public async Task AddCentralCommand_NewProfileKeepsNullMainTrackCurrentLimit_WhenUiDoesNotProvideOverride()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();

            var vm = new SettingsViewModel(manager);
            vm.SetCentralEditDialogFactory(_ => Task.FromResult<DccCentralProfile?>(new DccCentralProfile
            {
                Type = DccCentralType.Z21Legacy,
                Host = "192.168.0.111",
                Port = 21105,
                StartupBehavior = StartupFunctionBehavior.SendAllFunctions
            }));

            await vm.AddCentralCommand.ExecuteAsync(null);

            var added = Assert.Single(vm.ConfiguredCentrals);
            Assert.Null(added.Profile.MainTrackCurrentLimitAmperes);
        }
        finally
        {
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
        }
    }

    [Fact]
    public void SaveAndReload_PreservesExplicitMainTrackCurrentLimitOverride_InAppSettingsRoundtrip()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"trackflow-settings-{Guid.NewGuid():N}.json");

        try
        {
            var manager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            manager.LoadApp();

            var profile = new DccCentralProfile
            {
                Id = Guid.NewGuid(),
                Type = DccCentralType.Z21,
                Host = "192.168.0.60",
                Port = 21105,
                MainTrackCurrentLimitAmperes = 3.6d,
                StartupBehavior = StartupFunctionBehavior.SendAllFunctions
            };

            var vm = new SettingsViewModel(manager);
            vm.ConfiguredCentrals.Add(new ConfiguredDccCentralItem(profile, 1));
            vm.SelectedConfiguredCentral = vm.ConfiguredCentrals[0];

            Assert.True(vm.Save());

            var reloadedManager = new SettingsManager(appStore: new AppSettingsStore(settingsPath));
            reloadedManager.LoadApp();

            var reloaded = Assert.Single(reloadedManager.App.DccCentralProfiles);
            Assert.Equal(3.6d, reloaded.MainTrackCurrentLimitAmperes);

            var reloadedVm = new SettingsViewModel(reloadedManager);
            Assert.NotNull(reloadedVm.SelectedConfiguredCentral);
            Assert.Equal(3.6d, reloadedVm.SelectedConfiguredCentral!.Profile.MainTrackCurrentLimitAmperes);
        }
        finally
        {
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
        }
    }
}


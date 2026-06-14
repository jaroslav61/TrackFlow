using System.Linq;
using System.IO;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class ProjectMigrationServiceTests
{
	[Fact]
	public void MigrateIfNeeded_ReportsExactDoctorWarningsForProjectProblems()
	{
		TrackFlowDoctorService.Instance.Events.Clear();

		var enabledProfileId = System.Guid.NewGuid();
		var project = new TrackFlowProject
		{
			SchemaVersion = 3,
			Locomotives =
			{
				new LocoRecord { Name = "sdvfsdffsdfs", DccAddress = 4 },
				new LocoRecord { Name = "750.131-5", DccAddress = 4 }
			},
			Layout = new TrackLayout
			{
				SchemaVersion = 3,
				Elements =
				{
					new TurnoutElement
					{
						Label = "Vľ 2",
						DccAddress = 0,
						DccCentralProfileId = enabledProfileId
					},
					new TurnoutElement
					{
						Label = "VP 2",
						DccAddress = 24,
						DccCentralProfileId = null
					},
					new SignalElement { Label = "Na2", DccAddress = 0 },
					new SignalElement { Label = "Na4", DccAddress = 0 },
					new BlockElement { Label = "Blok 9", LengthCm = 0 }
				}
			}
		};

		var migration = new ProjectMigrationService();
		migration.MigrateIfNeeded(project);

		var events = TrackFlowDoctorService.Instance.GetEventsChronologicalSnapshot()
			.Where(evt => evt.Level == DiagnosticLevel.Warning && evt.Source == "Projekt")
			.Select(evt => evt.Message)
			.ToList();

		Assert.Contains("⚠️ Duplicitná DCC adresa lokomotívy.  sdvfsdffsdfs a 750.131-5 majú obe adresu 4. Jedna z nich sa nikdy nebude dať ovládať správne.", events);
		Assert.Contains("⚠️ Výhybka „Vľ 2“ má nastavenú adresu 0. Má priradený ovládací profil, ale s adresou 0 sa nebude dať ovládať.", events);
		Assert.Contains("⚠️ Výhybka „VP 2“ nemá priradený ovládací profil. Adresa 24 je nastavená, ale bez profilu sa nebude dať ovládať spoľahlivo.", events);
		Assert.Contains("⚠️ Návestidlo Na2 má nastavenú adresu 0. Nebude sa dať ovládať cez DCC.", events);
		Assert.Contains("⚠️ Návestidlo Na4 má nastavenú adresu 0. Nebude sa dať ovládať cez DCC.", events);
		Assert.Contains("⚠️ Blok 9 nemá nastavenú dĺžku. Kalibrovaná jazda nebude fungovať.", events);
		Assert.DoesNotContain(events, msg => msg.Contains("Id:", System.StringComparison.Ordinal));
	}

	[Fact]
	public void MigrateIfNeeded_V1Project_BackfillsSignalSystemsAndSignalAssignments()
	{
		var project = new TrackFlowProject
		{
			SchemaVersion = 1,
			Layout = new TrackLayout
			{
				SchemaVersion = 1,
				SignalSystems = new(),
				Elements =
				{
					new SignalElement
					{
						MarkerKey = "Signal",
						SignalSystemId = null,
						ProtectsBlockId = "b1"
					}
				}
			}
		};

		var migration = new ProjectMigrationService();
		var migrated = migration.MigrateIfNeeded(project);

		Assert.Equal(3, migrated.SchemaVersion);
		Assert.Equal(3, migrated.Layout.SchemaVersion);
		var defaultSystem = Assert.Single(migrated.Layout.SignalSystems);
		Assert.Equal(SignalSystemDefinition.DefaultSystemId, defaultSystem.Id);

		var migratedSignal = Assert.IsType<SignalElement>(Assert.Single(migrated.Layout.Elements));
		Assert.Equal(SignalSystemDefinition.DefaultSystemId, migratedSignal.SignalSystemId);
	}

	[Fact]
	public void MigrateIfNeeded_V2Project_DoesNotDuplicateDefaultSystem()
	{
		var project = new TrackFlowProject
		{
			SchemaVersion = 2,
			Layout = new TrackLayout
			{
				SchemaVersion = 2,
				SignalSystems = new()
				{
					new SignalSystemDefinition
					{
						Id = SignalSystemDefinition.DefaultSystemId,
						Name = "Custom SK"
					}
				},
				Elements =
				{
					new SignalElement
					{
						MarkerKey = "Signal",
						SignalSystemId = "CUSTOM_A"
					}
				}
			}
		};

		var migration = new ProjectMigrationService();
		var migrated = migration.MigrateIfNeeded(project);

		Assert.Equal(3, migrated.SchemaVersion);
		Assert.Equal(3, migrated.Layout.SchemaVersion);
		Assert.Equal(1, migrated.Layout.SignalSystems.Count(s => s.Id == SignalSystemDefinition.DefaultSystemId));

		var migratedSignal = Assert.IsType<SignalElement>(Assert.Single(migrated.Layout.Elements));
		Assert.Equal("CUSTOM_A", migratedSignal.SignalSystemId);
	}

	[Fact]
	public void MigrateIfNeeded_V2Project_NormalizesRouteDirections_AndBackfillsSafetyFallback()
	{
		var project = new TrackFlowProject
		{
			SchemaVersion = 2,
			Layout = new TrackLayout
			{
				SchemaVersion = 2,
				Routes = new()
				{
					new RouteDefinition
					{
						Id = "r1",
						FromBlockDirection = "Forward",
						ToBlockDirection = "Backward",
						StartNavigationDirection = "Forward",
						SafetyFallbackAspect = "Yellow",
						RouteSignalIds = null!
					}
				}
			}
		};

		var migration = new ProjectMigrationService();
		var migrated = migration.MigrateIfNeeded(project);

		Assert.Equal(3, migrated.SchemaVersion);
		Assert.Equal(3, migrated.Layout.SchemaVersion);

		var route = Assert.Single(migrated.Layout.Routes);
		Assert.Equal(RouteDirection.Right, route.FromBlockDirection);
		Assert.Equal(RouteDirection.Left, route.ToBlockDirection);
		Assert.Equal(RouteDirection.Right, route.StartNavigationDirection);
		Assert.Equal("Stop", route.SafetyFallbackAspect);
		Assert.NotNull(route.RouteSignalIds);
		Assert.Empty(route.RouteSignalIds);
	}

	[Fact]
	public void MigrateIfNeeded_V3Project_IsIdempotent_ForRouteDirectionAndSafetyFallback()
	{
		var project = new TrackFlowProject
		{
			SchemaVersion = 3,
			Layout = new TrackLayout
			{
				SchemaVersion = 3,
				Routes = new()
				{
					new RouteDefinition
					{
						Id = "r2",
						FromBlockDirection = RouteDirection.Left,
						ToBlockDirection = RouteDirection.Up,
						StartNavigationDirection = RouteDirection.Down,
						SafetyFallbackAspect = "Red",
						RouteSignalIds = new() { "s-1" }
					}
				}
			}
		};

		var migration = new ProjectMigrationService();
		var migrated = migration.MigrateIfNeeded(project);

		Assert.Equal(3, migrated.SchemaVersion);
		Assert.Equal(3, migrated.Layout.SchemaVersion);

		var route = Assert.Single(migrated.Layout.Routes);
		Assert.Equal(RouteDirection.Left, route.FromBlockDirection);
		Assert.Equal(RouteDirection.Up, route.ToBlockDirection);
		Assert.Equal(RouteDirection.Down, route.StartNavigationDirection);
		Assert.Equal("Stop", route.SafetyFallbackAspect);
		Assert.Single(route.RouteSignalIds);
		Assert.Equal("s-1", route.RouteSignalIds[0]);
	}

	[Fact]
	public void SignalElementAspect_LegacyRed_NormalizujeNaStop()
	{
		var signal = new SignalElement
		{
			Aspect = SignalAspect.Red
		};

		Assert.Equal(SignalAspect.Stop, signal.Aspect);
	}

	[Fact]
	public void MigrateIfNeeded_ContactIndicatorWithoutProfile_InheritsSelectedEnabledProfile()
	{
		var profileId = System.Guid.NewGuid();
		var project = new TrackFlowProject
		{
			SchemaVersion = 3,
			Settings = new ProjectSettingsData
			{
				SelectedDccCentralProfileId = profileId,
				DccCentralProfiles = new()
				{
					new DccCentralProfile
					{
						Id = profileId,
						IsEnabled = true,
						Type = DccCentralType.Z21,
						Host = "192.168.0.111",
						Port = 21105
					}
				}
			},
			Layout = new TrackLayout
			{
				SchemaVersion = 3,
				Elements =
				{
					new BlockElement
					{
						Id = "b1",
						Label = "Blok 1",
						Indicators =
						{
							new BlockIndicator
							{
								Type = BlockIndicatorType.Contact,
								ModuleAddress = 1,
								PortNumber = 7,
								DccCentralProfileId = null
							}
						}
					}
				}
			}
		};

		var migration = new ProjectMigrationService();
		var migrated = migration.MigrateIfNeeded(project);

		var block = Assert.IsType<BlockElement>(Assert.Single(migrated.Layout.Elements));
		var indicator = Assert.Single(block.Indicators);
		Assert.Equal(profileId, indicator.DccCentralProfileId);
	}

	[Fact]
	public void OpenProject_ContactIndicatorWithoutProfile_InheritsSelectedEnabledAppProfile()
	{
		var profileId = System.Guid.NewGuid();
		var projectPath = Path.Combine(Path.GetTempPath(), $"trackflow-project-{System.Guid.NewGuid():N}.json");

		try
		{
			var project = new TrackFlowProject
			{
				SchemaVersion = 3,
				Settings = new ProjectSettingsData
				{
					DccCentralProfiles = null,
					SelectedDccCentralProfileId = null
				},
				Layout = new TrackLayout
				{
					SchemaVersion = 3,
					Elements =
					{
						new BlockElement
						{
							Id = "b-app-1",
							Label = "Blok App 1",
							Indicators =
							{
								new BlockIndicator
								{
									Type = BlockIndicatorType.Contact,
									ModuleAddress = 17,
									PortNumber = 6,
									DccCentralProfileId = null
								}
							}
						}
					}
				}
			};

			var store = new ProjectStore();
			Assert.True(store.Save(projectPath, project));

			var manager = new SettingsManager();
			manager.LoadApp();
			manager.App.DccCentralProfiles.Add(new DccCentralProfile
			{
				Id = profileId,
				IsEnabled = true,
				Type = DccCentralType.Z21,
				Host = "192.168.0.111",
				Port = 21105
			});
			manager.App.SelectedDccCentralProfileId = profileId;

			manager.OpenProject(projectPath);

			var block = Assert.IsType<BlockElement>(Assert.Single(manager.CurrentProject!.Layout.Elements));
			var indicator = Assert.Single(block.Indicators);
			Assert.Equal(profileId, indicator.DccCentralProfileId);
		}
		finally
		{
			if (File.Exists(projectPath))
				File.Delete(projectPath);
		}
	}
}



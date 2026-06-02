using System.Linq;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class ProjectMigrationServiceTests
{
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
}



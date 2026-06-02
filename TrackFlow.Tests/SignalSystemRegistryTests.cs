using TrackFlow.Models.Layout;
using TrackFlow.Services;
using System.Linq;
using Xunit;

namespace TrackFlow.Tests;

/// <summary>
/// Testy pre SignalSystemRegistry – SK návestná sústava podľa normy ŽSR.
/// Validácia: 2/3/4/5-znakové návestidlá s korektným poradím svetiel zhora nadol.
/// </summary>
public class SignalSystemRegistryTests
{
    [Fact]
    public void BuiltinSystems_Contains_Slovak()
    {
        // Assert
        var slovak = SignalSystemRegistry.GetBuiltin(SignalSystemDefinition.DefaultSystemId);
        Assert.NotNull(slovak);
        Assert.Equal(SignalSystemDefinition.DefaultSystemId, slovak.Id);
    }

    [Fact]
    public void Slovak_System_Has_All_Profiles()
    {
        // Act
        var profiles = SignalSystemRegistry.GetProfiles(SignalSystemDefinition.DefaultSystemId);

        // Assert
        Assert.NotEmpty(profiles);

        var ids = profiles.Select(p => p.Id).ToHashSet();
        Assert.Contains("2-aspect", ids);
        Assert.Contains("2-aspect-main", ids);
        Assert.Contains("2-aspect-shunt", ids);
        Assert.Contains("2-aspect-route", ids);
        Assert.Contains("3-aspect", ids);
        Assert.Contains("3-aspect-entry", ids);
        Assert.Contains("4-aspect", ids);
        Assert.Contains("4-aspect-departure", ids);
        Assert.Contains("5-aspect", ids);
        Assert.Contains("5-aspect-departure", ids);
    }

    [Fact]
    public void Slovak_System_Profile_Ids_Are_Unique_And_HeadCounts_Are_Supported()
    {
        var system = SignalSystemRegistry.GetBuiltin(SignalSystemDefinition.DefaultSystemId);
        Assert.NotNull(system);

        var profiles = system.Profiles;
        Assert.Equal(profiles.Count, profiles.Select(p => p.Id).Distinct().Count());

        foreach (var profile in profiles)
            Assert.Contains(profile.HeadCount, system.SupportedHeadCounts);
    }

    [Theory]
    [InlineData("2-aspect", 2)]
    [InlineData("2-aspect-main", 2)]
    [InlineData("2-aspect-shunt", 2)]
    [InlineData("2-aspect-route", 2)]
    [InlineData("3-aspect", 3)]
    [InlineData("3-aspect-entry", 3)]
    [InlineData("4-aspect", 4)]
    [InlineData("4-aspect-departure", 4)]
    [InlineData("5-aspect", 5)]
    [InlineData("5-aspect-departure", 5)]
    public void Slovak_System_Profile_Has_Correct_Count(string profileId, int expectedCount)
    {
        // Act
        var profile = SignalSystemRegistry.GetProfile(SignalSystemDefinition.DefaultSystemId, profileId);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(expectedCount, profile.HeadCount);
        Assert.True(profile.Aspects.Count >= expectedCount);
    }

    [Fact]
    public void Profile_2AspectRoute_Has_Expected_Aspects()
    {
        var profile = SignalSystemRegistry.GetProfile(SignalSystemDefinition.DefaultSystemId, "2-aspect-route");
        Assert.NotNull(profile);

        var aspects = profile.Aspects.Select(a => a.Aspect).ToHashSet();
        Assert.Contains(SignalAspect.Stop, aspects);
        Assert.Contains(SignalAspect.ShuntingPermitted, aspects);
    }

    [Fact]
    public void Profile_3AspectEntry_Has_Expected_Aspects()
    {
        var profile = SignalSystemRegistry.GetProfile(SignalSystemDefinition.DefaultSystemId, "3-aspect-entry");
        Assert.NotNull(profile);

        var aspects = profile.Aspects.Select(a => a.Aspect).ToHashSet();
        Assert.Contains(SignalAspect.Proceed, aspects);
        Assert.Contains(SignalAspect.Stop, aspects);
        Assert.Contains(SignalAspect.ShuntingPermitted, aspects);
    }

    [Fact]
    public void Profile_2Sign_Has_Correct_Order()
    {
        // Arrange - SK norma: žltá (1), zelená (2)
        var profile = SignalSystemRegistry.GetProfile(SignalSystemDefinition.DefaultSystemId, "2-aspect");
        Assert.NotNull(profile);

        // Act & Assert
        Assert.Equal(2, profile.Aspects.Count);
        Assert.Equal(SignalAspect.Caution, profile.Aspects[0].Aspect);
        Assert.Equal(SignalAspect.Proceed, profile.Aspects[1].Aspect);
    }

    [Fact]
    public void Profile_3Sign_Has_Correct_Order()
    {
        // Arrange - SK norma: žltá (1), zelená (2), červená (3)
        var profile = SignalSystemRegistry.GetProfile(SignalSystemDefinition.DefaultSystemId, "3-aspect");
        Assert.NotNull(profile);

        // Act & Assert
        Assert.Equal(3, profile.Aspects.Count);
        Assert.Equal(SignalAspect.Caution, profile.Aspects[0].Aspect);
        Assert.Equal(SignalAspect.Proceed, profile.Aspects[1].Aspect);
        Assert.Equal(SignalAspect.Stop, profile.Aspects[2].Aspect);
    }

    [Fact]
    public void Profile_4Sign_Has_Correct_Order()
    {
        // Arrange - SK norma: žltá (1), červená (2), biela (3), žltá (4)
        var profile = SignalSystemRegistry.GetProfile(SignalSystemDefinition.DefaultSystemId, "4-aspect");
        Assert.NotNull(profile);

        // Act & Assert
        Assert.Equal(4, profile.Aspects.Count);
        Assert.Equal(SignalAspect.Caution, profile.Aspects[0].Aspect);  // Výstraha
        Assert.Equal(SignalAspect.Stop, profile.Aspects[1].Aspect);     // Stoj
        Assert.Equal(SignalAspect.ShuntingPermitted, profile.Aspects[2].Aspect);   // Posun (White je deprecated)
        Assert.Equal(SignalAspect.SlowProceed, profile.Aspects[3].Aspect);  // Dolná žltá (SlowProceed = kód 4)
    }

    [Fact]
    public void Profile_4Sign_Departure_Has_Correct_Order()
    {
        // Arrange - odchodové: zelená (1), červená (2), biela (3), žltá (4)
        var profile = SignalSystemRegistry.GetProfile(SignalSystemDefinition.DefaultSystemId, "4-aspect-departure");
        Assert.NotNull(profile);

        // Act & Assert
        Assert.Equal(4, profile.Aspects.Count);
        Assert.Equal(SignalAspect.Proceed, profile.Aspects[0].Aspect);   // Voľno
        Assert.Equal(SignalAspect.Stop, profile.Aspects[1].Aspect);     // Stoj
        Assert.Equal(SignalAspect.ShuntingPermitted, profile.Aspects[2].Aspect);   // Posun (White je deprecated)
        Assert.Equal(SignalAspect.SlowProceed, profile.Aspects[3].Aspect);  // Dolná žltá (SlowProceed = kód 4)
    }

    [Fact]
    public void Profile_5Sign_Has_Correct_Order()
    {
        // Arrange - SK norma: žltá (1), zelená (2), červená (3), biela (4), žltá (5)
        var profile = SignalSystemRegistry.GetProfile(SignalSystemDefinition.DefaultSystemId, "5-aspect");
        Assert.NotNull(profile);

        // Act & Assert
        Assert.Equal(5, profile.Aspects.Count);
        Assert.Equal(SignalAspect.SlowExpect40, profile.Aspects[0].Aspect);  // Horná žltá blik. (SlowExpect40 = kód 6)
        Assert.Equal(SignalAspect.Proceed, profile.Aspects[1].Aspect);   // Voľno
        Assert.Equal(SignalAspect.Stop, profile.Aspects[2].Aspect);     // Stoj
        Assert.Equal(SignalAspect.ShuntingPermitted, profile.Aspects[3].Aspect);   // Posun (White je deprecated)
        Assert.Equal(SignalAspect.SlowProceed, profile.Aspects[4].Aspect);  // Dolná žltá (SlowProceed = kód 4)
    }

    [Fact]
    public void Profile_5Sign_Departure_Has_Full_Sr_Speed_Signalling_Aspects()
    {
        var profile = SignalSystemRegistry.GetProfile(SignalSystemDefinition.DefaultSystemId, "5-aspect-departure");
        Assert.NotNull(profile);

        Assert.Equal(5, profile.HeadCount);

        var aspects = profile.Aspects.Select(a => a.Aspect).ToHashSet();
        Assert.Contains(SignalAspect.Stop, aspects);
        Assert.Contains(SignalAspect.Proceed, aspects);
        Assert.Contains(SignalAspect.Caution, aspects);
        Assert.Contains(SignalAspect.SlowProceed, aspects);
        Assert.Contains(SignalAspect.SlowCaution, aspects);
        Assert.Contains(SignalAspect.SlowExpect40, aspects);
        Assert.Contains(SignalAspect.ShuntingPermitted, aspects);
    }

    [Fact]
    public void SupportsPhysicalAspect_ReturnsFalse_For_Unsupported_ProfileAspect_Combination()
    {
        Assert.False(SignalSystemRegistry.SupportsPhysicalAspect(
            SignalSystemDefinition.DefaultSystemId,
            "2-aspect-shunt",
            SignalAspect.Proceed));

        Assert.False(SignalSystemRegistry.SupportsPhysicalAspect(
            SignalSystemDefinition.DefaultSystemId,
            "3-aspect",
            SignalAspect.SlowProceed));

        Assert.False(SignalSystemRegistry.SupportsPhysicalAspect(
            SignalSystemDefinition.DefaultSystemId,
            "3-aspect-entry",
            SignalAspect.Caution));

        Assert.True(SignalSystemRegistry.SupportsPhysicalAspect(
            SignalSystemDefinition.DefaultSystemId,
            "5-aspect-departure",
            SignalAspect.SlowProceed));
    }

    [Fact]
    public void SupportsTrainRouteRole_ReturnsFalse_For_Shunting_Profile()
    {
        Assert.False(SignalSystemRegistry.SupportsTrainRouteRole(
            SignalSystemDefinition.DefaultSystemId,
            "2-aspect-shunt"));

        Assert.True(SignalSystemRegistry.SupportsTrainRouteRole(
            SignalSystemDefinition.DefaultSystemId,
            "2-aspect-main"));
    }

    [Theory]
    [InlineData("3-aspect", SignalAspect.SlowProceed, SignalAspect.Caution)]
    [InlineData("3-aspect-entry", SignalAspect.SlowProceed, SignalAspect.Stop)]
    [InlineData("2-aspect", SignalAspect.Stop, SignalAspect.Caution)]
    [InlineData("5-aspect-departure", SignalAspect.SlowProceed, SignalAspect.SlowProceed)]
    public void ResolveFailSafeAspect_Returns_Safe_Supported_Aspect(string profileId, SignalAspect requestedAspect, SignalAspect expected)
    {
        var resolved = SignalSystemRegistry.ResolveFailSafeAspect(
            SignalSystemDefinition.DefaultSystemId,
            profileId,
            requestedAspect);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void GetProfile_Returns_Null_For_Invalid_Profile()
    {
        // Act
        var profile = SignalSystemRegistry.GetProfile(SignalSystemDefinition.DefaultSystemId, "invalid");

        // Assert
        Assert.Null(profile);
    }

    [Fact]
    public void GetProfile_Returns_Null_For_Invalid_System()
    {
        // Act
        var profile = SignalSystemRegistry.GetProfile("INVALID_SYSTEM", "3-aspect");

        // Assert
        Assert.Null(profile);
    }

    /// <summary>
    /// Spätná kompatibilita: staré projekty môžu mať uložené "3-head" namiesto "3-aspect".
    /// Registry ich musí taktiež nájsť.
    /// </summary>
    [Theory]
    [InlineData("2-head", 2)]
    [InlineData("3-head", 3)]
    [InlineData("4-head", 4)]
    [InlineData("5-head", 5)]
    public void GetProfile_LegacyHeadIds_AreResolved(string legacyId, int expectedCount)
    {
        var profile = SignalSystemRegistry.GetProfile(SignalSystemDefinition.DefaultSystemId, legacyId);

        Assert.NotNull(profile);
        Assert.Equal(expectedCount, profile.HeadCount);
    }

    [Fact]
    public void Default_System_Id_Is_Correct()
    {
        // Assert
        Assert.Equal("SK_DEFAULT", SignalSystemDefinition.DefaultSystemId);
    }

    [Fact]
    public void All_Profiles_Have_Non_Empty_Names()
    {
        // Act
        var profiles = SignalSystemRegistry.GetProfiles(SignalSystemDefinition.DefaultSystemId);

        // Assert
        foreach (var profile in profiles)
        {
            Assert.NotEmpty(profile.DisplayName);
            Assert.NotEmpty(profile.Id);
        }
    }

    [Fact]
    public void All_Aspects_Have_MarkerAssetNames()
    {
        // Act
        var profiles = SignalSystemRegistry.GetProfiles(SignalSystemDefinition.DefaultSystemId);

        // Assert
        foreach (var profile in profiles)
        {
            foreach (var aspect in profile.Aspects)
            {
                Assert.NotEmpty(aspect.MarkerAssetName);
                Assert.NotEmpty(aspect.DisplayName);
            }
        }
    }
}



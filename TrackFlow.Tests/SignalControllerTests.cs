using System.Collections.Generic;
using System.Threading.Tasks;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;
namespace TrackFlow.Tests;
public class SignalControllerTests
{
    // ── Hlavné návestidlo (default / 2-aspect-main / 3-aspect / ...) ────────
    [Fact]
    public void RefreshAspects_ObsadenyChranenyBlok_NastaviStop()
    {
        var service = new SignalController();
        var block = new BlockElement { MarkerKey = "Block", IsOccupied = true };
        var signal = new SignalElement { MarkerKey = "Signal", ProtectsBlockId = block.Id, DccAddress = 1, Aspect = SignalAspect.Proceed };
        var changed = service.RefreshAspects(new List<LayoutElement> { block, signal });
        Assert.Single(changed);
        Assert.Equal(SignalAspect.Stop, signal.Aspect);
    }
    [Fact]
    public void RefreshAspects_VolnyBlokSRequestYellow_NastaviCaution()
    {
        var service = new SignalController();
        var block = new BlockElement { MarkerKey = "Block", RequestYellow = true };
        var signal = new SignalElement { MarkerKey = "Signal", ProtectsBlockId = block.Id, DccAddress = 1, Aspect = SignalAspect.Stop };
        service.RefreshAspects(new List<LayoutElement> { block, signal });
        Assert.Equal(SignalAspect.Caution, signal.Aspect);
    }
    [Fact]
    public void RefreshAspects_VolnyBlokBezRequestYellow_NastaviProceed()
    {
        var service = new SignalController();
        var block = new BlockElement { MarkerKey = "Block", IsOccupied = false, RequestYellow = false };
        var signal = new SignalElement { MarkerKey = "Signal", ProtectsBlockId = block.Id, DccAddress = 1, Aspect = SignalAspect.Stop };
        service.RefreshAspects(new List<LayoutElement> { block, signal });
        Assert.Equal(SignalAspect.Proceed, signal.Aspect);
    }
    [Fact]
    public void RefreshAspects_BezChranenehoBloku_NastaviStop()
    {
        var service = new SignalController();
        var signal = new SignalElement { MarkerKey = "Signal", ProtectsBlockId = "missing", DccAddress = 1, Aspect = SignalAspect.Proceed };
        service.RefreshAspects(new List<LayoutElement> { signal });
        Assert.Equal(SignalAspect.Stop, signal.Aspect);
    }
    // ── Zriaďovacie návestidlo (2-aspect-shunt) ──────────────────────────────
    [Fact]
    public void RefreshAspects_Shunt_ObsadenyBlok_NastaviStop()
    {
        // Regression: predtým vrátilo Red, teraz musí vrátiť Stop (posun zakázaný)
        var service = new SignalController();
        var block = new BlockElement { MarkerKey = "Block", IsOccupied = true };
        var signal = new SignalElement
        {
            MarkerKey = "Signal",
            SignalProfile = "2-aspect-shunt",
            ProtectsBlockId = block.Id,
            DccAddress = 1,
            Aspect = SignalAspect.ShuntingPermitted
        };
        service.RefreshAspects(new List<LayoutElement> { block, signal });
        Assert.Equal(SignalAspect.Stop, signal.Aspect);
    }
    [Fact]
    public void RefreshAspects_Shunt_VolnyBlok_NastaviShuntingPermitted()
    {
        var service = new SignalController();
        var block = new BlockElement { MarkerKey = "Block", IsOccupied = false };
        var signal = new SignalElement
        {
            MarkerKey = "Signal",
            SignalProfile = "2-aspect-shunt",
            ProtectsBlockId = block.Id,
            DccAddress = 1,
            Aspect = SignalAspect.Stop
        };
        service.RefreshAspects(new List<LayoutElement> { block, signal });
        Assert.Equal(SignalAspect.ShuntingPermitted, signal.Aspect);
    }
    [Fact]
    public void RefreshAspects_Shunt_VolnyBlokSRequestYellow_NastaviShuntingPermitted()
    {
        // Zriaďovacie návestidlo nerozlišuje Yellow – voľný blok vždy ShuntingPermitted
        var service = new SignalController();
        var block = new BlockElement { MarkerKey = "Block", IsOccupied = false, RequestYellow = true };
        var signal = new SignalElement
        {
            MarkerKey = "Signal",
            SignalProfile = "2-aspect-shunt",
            ProtectsBlockId = block.Id,
            DccAddress = 1,
            Aspect = SignalAspect.Stop
        };
        service.RefreshAspects(new List<LayoutElement> { block, signal });
        Assert.Equal(SignalAspect.ShuntingPermitted, signal.Aspect);
    }
    // ── Predzvesť (2-aspect) ─────────────────────────────────────────────────
    [Fact]
    public void RefreshAspects_Predzvest_ObsadenyBlok_NastaviCaution()
    {
        // Regression: predtým vrátilo Stop, teraz musí vrátiť Caution (výstraha)
        var service = new SignalController();
        var block = new BlockElement { MarkerKey = "Block", IsOccupied = true };
        var signal = new SignalElement
        {
            MarkerKey = "Signal",
            SignalProfile = "2-aspect",
            ProtectsBlockId = block.Id,
            DccAddress = 1,
            Aspect = SignalAspect.Proceed
        };
        service.RefreshAspects(new List<LayoutElement> { block, signal });
        Assert.Equal(SignalAspect.Caution, signal.Aspect);
    }
    [Fact]
    public void RefreshAspects_Predzvest_VolnyBlok_NastaviProceed()
    {
        var service = new SignalController();
        var block = new BlockElement { MarkerKey = "Block", IsOccupied = false };
        var signal = new SignalElement
        {
            MarkerKey = "Signal",
            SignalProfile = "2-aspect",
            ProtectsBlockId = block.Id,
            DccAddress = 1,
            Aspect = SignalAspect.Caution
        };
        service.RefreshAspects(new List<LayoutElement> { block, signal });
        Assert.Equal(SignalAspect.Proceed, signal.Aspect);
    }
    // ── ResolveAspectForProfile – priame unit testy ──────────────────────────
    [Theory]
    [InlineData("2-aspect-main",  true,  false, SignalAspect.Stop)]
    [InlineData("2-aspect-main",  false, false, SignalAspect.Proceed)]
    [InlineData("2-aspect-main",  false, true,  SignalAspect.Caution)]
    [InlineData("2-aspect-shunt", true,  false, SignalAspect.Stop)]
    [InlineData("2-aspect-shunt", false, false, SignalAspect.ShuntingPermitted)]
    [InlineData("2-aspect-shunt", false, true,  SignalAspect.ShuntingPermitted)]  // shunt ignoruje RequestYellow
    [InlineData("2-aspect",       true,  false, SignalAspect.Caution)]
    [InlineData("2-aspect",       false, false, SignalAspect.Proceed)]
    [InlineData("2-aspect",       false, true,  SignalAspect.Proceed)]  // predzvesť ignoruje RequestYellow
    [InlineData("3-aspect",       true,  false, SignalAspect.Stop)]
    [InlineData("3-aspect",       false, true,  SignalAspect.Caution)]
    [InlineData("4-aspect",       true,  false, SignalAspect.Stop)]
    [InlineData("4-aspect-departure", true, false, SignalAspect.Stop)]
    [InlineData("4-aspect-departure", false, false, SignalAspect.Proceed)]
    [InlineData("4-aspect-departure", false, true, SignalAspect.Caution)]
    [InlineData("5-aspect-departure", true, false, SignalAspect.Stop)]
    [InlineData("5-aspect-departure", false, false, SignalAspect.Proceed)]
    [InlineData("5-aspect-departure", false, true, SignalAspect.Caution)]
    [InlineData("5-aspect",       false, false, SignalAspect.Proceed)]
    [InlineData(null,             true,  false, SignalAspect.Stop)]    // null profil = hlavné
    public void ResolveAspectForProfile_ReturnsExpected(
        string? profile, bool occupied, bool requestYellow, SignalAspect expected)
    {
        var block = new BlockElement { IsOccupied = occupied, RequestYellow = requestYellow };
        var result = SignalController.ResolveAspectForProfile(profile, block);
        Assert.Equal(expected, result);
    }
    // ── MapAspectToAccessory ─────────────────────────────────────────────────
    [Theory]
    [InlineData(SignalAspect.Stop,    false)]
    [InlineData(SignalAspect.Proceed,  true)]
    [InlineData(SignalAspect.Caution, true)]
    [InlineData(SignalAspect.SlowProceed, true)]
    [InlineData(SignalAspect.SlowCaution, true)]
    [InlineData(SignalAspect.SlowExpect40, true)]
    [InlineData(SignalAspect.ShuntingPermitted,  true)]   // posun dovolený = 1
    public void MapAspectToAccessory_ReturnsExpectedState(SignalAspect aspect, bool expected)
    {
        Assert.Equal(expected, SignalController.MapAspectToAccessory(aspect));
    }
    [Theory]
    [InlineData(SignalAspect.Proceed, 100)]
    [InlineData(SignalAspect.SlowExpect40, 35)]
    [InlineData(SignalAspect.Caution, 50)]
    [InlineData(SignalAspect.SlowProceed, 40)]
    [InlineData(SignalAspect.SlowCaution, 35)]
    [InlineData(SignalAspect.ShuntingPermitted, 25)]
    [InlineData(SignalAspect.Stop, 0)]
    public void ResolveSpeedLimitForAspect_ReturnsExpectedLimit(SignalAspect aspect, int expectedLimit)
    {
        Assert.Equal(expectedLimit, SignalController.ResolveSpeedLimitForAspect(aspect));
    }
    [Fact]
    public void CalculateRouteAspect_AllStraight_ReturnsCaution()
    {
        var service = new SignalController();
        var route = new RouteDefinition
        {
            TurnoutSettings = new List<RouteTurnoutSetting>
            {
                new() { TurnoutId = "sw1", RequiredState = TurnoutState.Straight },
                new() { TurnoutId = "sw2", RequiredState = TurnoutState.Straight }
            }
        };
        var aspect = service.CalculateRouteAspect(route);
        Assert.Equal(SignalAspect.Caution, aspect);
    }
    [Theory]
    [InlineData(TurnoutState.Diverge)]
    [InlineData(TurnoutState.DivergeLeft)]
    [InlineData(TurnoutState.DivergeRight)]
    public void CalculateRouteAspect_AnyDivergingTurnout_ReturnsSlowProceed(TurnoutState divergingState)
    {
        var service = new SignalController();
        var route = new RouteDefinition
        {
            TurnoutSettings = new List<RouteTurnoutSetting>
            {
                new() { TurnoutId = "sw1", RequiredState = TurnoutState.Straight },
                new() { TurnoutId = "sw2", RequiredState = divergingState }
            }
        };
        var aspect = service.CalculateRouteAspect(route);
        Assert.Equal(SignalAspect.SlowProceed, aspect);
    }
    [Fact]
    public async Task ApplySignalAspectsForRouteAsync_ResolvesStartSignalByDirection_AndSendsDcc()
    {
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var fromBlock = new BlockElement
        {
            Id = "blk-from",
            MarkerKey = "Block",
            SignalRightId = "sig-start"
        };
        var signal = new SignalElement
        {
            Id = "sig-start",
            MarkerKey = "Signal",
            DccAddress = 42,
            IsBasicMode = false,
            Aspect = SignalAspect.Stop
        };
        var route = new RouteDefinition
        {
            Id = "route-1",
            FromBlockId = fromBlock.Id,
            StartNavigationDirection = RouteDirection.Right,
            TurnoutSettings = new List<RouteTurnoutSetting>
            {
                new() { TurnoutId = "sw1", RequiredState = TurnoutState.Diverge }
            }
        };
        var applied = await service.ApplySignalAspectsForRouteAsync(route, new LayoutElement[] { fromBlock, signal }, client);
        Assert.True(applied);
        Assert.Equal(SignalAspect.SlowProceed, signal.Aspect);
        Assert.Single(client.ExtendedAccessoryCommands);
        Assert.Equal((42, SignalController.MapAspectToExtendedNumber(SignalAspect.SlowProceed)), client.ExtendedAccessoryCommands[0]);
    }
    [Fact]
    public async Task ApplySignalAspectsForRouteAsync_MissingAssignedSignal_ReturnsFalse_AndDoesNotSendDcc()
    {
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var fromBlock = new BlockElement
        {
            Id = "blk-from",
            MarkerKey = "Block",
            SignalRightId = "missing-signal"
        };
        var route = new RouteDefinition
        {
            Id = "route-missing",
            FromBlockId = fromBlock.Id,
            StartNavigationDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        var applied = await service.ApplySignalAspectsForRouteAsync(route, new LayoutElement[] { fromBlock }, client);
        Assert.False(applied);
        Assert.Empty(client.TurnoutCommands);
        Assert.Empty(client.ExtendedAccessoryCommands);
    }

    [Fact]
    public void TryValidateRouteSignalSupport_FailsForIntermediateSignalThatMayNeedCaution()
    {
        var service = new SignalController();

        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", X = 0, Y = 0, SignalRightId = "sig_a" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", X = 100, Y = 0, SignalRightId = "sig_b" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block", X = 200, Y = 0, SignalRightId = "sig_c" };
        var blockD = new BlockElement { Id = "blk_d", MarkerKey = "Block", X = 300, Y = 0 };

        var signalA = new SignalElement { Id = "sig_a", MarkerKey = "Signal", SignalProfile = "2-aspect-main", Aspect = SignalAspect.Stop };
        var signalB = new SignalElement { Id = "sig_b", MarkerKey = "Signal", Label = "S_B", SignalProfile = "3-aspect-entry", Aspect = SignalAspect.Stop };
        var signalC = new SignalElement { Id = "sig_c", MarkerKey = "Signal", SignalProfile = "2-aspect-main", Aspect = SignalAspect.Stop };

        var route = new RouteDefinition
        {
            Id = "r_chain_validate",
            Name = "Chain validate",
            FromBlockId = "blk_a",
            ToBlockId = "blk_d",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c", "blk_d" });
        route.RouteSignalIds.AddRange(new[] { "sig_a", "sig_b", "sig_c" });

        var isValid = service.TryValidateRouteSignalSupport(
            route,
            new LayoutElement[] { blockA, blockB, blockC, blockD, signalA, signalB, signalC },
            out var failureReason);

        Assert.False(isValid);
        Assert.NotNull(failureReason);
        Assert.Contains("S_B", failureReason);
        Assert.Contains("blk_b", failureReason, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Výstraha", failureReason);
    }

    [Fact]
    public void TryValidateRouteSignalSupport_FailsForShuntingSignalUsedOnTrainRoute()
    {
        var service = new SignalController();

        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", X = 0, Y = 0, SignalRightId = "sig_a" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", X = 100, Y = 0, SignalRightId = "sig_b" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block", X = 200, Y = 0 };

        var signalA = new SignalElement { Id = "sig_a", MarkerKey = "Signal", SignalProfile = "2-aspect-main", Aspect = SignalAspect.Stop };
        var signalB = new SignalElement { Id = "sig_b", Label = "S_SH", MarkerKey = "Signal", SignalProfile = "2-aspect-shunt", Aspect = SignalAspect.Stop };

        var route = new RouteDefinition
        {
            Id = "r_shunt_invalid",
            Name = "Shunt invalid",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });
        route.RouteSignalIds.AddRange(new[] { "sig_a", "sig_b" });

        var isValid = service.TryValidateRouteSignalSupport(
            route,
            new LayoutElement[] { blockA, blockB, blockC, signalA, signalB },
            out var failureReason);

        Assert.False(isValid);
        Assert.NotNull(failureReason);
        Assert.Contains("S_SH", failureReason);
        Assert.Contains("nie je vhodný pre vlakovú cestu", failureReason);
    }

    [Theory]
    [InlineData(SignalAspect.Stop, 1)]
    [InlineData(SignalAspect.Proceed, 2)]
    [InlineData(SignalAspect.Caution, 3)]
    [InlineData(SignalAspect.SlowProceed, 4)]
    [InlineData(SignalAspect.SlowCaution, 5)]
    [InlineData(SignalAspect.SlowExpect40, 6)]
    [InlineData(SignalAspect.ShuntingPermitted, 14)]
    public void MapAspectToExtendedNumber_PeLiTab2Mapping(SignalAspect aspect, int expected)
    {
        // Overenie mapovania podla PeLi TAB2.
        Assert.Equal(expected, SignalController.MapAspectToExtendedNumber(aspect));
    }
    [Fact]
    public async Task SendCurrentStateToCentral_ExtendedMode_SlowProceed_SendsExpectedAspectNumber()
    {
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var signal = new SignalElement
        {
            Id = "sig-na1",
            MarkerKey = "Signal",
            DccAddress = 140,
            IsBasicMode = false,
            Aspect = SignalAspect.SlowProceed
        };
        var sent = await service.SendCurrentStateToCentral(signal, client);
        Assert.True(sent);
        Assert.Single(client.ExtendedAccessoryCommands);
        Assert.Equal((140, 4), client.ExtendedAccessoryCommands[0]);
    }
    [Fact]
    public async Task SendCurrentStateToCentral_ExtendedMode_SlowCaution_SendsExpectedAspectNumber()
    {
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var signal = new SignalElement
        {
            Id = "sig-na1",
            MarkerKey = "Signal",
            DccAddress = 141,
            IsBasicMode = false,
            Aspect = SignalAspect.SlowCaution
        };
        var sent = await service.SendCurrentStateToCentral(signal, client);
        Assert.True(sent);
        Assert.Single(client.ExtendedAccessoryCommands);
        Assert.Equal((141, 5), client.ExtendedAccessoryCommands[0]);
    }
    // ── DCC integracia ───────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshAllAsync_PriZmenePosleDccPrikaz()
    {
        // Blok je obsadený → aspekt sa zmení z Proceed na Stop.
        // Basic mode: Stop = turnout 49, straight output → branch=false, activate=true.
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var block = new BlockElement { MarkerKey = "Block", IsOccupied = true };
        var signal = new SignalElement
        {
            MarkerKey = "Signal",
            ProtectsBlockId = block.Id,
            DccAddress = 12,
            Aspect = SignalAspect.Proceed
        };
        var changedCount = await service.RefreshAllAsync(new List<LayoutElement> { block, signal }, client);
        Assert.Equal(1, changedCount);
        Assert.Single(client.TurnoutCommands);
        Assert.Contains((49, false, true), client.TurnoutCommands);  // board 12 → turnout 49 = Stoj (Stop)
    }
    [Fact]
    public async Task RefreshAllAsync_BezZmenyNeposielaDccPrikaz()
    {
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var block = new BlockElement { MarkerKey = "Block", IsOccupied = true };
        var signal = new SignalElement
        {
            MarkerKey = "Signal",
            ProtectsBlockId = block.Id,
            DccAddress = 12,
            Aspect = SignalAspect.Stop
        };
        var changedCount = await service.RefreshAllAsync(new List<LayoutElement> { block, signal }, client);
        Assert.Equal(0, changedCount);
        Assert.Empty(client.TurnoutCommands);
    }
    [Fact]
    public async Task RefreshAllAsync_ShuntObsadeny_PosleDccTrue()
    {
        // Stop (zriaďovacie) = posun zakázaný → turnout 21, straight output → branch=false, activate=true.
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var block = new BlockElement { MarkerKey = "Block", IsOccupied = true };
        var signal = new SignalElement
        {
            MarkerKey = "Signal",
            SignalProfile = "2-aspect-shunt",
            ProtectsBlockId = block.Id,
            DccAddress = 5,
            Aspect = SignalAspect.ShuntingPermitted   // zmení sa na Stop → ODBOČKA at base
        };
        await service.RefreshAllAsync(new List<LayoutElement> { block, signal }, client);
        Assert.Single(client.TurnoutCommands);
        Assert.Contains((21, false, true), client.TurnoutCommands);  // board 5 → turnout 21 = Posun zakázaný (Stop)
    }
    [Fact]
    public async Task SendCurrentStateToCentral_BasicMode_PosleJedenSpravnyPrikaz()
    {
        // Proceed = turnout 401, odbočka/thrown output → branch=true, activate=true.
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var signal = new SignalElement
        {
            DccAddress = 100,
            IsBasicMode = true,
            Aspect = SignalAspect.Proceed
        };
        var sent = await service.SendCurrentStateToCentral(signal, client);
        Assert.True(sent);
        Assert.Single(client.TurnoutCommands);
        Assert.Contains((401, true, true), client.TurnoutCommands);  // board 100 → turnout 401 = Voľno (Proceed)
        Assert.Empty(client.ExtendedAccessoryCommands);
    }
    [Fact]
    public async Task SendCurrentStateToCentral_BasicMode_Stop_PosleOdbockaPrikaz()
    {
        // Stop = turnout 201, straight output → branch=false, activate=true.
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var signal = new SignalElement
        {
            DccAddress = 50,
            IsBasicMode = true,
            Aspect = SignalAspect.Stop
        };
        var sent = await service.SendCurrentStateToCentral(signal, client);
        Assert.True(sent);
        Assert.Single(client.TurnoutCommands);
        Assert.Contains((201, false, true), client.TurnoutCommands);  // board 50 → turnout 201 = Stoj (Stop)
        Assert.Empty(client.ExtendedAccessoryCommands);
    }

    [Fact]
    public async Task ApplyDccAsync_StopAspect_AlwaysSendsEvenWhenCacheAlreadyContainsStop()
    {
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var signal = new SignalElement
        {
            Id = "sig-stop",
            MarkerKey = "Signal",
            DccAddress = 24,
            Aspect = SignalAspect.Stop,
            IsBasicMode = true
        };

        await service.ApplyDccAsync(new[] { signal }, client);
        await service.ApplyDccAsync(new[] { signal }, client);

        Assert.Equal(2, client.TurnoutCommands.Count);
        Assert.All(client.TurnoutCommands, command => Assert.Equal((97, false, true), command));
    }

    [Fact]
    public async Task SendCurrentStateToCentral_BasicMode_Caution_PosleOdbockaAdresaPlusJeden()
    {
        // Caution = turnout 402, straight output → branch=false, activate=true.
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var signal = new SignalElement
        {
            DccAddress = 100,
            IsBasicMode = true,
            Aspect = SignalAspect.Caution
        };
        var sent = await service.SendCurrentStateToCentral(signal, client);
        Assert.True(sent);
        Assert.Single(client.TurnoutCommands);
        Assert.Contains((402, false, true), client.TurnoutCommands);  // board 100 → turnout 402 = Výstraha (Caution)
        Assert.Empty(client.ExtendedAccessoryCommands);
    }
    [Fact]
    public async Task SendCurrentStateToCentral_ExtendedMode_PosleJednuAdresuSAspektom()
    {
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var signal = new SignalElement
        {
            DccAddress = 200,
            IsBasicMode = false,
            Aspect = SignalAspect.Caution
        };
        var sent = await service.SendCurrentStateToCentral(signal, client);
        Assert.True(sent);
        Assert.Single(client.ExtendedAccessoryCommands);
        Assert.Equal((200, SignalController.MapAspectToExtendedNumber(SignalAspect.Caution)), client.ExtendedAccessoryCommands[0]);
        Assert.Empty(client.TurnoutCommands);
    }
    [Fact]
    public async Task SendAllCurrentStatesToCentralAsync_PosleLenSignalySKorektnymRezimom()
    {
        var service = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };
        var block = new BlockElement { MarkerKey = "Block" };
        var basicSignal = new SignalElement { DccAddress = 10, IsBasicMode = true, Aspect = SignalAspect.Stop };
        var extendedSignal = new SignalElement { DccAddress = 20, IsBasicMode = false, Aspect = SignalAspect.ShuntingPermitted };
        var sent = await service.SendAllCurrentStatesToCentralAsync(new LayoutElement[] { block, basicSignal, extendedSignal }, client);
        Assert.Equal(2, sent);
        // basicSignal Stop → board 10 → turnout 41, straight output = 1 príkaz
        Assert.Single(client.TurnoutCommands);
        Assert.Contains((41, false, true), client.TurnoutCommands);
        Assert.Single(client.ExtendedAccessoryCommands);
        Assert.Equal((20, SignalController.MapAspectToExtendedNumber(SignalAspect.ShuntingPermitted)), client.ExtendedAccessoryCommands[0]);
    }
}
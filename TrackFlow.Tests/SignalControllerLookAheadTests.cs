using System.Collections.Generic;
using System.Threading.Tasks;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

/// <summary>
/// Fáza 2.2 – Look-ahead / blikanie (UpperYellowBlinking).
/// Testuje upgrading navestidiel na UpperYellowBlinking keď tvoria aktívna
/// reťaz ciest a predchádzajúca zastávka by bola obmedzujúci aspekt.
/// </summary>
public class SignalControllerLookAheadTests
{
    // ══ CalculateRouteAspect (rozšírenie 2.2) ══════════════════════════════

    [Fact]
    public void CalculateRouteAspect_AllStraight_NoRestrictedOnward_ReturnsYellow()
    {
        var svc = new SignalController();
        var route = new RouteDefinition
        {
            TurnoutSettings = new List<RouteTurnoutSetting>
            {
                new() { TurnoutId = "sw1", RequiredState = TurnoutState.Straight }
            }
        };

        Assert.Equal(SignalAspect.Caution, svc.CalculateRouteAspect(route, nextSignalIsRestricted: false));
    }

    [Fact]
    public void CalculateRouteAspect_AllStraight_WithRestrictedOnward_ReturnsUpperYellowBlinking()
    {
        var svc = new SignalController();
        var route = new RouteDefinition
        {
            TurnoutSettings = new List<RouteTurnoutSetting>
            {
                new() { TurnoutId = "sw1", RequiredState = TurnoutState.Straight }
            }
        };

        Assert.Equal(SignalAspect.SlowExpect40, svc.CalculateRouteAspect(route, nextSignalIsRestricted: true));
    }

    [Theory]
    [InlineData(TurnoutState.Diverge)]
    [InlineData(TurnoutState.DivergeLeft)]
    [InlineData(TurnoutState.DivergeRight)]
    public void CalculateRouteAspect_Diverge_EvenWithRestrictedOnward_ReturnsLowerYellow(TurnoutState diverge)
    {
        // Odbočka má prednosť – LowerYellow sa neupgraduje na UpperYellowBlinking.
        var svc = new SignalController();
        var route = new RouteDefinition
        {
            TurnoutSettings = new List<RouteTurnoutSetting>
            {
                new() { TurnoutId = "sw1", RequiredState = diverge }
            }
        };

        Assert.Equal(SignalAspect.SlowProceed, svc.CalculateRouteAspect(route, nextSignalIsRestricted: true));
    }

    [Fact]
    public void CalculateRouteAspect_NoTurnouts_WithRestrictedOnward_ReturnsUpperYellowBlinking()
    {
        var svc = new SignalController();
        var route = new RouteDefinition(); // prázdne TurnoutSettings = rovná cesta

        Assert.Equal(SignalAspect.SlowExpect40, svc.CalculateRouteAspect(route, nextSignalIsRestricted: true));
    }

    // ══ IsRestrictedAspect ══════════════════════════════════════════════════

    [Theory]
    [InlineData(SignalAspect.Caution, true)]
    [InlineData(SignalAspect.SlowProceed, true)]
    [InlineData(SignalAspect.SlowExpect40, true)]
    [InlineData(SignalAspect.Stop, false)]
    [InlineData(SignalAspect.Proceed, false)]
    [InlineData(SignalAspect.ShuntingPermitted, false)]
    public void IsRestrictedAspect_ReturnsExpected(SignalAspect aspect, bool expected)
    {
        Assert.Equal(expected, SignalController.IsRestrictedAspect(aspect));
    }

    // ══ ApplyLookAheadAspectsAsync ══════════════════════════════════════════

    /// <summary>
    /// Primárny scenár Phase 2.2:
    /// Aktívna reťaz ciest A→B a B→C – navestidlo A→B je Yellow (rovná cesta),
    /// navestidlo B→C je Yellow. Look-ahead má upgradnúť navestidlo A→B na UpperYellowBlinking.
    /// </summary>
    [Fact]
    public async Task ApplyLookAheadAspectsAsync_TwoConsecutiveRoutes_YellowChain_UpgradesFirstSignal()
    {
        var svc = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };

        // Bloky: A → B → C
        var blkA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        var blkB = new BlockElement { Id = "blk_b", MarkerKey = "Block", SignalRightId = "sig_bc" };
        var blkC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };

        // Navestidlá: sig_ab (odchod z A, chráni B), sig_bc (odchod z B, chráni C)
        var sigAb = new SignalElement { Id = "sig_ab", DccAddress = 10, IsBasicMode = false, Aspect = SignalAspect.Caution };
        var sigBc = new SignalElement { Id = "sig_bc", DccAddress = 20, IsBasicMode = false, Aspect = SignalAspect.Caution };

        // Cesty: r_ab (A→B), r_bc (B→C)
        var routeAb = new RouteDefinition
        {
            Id = "r_ab",
            FromBlockId = blkA.Id,
            ToBlockId = blkB.Id,
            StartNavigationDirection = RouteDirection.Right,
        };
        var routeBc = new RouteDefinition
        {
            Id = "r_bc",
            FromBlockId = blkB.Id,
            ToBlockId = blkC.Id,
            StartNavigationDirection = RouteDirection.Right,
        };

        var elements = new List<LayoutElement> { blkA, blkB, blkC, sigAb, sigBc };
        var activeRouteIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "r_ab", "r_bc" };
        var allRoutes = new List<RouteDefinition> { routeAb, routeBc };

        var upgraded = await svc.ApplyLookAheadAspectsAsync(activeRouteIds, allRoutes, elements, client);

        Assert.Equal(1, upgraded);
        // Navestidlo A→B bolo upgradnuté z Yellow – UpperYellowBlinking.
        Assert.Equal(SignalAspect.SlowExpect40, sigAb.Aspect);
        // Navestidlo B→C zostáva Yellow (cieľový blok C nemá ďalšiu aktívnu cestu).
        Assert.Equal(SignalAspect.Caution, sigBc.Aspect);
        // Jeden DCC príkaz pre upgradnutý signál (extended mode, aspect=7).
        Assert.Single(client.ExtendedAccessoryCommands);
        Assert.Equal((10, SignalController.MapAspectToExtendedNumber(SignalAspect.SlowExpect40)),
            client.ExtendedAccessoryCommands[0]);
    }

    /// <summary>
    /// Jedna aktívna cesta – žiadny look-ahead (menej ako 2 aktívne cesty).
    /// </summary>
    [Fact]
    public async Task ApplyLookAheadAspectsAsync_SingleRoute_NoChange()
    {
        var svc = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };

        var blkA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_a" };
        var blkB = new BlockElement { Id = "blk_b", MarkerKey = "Block" };
        var sigA = new SignalElement { Id = "sig_a", DccAddress = 10, IsBasicMode = false, Aspect = SignalAspect.Caution };
        var route = new RouteDefinition
        {
            Id = "r_ab",
            FromBlockId = blkA.Id,
            ToBlockId = blkB.Id,
            StartNavigationDirection = RouteDirection.Right,
        };

        var elements = new List<LayoutElement> { blkA, blkB, sigA };
        var activeRouteIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "r_ab" };

        var upgraded = await svc.ApplyLookAheadAspectsAsync(activeRouteIds, new[] { route }, elements, client);

        Assert.Equal(0, upgraded);
        Assert.Equal(SignalAspect.Caution, sigA.Aspect); // žiadna zmena
        Assert.Empty(client.ExtendedAccessoryCommands);
    }

    /// <summary>
    /// Dve aktívne cesty, ale netvoriace reťaz (žiadne spoločné bloky).
    /// Žiadne upgrady.
    /// </summary>
    [Fact]
    public async Task ApplyLookAheadAspectsAsync_TwoIndependentRoutes_NoChange()
    {
        var svc = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };

        var blkA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_a" };
        var blkB = new BlockElement { Id = "blk_b", MarkerKey = "Block" };
        var blkC = new BlockElement { Id = "blk_c", MarkerKey = "Block", SignalRightId = "sig_c" };
        var blkD = new BlockElement { Id = "blk_d", MarkerKey = "Block" };

        var sigA = new SignalElement { Id = "sig_a", DccAddress = 10, IsBasicMode = false, Aspect = SignalAspect.Caution };
        var sigC = new SignalElement { Id = "sig_c", DccAddress = 30, IsBasicMode = false, Aspect = SignalAspect.Caution };

        var routeAb = new RouteDefinition { Id = "r_ab", FromBlockId = blkA.Id, ToBlockId = blkB.Id, StartNavigationDirection = RouteDirection.Right };
        var routeCd = new RouteDefinition { Id = "r_cd", FromBlockId = blkC.Id, ToBlockId = blkD.Id, StartNavigationDirection = RouteDirection.Right };

        var elements = new List<LayoutElement> { blkA, blkB, blkC, blkD, sigA, sigC };
        var activeRouteIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "r_ab", "r_cd" };

        var upgraded = await svc.ApplyLookAheadAspectsAsync(activeRouteIds, new[] { routeAb, routeCd }, elements, client);

        Assert.Equal(0, upgraded);
        Assert.Equal(SignalAspect.Caution, sigA.Aspect);
        Assert.Equal(SignalAspect.Caution, sigC.Aspect);
        Assert.Empty(client.ExtendedAccessoryCommands);
    }

    /// <summary>
    /// Reťaz s odbočkou: A→B má LowerYellow (odbočka) a B→C má Yellow.
    /// Look-ahead nesmie upgradnúť A→B z LowerYellow (odbočka má prednosť).
    /// </summary>
    [Fact]
    public async Task ApplyLookAheadAspectsAsync_DivergeRoute_SlowProceedStaysSlowProceed()
    {
        var svc = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };

        var blkA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        var blkB = new BlockElement { Id = "blk_b", MarkerKey = "Block", SignalRightId = "sig_bc" };
        var blkC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };

        // sig_ab je nastavené na SlowProceed (odbočka po trase A→B)
        var sigAb = new SignalElement { Id = "sig_ab", DccAddress = 10, IsBasicMode = false, Aspect = SignalAspect.SlowProceed };
        // sig_bc je nastavené na Caution (priama cesta B→C)
        var sigBc = new SignalElement { Id = "sig_bc", DccAddress = 20, IsBasicMode = false, Aspect = SignalAspect.Caution };

        var routeAb = new RouteDefinition
        {
            Id = "r_ab",
            FromBlockId = blkA.Id,
            ToBlockId = blkB.Id,
            StartNavigationDirection = RouteDirection.Right,
        };
        var routeBc = new RouteDefinition
        {
            Id = "r_bc",
            FromBlockId = blkB.Id,
            ToBlockId = blkC.Id,
            StartNavigationDirection = RouteDirection.Right,
        };

        var elements = new List<LayoutElement> { blkA, blkB, blkC, sigAb, sigBc };
        var activeRouteIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "r_ab", "r_bc" };

        var upgraded = await svc.ApplyLookAheadAspectsAsync(activeRouteIds, new[] { routeAb, routeBc }, elements, client);

        // SlowProceed sa NEmaní – odbočka zostáva.
        Assert.Equal(0, upgraded);
        Assert.Equal(SignalAspect.SlowProceed, sigAb.Aspect);
        Assert.Equal(SignalAspect.Caution, sigBc.Aspect);
        Assert.Empty(client.ExtendedAccessoryCommands);
    }

    /// <summary>
    /// Trojreťaz A→B→C→D: navestidlo A→B by malo dostať UpperYellowBlinking
    /// (B→C má Yellow – A→B sa upgraduje), navestidlo B→C dostane UpperYellowBlinking
    /// ak C→D je Yellow.
    /// Výsledok pre trojreťaz: A→B = UpperYellowBlinking, B→C = UpperYellowBlinking (cez C→D).
    /// </summary>
    [Fact]
    public async Task ApplyLookAheadAspectsAsync_ThreeChainRoutes_UpgradesFirstTwo()
    {
        var svc = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };

        var blkA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        var blkB = new BlockElement { Id = "blk_b", MarkerKey = "Block", SignalRightId = "sig_bc" };
        var blkC = new BlockElement { Id = "blk_c", MarkerKey = "Block", SignalRightId = "sig_cd" };
        var blkD = new BlockElement { Id = "blk_d", MarkerKey = "Block" };

        var sigAb = new SignalElement { Id = "sig_ab", DccAddress = 10, IsBasicMode = false, Aspect = SignalAspect.Caution };
        var sigBc = new SignalElement { Id = "sig_bc", DccAddress = 20, IsBasicMode = false, Aspect = SignalAspect.Caution };
        var sigCd = new SignalElement { Id = "sig_cd", DccAddress = 30, IsBasicMode = false, Aspect = SignalAspect.Caution };

        var routeAb = new RouteDefinition { Id = "r_ab", FromBlockId = blkA.Id, ToBlockId = blkB.Id, StartNavigationDirection = RouteDirection.Right };
        var routeBc = new RouteDefinition { Id = "r_bc", FromBlockId = blkB.Id, ToBlockId = blkC.Id, StartNavigationDirection = RouteDirection.Right };
        var routeCd = new RouteDefinition { Id = "r_cd", FromBlockId = blkC.Id, ToBlockId = blkD.Id, StartNavigationDirection = RouteDirection.Right };

        var elements = new List<LayoutElement> { blkA, blkB, blkC, blkD, sigAb, sigBc, sigCd };
        var activeRouteIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "r_ab", "r_bc", "r_cd" };
        var allRoutes = new List<RouteDefinition> { routeAb, routeBc, routeCd };

        var upgraded = await svc.ApplyLookAheadAspectsAsync(activeRouteIds, allRoutes, elements, client);

        // A→B: B→C má Yellow – A→B upgradí na UpperYellowBlinking.
        // B→C: C→D má Yellow – B→C upgradí na UpperYellowBlinking.
        // C→D: D nemá ďalšiu cestu – zostáva Yellow.
        Assert.Equal(2, upgraded);
        Assert.Equal(SignalAspect.SlowExpect40, sigAb.Aspect);
        Assert.Equal(SignalAspect.SlowExpect40, sigBc.Aspect);
        Assert.Equal(SignalAspect.Caution, sigCd.Aspect);
    }

    /// <summary>
    /// Reťaz A→B (Yellow) a B→C aktívna a sigBc=Stop (obsadený blok, safety).
    /// Stop nie je "restricted" vo zmysle look-ahead – look-ahead sa netýka Stop.
    /// Navestidlo A→B zostáva Yellow.
    /// </summary>
    [Fact]
    public async Task ApplyLookAheadAspectsAsync_OnwardSignalIsRed_NoUpgrade()
    {
        var svc = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };

        var blkA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        var blkB = new BlockElement { Id = "blk_b", MarkerKey = "Block", SignalRightId = "sig_bc" };
        var blkC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };

        var sigAb = new SignalElement { Id = "sig_ab", DccAddress = 10, IsBasicMode = false, Aspect = SignalAspect.Caution };
        // sigBc je Stop (fall-safe stav – obsadený blok C alebo deaktivovaná cesta).
        var sigBc = new SignalElement { Id = "sig_bc", DccAddress = 20, IsBasicMode = false, Aspect = SignalAspect.Stop };

        var routeAb = new RouteDefinition { Id = "r_ab", FromBlockId = blkA.Id, ToBlockId = blkB.Id, StartNavigationDirection = RouteDirection.Right };
        var routeBc = new RouteDefinition { Id = "r_bc", FromBlockId = blkB.Id, ToBlockId = blkC.Id, StartNavigationDirection = RouteDirection.Right };

        var elements = new List<LayoutElement> { blkA, blkB, blkC, sigAb, sigBc };
        var activeRouteIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "r_ab", "r_bc" };

        var upgraded = await svc.ApplyLookAheadAspectsAsync(activeRouteIds, new[] { routeAb, routeBc }, elements, client);

        Assert.Equal(0, upgraded);
        // Stop nie je restricted pre look-ahead – A→B zostáva Yellow.
        Assert.Equal(SignalAspect.Caution, sigAb.Aspect);
    }

    /// <summary>
    /// Reťaz kde spojovací blok nemá nastavené SignalRightId – look-ahead sa nespustí
    /// pre cestu s chýbajúcim navestidlom (bezpečné správanie).
    /// </summary>
    [Fact]
    public async Task ApplyLookAheadAspectsAsync_MissingSignalOnOnwardRoute_NoUpgrade()
    {
        var svc = new SignalController();
        var client = new TestDccCentralClient { IsConnected = true };

        var blkA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        // blkB nemá nastavené SignalRightId – onwardRoute B→C nemá navestidlo.
        var blkB = new BlockElement { Id = "blk_b", MarkerKey = "Block" };
        var blkC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };

        var sigAb = new SignalElement { Id = "sig_ab", DccAddress = 10, IsBasicMode = false, Aspect = SignalAspect.Caution };

        var routeAb = new RouteDefinition { Id = "r_ab", FromBlockId = blkA.Id, ToBlockId = blkB.Id, StartNavigationDirection = RouteDirection.Right };
        var routeBc = new RouteDefinition { Id = "r_bc", FromBlockId = blkB.Id, ToBlockId = blkC.Id, StartNavigationDirection = RouteDirection.Right };

        var elements = new List<LayoutElement> { blkA, blkB, blkC, sigAb };
        var activeRouteIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "r_ab", "r_bc" };

        var upgraded = await svc.ApplyLookAheadAspectsAsync(activeRouteIds, new[] { routeAb, routeBc }, elements, client);

        Assert.Equal(0, upgraded);
        Assert.Equal(SignalAspect.Caution, sigAb.Aspect);
    }
}


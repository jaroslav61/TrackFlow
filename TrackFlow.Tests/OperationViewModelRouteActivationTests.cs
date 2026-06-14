using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.ViewModels.Operation;
using Xunit;

namespace TrackFlow.Tests;

public class OperationViewModelRouteActivationTests
{
    [Fact]
    public async Task MoveLocomotiveByRouteElementAsync_SpustiPresunPodlaVybratejCesty()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };
        var locos = new ObservableCollection<Locomotive> { loco };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        var routeMarker = new RouteElement
        {
            MarkerKey = "Route",
            SelectedRouteDefinitionId = "r_main"
        };

        var result = await vm.MoveLocomotiveByRouteElementAsync(routeMarker);

        Assert.True(result.IsSuccess);
        var target = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        Assert.True(target.IsOccupied);
        Assert.Equal(loco.Code, target.AssignedLocoId);
    }

    [Fact]
    public async Task MoveLocomotiveByRouteElementAsync_LegacyBezMarkerKey_StaleFunguje()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        var legacyRouteMarker = new RouteElement
        {
            // MarkerKey �myselne ch�ba (legacy d�ta).
            SelectedRouteDefinitionId = "r_main"
        };

        var result = await vm.MoveLocomotiveByRouteElementAsync(legacyRouteMarker);

        Assert.True(result.IsSuccess);
        var target = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        Assert.True(target.IsOccupied);
        Assert.Equal(loco.Code, target.AssignedLocoId);
    }

    [Fact]
    public async Task IsRouteUiActivationEnabled_PocasAktivnejInejRoute_PovoliDalsiuNezavisluRoute()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateTwoDisjointRoutesLayout();

        var locoMain = new Locomotive("754", "Main") { IsPlacedOnTrack = true, AssignedBlockId = "blk_a" };
        var locoAux = new Locomotive("770", "Aux") { IsPlacedOnTrack = true, AssignedBlockId = "blk_c" };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { locoMain, locoAux }, locoMain);
        var layout = settings.CurrentProject.Layout;

        layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a").AssignedLocoId = locoMain.Code;
        layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_c").AssignedLocoId = locoAux.Code;

        var activation = await vm.ActivateRouteAsync("r_main");
        Assert.True(activation.IsSuccess);

        var routeMarker = new RouteElement { MarkerKey = "Route", SelectedRouteDefinitionId = "r_aux" };

        var enabled = vm.IsRouteUiActivationEnabled(routeMarker, out var reason);

        Assert.True(enabled);
        Assert.StartsWith("povolené-počas-", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void IsRouteUiActivationEnabled_BezLokomotivyNaKoncochRoute_ZostaneDisabled()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var routeMarker = new RouteElement { MarkerKey = "Route", SelectedRouteDefinitionId = "r_main" };

        var enabled = vm.IsRouteUiActivationEnabled(routeMarker, out var reason);

        Assert.False(enabled);
        Assert.Equal("na-koncoch-cesty-nie-je-vlak", reason);
    }

    [Fact]
    public async Task AssignLocomotiveToBlockAsync_SafePresun_LenPriradiLokoBezObsadeniaBlokuAjPriPripojenomKlientovi()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();
        var dccClient = new TestDccCentralClient { IsConnected = true };

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco });
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var target = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        var result = await vm.AssignLocomotiveToBlockAsync(loco.Code, target.Id, isForward: true, dccClient);

        Assert.True(result.IsSafe);
        Assert.False(source.IsOccupied);
        Assert.Null(source.AssignedLocoId);
        Assert.False(target.IsOccupied);
        Assert.Equal(loco.Code, target.AssignedLocoId);
        Assert.Equal(target.Id, loco.AssignedBlockId);
        Assert.True(target.AssignedLocoIsForward);
        Assert.Null(target.ReservedLocoId);
        Assert.False(target.IsShadowSet);
    }

    [Fact]
    public async Task AssignLocomotiveToBlockAsync_PriOdpojenejCentralneLenPriradiLokoBezObsadeniaBloku()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();
        var dccClient = new TestDccCentralClient { IsConnected = false };

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco });
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var target = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        var result = await vm.AssignLocomotiveToBlockAsync(loco.Code, target.Id, isForward: true, dccClient);

        Assert.True(result.IsSafe);
        Assert.False(source.IsOccupied);
        Assert.Null(source.AssignedLocoId);
        Assert.False(target.IsOccupied);
        Assert.Equal(loco.Code, target.AssignedLocoId);
        Assert.Equal(target.Id, loco.AssignedBlockId);
        Assert.True(target.AssignedLocoIsForward);
        Assert.Null(target.ReservedLocoId);
        Assert.False(target.IsShadowSet);
    }

    [Fact]
    public async Task AssignLocomotiveToBlockAsync_BlockedNemutujeStav()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco });
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var target = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;
        target.IsLocked = true;

        var result = await vm.AssignLocomotiveToBlockAsync(loco.Code, target.Id, isForward: true);

        Assert.False(result.IsSafe);
        Assert.Equal("assign-block-locked", result.Reason);
        Assert.True(source.IsOccupied);
        Assert.Equal(loco.Code, source.AssignedLocoId);
        Assert.False(target.IsOccupied);
    }

    [Fact]
    public async Task MoveLocomotiveBetweenBlocksAsync_HappyPath_PresunieLokoDoCiela()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_b"
        };
        var locos = new ObservableCollection<Locomotive> { loco };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, "blk_a", "blk_b");

        Assert.True(result.IsSuccess);

        var target = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        Assert.False(source.IsOccupied);
        Assert.Null(source.AssignedLocoId);
        Assert.True(target.IsOccupied);
        Assert.Equal(loco.Code, target.AssignedLocoId);
        Assert.Equal("blk_b", loco.AssignedBlockId);
    }

    [Fact]
    public async Task MoveLocomotiveBetweenBlocksAsync_AutomatikaAktualizujeTargetSpeedPreDashboard()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        var speedChanges = new List<int>();
        loco.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Locomotive.TargetSpeed))
                speedChanges.Add(loco.TargetSpeed);
        };

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, "blk_a", "blk_b");

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(speedChanges);
        Assert.Contains(speedChanges, speed => speed > 0);

        var permissiveLimit = SignalController.ResolveSpeedLimitForAspect(SignalAspect.Proceed);
        Assert.All(speedChanges, speed => Assert.InRange(speed, 0, permissiveLimit));
        Assert.Equal(0, loco.TargetSpeed);
        Assert.Equal(0, loco.CurrentDisplaySpeed);
    }

    [Fact]
    public async Task MoveLocomotiveBetweenBlocksAsync_MarkeryMajuPrioritu_PrepnutieRychlostiPrebehneNaPrahoch()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var target = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        target.LengthCm = 20;
        target.FwdDistanceCm = 5;
        target.FwdBrakingCm = 10;
        target.FwdStopCm = 15;
        target.BwdDistanceCm = 5;
        target.BwdBrakingCm = 10;
        target.BwdStopCm = 15;
        target.ResSpeedKmh = 12;

        var targetSpeedChanges = new List<int>();
        loco.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Locomotive.TargetSpeed))
                targetSpeedChanges.Add(loco.TargetSpeed);
        };

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, "blk_a", "blk_b");

        Assert.True(result.IsSuccess);
        Assert.Contains(12, targetSpeedChanges);
        Assert.Contains(0, targetSpeedChanges);
    }

    [Fact]
    public async Task MoveLocomotiveBetweenBlocksAsync_BezMarkerovPriStopAspekte_PouzijeGlobalnyFallbackBrzdenia()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var target = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        // Simuluj ch�baj�ce �tartov� n�vestidlo -> route start aspekt ostane fail-safe Stop.
        source.SignalRightId = null;
        target.FwdDistanceCm = 0;
        target.FwdBrakingCm = 0;
        target.FwdStopCm = 0;

        var displayChanges = new List<int>();
        loco.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Locomotive.CurrentDisplaySpeed))
                displayChanges.Add(loco.CurrentDisplaySpeed);
        };

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, "blk_a", "blk_b");

        Assert.True(
            result.IsSuccess,
            $"{result.Reason} (blk_b: IsShadowSet={target.IsShadowSet}, ReservedLocoId='{target.ReservedLocoId}', IsOccupied={target.IsOccupied}, AssignedLocoId='{target.AssignedLocoId}')");
        Assert.Contains(displayChanges, speed => speed > 0);
        Assert.Equal(0, loco.TargetSpeed);
        Assert.Equal(0, loco.CurrentDisplaySpeed);
    }

    [Fact]
    public async Task MoveLocomotiveBetweenBlocksAsync_CurrentDisplaySpeedRampujePoMalomKroku()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        var displayChanges = new List<int>();
        loco.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Locomotive.CurrentDisplaySpeed))
                displayChanges.Add(loco.CurrentDisplaySpeed);
        };

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, "blk_a", "blk_b");

        Assert.True(result.IsSuccess);
        Assert.Contains(displayChanges, s => s > 0);

        for (int i = 1; i < displayChanges.Count; i++)
        {
            var delta = displayChanges[i] - displayChanges[i - 1];
            if (delta < 0)
                delta = -delta;
            Assert.InRange(delta, 0, 3);
        }
    }

    [Fact]
    public async Task MoveLocomotiveBetweenBlocksAsync_BezRoute_VratiRouteNotFound()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_c"
        };
        var locos = new ObservableCollection<Locomotive> { loco };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_c");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, "blk_c", "blk_b");

        Assert.False(result.IsSuccess);
        Assert.Equal("route-not-found", result.Reason);
    }

    [Fact]
    public async Task MoveLocomotiveBetweenBlocksAsync_OpacnySmer_PresunJePovoleny()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_b"
        };
        var locos = new ObservableCollection<Locomotive> { loco };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, "blk_b", "blk_a");

        Assert.True(result.IsSuccess);
        var target = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        Assert.True(target.IsOccupied);
        Assert.Equal(loco.Code, target.AssignedLocoId);
        Assert.Equal("blk_a", loco.AssignedBlockId);
        Assert.True(loco.IsReverse);
        Assert.False(loco.IsForward);
        Assert.True(target.AssignedLocoIsForward);
    }

    [Fact]
    public async Task MoveLocomotiveBetweenBlocksAsync_AutoDirectionSaNezmeniAkLocoNieJeZastavena()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_b",
            TargetSpeed = 12,
            CurrentDisplaySpeed = 8,
            IsForward = true
        };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, "blk_b", "blk_a");

        Assert.True(result.IsSuccess);
        // Guard: auto zmena smeru je dovolen� len pri nulovej r�chlosti.
        Assert.True(loco.IsForward);
        Assert.False(loco.IsReverse);
    }

    [Fact]
    public async Task MoveLocomotiveBetweenBlocksAsync_PriradeneNavestidloVSmereJazdy_RiadiTraversalWindow()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateChainedLayoutWithIntermediateStopSignal(assignIntermediateSignalToDirection: true);

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);

        var layout = settings.CurrentProject.Layout;
        var blockA = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var blockC = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_c");

        blockA.AssignedLocoId = loco.Code;
        blockA.IsOccupied = true;

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, "blk_a", "blk_c");

        Assert.True(result.IsSuccess);
        Assert.True(blockC.IsOccupied);
        Assert.Equal(loco.Code, blockC.AssignedLocoId);
        Assert.Empty(vm.ActiveRouteIds);
    }

    [Fact]
    public async Task MoveLocomotiveBetweenBlocksAsync_NepriradeneNavestidloProtectsBlockId_NegatujeGateRezervacie()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateChainedLayoutWithIntermediateStopSignal(assignIntermediateSignalToDirection: false);

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);

        var layout = settings.CurrentProject.Layout;
        var blockA = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var blockC = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_c");
        var unassignedSignal = layout.Elements.OfType<SignalElement>().Single(s => s.Id == "sig_to_c");

        blockA.AssignedLocoId = loco.Code;
        blockA.IsOccupied = true;

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, "blk_a", "blk_c");

        Assert.True(result.IsSuccess);
        Assert.True(blockC.IsOccupied);
        Assert.Equal(loco.Code, blockC.AssignedLocoId);
        Assert.Equal(SignalAspect.Stop, unassignedSignal.Aspect);
    }

    [Fact]
    public async Task ApplyTailClearStateAsync_NezhadzujeProtismerneNavestidloPodlaProtectsBlockId()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateTailClearOppositeSignalLayout();

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var layout = settings.CurrentProject.Layout;
        var route = layout.Routes.Single(r => r.Id == "r_ab");
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var target = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        var segmentSignal = layout.Elements.OfType<SignalElement>().Single(s => s.Id == "sig_a_to_b");
        var oppositeSignal = layout.Elements.OfType<SignalElement>().Single(s => s.Id == "sig_opposite_to_a");

        var method = typeof(OperationViewModel).GetMethod("ApplyTailClearStateAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task)method!.Invoke(vm, new object?[] { layout, route, source, target, null, CancellationToken.None })!;
        await task;

        Assert.Equal(SignalAspect.Stop, segmentSignal.Aspect);
        Assert.Equal(SignalAspect.Proceed, oppositeSignal.Aspect);
    }

    [Fact]
    public async Task BoundaryEntry_ReleaseSourceBlokuOstavaNaviazanyNaTailClear()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var source = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A", AssignedLocoId = "754", IsOccupied = true, AssignedLocoIsForward = true };
        var target = new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B", ReservedLocoId = "754", IsShadowSet = true };
        layout.Elements.Add(source);
        layout.Elements.Add(target);

        var route = new RouteDefinition
        {
            Id = "r_ab",
            FromBlockId = source.Id,
            ToBlockId = target.Id,
            StartNavigationDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { source.Id, target.Id });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("754", "Demo") { AssignedBlockId = source.Id, IsPlacedOnTrack = true };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);

        var boundaryMethod = typeof(OperationViewModel).GetMethod("ApplyBoundaryEntryState", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(boundaryMethod);
        boundaryMethod!.Invoke(vm, new object?[] { layout, source, target, loco, loco.Code, Array.Empty<object?>() });

        Assert.True(source.IsOccupied);
        Assert.Equal(loco.Code, source.AssignedLocoId);
        Assert.True(source.IsTailClearing);
        Assert.True(target.IsOccupied);
        Assert.Equal(loco.Code, target.AssignedLocoId);
        Assert.False(target.IsShadowSet);
        Assert.Null(target.ReservedLocoId);

        var tailClearMethod = typeof(OperationViewModel).GetMethod("ApplyTailClearStateAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(tailClearMethod);
        var task = (Task)tailClearMethod!.Invoke(vm, new object?[] { layout, route, source, target, null, CancellationToken.None })!;
        await task;

        Assert.False(source.IsOccupied);
        Assert.Null(source.AssignedLocoId);
        Assert.False(source.IsTailClearing);
    }

    [Fact]
    public void Locomotive_IsDirectionSelected_NotifikujeSaPriZmeneSmeru()
    {
        var loco = new Locomotive("754", "Demo") { IsForward = true };
        var notifications = new List<string>();
        loco.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
                notifications.Add(e.PropertyName!);
        };

        loco.IsReverse = true;

        Assert.Contains(nameof(Locomotive.IsDirectionSelected), notifications);
    }

    [Fact]
    public async Task ActivateRouteAsync_BezKonfliktu_AktvujeCestuANastaviLocky()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());

        var result = await vm.ActivateRouteAsync("r_main");

        Assert.True(result.IsSuccess);
        Assert.Contains("r_main", vm.ActiveRouteIds);

        var layout = settings.CurrentProject.Layout;
        var blockA = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var blockB = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        var blockC = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_c");

        Assert.True(blockA.IsLocked);
        Assert.True(blockB.IsLocked);
        Assert.False(blockC.IsLocked);
    }

    [Fact]
    public async Task ActivateRouteAsync_PreVybranuLokoNastaviGhostRezervaciuLenPredLokomotivou()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var layout = settings.CurrentProject.Layout;
        var blockA = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var blockB = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        var blockC = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_c");
        blockA.AssignedLocoId = loco.Code;
        blockA.IsOccupied = true;
        blockA.AssignedLocoIsForward = true;

        var result = await vm.ActivateRouteAsync("r_main");

        Assert.True(result.IsSuccess);
        // Faza 2.5.3: aktualny blok loka NEMA ghosta (full opacity z AssignedLocoId).
        Assert.Null(blockA.ReservedLocoId);
        // Bloky pred lokomotivou na ceste = ghost.
        Assert.Equal(loco.Code, blockB.ReservedLocoId);
        // Mimo cesty = nic.
        Assert.Null(blockC.ReservedLocoId);
        Assert.False(blockA.IsShadowSet);
        Assert.True(blockB.IsShadowSet);
        Assert.False(blockC.IsShadowSet);
    }

    [Fact]
    public async Task ActivateRouteAsync_ZaseknutyShadow_PredAktivaciouVycistiAReReservuje()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var layout = settings.CurrentProject.Layout;
        var blockA = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var blockB = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        blockA.AssignedLocoId = loco.Code;
        blockA.IsOccupied = true;
        // Simul�cia "zaseknut�ho" Shadow stavu (flag bez vlastn�ka) z predch�dzaj�cej jazdy.
        blockB.IsShadowSet = true;
        blockB.ReservedLocoId = null;

        var result = await vm.ActivateRouteAsync("r_main");

        Assert.True(result.IsSuccess);
        // Pred aktiv�ciou bol zaseknut� Shadow vy�isten�, nasledne ReserveInitialWindow vytvoril
        // nov� platn� Shadow rezerv�ciu pre pr�ve �tartuj�cu lokomot�vu.
        Assert.Equal(loco.Code, blockB.ReservedLocoId);
        Assert.True(blockB.IsShadowSet);
    }

    [Fact]
    public async Task ActivateRouteAsync_LokoStartujeNaKoncovomBloku_OtociPoradieARezervujePredchadzajuciBlok()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blocks = Enumerable.Range(1, 8)
            .Select(i => new BlockElement
            {
                Id = $"blk_{i}",
                MarkerKey = "Block",
                Label = $"Blok {i}"
            })
            .ToList();

        var route = new RouteDefinition
        {
            Id = "r_1_8",
            Name = "Blok 1 -> Blok 8",
            FromBlockId = "blk_1",
            ToBlockId = "blk_8",
            StartNavigationDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(blocks.Select(b => b.Id));

        layout.Elements.AddRange(blocks);
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_8"
        };

        var block8 = blocks.Single(b => b.Id == "blk_8");
        var block7 = blocks.Single(b => b.Id == "blk_7");
        var block2 = blocks.Single(b => b.Id == "blk_2");
        var block1 = blocks.Single(b => b.Id == "blk_1");
        block8.AssignedLocoId = loco.Code;
        block8.IsOccupied = true;
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var result = await vm.ActivateRouteAsync("r_1_8");

        Assert.True(result.IsSuccess);
        Assert.Null(block8.ReservedLocoId);
        Assert.Equal(loco.Code, block7.ReservedLocoId);
        Assert.True(block7.IsShadowSet);
        Assert.Null(block2.ReservedLocoId);
        Assert.Null(block1.ReservedLocoId);
        Assert.True(block8.IsLocked);
        Assert.True(block7.IsLocked);
        Assert.False(block2.IsLocked);
        Assert.False(block1.IsLocked);
    }

    [Fact]
    public async Task RequestRouteAsync_PrepneSignalNaPovolujucuNavestLenPreAktivnuCestu()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var layout = settings.CurrentProject.Layout;
        var signalToB = layout.Elements.OfType<SignalElement>().Single(s => s.Id == "sig_to_b");
        signalToB.DccAddress = 101;
        signalToB.IsBasicMode = false;
        var client = new TestDccCentralClient { IsConnected = true };

        // Pred route request musi ostat v bezpecnom stave.
        Assert.Equal(SignalAspect.Stop, signalToB.Aspect);

        var result = await vm.RequestRouteAsync("r_main", client);

        Assert.True(result.IsSuccess);
        Assert.Contains("r_main", vm.ActiveRouteIds);
        Assert.Equal(SignalAspect.Proceed, signalToB.Aspect);
        Assert.Contains((101, SignalController.MapAspectToExtendedNumber(SignalAspect.Proceed)), client.ExtendedAccessoryCommands);
    }

    [Fact]
    public async Task ActivateRouteAsync_OdbočkaPredStojacimDalsimNavestidlom_OdosleIbaFinalnySlowCaution()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_na6" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", SignalRightId = "sig_next" };
        var sw = new TurnoutElement { Id = "sw_1", MarkerKey = "Turnout_R" };
        var na6 = new SignalElement
        {
            Id = "sig_na6",
            Label = "Na6",
            MarkerKey = "Signal",
            DccAddress = 101,
            IsBasicMode = false,
            Aspect = SignalAspect.Stop,
            SignalProfile = "5-aspect"
        };
        var nextSignal = new SignalElement
        {
            Id = "sig_next",
            MarkerKey = "Signal",
            Aspect = SignalAspect.Stop,
            SignalProfile = "5-aspect"
        };

        var route = new RouteDefinition
        {
            Id = "r_diverge_to_stop",
            Name = "Diverge to stop",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b" });
        route.PathElementIds.Add("sw_1");
        route.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = "sw_1", RequiredState = TurnoutState.Diverge });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, sw, na6, nextSignal });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var client = new TestDccCentralClient { IsConnected = true };

        var result = await vm.ActivateRouteAsync("r_diverge_to_stop", client);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignalAspect.SlowCaution, na6.Aspect);
        Assert.DoesNotContain((101, SignalController.MapAspectToExtendedNumber(SignalAspect.SlowProceed)), client.ExtendedAccessoryCommands);
        Assert.Equal(new[] { (101, SignalController.MapAspectToExtendedNumber(SignalAspect.SlowCaution)) }, client.ExtendedAccessoryCommands);
        Assert.Contains(TrackFlowDoctorService.Instance.Events,
            e => e.Source == "Návestidlo" && e.Message.StartsWith("Syntéza aspektu pre Na6:"));
        Assert.Contains(TrackFlowDoctorService.Instance.Events,
            e => e.Source == "Návestidlo" && e.Message == "nahadzujem návestidlo Na6 na 40 a Výstraha");
        Assert.DoesNotContain(TrackFlowDoctorService.Instance.Events,
            e => e.Message.Contains("nahadzujem návestidlo Na6 na 40 a Voľno"));
    }

    [Fact]
    public async Task UpdateTraversalSignalWindowAsync_KratkaOdbočnaCesta_BezDalsiehoSignalu_NastaviSlowProceed()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var layout = settings.CurrentProject.Layout;
        var route = layout.Routes.Single(r => r.Id == "r_branch");
        var signalToB = layout.Elements.OfType<SignalElement>().Single(s => s.Id == "sig_to_b");
        signalToB.DccAddress = 101;
        signalToB.IsBasicMode = false;
        var client = new TestDccCentralClient { IsConnected = true };

        var method = typeof(OperationViewModel).GetMethod(
            "UpdateTraversalSignalWindowAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task)method!.Invoke(vm, new object[]
        {
            layout,
            route,
            new List<string> { "blk_a", "blk_c" },
            0,
            false,
            client,
            CancellationToken.None
        })!;
        await task;

        // Cesta ide cez odbočku, ale za cieľovým blokom blk_c nie je žiadne ďalšie hlavné
        // návestidlo => NESMIE byť SlowCaution, výsledok musí byť SlowProceed.
        Assert.Equal(SignalAspect.SlowProceed, signalToB.Aspect);
        Assert.DoesNotContain((101, SignalController.MapAspectToExtendedNumber(SignalAspect.SlowCaution)), client.ExtendedAccessoryCommands);
        Assert.DoesNotContain((101, SignalController.MapAspectToExtendedNumber(SignalAspect.Caution)), client.ExtendedAccessoryCommands);
        Assert.Contains((101, SignalController.MapAspectToExtendedNumber(SignalAspect.SlowProceed)), client.ExtendedAccessoryCommands);
    }

    [Fact]
    public async Task ActivateRouteAsync_IncompatibleStartSignalProfile_FailsBeforeActivation()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", X = 24, Y = 24, Rotation = 0, BlockLengthCells = 4, SignalRightId = "sig_a" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", X = 144, Y = 24, Rotation = 0, BlockLengthCells = 4 };
        var signalA = new SignalElement
        {
            Id = "sig_a",
            Label = "S_A",
            MarkerKey = "Signal",
            Aspect = SignalAspect.Stop,
            SignalProfile = "3-aspect",
            Rotation = 90,
            DccAddress = 101,
            IsBasicMode = false
        };

        var route = new RouteDefinition
        {
            Id = "r_invalid_profile",
            Name = "Invalid profile route",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b" });
        route.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = "sw_1", RequiredState = TurnoutState.Diverge });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, signalA });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());

        var result = await vm.ActivateRouteAsync("r_invalid_profile");

        Assert.False(result.IsSuccess);
        Assert.Contains("profilom 3-aspect", result.Reason);
        Assert.Contains("40 a Voľno", result.Reason);
        Assert.Equal(result.Reason, vm.RouteActivationMessage);
        Assert.Empty(vm.ActiveRouteIds);
    }

    [Fact]
    public async Task ActivateRouteAsync_IncompatibleIntermediateRouteSignalProfile_FailsBeforeActivation()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", X = 24, Y = 24, Rotation = 0, BlockLengthCells = 4, SignalRightId = "sig_a" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", X = 144, Y = 24, Rotation = 0, BlockLengthCells = 4, SignalRightId = "sig_b" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block", X = 264, Y = 24, Rotation = 0, BlockLengthCells = 4, SignalRightId = "sig_c" };
        var blockD = new BlockElement { Id = "blk_d", MarkerKey = "Block", X = 384, Y = 24, Rotation = 0, BlockLengthCells = 4 };
        var signalA = new SignalElement { Id = "sig_a", MarkerKey = "Signal", Aspect = SignalAspect.Stop, SignalProfile = "2-aspect-main", Rotation = 90 };
        var signalB = new SignalElement { Id = "sig_b", Label = "S_B", MarkerKey = "Signal", Aspect = SignalAspect.Stop, SignalProfile = "3-aspect-entry", Rotation = 90 };
        var signalC = new SignalElement { Id = "sig_c", MarkerKey = "Signal", Aspect = SignalAspect.Stop, SignalProfile = "2-aspect-main", Rotation = 90 };

        var route = new RouteDefinition
        {
            Id = "r_invalid_intermediate_profile",
            Name = "Invalid intermediate profile route",
            FromBlockId = "blk_a",
            ToBlockId = "blk_d",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c", "blk_d" });
        route.RouteSignalIds.AddRange(new[] { "sig_a", "sig_b", "sig_c" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, blockD, signalA, signalB, signalC });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());

        var result = await vm.ActivateRouteAsync("r_invalid_intermediate_profile");

        Assert.False(result.IsSuccess);
        Assert.Contains("S_B", result.Reason);
        Assert.Contains("Výstraha", result.Reason);
        Assert.Equal(result.Reason, vm.RouteActivationMessage);
        Assert.Empty(vm.ActiveRouteIds);
    }

    [Fact]
    public async Task ActivateRouteAsync_ShuntingIntermediateRouteSignal_FailsBeforeActivation()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", X = 24, Y = 24, Rotation = 0, BlockLengthCells = 4, SignalRightId = "sig_a" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", X = 144, Y = 24, Rotation = 0, BlockLengthCells = 4, SignalRightId = "sig_b" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block", X = 264, Y = 24, Rotation = 0, BlockLengthCells = 4 };
        var signalA = new SignalElement { Id = "sig_a", MarkerKey = "Signal", Aspect = SignalAspect.Stop, SignalProfile = "2-aspect-main", Rotation = 90 };
        var signalB = new SignalElement { Id = "sig_b", Label = "S_SH", MarkerKey = "Signal", Aspect = SignalAspect.Stop, SignalProfile = "2-aspect-shunt", Rotation = 90 };

        var route = new RouteDefinition
        {
            Id = "r_invalid_shunt_profile",
            Name = "Invalid shunt profile route",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });
        route.RouteSignalIds.AddRange(new[] { "sig_a", "sig_b" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, signalA, signalB });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());

        var result = await vm.ActivateRouteAsync("r_invalid_shunt_profile");

        Assert.False(result.IsSuccess);
        Assert.Contains("S_SH", result.Reason);
        Assert.Contains("nie je vhodný pre vlakovú cestu", result.Reason);
        Assert.Equal(result.Reason, vm.RouteActivationMessage);
        Assert.Empty(vm.ActiveRouteIds);
    }

    [Fact]
    public async Task UpdateTraversalSignalWindowAsync_OdbočkaPredDalsimPovolujucimNavestidlom_NegenerujeSlowCaution()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_na6" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", SignalRightId = "sig_next" };
        var sw = new TurnoutElement { Id = "sw_1", MarkerKey = "Turnout_R" };
        var na6 = new SignalElement
        {
            Id = "sig_na6",
            Label = "Na6",
            MarkerKey = "Signal",
            DccAddress = 101,
            IsBasicMode = false,
            Aspect = SignalAspect.Stop,
            SignalProfile = "5-aspect"
        };
        var nextSignal = new SignalElement
        {
            Id = "sig_next",
            MarkerKey = "Signal",
            DccAddress = 102,
            Aspect = SignalAspect.Proceed,
            SignalProfile = "5-aspect"
        };

        var route = new RouteDefinition
        {
            Id = "r_diverge_to_permissive",
            Name = "Diverge to permissive",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            StartNavigationDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b" });
        route.PathElementIds.Add("sw_1");
        route.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = "sw_1", RequiredState = TurnoutState.Diverge });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, sw, na6, nextSignal });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var client = new TestDccCentralClient { IsConnected = true };

        var method = typeof(OperationViewModel).GetMethod(
            "UpdateTraversalSignalWindowAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task)method!.Invoke(vm, new object[]
        {
            layout,
            route,
            new List<string> { "blk_a", "blk_b" },
            0,
            false,
            client,
            CancellationToken.None
        })!;
        await task;

        Assert.Equal(SignalAspect.SlowProceed, na6.Aspect);
        Assert.DoesNotContain((101, SignalController.MapAspectToExtendedNumber(SignalAspect.SlowCaution)), client.ExtendedAccessoryCommands);
        Assert.Equal(new[] { (101, SignalController.MapAspectToExtendedNumber(SignalAspect.SlowProceed)) }, client.ExtendedAccessoryCommands);
        Assert.Contains(TrackFlowDoctorService.Instance.Events,
            e => e.Source == "Návestidlo"
                 && e.Message.StartsWith("Syntéza aspektu pre Na6:")
                 && e.Message.Contains("NextSignalAspect=Voľno")
                 && e.Message.Contains("NextSignalStop=False")
                 && e.Message.Contains("Výsledok=40 a Voľno"));
    }

    [Fact]
    public async Task UpdateTraversalSignalWindowAsync_DalsiSegmentMaPlanovanyProceed_NegenerujeCautionZPredsyntetickehoStop()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", SignalRightId = "sig_bc" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };
        var signalAb = new SignalElement
        {
            Id = "sig_ab",
            Label = "Na6",
            MarkerKey = "Signal",
            DccAddress = 111,
            IsBasicMode = false,
            Aspect = SignalAspect.Stop,
            SignalProfile = "5-aspect"
        };
        var signalBc = new SignalElement
        {
            Id = "sig_bc",
            Label = "NextMain",
            MarkerKey = "Signal",
            Aspect = SignalAspect.Stop,
            SignalProfile = "5-aspect"
        };

        var route = new RouteDefinition
        {
            Id = "r_chain_planned",
            Name = "Chain planned",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, signalAb, signalBc });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var client = new TestDccCentralClient { IsConnected = true };

        var method = typeof(OperationViewModel).GetMethod(
            "UpdateTraversalSignalWindowAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task)method!.Invoke(vm, new object[]
        {
            layout,
            route,
            new List<string> { "blk_a", "blk_b", "blk_c" },
            0,
            false,
            client,
            CancellationToken.None
        })!;
        await task;

        Assert.Equal(SignalAspect.Proceed, signalAb.Aspect);
        Assert.Equal(SignalAspect.Stop, signalBc.Aspect); // ďalší segment sa fyzicky ešte nenahadzuje
        Assert.DoesNotContain((111, SignalController.MapAspectToExtendedNumber(SignalAspect.Caution)), client.ExtendedAccessoryCommands);
        Assert.Equal(new[] { (111, SignalController.MapAspectToExtendedNumber(SignalAspect.Proceed)) }, client.ExtendedAccessoryCommands);
        Assert.Contains(TrackFlowDoctorService.Instance.Events,
            e => e.Source == "Návestidlo"
                 && e.Message.StartsWith("Syntéza aspektu pre Na6:")
                 && e.Message.Contains("NextSignalAspect=Voľno")
                 && e.Message.Contains("NextSignalStop=False")
                 && e.Message.Contains("Výsledok=Voľno"));
    }

    [Fact]
    public async Task RequestRouteAsync_ExplicitRouteSignals_PriOdbochkeNastaviLowerYellow()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement
        {
            Id = "blk_a",
            MarkerKey = "Block",
            SignalRightId = "sig_to_b"
        };
        var blockB = new BlockElement
        {
            Id = "blk_b",
            MarkerKey = "Block"
        };
        var sw = new TurnoutElement { Id = "sw_1", MarkerKey = "Turnout_R" };
        var signal = new SignalElement
        {
            Id = "sig_to_b",
            MarkerKey = "Signal",
            Aspect = SignalAspect.Stop,
            SignalProfile = "3-aspect-main"
        };

        var route = new RouteDefinition
        {
            Id = "r_manual",
            Name = "Manual",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b" });
        route.PathElementIds.Add("sw_1");
        route.RouteSignalIds.Add("sig_to_b");
        route.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = "sw_1", RequiredState = TurnoutState.Diverge });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, sw, signal });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var client = new TestDccCentralClient { IsConnected = true };
        signal.DccAddress = 140;
        signal.IsBasicMode = false;
        var result = await vm.RequestRouteAsync("r_manual", client);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignalAspect.SlowProceed, signal.Aspect);
        Assert.Contains((140, SignalController.MapAspectToExtendedNumber(SignalAspect.SlowProceed)), client.ExtendedAccessoryCommands);
    }

    [Fact]
    public async Task ActivateRouteAsync_ChybajuceStartoveNavestidlo_ZachovaFailSafeStop()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var layout = settings.CurrentProject.Layout;
        var blockA = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var signalToB = layout.Elements.OfType<SignalElement>().Single(s => s.Id == "sig_to_b");
        var client = new TestDccCentralClient { IsConnected = true };

        blockA.SignalRightId = null; // simulacia chybajuceho priradenia startoveho navestidla
        signalToB.DccAddress = 201;
        signalToB.IsBasicMode = false;

        var result = await vm.ActivateRouteAsync("r_main", client);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignalAspect.Stop, signalToB.Aspect);
        Assert.Empty(client.ExtendedAccessoryCommands);
    }

    [Fact]
    public async Task ActivateRouteAsync_StartNavigationDirectionIneAkoOsBloku_NeprepneDccNaReverse()
    {
        // Regression: B1 → B5 case. RouteDirection (Left/Right/Up/Down) je geometria layoutu,
        // NIE forward/reverse travel direction. Predtým AnalyzeOrientationSyncForRoute klasifikoval
        // Up/Left ako "reverse" cez IsForwardRouteDirection a tým pádom prepol DCC paket na reverse,
        // hoci traversal smer (from→to) je forward a fyzická orientácia loca sa nemení.
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var layout = settings.CurrentProject.Layout;
        var route = layout.Routes.Single(r => r.Id == "r_main");
        route.StartNavigationDirection = RouteDirection.Left;

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a",
            DccAddress = 754,
            CurrentDisplaySpeed = 0
        };

        var blockA = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        blockA.AssignedLocoId = loco.Code;
        blockA.IsOccupied = true;
        blockA.AssignedLocoIsForward = true;
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var client = new TestDccCentralClient { IsConnected = true };
        var result = await vm.ActivateRouteAsync("r_main", client);

        Assert.True(result.IsSuccess);
        // Travel direction = forward (loco at FromBlockId). Cab orientation = forward.
        // Žiadny mismatch → IsReversedByOrientation=false a DCC zostáva forward.
        Assert.False(loco.IsReversedByOrientation);
        Assert.True(loco.IsForward);
        Assert.False(loco.IsReverse);
        Assert.True(loco.IsDashboardForwardLit);
        Assert.False(loco.IsDashboardReverseLit);
        Assert.DoesNotContain(client.LocomotiveSpeedCommands, c => c.Address == 754 && c.Forward == false);
    }

    [Fact]
    public async Task ActivateRouteAsync_OrientaciaSediSoStartDirection_NastaviForwardDashboardLogiku()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var layout = settings.CurrentProject.Layout;
        var route = layout.Routes.Single(r => r.Id == "r_main");
        route.StartNavigationDirection = RouteDirection.Right;

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a",
            DccAddress = 754,
            CurrentDisplaySpeed = 0
        };

        var blockA = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        blockA.AssignedLocoId = loco.Code;
        blockA.IsOccupied = true;
        blockA.AssignedLocoIsForward = true;
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var client = new TestDccCentralClient { IsConnected = true };
        var result = await vm.ActivateRouteAsync("r_main", client);

        Assert.True(result.IsSuccess);
        Assert.False(loco.IsReversedByOrientation);
        Assert.True(loco.IsForward);
        Assert.False(loco.IsReverse);
        Assert.True(loco.IsDashboardForwardLit);
        Assert.False(loco.IsDashboardReverseLit);
        Assert.Contains(client.LocomotiveSpeedCommands, c => c.Address == 754 && c.Forward);
    }

    [Fact]
    public async Task ActivateRouteAsync_ExplicitRouteSignals_NenastaviSlowProceedNaVsetkyNavestidla()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_a" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", SignalRightId = "sig_b" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };
        var sw = new TurnoutElement { Id = "sw_1", MarkerKey = "Turnout_R" };

        var sigA = new SignalElement
        {
            Id = "sig_a",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_b",
            Aspect = SignalAspect.Stop,
            SignalProfile = "4-aspect-departure",
            DccAddress = 101,
            IsBasicMode = false
        };
        var sigB = new SignalElement
        {
            Id = "sig_b",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_c",
            Aspect = SignalAspect.Stop,
            SignalProfile = "4-aspect-departure",
            DccAddress = 102,
            IsBasicMode = false
        };

        var route = new RouteDefinition
        {
            Id = "r_chain_diverge",
            Name = "Chain diverge",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });
        route.PathElementIds.Add("sw_1");
        route.RouteSignalIds.AddRange(new[] { "sig_a", "sig_b" });
        route.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = "sw_1", RequiredState = TurnoutState.Diverge });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, sw, sigA, sigB });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var result = await vm.ActivateRouteAsync("r_chain_diverge");

        Assert.True(result.IsSuccess);

        // Startové návestidlo pri odbočke → SlowProceed.
        Assert.Equal(SignalAspect.SlowProceed, sigA.Aspect);

        // Pri aktivácii cesty (rezervácia) sa ďalšie návestidlá na trase nemenia.
        // Ich aspekt sa nastaví až pri obsadení príslušného bloku počas jazdy.
        Assert.Equal(SignalAspect.Stop, sigB.Aspect);
    }

    [Fact]
    public async Task RequestRouteAsync_ExplicitRouteSignals_VTejtoFazeNastaviLenStartovacieNavestidlo()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", X = 24, Y = 24, Rotation = 0, BlockLengthCells = 4, SignalRightId = "sig_a" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", X = 144, Y = 24, Rotation = 0, BlockLengthCells = 4, SignalRightId = "sig_b" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block", X = 264, Y = 24, Rotation = 0, BlockLengthCells = 4 };
        var seg1 = new TrackSegmentElement { Id = "seg_1", MarkerKey = "TrackSegment", X = 120, Y = 24, Rotation = 0 };
        var seg2 = new TrackSegmentElement { Id = "seg_2", MarkerKey = "TrackSegment", X = 240, Y = 24, Rotation = 0 };
        var signalA = new SignalElement { Id = "sig_a", MarkerKey = "Signal", Aspect = SignalAspect.Stop, SignalProfile = "5-aspect", Rotation = 90 };
        var signalB = new SignalElement { Id = "sig_b", MarkerKey = "Signal", Aspect = SignalAspect.Stop, SignalProfile = "3-aspect", Rotation = 90 };

        var route = new RouteDefinition
        {
            Id = "r_chain",
            Name = "Chain",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });
        route.PathElementIds.AddRange(new[] { "seg_1", "seg_2" });
        route.RouteSignalIds.AddRange(new[] { "sig_a", "sig_b" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, seg1, seg2, signalA, signalB });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var result = await vm.RequestRouteAsync("r_chain");

        Assert.True(result.IsSuccess);
        Assert.Equal(SignalAspect.Proceed, signalA.Aspect);
        Assert.Equal(SignalAspect.Stop, signalB.Aspect);
    }

    [Fact]
    public async Task ActivateRouteAsync_NezmeniDalsieNavestidlaPriRezervaciiAniAkMajuDccAdresu()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", SignalRightId = "sig_bc" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };

        var sigAb = new SignalElement
        {
            Id = "sig_ab",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_b",
            Aspect = SignalAspect.Stop,
            SignalProfile = "2-aspect-main",
            DccAddress = 101,
            IsBasicMode = false
        };
        var sigBc = new SignalElement
        {
            Id = "sig_bc",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_c",
            Aspect = SignalAspect.Stop,
            SignalProfile = "2-aspect-main",
            DccAddress = 102,
            IsBasicMode = false
        };

        var route = new RouteDefinition
        {
            Id = "r_chain",
            Name = "Chain",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });
        route.RouteSignalIds.AddRange(new[] { "sig_ab", "sig_bc" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, sigAb, sigBc });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var result = await vm.ActivateRouteAsync("r_chain");

        Assert.True(result.IsSuccess);
        Assert.NotEqual(SignalAspect.Stop, sigAb.Aspect); // štartové návestidlo sa nastaví
        Assert.Equal(SignalAspect.Stop, sigBc.Aspect);    // ďalšie až pri obsadení bloku
    }

    [Fact]
    public async Task ActivateRouteAsync_ChainRoute_VizualneAktivujeLenPrvySegment()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", X = 24, Y = 24, Rotation = 0, BlockLengthCells = 4, SignalRightId = "sig_a" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", X = 144, Y = 24, Rotation = 0, BlockLengthCells = 4, SignalRightId = "sig_b" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block", X = 264, Y = 24, Rotation = 0, BlockLengthCells = 4 };
        var seg1 = new TrackSegmentElement { Id = "seg_1", MarkerKey = "TrackSegment", X = 120, Y = 24, Rotation = 0 };
        var seg2 = new TrackSegmentElement { Id = "seg_2", MarkerKey = "TrackSegment", X = 240, Y = 24, Rotation = 0 };
        var signalA = new SignalElement { Id = "sig_a", MarkerKey = "Signal", Aspect = SignalAspect.Stop, SignalProfile = "5-aspect", Rotation = 90 };
        var signalB = new SignalElement { Id = "sig_b", MarkerKey = "Signal", Aspect = SignalAspect.Stop, SignalProfile = "3-aspect", Rotation = 90 };

        var route = new RouteDefinition
        {
            Id = "r_chain",
            Name = "Chain",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });
        route.PathElementIds.AddRange(new[] { "seg_1", "seg_2" });
        route.RouteSignalIds.AddRange(new[] { "sig_a", "sig_b" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, seg1, seg2, signalA, signalB });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var result = await vm.ActivateRouteAsync("r_chain");

        Assert.True(result.IsSuccess);
        Assert.True(vm.IsElementOnActiveRoutePath("seg_1"));
        Assert.False(vm.IsElementOnActiveRoutePath("seg_2"));
        Assert.True(vm.IsElementOnActiveRoutePath("blk_a"));
        Assert.True(vm.IsElementOnActiveRoutePath("blk_b"));
        Assert.False(vm.IsElementOnActiveRoutePath("blk_c"));
    }

    [Fact]
    public void SetTraversalSegmentWindow_PoPosuneFrontierTrimneBlokyZaVlakom()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        layout.Elements.AddRange(new LayoutElement[]
        {
            new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A" },
            new BlockElement { Id = "blk_x", MarkerKey = "Block", Label = "X" },
            new BlockElement { Id = "blk_y", MarkerKey = "Block", Label = "Y" },
            new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B" }
        });

        var route = new RouteDefinition
        {
            Id = "r_frontier_trim",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_x", "blk_y", "blk_b" });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var method = typeof(OperationViewModel).GetMethod("SetTraversalSegmentWindow", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(vm, new object[]
        {
            layout,
            route,
            new List<string> { "blk_a", "blk_x", "blk_y", "blk_b" },
            1,
            false
        });

        Assert.False(vm.IsElementOnActiveRoutePath("blk_a"));
        Assert.True(vm.IsElementOnActiveRoutePath("blk_x"));
        Assert.True(vm.IsElementOnActiveRoutePath("blk_y"));
        Assert.False(vm.IsElementOnActiveRoutePath("blk_b"));
    }

    [Fact]
    public void SetTraversalSegmentWindow_NaFinalnomLeadNeobnoviCelyRoutePath()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        layout.Elements.AddRange(new LayoutElement[]
        {
            new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A" },
            new BlockElement { Id = "blk_x", MarkerKey = "Block", Label = "X" },
            new BlockElement { Id = "blk_y", MarkerKey = "Block", Label = "Y" },
            new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B" }
        });

        var route = new RouteDefinition
        {
            Id = "r_final_frontier",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_x", "blk_y", "blk_b" });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var method = typeof(OperationViewModel).GetMethod("SetTraversalSegmentWindow", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(vm, new object[]
        {
            layout,
            route,
            new List<string> { "blk_a", "blk_x", "blk_y", "blk_b" },
            3,
            false
        });

        Assert.False(vm.IsElementOnActiveRoutePath("blk_a"));
        Assert.False(vm.IsElementOnActiveRoutePath("blk_x"));
        Assert.False(vm.IsElementOnActiveRoutePath("blk_y"));
        Assert.True(vm.IsElementOnActiveRoutePath("blk_b"));
    }

    [Fact]
    public void IsElementOnActiveRoutePath_BezReservationWindowPouzijeRuntimeFrontierNamiestoFullRouteFallbacku()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        layout.Elements.AddRange(new LayoutElement[]
        {
            new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A" },
            new BlockElement { Id = "blk_x", MarkerKey = "Block", Label = "X" },
            new BlockElement { Id = "blk_y", MarkerKey = "Block", Label = "Y" },
            new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B" },
            new TrackSegmentElement { Id = "seg_xy", MarkerKey = "TrackSegment" }
        });

        var route = new RouteDefinition
        {
            Id = "r_ui_frontier",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_x", "blk_y", "blk_b" });
        route.PathElementIds.Add("seg_xy");
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());

        var runtimeRegistryField = typeof(OperationViewModel).GetField("_runtimeRegistry", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(runtimeRegistryField);
        var runtimeRegistry = runtimeRegistryField!.GetValue(vm);
        Assert.NotNull(runtimeRegistry);

        var registryType = runtimeRegistry!.GetType();
        registryType.GetMethod("RegisterOrCreateRuntime")!.Invoke(runtimeRegistry, new object?[]
        {
            route.Id,
            null,
            new[] { "blk_a", "blk_x", "blk_y", "blk_b" },
            2,
            "blk_y"
        });
        registryType.GetMethod("MarkTailClear")!.Invoke(runtimeRegistry, new object?[]
        {
            route.Id,
            "blk_x",
            "blk_y",
            DateTime.UtcNow
        });

        Assert.False(vm.IsElementOnActiveRoutePath("blk_a"));
        Assert.False(vm.IsElementOnActiveRoutePath("blk_x"));
        Assert.True(vm.IsElementOnActiveRoutePath("blk_y"));
        Assert.True(vm.IsElementOnActiveRoutePath("blk_b"));
        Assert.False(vm.IsElementOnActiveRoutePath("seg_xy"));

        Assert.DoesNotContain(TrackFlowDoctorService.Instance.Events,
            e => e.Source == "Prevádzka" && e.Message.Contains("[MULTI][UI-HIGHLIGHT]"));
    }

    [Fact]
    public async Task MoveLocomotiveBetweenBlocksAsync_ChainRoute_PoDokonceniVsetkySegmentoveNavestidlaPadnuNaStop()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", X = 24, Y = 24, Rotation = 0, BlockLengthCells = 4, SignalRightId = "sig_a" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", X = 144, Y = 24, Rotation = 0, BlockLengthCells = 4, SignalRightId = "sig_b" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block", X = 264, Y = 24, Rotation = 0, BlockLengthCells = 4 };
        var seg1 = new TrackSegmentElement { Id = "seg_1", MarkerKey = "TrackSegment", X = 120, Y = 24, Rotation = 0 };
        var seg2 = new TrackSegmentElement { Id = "seg_2", MarkerKey = "TrackSegment", X = 240, Y = 24, Rotation = 0 };
        var signalA = new SignalElement { Id = "sig_a", MarkerKey = "Signal", Aspect = SignalAspect.Stop, SignalProfile = "5-aspect", Rotation = 90 };
        var signalB = new SignalElement { Id = "sig_b", MarkerKey = "Signal", Aspect = SignalAspect.Stop, SignalProfile = "3-aspect", Rotation = 90 };

        var route = new RouteDefinition
        {
            Id = "r_chain",
            Name = "Chain",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });
        route.PathElementIds.AddRange(new[] { "seg_1", "seg_2" });
        route.RouteSignalIds.AddRange(new[] { "sig_a", "sig_b" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, seg1, seg2, signalA, signalB });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("754", "Demo") { IsPlacedOnTrack = true, AssignedBlockId = "blk_a" };
        blockA.AssignedLocoId = loco.Code;
        blockA.IsOccupied = true;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, "blk_a", "blk_c");

        Assert.True(
            result.IsSuccess,
            $"{result.Reason} (blk_b: IsShadowSet={blockB.IsShadowSet}, ReservedLocoId='{blockB.ReservedLocoId}', IsOccupied={blockB.IsOccupied}, AssignedLocoId='{blockB.AssignedLocoId}'; " +
            $"blk_c: IsShadowSet={blockC.IsShadowSet}, ReservedLocoId='{blockC.ReservedLocoId}', IsOccupied={blockC.IsOccupied}, AssignedLocoId='{blockC.AssignedLocoId}')");
        Assert.Equal(SignalAspect.Stop, signalA.Aspect);
        Assert.Equal(SignalAspect.Stop, signalB.Aspect);
    }

    [Fact]
    public async Task SetAllSignalsRedAndPushAsync_NastaviStopPreVsetkyNavestidla()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var layout = settings.CurrentProject.Layout;
        var signalToB = layout.Elements.OfType<SignalElement>().Single(s => s.Id == "sig_to_b");

        signalToB.Aspect = SignalAspect.Proceed;

        var sent = await vm.SetAllSignalsRedAndPushAsync(dccClient: null);

        Assert.Equal(0, sent);
        Assert.Equal(SignalAspect.Stop, signalToB.Aspect);
    }

    [Fact]
    public async Task ForceSendCurrentSignalStatesAsync_PushneAktualnySnapshotBezZmenyAspektu()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());

        var sent = await vm.ForceSendCurrentSignalStatesAsync(dccClient: null);

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task HandleExternalOccupancyUpdateAsync_ObsadenieChranenehoBloku_ZhodiSignalAReleseCestu()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());
        var layout = settings.CurrentProject.Layout;
        var target = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        var signalToB = layout.Elements.OfType<SignalElement>().Single(s => s.Id == "sig_to_b");

        var route = await vm.RequestRouteAsync("r_main");
        Assert.True(route.IsSuccess);
        Assert.Equal(SignalAspect.Proceed, signalToB.Aspect);

        target.IsOccupied = true;
        await vm.HandleExternalOccupancyUpdateAsync();

        Assert.Equal(SignalAspect.Stop, signalToB.Aspect);
        Assert.DoesNotContain("r_main", vm.ActiveRouteIds);
    }

    [Fact]
    public async Task HandleExternalOccupancyUpdateAsync_OccupancyByForeignLoco_ReleasesActiveRoute()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", SignalRightId = "sig_bc" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };
        var signalAb = new SignalElement { Id = "sig_ab", MarkerKey = "Signal", ProtectsBlockId = "blk_b", DccAddress = 24 };
        var signalBc = new SignalElement { Id = "sig_bc", MarkerKey = "Signal", ProtectsBlockId = "blk_c", DccAddress = 25 };

        var route = new RouteDefinition
        {
            Id = "r_seq",
            Name = "Seq",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, signalAb, signalBc });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var loco754 = new Locomotive("754", "Assigned") { IsPlacedOnTrack = true, AssignedBlockId = "blk_a" };
        blockA.AssignedLocoId = loco754.Code;
        blockA.IsOccupied = true;
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco754 }, loco754);
        var activated = await vm.ActivateRouteAsync("r_seq");
        Assert.True(activated.IsSuccess);
        Assert.Contains("r_seq", vm.ActiveRouteIds);

        // Foreign loco enters route block -> must not be treated as assigned-route movement.
        blockA.AssignedLocoId = null;
        blockA.IsOccupied = false;
        blockB.AssignedLocoId = "FOREIGN";
        blockB.IsOccupied = true;

        await vm.HandleExternalOccupancyUpdateAsync();

        Assert.DoesNotContain("r_seq", vm.ActiveRouteIds);
        Assert.Equal("ghost-alarm", vm.RouteActivationMessage);
    }

    [Fact]
    public async Task HandleExternalOccupancyUpdateAsync_OccupancyWithoutShadow_TriggersGhostAlarmAndStopsDashboard()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block" };
        var signalAb = new SignalElement { Id = "sig_ab", MarkerKey = "Signal", ProtectsBlockId = "blk_b", DccAddress = 24 };
        var route = new RouteDefinition
        {
            Id = "r_simple",
            Name = "Simple",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, signalAb });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a",
            TargetSpeed = 25,
            CurrentDisplaySpeed = 20,
            DccAddress = 754,
            IsForward = true
        };
        blockA.AssignedLocoId = loco.Code;
        blockA.IsOccupied = true;
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var activated = await vm.ActivateRouteAsync("r_simple");
        Assert.True(activated.IsSuccess);

        blockA.AssignedLocoId = null;
        blockA.IsOccupied = false;
        blockB.ReservedLocoId = null;
        blockB.IsOccupied = true;

        var client = new TestDccCentralClient { IsConnected = true };
        await vm.HandleExternalOccupancyUpdateAsync(client);

        Assert.Equal("ghost-alarm", vm.RouteActivationMessage);
        Assert.Empty(vm.ActiveRouteIds);
        Assert.Equal(0, loco.TargetSpeed);
        Assert.Equal(0, loco.CurrentDisplaySpeed);
        Assert.Equal(1, client.EmergencyStopCalls);
        Assert.Contains(client.LocomotiveSpeedCommands, c => c.Address == 754 && c.Speed == 0);
    }

    [Fact]
    public async Task HandleExternalOccupancyUpdateAsync_DynamicReservation_ReservesOnlyImmediateNextBlockAndKeepsLeadSignalPermissive()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", SignalRightId = "sig_bc" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block", SignalRightId = "sig_cd" };
        var blockD = new BlockElement { Id = "blk_d", MarkerKey = "Block" };
        var signalAb = new SignalElement { Id = "sig_ab", MarkerKey = "Signal", ProtectsBlockId = "blk_b", DccAddress = 24 };
        var signalBc = new SignalElement { Id = "sig_bc", MarkerKey = "Signal", ProtectsBlockId = "blk_c", DccAddress = 25 };
        var signalCd = new SignalElement { Id = "sig_cd", MarkerKey = "Signal", ProtectsBlockId = "blk_d", DccAddress = 26 };

        var route = new RouteDefinition
        {
            Id = "r_long",
            Name = "Long",
            FromBlockId = "blk_a",
            ToBlockId = "blk_d",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c", "blk_d" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, blockD, signalAb, signalBc, signalCd });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("754", "Demo") { IsPlacedOnTrack = true, AssignedBlockId = "blk_a" };
        blockA.AssignedLocoId = loco.Code;
        blockA.IsOccupied = true;
        blockA.AssignedLocoIsForward = true;
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var activated = await vm.ActivateRouteAsync("r_long");
        Assert.True(activated.IsSuccess);

        blockA.AssignedLocoId = null;
        blockA.IsOccupied = false;
        blockB.AssignedLocoId = loco.Code;
        blockB.IsOccupied = true;
        loco.AssignedBlockId = "blk_b";

        await vm.HandleExternalOccupancyUpdateAsync();

        Assert.Equal(loco.Code, blockC.ReservedLocoId);
        Assert.Null(blockD.ReservedLocoId);
        Assert.NotEqual(SignalAspect.Stop, signalBc.Aspect);
    }

    [Fact]
    public async Task HandleExternalOccupancyUpdateAsync_OccupiedNextBlock_ImmediatelyReleasesPreviousBlockAndDropsDepartureSignalToStop()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", SignalRightId = "sig_bc" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };

        // Odchodové návestidlo zo segmentu A->B (chráni vstup do B)
        var signalAb = new SignalElement { Id = "sig_ab", MarkerKey = "Signal", ProtectsBlockId = "blk_b", DccAddress = 24 };
        var signalBc = new SignalElement { Id = "sig_bc", MarkerKey = "Signal", ProtectsBlockId = "blk_c", DccAddress = 25 };

        var route = new RouteDefinition
        {
            Id = "r_seq_release",
            Name = "SeqRelease",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, signalAb, signalBc });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("754", "Demo") { IsPlacedOnTrack = true, AssignedBlockId = "blk_a" };
        blockA.AssignedLocoId = loco.Code;
        blockA.IsOccupied = true;
        blockA.AssignedLocoIsForward = true;

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var activated = await vm.ActivateRouteAsync(route.Id);
        Assert.True(activated.IsSuccess);
        Assert.NotEqual(SignalAspect.Stop, signalAb.Aspect); // štartový aspekt musí byť povoľujúci

        // Simulácia: senzor už hlási obsadený blok B, ale blok A ešte stále hlási obsadenie
        // (požiadavka: A sa musí okamžite uvoľniť a odchodové návestidlo A->B musí okamžite ísť na STOJ).
        blockB.AssignedLocoId = loco.Code;
        blockB.IsOccupied = true;
        loco.AssignedBlockId = "blk_b";

        await vm.HandleExternalOccupancyUpdateAsync();

        Assert.False(blockA.IsOccupied);
        Assert.Null(blockA.AssignedLocoId);
        Assert.Equal(SignalAspect.Stop, signalAb.Aspect);
        Assert.NotEqual(SignalAspect.Stop, signalBc.Aspect);
    }

    [Fact]
    public async Task HandleExternalOccupancyUpdateAsync_PriRiadenomVjazdePosunieGhostOknoAUvolniBlokZaLokomotivou()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };
        var signal = new SignalElement
        {
            Id = "sig_ab",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_b",
            Aspect = SignalAspect.Stop,
            SignalProfile = "2-aspect-main"
        };

        var route = new RouteDefinition
        {
            Id = "r_seq",
            Name = "Seq",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, signal });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };

        blockA.AssignedLocoId = loco.Code;
        blockA.IsOccupied = true;
        blockA.AssignedLocoIsForward = false; // �elo v�avo
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var activated = await vm.ActivateRouteAsync("r_seq");
        Assert.True(activated.IsSuccess);
        // Po aktivacii: A je obsadeny (full), B ma Shadow (N+1), A ani C nemaju Shadow.
        Assert.Null(blockA.ReservedLocoId);
        Assert.Equal(loco.Code, blockB.ReservedLocoId);
        Assert.Null(blockC.ReservedLocoId);
        // Fixna fyzicka orientacia (false = celo vlavo) sa preniesla na Shadow rezervaciu v B.
        Assert.False(blockB.ReservedLocoIsForward);
        // Lock window: iba A + B (current + next).
        Assert.True(blockA.IsLocked);
        Assert.True(blockB.IsLocked);
        Assert.False(blockC.IsLocked);

        // Simul�cia vjazdu do B cez extern� occupancy (napr. senzor).
        blockA.AssignedLocoId = null;
        blockA.IsOccupied = false;
        blockB.AssignedLocoId = loco.Code;
        blockB.IsOccupied = true;
        loco.AssignedBlockId = "blk_b";

        await vm.HandleExternalOccupancyUpdateAsync();

        Assert.Contains("r_seq", vm.ActiveRouteIds);
        // A je za lokomotivou - mus� by� �plne uvo�nen� (�iadny ghost, �iadny lock).
        Assert.Null(blockA.ReservedLocoId);
        Assert.False(blockA.IsOccupied);
        Assert.False(blockA.IsLocked);
        // B je current = full, �iadny ghost. Fixna orientacia zachovan�.
        Assert.Null(blockB.ReservedLocoId);
        Assert.False(blockB.AssignedLocoIsForward);
        // C je ahead = ghost s p�vodnou orient�ciou.
        Assert.Equal(loco.Code, blockC.ReservedLocoId);
        Assert.False(blockC.ReservedLocoIsForward);
        // Lock window sa posunul: B + C.
        Assert.True(blockB.IsLocked);
        Assert.True(blockC.IsLocked);
    }

    [Fact]
    public async Task HandleExternalOccupancyUpdateAsync_PriPosuneGhostOkna_ResetujeShadowFlagNaCurrentAJehoHistorii()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_ab" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };
        var signal = new SignalElement
        {
            Id = "sig_ab",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_b",
            Aspect = SignalAspect.Stop,
            SignalProfile = "2-aspect-main"
        };

        var route = new RouteDefinition
        {
            Id = "r_seq",
            Name = "Seq",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });

        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, blockC, signal });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };

        blockA.AssignedLocoId = loco.Code;
        blockA.IsOccupied = true;
        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive> { loco }, loco);
        var activated = await vm.ActivateRouteAsync("r_seq");
        Assert.True(activated.IsSuccess);
        Assert.False(blockA.IsShadowSet);
        Assert.True(blockB.IsShadowSet);
        Assert.False(blockC.IsShadowSet);

        blockA.AssignedLocoId = null;
        blockA.IsOccupied = false;
        blockB.AssignedLocoId = loco.Code;
        blockB.IsOccupied = true;
        loco.AssignedBlockId = "blk_b";

        await vm.HandleExternalOccupancyUpdateAsync();

        Assert.False(blockA.IsShadowSet);
        Assert.False(blockB.IsShadowSet);
        Assert.True(blockC.IsShadowSet);
    }

    [Fact]
    public async Task ActivateRouteAsync_Konflikt_PovoliParalelnuAktivaciu()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());

        var first = await vm.ActivateRouteAsync("r_main");
        var second = await vm.ActivateRouteAsync("r_branch");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, vm.ActiveRouteIds.Count);
        Assert.Contains("r_main", vm.ActiveRouteIds);
        Assert.Contains("r_branch", vm.ActiveRouteIds);
    }

    [Fact]
    public async Task MoveLocomotiveBetweenBlocksAsync_ZrusenieJednejCesty_NedeaktivujeOstatneAktivneCesty()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateTwoDisjointRoutesLayout();

        var loco = new Locomotive("754", "Demo")
        {
            IsPlacedOnTrack = true,
            AssignedBlockId = "blk_a"
        };

        var vm = new OperationViewModel(
            settings,
            new ObservableCollection<Locomotive> { loco },
            movementDelayAsync: (_, _) => throw new OperationCanceledException());
        vm.SelectedLoco = loco;

        var layout = settings.CurrentProject.Layout;
        var source = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        source.AssignedLocoId = loco.Code;
        source.IsOccupied = true;

        var otherActivation = await vm.ActivateRouteAsync("r_aux");
        Assert.True(otherActivation.IsSuccess);

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, "blk_a", "blk_b");

        Assert.False(result.IsSuccess);
        Assert.Equal("cancelled", result.Reason);
        Assert.DoesNotContain("r_main", vm.ActiveRouteIds);
        Assert.Contains("r_aux", vm.ActiveRouteIds);
    }

    [Fact]
    public async Task DeactivateRoute_OdomkneBlokyNeaktivnejCesty()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());

        await vm.ActivateRouteAsync("r_main");
        vm.DeactivateRoute("r_main");

        var layout = settings.CurrentProject.Layout;
        var signalToB = layout.Elements.OfType<SignalElement>().Single(s => s.Id == "sig_to_b");
        Assert.Empty(vm.ActiveRouteIds);
        Assert.All(layout.Elements.OfType<BlockElement>(), b => Assert.False(b.IsLocked));
        Assert.Equal(SignalAspect.Stop, signalToB.Aspect);
    }

    [Fact]
    public async Task DeactivateAllRoutes_VratiRouteSignalsNaSafetyFallback()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        settings.CurrentProject!.Layout = CreateLayout();

        var vm = CreateOperationViewModel(settings, new ObservableCollection<Locomotive>());

        var activated = await vm.ActivateRouteAsync("r_main");
        Assert.True(activated.IsSuccess);

        vm.DeactivateAllRoutes();

        var layout = settings.CurrentProject.Layout;
        var signalToB = layout.Elements.OfType<SignalElement>().Single(s => s.Id == "sig_to_b");
        Assert.Empty(vm.ActiveRouteIds);
        Assert.Equal(SignalAspect.Stop, signalToB.Aspect);
    }

    private static OperationViewModel CreateOperationViewModel(
        SettingsManager settings,
        ObservableCollection<Locomotive> locomotives,
        Locomotive? selectedLoco = null)
    {
        var vm = new OperationViewModel(settings, locomotives, movementDelayAsync: (_, _) => Task.CompletedTask);
        if (selectedLoco != null)
            vm.SelectedLoco = selectedLoco;
        return vm;
    }

    private static TrackLayout CreateLayout()
    {
        var blkA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_to_b" };
        var blkB = new BlockElement { Id = "blk_b", MarkerKey = "Block" };
        var blkC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };
        var sw = new TurnoutElement { Id = "sw_1", MarkerKey = "Turnout_R" };
        var sigToB = new SignalElement
        {
            Id = "sig_to_b",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_b",
            Aspect = SignalAspect.Stop,
            SignalProfile = "2-aspect-main"
        };

        var routeMain = new RouteDefinition
        {
            Id = "r_main",
            Name = "Main",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            StartNavigationDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        routeMain.BlockIds.AddRange(new[] { "blk_a", "blk_b" });
        routeMain.PathElementIds.Add("sw_1");
        routeMain.TurnoutSettings.Add(new RouteTurnoutSetting
        {
            TurnoutId = "sw_1",
            RequiredState = TurnoutState.Straight
        });

        var routeBranch = new RouteDefinition
        {
            Id = "r_branch",
            Name = "Branch",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        routeBranch.BlockIds.AddRange(new[] { "blk_a", "blk_c" });
        routeBranch.PathElementIds.Add("sw_1");
        routeBranch.TurnoutSettings.Add(new RouteTurnoutSetting
        {
            TurnoutId = "sw_1",
            RequiredState = TurnoutState.Diverge
        });

        var layout = new TrackLayout();
        layout.Elements.AddRange(new LayoutElement[] { blkA, blkB, blkC, sw, sigToB });
        layout.Routes.Add(routeMain);
        layout.Routes.Add(routeBranch);
        return layout;
    }

    private static TrackLayout CreateChainedLayoutWithIntermediateStopSignal(bool assignIntermediateSignalToDirection = true)
    {
        var blkA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_to_b" };
        // Segment B->C má nakonfigurované segmentové návestidlo len v teste, ktorý overuje gating.
        // Ak SignalRightId=null, samotné ProtectsBlockId nesmie stačiť na gating rezervácie.
        var blkB = new BlockElement
        {
            Id = "blk_b",
            MarkerKey = "Block",
            SignalRightId = assignIntermediateSignalToDirection ? "sig_to_c" : null
        };
        var blkC = new BlockElement { Id = "blk_c", MarkerKey = "Block" };

        // Start signal A->B (route activation ho nastaví na permissive).
        var sigToB = new SignalElement
        {
            Id = "sig_to_b",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_b",
            Aspect = SignalAspect.Stop,
            SignalProfile = "2-aspect-main"
        };

        // Intermediate signal B->C ostane na STOJ (DCC=0 => UpdateTraversalSignalWindowAsync ho neprepne).
        var sigToC = new SignalElement
        {
            Id = "sig_to_c",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_c",
            Aspect = SignalAspect.Stop,
            SignalProfile = "2-aspect-main",
            DccAddress = 1
        };

        var routeChain = new RouteDefinition
        {
            Id = "r_chain",
            Name = "Chain",
            FromBlockId = "blk_a",
            ToBlockId = "blk_c",
            StartNavigationDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        routeChain.BlockIds.AddRange(new[] { "blk_a", "blk_b", "blk_c" });

        var layout = new TrackLayout();
        layout.Elements.AddRange(new LayoutElement[] { blkA, blkB, blkC, sigToB, sigToC });
        layout.Routes.Add(routeChain);
        return layout;
    }

    private static TrackLayout CreateTwoDisjointRoutesLayout()
    {
        var blkA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_to_b" };
        var blkB = new BlockElement { Id = "blk_b", MarkerKey = "Block" };
        var blkC = new BlockElement { Id = "blk_c", MarkerKey = "Block", SignalRightId = "sig_to_d" };
        var blkD = new BlockElement { Id = "blk_d", MarkerKey = "Block" };

        var sigToB = new SignalElement
        {
            Id = "sig_to_b",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_b",
            Aspect = SignalAspect.Stop,
            SignalProfile = "2-aspect-main"
        };

        var sigToD = new SignalElement
        {
            Id = "sig_to_d",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_d",
            Aspect = SignalAspect.Stop,
            SignalProfile = "2-aspect-main"
        };

        var routeMain = new RouteDefinition
        {
            Id = "r_main",
            Name = "Main",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            StartNavigationDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        routeMain.BlockIds.AddRange(new[] { "blk_a", "blk_b" });

        var routeAux = new RouteDefinition
        {
            Id = "r_aux",
            Name = "Aux",
            FromBlockId = "blk_c",
            ToBlockId = "blk_d",
            StartNavigationDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        routeAux.BlockIds.AddRange(new[] { "blk_c", "blk_d" });

        var layout = new TrackLayout();
        layout.Elements.AddRange(new LayoutElement[] { blkA, blkB, blkC, blkD, sigToB, sigToD });
        layout.Routes.Add(routeMain);
        layout.Routes.Add(routeAux);
        return layout;
    }

    private static TrackLayout CreateTailClearOppositeSignalLayout()
    {
        var blkA = new BlockElement
        {
            Id = "blk_a",
            MarkerKey = "Block",
            SignalRightId = "sig_a_to_b"
        };
        var blkB = new BlockElement { Id = "blk_b", MarkerKey = "Block" };

        var segmentSignal = new SignalElement
        {
            Id = "sig_a_to_b",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_b",
            Aspect = SignalAspect.Proceed,
            DccAddress = 1,
            SignalProfile = "2-aspect-main"
        };

        var oppositeSignal = new SignalElement
        {
            Id = "sig_opposite_to_a",
            MarkerKey = "Signal",
            ProtectsBlockId = "blk_a",
            Aspect = SignalAspect.Proceed,
            DccAddress = 2,
            SignalProfile = "2-aspect-main"
        };

        var route = new RouteDefinition
        {
            Id = "r_ab",
            Name = "A-B",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            StartNavigationDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b" });

        var layout = new TrackLayout();
        layout.Elements.AddRange(new LayoutElement[] { blkA, blkB, segmentSignal, oppositeSignal });
        layout.Routes.Add(route);
        return layout;
    }
}

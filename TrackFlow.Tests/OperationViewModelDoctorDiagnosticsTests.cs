using System.Reflection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.ViewModels.Operation;
using Xunit;
using Xunit.Abstractions;

namespace TrackFlow.Tests;

public class OperationViewModelDoctorDiagnosticsTests
{
    private readonly ITestOutputHelper _output;

    public OperationViewModelDoctorDiagnosticsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void FormatRouteDiagnosticLabel_UsesOnlyStartAndEndBlockNames()
    {
        var layout = new TrackLayout();
        layout.Elements.Add(new BlockElement
        {
            Id = "blk-start",
            MarkerKey = "Block",
            Label = "Stanica A"
        });
        layout.Elements.Add(new BlockElement
        {
            Id = "blk-end",
            MarkerKey = "Block",
            Label = "Stanica B"
        });

        var route = new RouteDefinition
        {
            Id = "route-technical-id",
            Name = "AUTO_R_001 | interný názov",
            FromBlockId = "blk-start",
            ToBlockId = "blk-end",
            FromBlockDirection = RouteDirection.Right,
            ToBlockDirection = RouteDirection.Left
        };

        var method = typeof(OperationViewModel).GetMethod(
            "FormatRouteDiagnosticLabel",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = (string?)method?.Invoke(null, new object[] { layout, route });

        Assert.Equal("Stanica A → Stanica B", result);
    }

    [Fact]
    public void FormatRouteNameForDiagnostic_IgnoresTechnicalRouteNameAndUsesEndpoints()
    {
        var layout = new TrackLayout();
        layout.Elements.Add(new BlockElement { Id = "blk-start", MarkerKey = "Block", Label = "Stanica A" });
        layout.Elements.Add(new BlockElement { Id = "blk-end", MarkerKey = "Block", Label = "Stanica B" });

        var route = new RouteDefinition
        {
            Id = "route-technical-id",
            Name = "route_technical_001",
            FromBlockId = "blk-start",
            ToBlockId = "blk-end"
        };

        var method = typeof(OperationViewModel).GetMethod(
            "FormatRouteNameForDiagnostic",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = (string?)method?.Invoke(null, new object[] { layout, route });

        Assert.Equal("Stanica A → Stanica B", result);
    }

    [Fact]
    public void FormatActiveRouteDiagnosticLabel_PreReversePouzijeDrivingOrder()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        layout.Elements.Add(new BlockElement { Id = "blk_1", MarkerKey = "Block", Label = "Blok 1" });
        layout.Elements.Add(new BlockElement { Id = "blk_8", MarkerKey = "Block", Label = "Blok 8" });

        var route = new RouteDefinition
        {
            Id = "r_1_8",
            FromBlockId = "blk_1",
            ToBlockId = "blk_8"
        };
        route.BlockIds.AddRange(new[] { "blk_1", "blk_8" });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        SetRouteRuntime(vm, route.Id, stateName: "Active", segmentIndex: 0, currentBlockId: "blk_8", traversalBlockIds: new[] { "blk_8", "blk_1" });

        var method = typeof(OperationViewModel).GetMethod(
            "FormatActiveRouteDiagnosticLabel",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (string?)method?.Invoke(vm, new object[] { layout, route });

        Assert.Equal("Blok 8 → Blok 1", result);
    }

    [Fact]
    public void ReserveNextBlock_ZapiseDiagnostikuRezervacieBloku()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();
        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        var layout = new TrackLayout();
        layout.Elements.Add(new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "Blok 1" });
        layout.Elements.Add(new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "Blok 6" });

        var method = typeof(OperationViewModel).GetMethod("ReserveNextBlock", BindingFlags.NonPublic | BindingFlags.Instance);
        var args = new object?[] { layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b"), "754", true, "sdvfsdffsdfs", layout, "blk_a", NavigationDirection.Right, false };

        var result = (bool)method!.Invoke(vm, args)!;
        var events = TrackFlowDoctorService.Instance.Events.ToList();

        Assert.True(result);
        Assert.Contains(events,
            e => e.Source == "Dispečer" && e.Message == "Blok [Blok 6] rezervovaný [sdvfsdffsdfs]");
        Assert.Contains(events,
            e => e.Source == "Senzor" && e.Message == "Blok Blok 6 REZERVOVANÝ (Vlak: sdvfsdffsdfs)");
    }

    [Fact]
    public void ReserveNextBlock_PriObsadenomBloku_ZapiseDispatcherFailedReservation()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();
        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        var layout = new TrackLayout();
        layout.Elements.Add(new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "Blok 1" });
        layout.Elements.Add(new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "Blok 6", IsOccupied = true, AssignedLocoId = "999" });

        var method = typeof(OperationViewModel).GetMethod("ReserveNextBlock", BindingFlags.NonPublic | BindingFlags.Instance);
        var args = new object?[] { layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b"), "754", true, "Train 754", layout, "blk_a", NavigationDirection.Right, false };

        var result = (bool)method!.Invoke(vm, args)!;
        var events = TrackFlowDoctorService.Instance.Events.ToList();

        Assert.False(result);
        Assert.Contains(events,
            e => e.Source == "Dispečer" && e.Message == "Vlak [Train 754] nedokázal rezervovať blok [Blok 6]");
    }

    [Fact]
    public void ClearShadowReservation_ZapiseDispatcherRelease()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var block = new BlockElement
        {
            Id = "blk_b",
            MarkerKey = "Block",
            Label = "Blok 6",
            ReservedLocoId = "754",
            IsShadowSet = true
        };

        var method = typeof(OperationViewModel).GetMethod("ClearShadowReservation", BindingFlags.NonPublic | BindingFlags.Static);
        method!.Invoke(null, new object[] { block });
        var events = TrackFlowDoctorService.Instance.Events.ToList();

        Assert.Contains(events,
            e => e.Source == "Dispečer" && e.Message == "Vlak [754] uvoľnil blok [Blok 6]");
        Assert.False(block.IsShadowSet);
        Assert.Null(block.ReservedLocoId);
    }

    [Fact]
    public void AdvanceReservationWindow_ZapiseDispatcherAdvance()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        layout.Elements.Add(new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "Blok 1" });
        layout.Elements.Add(new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "Blok 6" });
        settings.CurrentProject!.Layout = layout;

        var route = new RouteDefinition { Id = "r_a_b", FromBlockId = "blk_a", ToBlockId = "blk_b" };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b" });
        layout.Routes.Add(route);

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        AddActiveRoute(vm, route.Id);
        SetRouteRuntime(vm, route.Id, stateName: "Active", segmentIndex: 0, currentBlockId: "blk_a");
        var method = typeof(OperationViewModel).GetMethod("AdvanceReservationWindow", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(vm, new object[] { layout, route, "754", "blk_a", true, true });
        var events = TrackFlowDoctorService.Instance.Events.ToList();

        Assert.Contains(events,
            e => e.Source == "Dispečer" && e.Message == "Blok [Blok 6] rezervovaný [Neznámy]");
    }

    [Fact]
    public async Task HandleOccupiedBlocks_NovyObsadenyBlok_ZapiseDispatcherOccupied()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var loco = new Locomotive("754", "Train 754");
        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive> { loco });
        var layout = new TrackLayout();
        layout.Elements.Add(new BlockElement
        {
            Id = "blk_occ",
            MarkerKey = "Block",
            Label = "Blok 3",
            IsOccupied = true,
            AssignedLocoId = "754"
        });

        var method = typeof(OperationViewModel).GetMethod("HandleOccupiedBlocks", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task<int>)method!.Invoke(vm, new object?[] { layout, null, default(CancellationToken), false })!;
        await task;
        var events = TrackFlowDoctorService.Instance.Events.ToList();

        Assert.Contains(events,
            e => e.Source == "Dispečer" && e.Message == "Obsadený blok [Blok 3] vlakom [Train 754]");
    }

    [Fact]
    public async Task DispatcherDiagnostics_SmokeFlow_VypiseVyslednyLog()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var currentBlock = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "B2" };
        var nextBlock = new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B3" };
        layout.Elements.Add(currentBlock);
        layout.Elements.Add(nextBlock);
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("754", "754");
        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive> { loco });
        var route = new RouteDefinition { Id = "r_a_b", FromBlockId = "blk_a", ToBlockId = "blk_b" };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b" });
        layout.Routes.Add(route);
        AddActiveRoute(vm, route.Id);
        SetRouteRuntime(vm, route.Id, stateName: "Active", segmentIndex: 0, currentBlockId: "blk_a");

        var advanceMethod = typeof(OperationViewModel).GetMethod("AdvanceReservationWindow", BindingFlags.NonPublic | BindingFlags.Instance);
        advanceMethod!.Invoke(vm, new object[] { layout, route, "754", "blk_a", true, true });

        nextBlock.IsOccupied = true;
        nextBlock.AssignedLocoId = "754";

        var handleOccupiedMethod = typeof(OperationViewModel).GetMethod("HandleOccupiedBlocks", BindingFlags.NonPublic | BindingFlags.Instance);
        var handleTask = (Task<int>)handleOccupiedMethod!.Invoke(vm, new object?[] { layout, null, default(CancellationToken), false })!;
        await handleTask;

        var clearMethod = typeof(OperationViewModel).GetMethod("ClearShadowReservation", BindingFlags.NonPublic | BindingFlags.Static);
        clearMethod!.Invoke(null, new object[] { nextBlock });

        var reserveMethod = typeof(OperationViewModel).GetMethod("ReserveNextBlock", BindingFlags.NonPublic | BindingFlags.Instance);
        _ = (bool)reserveMethod!.Invoke(vm, new object?[] { nextBlock, "754", true, "754", layout, "blk_a", NavigationDirection.Right, false })!;

        var eventsSnapshot = TrackFlowDoctorService.Instance.Events.ToList();
        var dispatcherMessages = eventsSnapshot
            .Where(e => e.Source == "Dispečer")
            .Select(e => e.Message)
            .ToList();

        _output.WriteLine("=== Dispatcher Smoke Log ===");
        foreach (var message in dispatcherMessages.AsEnumerable().Reverse())
            _output.WriteLine(message);

        Assert.Contains(dispatcherMessages, m => m == "Blok [B3] rezervovaný [754]");
        Assert.Contains(dispatcherMessages, m => m == "Obsadený blok [B3] vlakom [754]");
        Assert.Contains(dispatcherMessages, m => m == "Vlak [754] uvoľnil blok [B3]");
        Assert.Contains(dispatcherMessages, m => m == "Vlak [754] nedokázal rezervovať blok [B3]");
    }

    [Fact]
    public void AdvanceReservationWindow_PriRovnakomCurrentBlockuDruhykratIbaSkipneDuplicate()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        layout.Elements.Add(new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "Blok 1" });
        layout.Elements.Add(new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "Blok 6" });
        settings.CurrentProject!.Layout = layout;

        var route = new RouteDefinition { Id = "r_a_b", FromBlockId = "blk_a", ToBlockId = "blk_b" };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b" });
        layout.Routes.Add(route);

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        AddActiveRoute(vm, route.Id);
        SetRouteRuntime(vm, route.Id, stateName: "Active", segmentIndex: 0, currentBlockId: "blk_a");

        var method = typeof(OperationViewModel).GetMethod("AdvanceReservationWindow", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(vm, new object[] { layout, route, "754", "blk_a", true, true });
        method.Invoke(vm, new object[] { layout, route, "754", "blk_a", true, true });

        var events = TrackFlowDoctorService.Instance.Events.ToList();
        Assert.Single(events, e => e.Source == "Dispečer" && e.Message == "Blok [Blok 6] rezervovaný [Neznámy]");
    }

    [Fact]
    public async Task WaitForNextBlockReservationAsync_PriTimeoute_VycistiWaitStavABezRefreshSpamu()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route, _, targetBlock) = CreateMinimalWaitLayout();
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(
            settings,
            new ObservableCollection<Locomotive> { new("754", "754") },
            movementDelayAsync: (_, _) => Task.CompletedTask,
            waitMaxDuration: TimeSpan.Zero);

        AddActiveRoute(vm, route.Id);
        targetBlock.IsOccupied = true;

        var refreshCount = 0;
        vm.LayoutRefreshRequested += () => refreshCount++;

        var outcome = await InvokeWaitForNextBlockReservationAsync(
            vm,
            layout,
            route,
            new[] { "blk_a", "blk_b" },
            segmentIndex: 0,
            segmentTarget: targetBlock,
            locoCode: "754",
            orientationForward: true,
            travelDirection: NavigationDirection.Right,
            CancellationToken.None);

        Assert.Equal("TimedOut", outcome);
        Assert.Equal(1, refreshCount);
        Assert.Empty(GetWaitStateMap(vm));
    }

    [Fact]
    public async Task WaitForNextBlockReservationAsync_PriZruseniTokenu_VycistiWaitStav()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route, _, targetBlock) = CreateMinimalWaitLayout();
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(
            settings,
            new ObservableCollection<Locomotive> { new("754", "754") },
            movementDelayAsync: (_, _) => Task.CompletedTask,
            waitMaxDuration: TimeSpan.FromMinutes(2));

        AddActiveRoute(vm, route.Id);
        targetBlock.IsOccupied = true;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            _ = await InvokeWaitForNextBlockReservationAsync(
                vm,
                layout,
                route,
                new[] { "blk_a", "blk_b" },
                segmentIndex: 0,
                segmentTarget: targetBlock,
                locoCode: "754",
                orientationForward: true,
                travelDirection: NavigationDirection.Right,
                cts.Token);
        });

        Assert.Empty(GetWaitStateMap(vm));
    }

    [Fact]
    public async Task WaitForNextBlockReservationAsync_PriChybajucejLokomotive_VycistiWaitStavABezRefreshSpamu()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route, _, targetBlock) = CreateMinimalWaitLayout();
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(
            settings,
            new ObservableCollection<Locomotive>(),
            movementDelayAsync: (_, _) => Task.CompletedTask,
            waitMaxDuration: TimeSpan.FromMinutes(2));

        AddActiveRoute(vm, route.Id);
        targetBlock.IsOccupied = true;

        var refreshCount = 0;
        vm.LayoutRefreshRequested += () => refreshCount++;

        var outcome = await InvokeWaitForNextBlockReservationAsync(
            vm,
            layout,
            route,
            new[] { "blk_a", "blk_b" },
            segmentIndex: 0,
            segmentTarget: targetBlock,
            locoCode: "754",
            orientationForward: true,
            travelDirection: NavigationDirection.Right,
            CancellationToken.None);

        Assert.Equal("LocoMissing", outcome);
        Assert.Equal(1, refreshCount);
        Assert.Empty(GetWaitStateMap(vm));
    }

    [Fact]
    public async Task DeactivateRouteInternalAsync_PriAktivnomWaite_VycistiWaitStav()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route, _, _) = CreateMinimalWaitLayout();
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        AddActiveRoute(vm, route.Id);
        SetWaitState(vm, route.Id, "blk_b", "obsadený-blok");

        var deactivateMethod = typeof(OperationViewModel).GetMethod("DeactivateRouteInternalAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)deactivateMethod!.Invoke(vm, new object?[]
        {
            route.Id,
            false,
            null,
            default(CancellationToken),
            null,
            DiagnosticLevel.Info
        })!;
        await task;

        Assert.Empty(GetWaitStateMap(vm));
    }

    [Fact]
    public async Task ActivateRouteAsync_PriUspechu_VytvoriRouteRuntimeEntry()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route, _, _) = CreateMinimalWaitLayout();
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());

        var result = await vm.ActivateRouteAsync(route.Id);

        Assert.True(result.IsSuccess, result.Reason);

        var runtimeInfo = AssertRuntimeExists(vm, route.Id);
        Assert.Equal(route.Id, GetRuntimeProperty<string>(runtimeInfo, "RouteId"));
        Assert.Equal("Active", GetRuntimeProperty<object>(runtimeInfo, "State")?.ToString());
        Assert.Equal(0, GetRuntimeProperty<int>(runtimeInfo, "SegmentIndex"));
        Assert.Equal("blk_a", GetRuntimeProperty<string>(runtimeInfo, "CurrentBlockId"));
    }

    [Fact]
    public void EnterTraversalWait_SynchronizujeRouteRuntimeDoWaitingStavu()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        SetRouteRuntime(vm, "r_wait", stateName: "Active", segmentIndex: 0, currentBlockId: "blk_a");

        var method = typeof(OperationViewModel).GetMethod("EnterTraversalWait", BindingFlags.NonPublic | BindingFlags.Instance);
        var entered = (bool)method!.Invoke(vm, new object[] { "r_wait", "blk_b", "obsadený-blok" })!;

        Assert.True(entered);

        var runtimeInfo = AssertRuntimeExists(vm, "r_wait");
        Assert.Equal("Waiting", GetRuntimeProperty<object>(runtimeInfo, "State")?.ToString());
        Assert.Equal("blk_b", GetRuntimeProperty<string>(runtimeInfo, "WaitingBlockId"));
        Assert.Equal("obsadený-blok", GetRuntimeProperty<string>(runtimeInfo, "WaitingReason"));
        Assert.NotNull(GetRuntimeProperty<DateTime?>(runtimeInfo, "WaitingSinceUtc"));
    }

    [Fact]
    public void ExitTraversalWaitSuccess_ResetneWaitPoliaVRuntime()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        SetRouteRuntime(
            vm,
            "r_wait",
            stateName: "Waiting",
            segmentIndex: 1,
            currentBlockId: "blk_a",
            waitingBlockId: "blk_b",
            waitingReason: "obsadený-blok",
            waitingSinceUtc: DateTime.UtcNow);
        SetWaitState(vm, "r_wait", "blk_b", "obsadený-blok");

        var method = typeof(OperationViewModel).GetMethod("ExitTraversalWaitSuccess", BindingFlags.NonPublic | BindingFlags.Instance);
        var exited = (bool)method!.Invoke(vm, new object[] { "r_wait", "blk_b" })!;

        Assert.True(exited);

        var runtimeInfo = AssertRuntimeExists(vm, "r_wait");
        Assert.Equal("Active", GetRuntimeProperty<object>(runtimeInfo, "State")?.ToString());
        Assert.Null(GetRuntimeProperty<string>(runtimeInfo, "WaitingBlockId"));
        Assert.Null(GetRuntimeProperty<string>(runtimeInfo, "WaitingReason"));
        Assert.Null(GetRuntimeProperty<DateTime?>(runtimeInfo, "WaitingSinceUtc"));
    }

    [Fact]
    public async Task ActivateRouteAsync_PriInejAktivnejCeste_NezhodiJejSignalNaStoj()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route1, route2, _, signalRoute2) = CreateTwoDisjointRoutesWithSignals();
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        AddActiveRoute(vm, route2.Id);
        signalRoute2.Aspect = SignalAspect.Proceed;

        var result = await vm.ActivateRouteAsync(route1.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignalAspect.Proceed, signalRoute2.Aspect);
    }

    [Fact]
    public async Task DeactivateRouteInternalAsync_PriInejAktivnejCeste_NezhodiJejSignalNaStoj()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route1, route2, _, signalRoute2) = CreateTwoDisjointRoutesWithSignals();
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        AddActiveRoute(vm, route1.Id);
        AddActiveRoute(vm, route2.Id);
        signalRoute2.Aspect = SignalAspect.Proceed;

        var deactivateMethod = typeof(OperationViewModel).GetMethod("DeactivateRouteInternalAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)deactivateMethod!.Invoke(vm, new object?[]
        {
            route1.Id,
            false,
            null,
            default(CancellationToken),
            null,
            DiagnosticLevel.Info
        })!;
        await task;

        Assert.Equal(SignalAspect.Proceed, signalRoute2.Aspect);
        Assert.Contains(route2.Id, vm.ActiveRouteIds);
    }

    [Fact]
    public async Task DeactivateRouteInternalAsync_PriSharedBloku_NemazeRezervaciuInejAktivnejCesty()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route1, route2, sharedBlock) = CreateTwoRoutesSharingReservedBlock();
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        AddActiveRoute(vm, route1.Id);
        AddActiveRoute(vm, route2.Id);
        sharedBlock.IsShadowSet = true;
        sharedBlock.ReservedLocoId = "train-2";

        var usedByOtherMethod = typeof(OperationViewModel).GetMethod("IsBlockUsedByAnotherActiveRoute", BindingFlags.NonPublic | BindingFlags.Instance);
        var isUsedByOther = (bool)usedByOtherMethod!.Invoke(vm, new object[] { layout, route1.Id, sharedBlock.Id })!;
        Assert.True(isUsedByOther);

        var deactivateMethod = typeof(OperationViewModel).GetMethod("DeactivateRouteInternalAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)deactivateMethod!.Invoke(vm, new object?[]
        {
            route1.Id,
            false,
            null,
            default(CancellationToken),
            null,
            DiagnosticLevel.Info
        })!;
        await task;

        Assert.True(sharedBlock.IsShadowSet);
        Assert.Equal("train-2", sharedBlock.ReservedLocoId);
    }

    [Fact]
    public void ApplyActivationLockWindow_PriInejAktivnejCestePouzijeJejAktualnySegmentNamiestoStartu()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var a = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A" };
        var b = new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B", IsOccupied = true, AssignedLocoId = "train-1" };
        var c = new BlockElement { Id = "blk_c", MarkerKey = "Block", Label = "C" };
        var x = new BlockElement { Id = "blk_x", MarkerKey = "Block", Label = "X", IsOccupied = true, AssignedLocoId = "train-2" };
        var y = new BlockElement { Id = "blk_y", MarkerKey = "Block", Label = "Y" };
        layout.Elements.Add(a);
        layout.Elements.Add(b);
        layout.Elements.Add(c);
        layout.Elements.Add(x);
        layout.Elements.Add(y);

        var route1 = new RouteDefinition { Id = "r_1", FromBlockId = a.Id, ToBlockId = c.Id, StartNavigationDirection = RouteDirection.Right };
        route1.BlockIds.AddRange(new[] { a.Id, b.Id, c.Id });
        var route2 = new RouteDefinition { Id = "r_2", FromBlockId = x.Id, ToBlockId = y.Id, StartNavigationDirection = RouteDirection.Right };
        route2.BlockIds.AddRange(new[] { x.Id, y.Id });
        layout.Routes.Add(route1);
        layout.Routes.Add(route2);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        AddActiveRoute(vm, route1.Id);
        AddActiveRoute(vm, route2.Id);

        var method = typeof(OperationViewModel).GetMethod("ApplyActivationLockWindow", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(vm, new object[] { layout, route2 });

        Assert.False(a.IsLocked);
        Assert.True(b.IsLocked);
        Assert.True(c.IsLocked);
        Assert.True(x.IsLocked);
        Assert.True(y.IsLocked);
    }

    [Fact]
    public void ResetStuckShadowsBeforeActivation_CistiShadowMimoRuntimeOwnedSegmentAktivnejCesty()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var a = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A", IsOccupied = true, AssignedLocoId = "train-1" };
        var b = new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B", IsShadowSet = true, ReservedLocoId = "train-1" };
        var c = new BlockElement { Id = "blk_c", MarkerKey = "Block", Label = "C", IsShadowSet = true, ReservedLocoId = "stale-train" };
        layout.Elements.Add(a);
        layout.Elements.Add(b);
        layout.Elements.Add(c);

        var route = new RouteDefinition { Id = "r_runtime", FromBlockId = a.Id, ToBlockId = c.Id, StartNavigationDirection = RouteDirection.Right };
        route.BlockIds.AddRange(new[] { a.Id, b.Id, c.Id });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        b.IsShadowSet = true;
        b.ReservedLocoId = "train-1";
        c.IsShadowSet = true;
        c.ReservedLocoId = "stale-train";
        AddActiveRoute(vm, route.Id);
        SetRouteRuntime(vm, route.Id, stateName: "Waiting", segmentIndex: 0, currentBlockId: a.Id, waitingBlockId: b.Id, waitingReason: "obsadený-blok", waitingSinceUtc: DateTime.UtcNow);

        var resetMethod = typeof(OperationViewModel).GetMethod("ResetStuckShadowsBeforeActivation", BindingFlags.NonPublic | BindingFlags.Instance);
        resetMethod!.Invoke(vm, new object[] { layout });

        Assert.True(b.IsShadowSet);
        Assert.Equal("train-1", b.ReservedLocoId);
        Assert.False(c.IsShadowSet);
        Assert.Null(c.ReservedLocoId);
    }

    [Fact]
    public void GetRouteActiveBlockIds_PriPosunutomSegmenteZahrnieAjRuntimeAktualnyBlok()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var a = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A", IsOccupied = true, AssignedLocoId = "train-1" };
        var b = new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B" };
        var c = new BlockElement { Id = "blk_c", MarkerKey = "Block", Label = "C" };
        layout.Elements.Add(a);
        layout.Elements.Add(b);
        layout.Elements.Add(c);

        var route = new RouteDefinition { Id = "r_runtime", FromBlockId = a.Id, ToBlockId = c.Id, StartNavigationDirection = RouteDirection.Right };
        route.BlockIds.AddRange(new[] { a.Id, b.Id, c.Id });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        AddActiveRoute(vm, route.Id);
        SetRouteRuntime(vm, route.Id, stateName: "Active", segmentIndex: 0, currentBlockId: a.Id);
        SetRouteActiveWindow(vm, route.Id, pathElementIds: Array.Empty<string>(), blockIds: new[] { b.Id, c.Id });

        var method = typeof(OperationViewModel).GetMethod("GetRouteActiveBlockIds", BindingFlags.NonPublic | BindingFlags.Instance);
        var activeBlockIds = ((IEnumerable<string>)method!.Invoke(vm, new object[] { layout, route.Id })!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(3, activeBlockIds.Count);
        Assert.Contains(a.Id, activeBlockIds);
        Assert.Contains(b.Id, activeBlockIds);
        Assert.Contains(c.Id, activeBlockIds);
    }

    [Fact]
    public void ClearRouteReservations_CistiLenRuntimeOwnedBlokyAktivnejCesty()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var a = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A", IsOccupied = true, AssignedLocoId = "train-1" };
        var b = new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B", IsShadowSet = true, ReservedLocoId = "train-1" };
        var c = new BlockElement { Id = "blk_c", MarkerKey = "Block", Label = "C", IsShadowSet = true, ReservedLocoId = "train-1" };
        layout.Elements.Add(a);
        layout.Elements.Add(b);
        layout.Elements.Add(c);

        var route = new RouteDefinition { Id = "r_runtime", FromBlockId = a.Id, ToBlockId = c.Id, StartNavigationDirection = RouteDirection.Right };
        route.BlockIds.AddRange(new[] { a.Id, b.Id, c.Id });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        b.IsShadowSet = true;
        b.ReservedLocoId = "train-1";
        c.IsShadowSet = true;
        c.ReservedLocoId = "train-1";
        AddActiveRoute(vm, route.Id);
        SetRouteRuntime(vm, route.Id, stateName: "Waiting", segmentIndex: 0, currentBlockId: a.Id, waitingBlockId: b.Id, waitingReason: "rezervované-iným", waitingSinceUtc: DateTime.UtcNow);

        var method = typeof(OperationViewModel).GetMethod("ClearRouteReservations", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(vm, new object[] { layout, route });

        Assert.False(b.IsShadowSet);
        Assert.Null(b.ReservedLocoId);
        Assert.True(c.IsShadowSet);
        Assert.Equal("train-1", c.ReservedLocoId);
    }

    [Fact]
    public void ReleaseTraversedTurnouts_PriInejAktivnejCeste_NeresetneVyhybku()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route1, route2, turnout) = CreateTwoRoutesSharingTurnout();
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        AddActiveRoute(vm, route2.Id);
        turnout.State = TurnoutState.Diverge;

        var method = typeof(OperationViewModel).GetMethod("ReleaseTraversedTurnouts", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(vm, new object[] { layout, route1, "blk_a", "blk_b" });

        Assert.Equal(TurnoutState.Diverge, turnout.State);
    }

    [Fact]
    public async Task TryEnsureTurnoutsForSegmentAsync_PrestaviVyhybkuAzPriSegmente()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route, turnout) = CreateSegmentTurnoutLayout();
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        turnout.State = TurnoutState.Straight;
        var dcc = new TestDccCentralClient { IsConnected = true };

        var result = await InvokeTryEnsureTurnoutsForSegmentAsync(vm, layout, route, "blk_a", "blk_b", dcc);

        Assert.True(result.IsReady);
        Assert.Null(result.WaitReason);
        Assert.Equal(TurnoutState.Diverge, turnout.State);
        Assert.Contains(dcc.TurnoutCommands, c => c.Address == turnout.DccAddress && c.Activate);

        var reservations = GetTurnoutReservationMap(vm);
        Assert.Equal(route.Id, reservations[turnout.Id]);
    }

    [Fact]
    public async Task TryEnsureTurnoutsForSegmentAsync_PriKonflikteVratiTurnoutConflict()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route, turnout) = CreateSegmentTurnoutLayout();
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        var reservations = GetTurnoutReservationMap(vm);
        reservations[turnout.Id] = "ina-cesta";

        var result = await InvokeTryEnsureTurnoutsForSegmentAsync(vm, layout, route, "blk_a", "blk_b", null);

        Assert.False(result.IsReady);
        Assert.Equal("konflikt-vyhybky", result.WaitReason);
        Assert.Equal(TurnoutState.Straight, turnout.State);
    }

    [Fact]
    public void ReleaseTraversedTurnouts_PoTailCleare_UvolniRuntimeRezervaciuVyhybky()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route, turnout) = CreateSegmentTurnoutLayout();
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        var reservations = GetTurnoutReservationMap(vm);
        reservations[turnout.Id] = route.Id;
        turnout.State = TurnoutState.Diverge;

        var method = typeof(OperationViewModel).GetMethod("ReleaseTraversedTurnouts", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(vm, new object[] { layout, route, "blk_a", "blk_b" });

        Assert.False(reservations.Contains(turnout.Id));
        Assert.Equal(TurnoutState.Straight, turnout.State);
    }

    [Fact]
    public async Task ApplySignalAspectsForRouteAsync_ZapiseDiagnostikuNahodeniaNavestidlaAjBezDcc()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var signal = new SignalElement { Id = "sig_a", Label = "Na6", DccAddress = 0 };
        var from = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "Blok 1", SignalRightId = signal.Id };
        var to = new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "Blok 6" };
        var route = new RouteDefinition
        {
            Id = "r_a_b",
            FromBlockId = from.Id,
            ToBlockId = to.Id,
            StartNavigationDirection = RouteDirection.Right
        };

        var controller = new SignalController(new LayoutElement[] { from, to, signal });

        var result = await controller.ApplySignalAspectsForRouteAsync(route);
        var events = TrackFlowDoctorService.Instance.Events.ToList();

        Assert.True(result);
        Assert.Contains(events,
            e => e.Source == "Návestidlo" && e.Message == "Nahadzujem návestidlo Na6 na Výstraha");
    }

    [Fact]
    public async Task ActivateRouteAsync_NepovolenySmerVBloku_PouzijeNazovBlokuNamiestoId()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var from = new BlockElement
        {
            Id = "blk_hidden_id_001",
            MarkerKey = "Block",
            Label = "Stanica sever",
            AllowForward = false,
            AllowBackward = true,
            Rotation = 0
        };
        var to = new BlockElement
        {
            Id = "blk_hidden_id_002",
            MarkerKey = "Block",
            Label = "Stanica juh",
            AllowForward = true,
            AllowBackward = true,
            Rotation = 0
        };

        layout.Elements.Add(from);
        layout.Elements.Add(to);

        var route = new RouteDefinition
        {
            Id = "r_forbidden_direction",
            FromBlockId = from.Id,
            ToBlockId = to.Id,
            StartNavigationDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { from.Id, to.Id });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());

        var result = await vm.ActivateRouteAsync(route.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Smer jazdy v bloku Stanica sever nie je povolený", result.Reason);
        Assert.DoesNotContain(from.Id, result.Reason);
    }

    [Fact]
    public async Task RuntimeSharedBlockWait_PoUvolneniSharedBlokuKorektneObnoviSignalWindowAContinuujeJazdu()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, waitRoute, blockerRoute, sourceBlock, sharedBlock, blockerSourceBlock, waitSignal) = CreateRuntimeSharedBlockWaitLayout();
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("train-1", "Train 1") { IsPlacedOnTrack = true, AssignedBlockId = sourceBlock.Id };
        sourceBlock.AssignedLocoId = loco.Code;
        sourceBlock.IsOccupied = true;

        var vmRef = new Box<OperationViewModel>();
        var sawStopDuringWait = false;
        var routeStayedActiveDuringWait = false;
        var releasedSharedReservation = false;

        var vm = new OperationViewModel(
            settings,
            new ObservableCollection<Locomotive> { loco },
            movementDelayAsync: (_, _) =>
            {
                var currentVm = vmRef.Value;
                if (!releasedSharedReservation && currentVm != null && GetWaitStateMap(currentVm).Contains(waitRoute.Id))
                {
                    routeStayedActiveDuringWait = currentVm.ActiveRouteIds.Contains(waitRoute.Id);
                    sawStopDuringWait = waitSignal.Aspect == SignalAspect.Stop;
                    Assert.True(sharedBlock.IsShadowSet);
                    Assert.Equal("train-2", sharedBlock.ReservedLocoId);

                    sharedBlock.IsShadowSet = false;
                    sharedBlock.ReservedLocoId = null;
                    releasedSharedReservation = true;
                }

                return Task.CompletedTask;
            }) { IsSimulationMode = true };
        vmRef.Value = vm;

        AddActiveRoute(vm, blockerRoute.Id);
        SetRouteRuntime(vm, blockerRoute.Id, stateName: "Active", segmentIndex: 0, currentBlockId: blockerSourceBlock.Id);
        SetRouteActiveWindow(vm, blockerRoute.Id, Array.Empty<string>(), new[] { blockerSourceBlock.Id, sharedBlock.Id });

        sharedBlock.IsShadowSet = true;
        sharedBlock.ReservedLocoId = "train-2";
        blockerSourceBlock.AssignedLocoId = "train-2";
        blockerSourceBlock.IsOccupied = true;

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, sourceBlock.Id, sharedBlock.Id, preferredRouteDefinitionId: waitRoute.Id);

        Assert.True(result.IsSuccess);
        Assert.True(releasedSharedReservation);
        Assert.True(routeStayedActiveDuringWait);
        Assert.True(sawStopDuringWait);
        Assert.True(sharedBlock.IsOccupied);
        Assert.Equal(loco.Code, sharedBlock.AssignedLocoId);
        Assert.False(sharedBlock.IsShadowSet);
        Assert.Null(sharedBlock.ReservedLocoId);

        AssertRouteLeakCleared(vm, waitRoute.Id);
        Assert.Contains(blockerRoute.Id, vm.ActiveRouteIds);

        await InvokeDeactivateRouteInternalAsync(vm, blockerRoute.Id);
        AssertRouteLeakCleared(vm, blockerRoute.Id);
        AssertNoGlobalRuntimeLeaks(vm);
    }

    [Fact]
    public async Task WaitForNextBlockReservationAsync_PriUvolneniSharedBlokuObnoviSignalOknoARezervaciuPrePokracovanieSegmentu()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, waitRoute, blockerRoute, sourceBlock, sharedBlock, blockerSourceBlock, waitSignal) = CreateRuntimeSharedBlockWaitLayout();
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("train-1", "Train 1") { IsPlacedOnTrack = true, AssignedBlockId = sourceBlock.Id };
        sourceBlock.AssignedLocoId = loco.Code;
        sourceBlock.IsOccupied = true;

        var vmRef = new Box<OperationViewModel>();
        var sawStopDuringWait = false;
        var releasedSharedReservation = false;

        var vm = new OperationViewModel(
            settings,
            new ObservableCollection<Locomotive> { loco },
            movementDelayAsync: (_, _) =>
            {
                var currentVm = vmRef.Value;
                if (!releasedSharedReservation && currentVm != null && GetWaitStateMap(currentVm).Contains(waitRoute.Id))
                {
                    sawStopDuringWait = waitSignal.Aspect == SignalAspect.Stop;
                    sharedBlock.IsShadowSet = false;
                    sharedBlock.ReservedLocoId = null;
                    releasedSharedReservation = true;
                }

                return Task.CompletedTask;
            });
        vmRef.Value = vm;

        AddActiveRoute(vm, waitRoute.Id);
        AddActiveRoute(vm, blockerRoute.Id);
        SetRouteRuntime(vm, waitRoute.Id, stateName: "Active", segmentIndex: 0, currentBlockId: sourceBlock.Id);
        SetRouteRuntime(vm, blockerRoute.Id, stateName: "Active", segmentIndex: 0, currentBlockId: blockerSourceBlock.Id);
        SetRouteActiveWindow(vm, blockerRoute.Id, Array.Empty<string>(), new[] { blockerSourceBlock.Id, sharedBlock.Id });

        sharedBlock.IsShadowSet = true;
        sharedBlock.ReservedLocoId = "train-2";
        blockerSourceBlock.AssignedLocoId = "train-2";
        blockerSourceBlock.IsOccupied = true;

        var outcome = await InvokeWaitForNextBlockReservationAsync(
            vm,
            layout,
            waitRoute,
            new[] { sourceBlock.Id, sharedBlock.Id },
            segmentIndex: 0,
            segmentTarget: sharedBlock,
            locoCode: loco.Code,
            orientationForward: true,
            travelDirection: NavigationDirection.Right,
            CancellationToken.None);

        Assert.Equal("Reserved", outcome);
        Assert.True(releasedSharedReservation);
        Assert.True(sawStopDuringWait);
        Assert.Empty(GetWaitStateMap(vm));
        Assert.True(sharedBlock.IsShadowSet);
        Assert.Equal(loco.Code, sharedBlock.ReservedLocoId);
        Assert.NotEqual(SignalAspect.Stop, waitSignal.Aspect);
        Assert.True(GetRouteActiveWindowBlockIds(vm, waitRoute.Id).SetEquals(new[] { sourceBlock.Id, sharedBlock.Id }));
        Assert.Equal("Active", GetRuntimeProperty<object>(AssertRuntimeExists(vm, waitRoute.Id), "State")?.ToString());
    }

    [Fact]
    public async Task MovementCommit_PoStrateRezervacieTesnePredBoundaryEntry_VratiSegmentDoWaitARevalidujeOwnership()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, waitRoute, _, sourceBlock, sharedBlock, _, _) = CreateRuntimeSharedBlockWaitLayout();
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("train-1", "Train 1") { IsPlacedOnTrack = true, AssignedBlockId = sourceBlock.Id };
        sourceBlock.AssignedLocoId = loco.Code;
        sourceBlock.IsOccupied = true;

        var vmRef = new Box<OperationViewModel>();
        var strippedReservationBeforeCommit = false;

        var vm = new OperationViewModel(
            settings,
            new ObservableCollection<Locomotive> { loco },
            movementDelayAsync: (_, _) =>
            {
                var currentVm = vmRef.Value;
                if (!strippedReservationBeforeCommit
                    && currentVm != null
                    && !GetWaitStateMap(currentVm).Contains(waitRoute.Id)
                    && sharedBlock.IsShadowSet
                    && string.Equals(sharedBlock.ReservedLocoId, loco.Code, StringComparison.OrdinalIgnoreCase))
                {
                    sharedBlock.IsShadowSet = false;
                    sharedBlock.ReservedLocoId = null;
                    strippedReservationBeforeCommit = true;
                }

                return Task.CompletedTask;
            }) { IsSimulationMode = true };
        vmRef.Value = vm;

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, sourceBlock.Id, sharedBlock.Id, preferredRouteDefinitionId: waitRoute.Id);

        Assert.True(result.IsSuccess);
        Assert.True(strippedReservationBeforeCommit);
        Assert.True(sharedBlock.IsOccupied);
        Assert.Equal(loco.Code, sharedBlock.AssignedLocoId);
        Assert.False(sharedBlock.IsShadowSet);
        Assert.Null(sharedBlock.ReservedLocoId);

        var events = TrackFlowDoctorService.Instance.Events.ToList();
        Assert.Contains(events, e => e.Message.Contains("[MULTI][CAKANIE]")
            && e.Message.Contains("stav=[vstup]")
            && e.Message.Contains("blok=[Shared Target]"));

        AssertRouteLeakCleared(vm, waitRoute.Id);
        AssertNoGlobalRuntimeLeaks(vm);
    }

    [Fact]
    public async Task TailClear_NezmazeCerstvuRezervaciuNadvaznejTrasyAniPriRovnakejLokomotive()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A" };
        var sharedX = new BlockElement { Id = "blk_x", MarkerKey = "Block", Label = "X" };
        var blockY = new BlockElement { Id = "blk_y", MarkerKey = "Block", Label = "Y" };
        layout.Elements.AddRange(new LayoutElement[] { blockA, sharedX, blockY });

        var oldRoute = new RouteDefinition { Id = "r_old", FromBlockId = sharedX.Id, ToBlockId = blockY.Id, StartNavigationDirection = RouteDirection.Right };
        oldRoute.BlockIds.AddRange(new[] { sharedX.Id, blockY.Id });
        var nextRoute = new RouteDefinition { Id = "r_next", FromBlockId = blockA.Id, ToBlockId = sharedX.Id, StartNavigationDirection = RouteDirection.Right };
        nextRoute.BlockIds.AddRange(new[] { blockA.Id, sharedX.Id });
        layout.Routes.Add(oldRoute);
        layout.Routes.Add(nextRoute);
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("train-1", "Train 1") { IsPlacedOnTrack = true, AssignedBlockId = blockY.Id };
        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive> { loco });

        InvokeInitializeRouteRuntime(vm, oldRoute.Id, blockY.Id, oldRoute.BlockIds, loco.Code);
        InvokeSetTraversalSegmentWindow(vm, layout, oldRoute, oldRoute.BlockIds, leadSegmentIndex: 1);
        InvokeInitializeRouteRuntime(vm, nextRoute.Id, blockA.Id, nextRoute.BlockIds, loco.Code);
        InvokeSetTraversalSegmentWindow(vm, layout, nextRoute, nextRoute.BlockIds, leadSegmentIndex: 0);

        // blockY je CurrentBlockId pre oldRoute runtime → ResolvePrimaryRouteLocoId ho číta.
        blockY.AssignedLocoId = loco.Code;

        sharedX.IsShadowSet = true;
        sharedX.ReservedLocoId = loco.Code;
        sharedX.ReservedLocoIsForward = true;
        sharedX.AssignedLocoId = loco.Code;
        sharedX.IsOccupied = true;
        sharedX.IsTailClearing = true;

        await InvokeApplyTailClearStateAsync(vm, layout, oldRoute, sharedX, blockY);

        Assert.True(sharedX.IsShadowSet);
        Assert.Equal(loco.Code, sharedX.ReservedLocoId);
        Assert.False(sharedX.IsOccupied);
        Assert.Null(sharedX.AssignedLocoId);
    }

    [Fact]
    public async Task OccupancyCallback_IgnorujeStaryBlokMimoAktualnehoRuntimeOknaAjPriRovnakejLokomotive()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A" };
        var blockX = new BlockElement { Id = "blk_x", MarkerKey = "Block", Label = "X" };
        var blockY = new BlockElement { Id = "blk_y", MarkerKey = "Block", Label = "Y" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B" };
        layout.Elements.AddRange(new LayoutElement[] { blockA, blockX, blockY, blockB });

        var route = new RouteDefinition { Id = "r_ab", FromBlockId = blockA.Id, ToBlockId = blockB.Id, StartNavigationDirection = RouteDirection.Right };
        route.BlockIds.AddRange(new[] { blockA.Id, blockX.Id, blockY.Id, blockB.Id });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("train-1", "Train 1") { IsPlacedOnTrack = true, AssignedBlockId = blockY.Id };
        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive> { loco });

        InvokeInitializeRouteRuntime(vm, route.Id, blockY.Id, route.BlockIds, loco.Code);
        InvokeSetTraversalSegmentWindow(vm, layout, route, route.BlockIds, leadSegmentIndex: 2);

        blockX.AssignedLocoId = loco.Code;
        blockX.IsOccupied = true;

        var changed = await InvokeOnBlockOccupiedAsync(vm, layout, blockX.Id);

        Assert.Equal(0, changed);
        Assert.True(GetRouteActiveWindowBlockIds(vm, route.Id).SetEquals(new[] { blockY.Id, blockB.Id }));
        Assert.Equal(loco.Code, blockX.AssignedLocoId);
        Assert.Equal(blockY.Id, GetRuntimeProperty<string>(AssertRuntimeExists(vm, route.Id), "CurrentBlockId"));
    }

    [Fact]
    public void ReserveNextBlock_OdmietneBlokKtoryEsteCakaNaTailClear()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var source = new BlockElement { Id = "blk_src", MarkerKey = "Block", Label = "Source" };
        var tailClearing = new BlockElement
        {
            Id = "blk_tail",
            MarkerKey = "Block",
            Label = "TailClearing",
            IsTailClearing = true,
            IsOccupied = false
        };
        layout.Elements.AddRange(new LayoutElement[] { source, tailClearing });

        var route = new RouteDefinition { Id = "r_tail_guard", FromBlockId = source.Id, ToBlockId = tailClearing.Id, StartNavigationDirection = RouteDirection.Right };
        route.BlockIds.AddRange(new[] { source.Id, tailClearing.Id });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        InvokeInitializeRouteRuntime(vm, route.Id, source.Id, route.BlockIds, "train-1");
        InvokeSetTraversalSegmentWindow(vm, layout, route, route.BlockIds, leadSegmentIndex: 0);

        var result = InvokeReserveNextBlockInternal(vm, tailClearing, "train-1", source.Id, layout);

        Assert.False(result.IsReserved);
        Assert.False(tailClearing.IsShadowSet);
        Assert.Null(tailClearing.ReservedLocoId);
        Assert.Contains(TrackFlowDoctorService.Instance.Events, e => e.Message.Contains("stav=[rezervácia-odmietnutá-tail-clear]"));
    }

    [Fact]
    public async Task RuntimeSharedTurnoutWait_PoUvolneniVyhybkyDrziWaitStopAHandoverBezLeaku()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, waitRoute, blockerRoute, sourceBlock, targetBlock, blockerSourceBlock, turnout, waitSignal) = CreateRuntimeSharedTurnoutWaitLayout();
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("train-1", "Train 1") { IsPlacedOnTrack = true, AssignedBlockId = sourceBlock.Id };
        sourceBlock.AssignedLocoId = loco.Code;
        sourceBlock.IsOccupied = true;

        var vmRef = new Box<OperationViewModel>();
        var sawStopDuringWait = false;
        var routeStayedActiveDuringWait = false;
        var releasedTurnoutConflict = false;

        var vm = new OperationViewModel(
            settings,
            new ObservableCollection<Locomotive> { loco },
            movementDelayAsync: (_, _) =>
            {
                var currentVm = vmRef.Value;
                if (!releasedTurnoutConflict && currentVm != null && GetWaitStateMap(currentVm).Contains(waitRoute.Id))
                {
                    routeStayedActiveDuringWait = currentVm.ActiveRouteIds.Contains(waitRoute.Id);
                    sawStopDuringWait = waitSignal.Aspect == SignalAspect.Stop;

                    var turnoutReservations = GetTurnoutReservationMap(currentVm);
                    Assert.Equal(blockerRoute.Id, turnoutReservations[turnout.Id]);
                    turnoutReservations.Remove(turnout.Id);
                    releasedTurnoutConflict = true;
                }

                return Task.CompletedTask;
            }) { IsSimulationMode = true };
        vmRef.Value = vm;

        AddActiveRoute(vm, blockerRoute.Id);
        SetRouteRuntime(vm, blockerRoute.Id, stateName: "Active", segmentIndex: 0, currentBlockId: blockerSourceBlock.Id);
        SetRouteActiveWindow(vm, blockerRoute.Id, new[] { turnout.Id }, new[] { blockerSourceBlock.Id });
        GetTurnoutReservationMap(vm)[turnout.Id] = blockerRoute.Id;

        blockerSourceBlock.AssignedLocoId = "train-2";
        blockerSourceBlock.IsOccupied = true;

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, sourceBlock.Id, targetBlock.Id, preferredRouteDefinitionId: waitRoute.Id);

        Assert.True(result.IsSuccess);
        Assert.True(releasedTurnoutConflict);
        Assert.True(routeStayedActiveDuringWait);
        Assert.True(sawStopDuringWait);
        Assert.True(targetBlock.IsOccupied);
        Assert.Equal(loco.Code, targetBlock.AssignedLocoId);
        Assert.Equal(TurnoutState.Diverge, turnout.State);

        AssertRouteLeakCleared(vm, waitRoute.Id);
        Assert.DoesNotContain(turnout.Id, GetTurnoutReservationMap(vm).Keys.Cast<object>());
        Assert.Contains(blockerRoute.Id, vm.ActiveRouteIds);

        await InvokeDeactivateRouteInternalAsync(vm, blockerRoute.Id);
        AssertRouteLeakCleared(vm, blockerRoute.Id);
        AssertNoGlobalRuntimeLeaks(vm);
    }

    [Fact]
    public void StickyWaitWinner_PriSharedBlokuUprednostniSkorCakajucuRouteAEmitneArbiterDiagnostiku()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var src1 = new BlockElement { Id = "blk_wait_1", MarkerKey = "Block", Label = "Wait 1" };
        var src2 = new BlockElement { Id = "blk_wait_2", MarkerKey = "Block", Label = "Wait 2" };
        var shared = new BlockElement { Id = "blk_shared", MarkerKey = "Block", Label = "Shared" };
        layout.Elements.AddRange(new LayoutElement[] { src1, src2, shared });

        var route1 = new RouteDefinition { Id = "r_wait_1", FromBlockId = src1.Id, ToBlockId = shared.Id, StartNavigationDirection = RouteDirection.Right };
        route1.BlockIds.AddRange(new[] { src1.Id, shared.Id });
        var route2 = new RouteDefinition { Id = "r_wait_2", FromBlockId = src2.Id, ToBlockId = shared.Id, StartNavigationDirection = RouteDirection.Right };
        route2.BlockIds.AddRange(new[] { src2.Id, shared.Id });
        layout.Routes.Add(route1);
        layout.Routes.Add(route2);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        AddActiveRoute(vm, route1.Id);
        AddActiveRoute(vm, route2.Id);
        SetWaitState(vm, route1.Id, shared.Id, "rezervované-iným");
        SetWaitState(vm, route2.Id, shared.Id, "rezervované-iným");

        InvokeTrackWaitingResource(vm, route1.Id, "Block", shared.Id);
        InvokeTrackWaitingResource(vm, route2.Id, "Block", shared.Id);
        InvokeAssignStickyWaitWinner(vm, layout, "Block", shared.Id);

        var denied = InvokeReserveNextBlockInternal(vm, shared, "train-2", src2.Id, layout);
        Assert.False(denied.IsReserved);
        Assert.False(shared.IsShadowSet);
        Assert.Null(shared.ReservedLocoId);

        var granted = InvokeReserveNextBlockInternal(vm, shared, "train-1", src1.Id, layout);
        Assert.True(granted.IsReserved);
        Assert.False(granted.IsCriticalFailure);
        Assert.True(shared.IsShadowSet);
        Assert.Equal("train-1", shared.ReservedLocoId);

        var events = TrackFlowDoctorService.Instance.Events.ToList();
        Assert.Contains(events, e => e.Message.Contains("[MULTI][ARBITRAZ]") && e.Message.Contains("víťaz=[Wait 1 → Shared]") && e.Message.Contains("odovzdanie=[udelený]"));
        Assert.Contains(events, e => e.Message.Contains("[MULTI][ARBITRAZ]") && e.Message.Contains("víťaz=[Wait 1 → Shared]") && e.Message.Contains("odovzdanie=[odmietnutý]"));
    }

    [Fact]
    public async Task StickyWaitWinner_PriSharedVyhybkeUprednostniSkorCakajucuRouteAZabraniThrashingu()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var turnout = new TurnoutElement { Id = "sw_sticky", MarkerKey = "Turnout_R", Label = "Sticky turnout", DccAddress = 17, State = TurnoutState.Straight };
        var src1 = new BlockElement { Id = "blk_t1_src", MarkerKey = "Block", Label = "T1 Src" };
        var dst1 = new BlockElement { Id = "blk_t1_dst", MarkerKey = "Block", Label = "T1 Dst" };
        var src2 = new BlockElement { Id = "blk_t2_src", MarkerKey = "Block", Label = "T2 Src" };
        var dst2 = new BlockElement { Id = "blk_t2_dst", MarkerKey = "Block", Label = "T2 Dst" };
        layout.Elements.AddRange(new LayoutElement[] { src1, dst1, src2, dst2, turnout });

        var route1 = new RouteDefinition { Id = "r_t1", FromBlockId = src1.Id, ToBlockId = dst1.Id, StartNavigationDirection = RouteDirection.Right };
        route1.BlockIds.AddRange(new[] { src1.Id, dst1.Id });
        route1.PathElementIds.Add(turnout.Id);
        route1.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = turnout.Id, RequiredState = TurnoutState.Diverge });

        var route2 = new RouteDefinition { Id = "r_t2", FromBlockId = src2.Id, ToBlockId = dst2.Id, StartNavigationDirection = RouteDirection.Right };
        route2.BlockIds.AddRange(new[] { src2.Id, dst2.Id });
        route2.PathElementIds.Add(turnout.Id);
        route2.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = turnout.Id, RequiredState = TurnoutState.Diverge });

        layout.Routes.Add(route1);
        layout.Routes.Add(route2);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        AddActiveRoute(vm, route1.Id);
        AddActiveRoute(vm, route2.Id);
        SetWaitState(vm, route1.Id, dst1.Id, "konflikt-vyhybky");
        SetWaitState(vm, route2.Id, dst2.Id, "konflikt-vyhybky");

        InvokeTrackWaitingResource(vm, route1.Id, "Turnout", turnout.Id);
        InvokeTrackWaitingResource(vm, route2.Id, "Turnout", turnout.Id);
        InvokeAssignStickyWaitWinner(vm, layout, "Turnout", turnout.Id);

        var denied = await InvokeTryEnsureTurnoutsForSegmentAsync(vm, layout, route2, src2.Id, dst2.Id, dcc: null);
        Assert.False(denied.IsReady);
        Assert.Equal("konflikt-vyhybky", denied.WaitReason);
        Assert.False(GetTurnoutReservationMap(vm).Contains(turnout.Id));

        var granted = await InvokeTryEnsureTurnoutsForSegmentAsync(vm, layout, route1, src1.Id, dst1.Id, dcc: null);
        Assert.True(granted.IsReady);
        Assert.True(GetTurnoutReservationMap(vm).Contains(turnout.Id));
        Assert.Equal(route1.Id, GetTurnoutReservationMap(vm)[turnout.Id]);

        var events = TrackFlowDoctorService.Instance.Events.ToList();
        Assert.Contains(events, e => e.Message.Contains("[MULTI][ARBITRAZ]") && e.Message.Contains("víťaz=[T1 Src → T1 Dst]") && e.Message.Contains("odovzdanie=[udelený]"));
        Assert.Contains(events, e => e.Message.Contains("[MULTI][ARBITRAZ]") && e.Message.Contains("víťaz=[T1 Src → T1 Dst]") && e.Message.Contains("odovzdanie=[odmietnutý]"));
    }

    [Fact]
    public void DeadlockPotentialSample_OlderWaitWinnerRozbijeCircularWaitAObeRoutePostupnePokracuju()
    {
        TrackFlowDoctorService.Instance.Events.Clear();

        var settings = new SettingsManager();
        settings.NewProject();

        var layout = new TrackLayout();
        var a = new BlockElement { Id = "blk_dl_a", MarkerKey = "Block", Label = "A" };
        var x = new BlockElement { Id = "blk_dl_x", MarkerKey = "Block", Label = "X" };
        var y = new BlockElement { Id = "blk_dl_y", MarkerKey = "Block", Label = "Y" };
        var b = new BlockElement { Id = "blk_dl_b", MarkerKey = "Block", Label = "B" };
        var c = new BlockElement { Id = "blk_dl_c", MarkerKey = "Block", Label = "C" };
        var d = new BlockElement { Id = "blk_dl_d", MarkerKey = "Block", Label = "D" };
        layout.Elements.AddRange(new LayoutElement[] { a, x, y, b, c, d });

        var routeA = new RouteDefinition { Id = "r_deadlock_a", FromBlockId = a.Id, ToBlockId = b.Id, StartNavigationDirection = RouteDirection.Right };
        routeA.BlockIds.AddRange(new[] { a.Id, x.Id, y.Id, b.Id });
        var routeB = new RouteDefinition { Id = "r_deadlock_b", FromBlockId = c.Id, ToBlockId = d.Id, StartNavigationDirection = RouteDirection.Right };
        routeB.BlockIds.AddRange(new[] { c.Id, y.Id, x.Id, d.Id });
        layout.Routes.Add(routeA);
        layout.Routes.Add(routeB);
        settings.CurrentProject!.Layout = layout;

        var locoA = new Locomotive("train-1", "Train 1") { IsPlacedOnTrack = true, AssignedBlockId = a.Id };
        var locoB = new Locomotive("train-2", "Train 2") { IsPlacedOnTrack = true, AssignedBlockId = c.Id };
        a.AssignedLocoId = locoA.Code;
        a.IsOccupied = true;
        c.AssignedLocoId = locoB.Code;
        c.IsOccupied = true;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive> { locoA, locoB });

        x.IsShadowSet = true;
        x.ReservedLocoId = locoA.Code;
        y.IsShadowSet = true;
        y.ReservedLocoId = locoB.Code;

        AddActiveRoute(vm, routeA.Id);
        AddActiveRoute(vm, routeB.Id);
        SetRouteRuntime(vm, routeA.Id, stateName: "Waiting", segmentIndex: 1, currentBlockId: x.Id, waitingBlockId: y.Id, waitingReason: "rezervované-iným", waitingSinceUtc: DateTime.UtcNow.AddSeconds(-2));
        SetRouteRuntime(vm, routeB.Id, stateName: "Waiting", segmentIndex: 1, currentBlockId: y.Id, waitingBlockId: x.Id, waitingReason: "rezervované-iným", waitingSinceUtc: DateTime.UtcNow.AddSeconds(-1));
        SetRouteActiveWindow(vm, routeA.Id, Array.Empty<string>(), new[] { x.Id });
        SetRouteActiveWindow(vm, routeB.Id, Array.Empty<string>(), new[] { y.Id });
        SetWaitState(vm, routeA.Id, y.Id, "rezervované-iným");
        SetWaitState(vm, routeB.Id, x.Id, "rezervované-iným");
        InvokeTrackWaitingResource(vm, routeA.Id, "Block", y.Id);
        InvokeTrackWaitingResource(vm, routeB.Id, "Block", x.Id);

        var youngerYielded = InvokeProcessDeadlockYieldState(vm, layout, routeB.Id);
        Assert.True(youngerYielded);
        Assert.True(GetDeadlockYieldMap(vm).Contains(routeB.Id));
        Assert.False(y.IsShadowSet);
        Assert.Null(y.ReservedLocoId);

        var olderReserved = InvokeReserveNextBlockInternal(vm, y, locoA.Code, x.Id, layout);
        Assert.True(olderReserved.IsReserved);
        Assert.False(olderReserved.IsCriticalFailure);
        Assert.Equal(locoA.Code, y.ReservedLocoId);

        x.IsShadowSet = false;
        x.ReservedLocoId = null;
        SetRouteRuntime(vm, routeA.Id, stateName: "Waiting", segmentIndex: 2, currentBlockId: y.Id, waitingBlockId: b.Id, waitingReason: "bezpečnostné-obmedzenie-za-jazdy", waitingSinceUtc: DateTime.UtcNow.AddSeconds(-2));
        SetRouteActiveWindow(vm, routeA.Id, Array.Empty<string>(), new[] { y.Id });

        var stillYielding = InvokeProcessDeadlockYieldState(vm, layout, routeB.Id);
        Assert.False(stillYielding);
        Assert.Empty(GetDeadlockYieldMap(vm));

        var youngerReserved = InvokeReserveNextBlockInternal(vm, x, locoB.Code, y.Id, layout);
        Assert.True(youngerReserved.IsReserved);
        Assert.False(youngerReserved.IsCriticalFailure);
        Assert.Equal(locoB.Code, x.ReservedLocoId);

        var events = TrackFlowDoctorService.Instance.Events.ToList();
        Assert.Equal(2, events.Count(e => e.Message.Contains("[MULTI][PAT]") && e.Message.Contains("pat=[detegovaný]")));
        Assert.Contains(events, e => e.Message.Contains("[MULTI][PAT]") && e.Message.Contains("pat=[ustúpenie]") && e.Message.Contains("blokuje=["));
        Assert.Contains(events, e => e.Message.Contains("[MULTI][PAT]") && e.Message.Contains("pat=[vyriešený]"));
        Assert.DoesNotContain(events, e => e.Message.Contains("[MULTI][PAT]") && e.Message.Contains("pat=[časový-limit]"));
    }

    [Fact]
    public async Task RuntimeParallelNonConflictingRoutes_DokonciaSaParalelneABezLeakov()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route1, route2, source1, target1, source2, target2) = CreateParallelRuntimeLayout();
        settings.CurrentProject!.Layout = layout;

        var loco1 = new Locomotive("train-1", "Train 1") { IsPlacedOnTrack = true, AssignedBlockId = source1.Id };
        var loco2 = new Locomotive("train-2", "Train 2") { IsPlacedOnTrack = true, AssignedBlockId = source2.Id };
        source1.AssignedLocoId = loco1.Code;
        source1.IsOccupied = true;
        source2.AssignedLocoId = loco2.Code;
        source2.IsOccupied = true;

        var vm = new OperationViewModel(
            settings,
            new ObservableCollection<Locomotive> { loco1, loco2 },
            movementDelayAsync: (_, _) => Task.CompletedTask)
        {
            IsSimulationMode = true
        };

        var task1 = vm.MoveLocomotiveBetweenBlocksAsync(loco1.Code, source1.Id, target1.Id, preferredRouteDefinitionId: route1.Id);
        var task2 = vm.MoveLocomotiveBetweenBlocksAsync(loco2.Code, source2.Id, target2.Id, preferredRouteDefinitionId: route2.Id);
        var results = await Task.WhenAll(task1, task2);

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.True(target1.IsOccupied);
        Assert.Equal(loco1.Code, target1.AssignedLocoId);
        Assert.True(target2.IsOccupied);
        Assert.Equal(loco2.Code, target2.AssignedLocoId);
        foreach (var block in layout.Elements.OfType<BlockElement>())
        {
            Assert.False(block.IsShadowSet);
            Assert.Null(block.ReservedLocoId);
        }

        AssertNoGlobalRuntimeLeaks(vm);
    }

    [Fact]
    public async Task RuntimeCancelJednejRoutePocasWait_VykonaRouteLocalCleanupABlokujucuRouteNechaNedotknutu()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, waitRoute, blockerRoute, sourceBlock, sharedBlock, blockerSourceBlock, _) = CreateRuntimeSharedBlockWaitLayout();
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("train-1", "Train 1") { IsPlacedOnTrack = true, AssignedBlockId = sourceBlock.Id };
        sourceBlock.AssignedLocoId = loco.Code;
        sourceBlock.IsOccupied = true;

        var ctsBox = new Box<CancellationTokenSource> { Value = new CancellationTokenSource() };
        OperationViewModel? vm = null;
        var observedBothRoutesActiveDuringWait = false;
        var cancelTriggered = false;

        try
        {
            vm = new OperationViewModel(
                settings,
                new ObservableCollection<Locomotive> { loco },
                movementDelayAsync: (_, token) =>
                {
                    var currentVm = vm;
                    if (!cancelTriggered && currentVm != null && GetWaitStateMap(currentVm).Contains(waitRoute.Id))
                    {
                        observedBothRoutesActiveDuringWait = currentVm.ActiveRouteIds.Contains(waitRoute.Id)
                            && currentVm.ActiveRouteIds.Contains(blockerRoute.Id);
                        cancelTriggered = true;
                        ctsBox.Value?.Cancel();
                    }

                    return Task.Delay(1, token);
                })
            {
                IsSimulationMode = true
            };

            AddActiveRoute(vm, blockerRoute.Id);
            SetRouteRuntime(vm, blockerRoute.Id, stateName: "Active", segmentIndex: 0, currentBlockId: blockerSourceBlock.Id);
            SetRouteActiveWindow(vm, blockerRoute.Id, Array.Empty<string>(), new[] { blockerSourceBlock.Id, sharedBlock.Id });

            sharedBlock.IsShadowSet = true;
            sharedBlock.ReservedLocoId = "train-2";
            blockerSourceBlock.AssignedLocoId = "train-2";
            blockerSourceBlock.IsOccupied = true;

            var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, sourceBlock.Id, sharedBlock.Id, ct: ctsBox.Value!.Token, preferredRouteDefinitionId: waitRoute.Id);

            Assert.False(result.IsSuccess);
            Assert.Equal("cancelled", result.Reason);
            Assert.True(cancelTriggered);
            Assert.True(observedBothRoutesActiveDuringWait);
            Assert.Empty(GetWaitStateMap(vm));
            Assert.True(sharedBlock.IsShadowSet);
            Assert.Equal("train-2", sharedBlock.ReservedLocoId);
            Assert.Contains(blockerRoute.Id, vm.ActiveRouteIds);
            AssertRouteLeakCleared(vm, waitRoute.Id);
            await InvokeDeactivateRouteInternalAsync(vm, blockerRoute.Id);
            AssertRouteLeakCleared(vm, blockerRoute.Id);
            AssertNoGlobalRuntimeLeaks(vm);
        }
        finally
        {
            ctsBox.Value?.Dispose();
        }
    }

    [Fact]
    public async Task RuntimeWaitTimeoutCleanup_NezanechaTurnoutAniRouteLeakANeodstraniCudziOwnership()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, waitRoute, blockerRoute, sourceBlock, sharedBlock, blockerSourceBlock, turnout) = CreateRuntimeTimeoutTurnoutWaitLayout();
        settings.CurrentProject!.Layout = layout;

        var loco = new Locomotive("train-1", "Train 1") { IsPlacedOnTrack = true, AssignedBlockId = sourceBlock.Id };
        sourceBlock.AssignedLocoId = loco.Code;
        sourceBlock.IsOccupied = true;

        var vm = new OperationViewModel(
            settings,
            new ObservableCollection<Locomotive> { loco },
            movementDelayAsync: (_, _) => Task.CompletedTask,
            waitMaxDuration: TimeSpan.Zero)
        {
            IsSimulationMode = true
        };

        AddActiveRoute(vm, blockerRoute.Id);
        SetRouteRuntime(vm, blockerRoute.Id, stateName: "Active", segmentIndex: 0, currentBlockId: blockerSourceBlock.Id);
        SetRouteActiveWindow(vm, blockerRoute.Id, new[] { turnout.Id }, new[] { blockerSourceBlock.Id, sharedBlock.Id });

        sharedBlock.IsShadowSet = true;
        sharedBlock.ReservedLocoId = "train-2";
        blockerSourceBlock.AssignedLocoId = "train-2";
        blockerSourceBlock.IsOccupied = true;

        var result = await vm.MoveLocomotiveBetweenBlocksAsync(loco.Code, sourceBlock.Id, sharedBlock.Id, preferredRouteDefinitionId: waitRoute.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("wait-timeout", result.Reason);
        Assert.True(sharedBlock.IsShadowSet);
        Assert.Equal("train-2", sharedBlock.ReservedLocoId);
        Assert.Contains(blockerRoute.Id, vm.ActiveRouteIds);
        var turnoutReservationValues = GetTurnoutReservationMap(vm).Values.Cast<object?>().ToList();
        Assert.DoesNotContain(
            turnoutReservationValues,
            v => string.Equals(v?.ToString(), waitRoute.Id, StringComparison.OrdinalIgnoreCase));
        AssertRouteLeakCleared(vm, waitRoute.Id);

        await InvokeDeactivateRouteInternalAsync(vm, blockerRoute.Id);
        AssertRouteLeakCleared(vm, blockerRoute.Id);
        AssertNoGlobalRuntimeLeaks(vm);
    }

    [Fact]
    public async Task RuntimeTailClearTurnoutRelease_DrziOwnershipDoTailClearAPotomUvolniBezLeaku()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var (layout, route, turnout) = CreateSegmentTurnoutLayout();
        settings.CurrentProject!.Layout = layout;

        var sourceBlock = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_a");
        var targetBlock = layout.Elements.OfType<BlockElement>().Single(b => b.Id == "blk_b");
        var loco = new Locomotive("train-1", "Train 1") { IsPlacedOnTrack = true, AssignedBlockId = sourceBlock.Id };
        sourceBlock.AssignedLocoId = loco.Code;
        sourceBlock.IsOccupied = true;
        targetBlock.IsShadowSet = true;
        targetBlock.ReservedLocoId = loco.Code;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive> { loco });
        AddActiveRoute(vm, route.Id);
        SetRouteRuntime(vm, route.Id, stateName: "Active", segmentIndex: 0, currentBlockId: sourceBlock.Id);
        SetRouteActiveWindow(vm, route.Id, new[] { turnout.Id }, new[] { sourceBlock.Id, targetBlock.Id });

        var turnoutEnsure = await InvokeTryEnsureTurnoutsForSegmentAsync(vm, layout, route, sourceBlock.Id, targetBlock.Id, null);
        Assert.True(turnoutEnsure.IsReady);

        targetBlock.IsShadowSet = true;
        targetBlock.ReservedLocoId = loco.Code;

        InvokeApplyBoundaryEntryState(vm, layout, sourceBlock, targetBlock, loco, loco.Code);

        Assert.Equal(route.Id, GetTurnoutReservationMap(vm)[turnout.Id]);
        Assert.Equal(TurnoutState.Diverge, turnout.State);
        Assert.True(sourceBlock.IsOccupied);
        Assert.True(sourceBlock.IsTailClearing);
        Assert.Equal(loco.Code, sourceBlock.AssignedLocoId);
        Assert.True(targetBlock.IsOccupied);
        Assert.Equal(loco.Code, targetBlock.AssignedLocoId);
        Assert.False(targetBlock.IsShadowSet);
        Assert.Null(targetBlock.ReservedLocoId);

        await InvokeApplyTailClearStateAsync(vm, layout, route, sourceBlock, targetBlock);

        Assert.False(GetTurnoutReservationMap(vm).Contains(turnout.Id));
        Assert.Equal(TurnoutState.Straight, turnout.State);
        Assert.False(sourceBlock.IsOccupied);
        Assert.False(sourceBlock.IsTailClearing);
        Assert.Null(sourceBlock.AssignedLocoId);

        await InvokeDeactivateRouteInternalAsync(vm, route.Id);
        AssertRouteLeakCleared(vm, route.Id);
        AssertNoGlobalRuntimeLeaks(vm);
    }

    private static (TrackLayout, RouteDefinition, BlockElement, BlockElement) CreateMinimalWaitLayout()
    {
        var layout = new TrackLayout();
        var sourceBlock = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "Blok A" };
        var targetBlock = new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "Blok B" };
        layout.Elements.Add(sourceBlock);
        layout.Elements.Add(targetBlock);

        var route = new RouteDefinition
        {
            Id = "r_wait",
            FromBlockId = sourceBlock.Id,
            ToBlockId = targetBlock.Id,
            StartNavigationDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { sourceBlock.Id, targetBlock.Id });
        layout.Routes.Add(route);

        return (layout, route, sourceBlock, targetBlock);
    }

    private static (TrackLayout Layout, RouteDefinition Route1, RouteDefinition Route2, SignalElement SignalRoute1, SignalElement SignalRoute2) CreateTwoDisjointRoutesWithSignals()
    {
        var layout = new TrackLayout();

        var signalRoute1 = new SignalElement { Id = "sig_r1", Label = "S1", DccAddress = 1, Aspect = SignalAspect.Stop };
        var signalRoute2 = new SignalElement { Id = "sig_r2", Label = "S2", DccAddress = 2, Aspect = SignalAspect.Stop };

        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A", SignalRightId = signalRoute1.Id };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block", Label = "C", SignalRightId = signalRoute2.Id };
        var blockD = new BlockElement { Id = "blk_d", MarkerKey = "Block", Label = "D" };

        layout.Elements.Add(blockA);
        layout.Elements.Add(blockB);
        layout.Elements.Add(blockC);
        layout.Elements.Add(blockD);
        layout.Elements.Add(signalRoute1);
        layout.Elements.Add(signalRoute2);

        var route1 = new RouteDefinition { Id = "r_1", FromBlockId = blockA.Id, ToBlockId = blockB.Id, StartNavigationDirection = RouteDirection.Right };
        route1.BlockIds.AddRange(new[] { blockA.Id, blockB.Id });

        var route2 = new RouteDefinition { Id = "r_2", FromBlockId = blockC.Id, ToBlockId = blockD.Id, StartNavigationDirection = RouteDirection.Right };
        route2.BlockIds.AddRange(new[] { blockC.Id, blockD.Id });

        layout.Routes.Add(route1);
        layout.Routes.Add(route2);
        return (layout, route1, route2, signalRoute1, signalRoute2);
    }

    private static (TrackLayout Layout, RouteDefinition Route1, RouteDefinition Route2, BlockElement SharedBlock) CreateTwoRoutesSharingReservedBlock()
    {
        var layout = new TrackLayout();
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A" };
        var sharedBlock = new BlockElement
        {
            Id = "blk_shared",
            MarkerKey = "Block",
            Label = "Shared",
            IsShadowSet = true,
            ReservedLocoId = "train-2"
        };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block", Label = "C" };

        layout.Elements.Add(blockA);
        layout.Elements.Add(sharedBlock);
        layout.Elements.Add(blockC);

        var route1 = new RouteDefinition { Id = "r_1", FromBlockId = blockA.Id, ToBlockId = sharedBlock.Id, StartNavigationDirection = RouteDirection.Right };
        route1.BlockIds.AddRange(new[] { blockA.Id, sharedBlock.Id });

        var route2 = new RouteDefinition { Id = "r_2", FromBlockId = sharedBlock.Id, ToBlockId = blockC.Id, StartNavigationDirection = RouteDirection.Right };
        route2.BlockIds.AddRange(new[] { sharedBlock.Id, blockC.Id });

        layout.Routes.Add(route1);
        layout.Routes.Add(route2);
        return (layout, route1, route2, sharedBlock);
    }

    private static (TrackLayout Layout, RouteDefinition Route1, RouteDefinition Route2, TurnoutElement Turnout) CreateTwoRoutesSharingTurnout()
    {
        var layout = new TrackLayout();
        var turnout = new TurnoutElement { Id = "sw_1", MarkerKey = "Turnout_R", State = TurnoutState.Diverge };
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B" };
        var blockC = new BlockElement { Id = "blk_c", MarkerKey = "Block", Label = "C" };
        var blockD = new BlockElement { Id = "blk_d", MarkerKey = "Block", Label = "D" };

        layout.Elements.Add(blockA);
        layout.Elements.Add(blockB);
        layout.Elements.Add(blockC);
        layout.Elements.Add(blockD);
        layout.Elements.Add(turnout);

        var route1 = new RouteDefinition { Id = "r_1", FromBlockId = blockA.Id, ToBlockId = blockB.Id, StartNavigationDirection = RouteDirection.Right };
        route1.BlockIds.AddRange(new[] { blockA.Id, blockB.Id });
        route1.PathElementIds.Add(turnout.Id);
        route1.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = turnout.Id, RequiredState = TurnoutState.Diverge });

        var route2 = new RouteDefinition { Id = "r_2", FromBlockId = blockC.Id, ToBlockId = blockD.Id, StartNavigationDirection = RouteDirection.Right };
        route2.BlockIds.AddRange(new[] { blockC.Id, blockD.Id });
        route2.PathElementIds.Add(turnout.Id);
        route2.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = turnout.Id, RequiredState = TurnoutState.Diverge });

        layout.Routes.Add(route1);
        layout.Routes.Add(route2);
        return (layout, route1, route2, turnout);
    }

    private static (TrackLayout Layout, RouteDefinition Route, TurnoutElement Turnout) CreateSegmentTurnoutLayout()
    {
        var layout = new TrackLayout();
        var turnout = new TurnoutElement { Id = "sw_seg", MarkerKey = "Turnout_R", DccAddress = 7, State = TurnoutState.Straight };
        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", Label = "A" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block", Label = "B" };

        layout.Elements.Add(blockA);
        layout.Elements.Add(blockB);
        layout.Elements.Add(turnout);

        var route = new RouteDefinition
        {
            Id = "r_seg",
            FromBlockId = blockA.Id,
            ToBlockId = blockB.Id,
            StartNavigationDirection = RouteDirection.Right
        };
        route.BlockIds.AddRange(new[] { blockA.Id, blockB.Id });
        route.PathElementIds.Add(turnout.Id);
        route.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = turnout.Id, RequiredState = TurnoutState.Diverge });
        layout.Routes.Add(route);

        return (layout, route, turnout);
    }

    private static (TrackLayout Layout, RouteDefinition WaitRoute, RouteDefinition BlockerRoute, BlockElement SourceBlock, BlockElement SharedBlock, BlockElement BlockerSourceBlock, SignalElement WaitSignal) CreateRuntimeSharedBlockWaitLayout()
    {
        var layout = new TrackLayout();
        var waitSignal = new SignalElement { Id = "sig_wait", MarkerKey = "Signal", Label = "S_WAIT", DccAddress = 1, Aspect = SignalAspect.Stop, SignalProfile = "2-aspect-main" };
        var sourceBlock = new BlockElement { Id = "blk_wait_src", MarkerKey = "Block", Label = "Wait Source", SignalRightId = waitSignal.Id };
        var sharedBlock = new BlockElement { Id = "blk_shared", MarkerKey = "Block", Label = "Shared Target" };
        var blockerSourceBlock = new BlockElement { Id = "blk_blocker_src", MarkerKey = "Block", Label = "Blocker Source" };

        layout.Elements.AddRange(new LayoutElement[] { sourceBlock, sharedBlock, blockerSourceBlock, waitSignal });

        var waitRoute = new RouteDefinition
        {
            Id = "r_wait_shared_block",
            FromBlockId = sourceBlock.Id,
            ToBlockId = sharedBlock.Id,
            StartNavigationDirection = RouteDirection.Right
        };
        waitRoute.BlockIds.AddRange(new[] { sourceBlock.Id, sharedBlock.Id });

        var blockerRoute = new RouteDefinition
        {
            Id = "r_blocker_shared_block",
            FromBlockId = blockerSourceBlock.Id,
            ToBlockId = sharedBlock.Id,
            StartNavigationDirection = RouteDirection.Right
        };
        blockerRoute.BlockIds.AddRange(new[] { blockerSourceBlock.Id, sharedBlock.Id });

        layout.Routes.Add(waitRoute);
        layout.Routes.Add(blockerRoute);
        return (layout, waitRoute, blockerRoute, sourceBlock, sharedBlock, blockerSourceBlock, waitSignal);
    }

    private static (TrackLayout Layout, RouteDefinition WaitRoute, RouteDefinition BlockerRoute, BlockElement SourceBlock, BlockElement TargetBlock, BlockElement BlockerSourceBlock, TurnoutElement Turnout, SignalElement WaitSignal) CreateRuntimeSharedTurnoutWaitLayout()
    {
        var layout = new TrackLayout();
        var waitSignal = new SignalElement { Id = "sig_turnout_wait", MarkerKey = "Signal", Label = "S_TURNOUT", DccAddress = 2, Aspect = SignalAspect.Stop, SignalProfile = "2-aspect-main" };
        var turnout = new TurnoutElement { Id = "sw_runtime_wait", MarkerKey = "Turnout_R", DccAddress = 8, State = TurnoutState.Straight };
        var sourceBlock = new BlockElement { Id = "blk_turnout_src", MarkerKey = "Block", Label = "Turnout Source", SignalRightId = waitSignal.Id };
        var targetBlock = new BlockElement { Id = "blk_turnout_dst", MarkerKey = "Block", Label = "Turnout Target" };
        var blockerSourceBlock = new BlockElement { Id = "blk_turnout_blocker", MarkerKey = "Block", Label = "Turnout Blocker" };
        var blockerTargetBlock = new BlockElement { Id = "blk_turnout_blocker_dst", MarkerKey = "Block", Label = "Turnout Blocker Dst" };

        layout.Elements.AddRange(new LayoutElement[] { sourceBlock, targetBlock, blockerSourceBlock, blockerTargetBlock, turnout, waitSignal });

        var waitRoute = new RouteDefinition
        {
            Id = "r_wait_shared_turnout",
            FromBlockId = sourceBlock.Id,
            ToBlockId = targetBlock.Id,
            StartNavigationDirection = RouteDirection.Right
        };
        waitRoute.BlockIds.AddRange(new[] { sourceBlock.Id, targetBlock.Id });
        waitRoute.PathElementIds.Add(turnout.Id);
        waitRoute.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = turnout.Id, RequiredState = TurnoutState.Diverge });

        var blockerRoute = new RouteDefinition
        {
            Id = "r_blocker_shared_turnout",
            FromBlockId = blockerSourceBlock.Id,
            ToBlockId = blockerTargetBlock.Id,
            StartNavigationDirection = RouteDirection.Right
        };
        blockerRoute.BlockIds.AddRange(new[] { blockerSourceBlock.Id, blockerTargetBlock.Id });
        blockerRoute.PathElementIds.Add(turnout.Id);
        blockerRoute.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = turnout.Id, RequiredState = TurnoutState.Diverge });

        layout.Routes.Add(waitRoute);
        layout.Routes.Add(blockerRoute);
        return (layout, waitRoute, blockerRoute, sourceBlock, targetBlock, blockerSourceBlock, turnout, waitSignal);
    }

    private static (TrackLayout Layout, RouteDefinition WaitRoute, RouteDefinition BlockerRoute, BlockElement SourceBlock, BlockElement SharedBlock, BlockElement BlockerSourceBlock, TurnoutElement Turnout) CreateRuntimeTimeoutTurnoutWaitLayout()
    {
        var layout = new TrackLayout();
        var turnout = new TurnoutElement { Id = "sw_timeout_wait", MarkerKey = "Turnout_R", DccAddress = 9, State = TurnoutState.Straight };
        var sourceBlock = new BlockElement { Id = "blk_timeout_src", MarkerKey = "Block", Label = "Timeout Source" };
        var sharedBlock = new BlockElement { Id = "blk_timeout_shared", MarkerKey = "Block", Label = "Timeout Shared" };
        var blockerSourceBlock = new BlockElement { Id = "blk_timeout_blocker", MarkerKey = "Block", Label = "Timeout Blocker" };

        layout.Elements.AddRange(new LayoutElement[] { sourceBlock, sharedBlock, blockerSourceBlock, turnout });

        var waitRoute = new RouteDefinition
        {
            Id = "r_wait_timeout_turnout",
            FromBlockId = sourceBlock.Id,
            ToBlockId = sharedBlock.Id,
            StartNavigationDirection = RouteDirection.Right
        };
        waitRoute.BlockIds.AddRange(new[] { sourceBlock.Id, sharedBlock.Id });
        waitRoute.PathElementIds.Add(turnout.Id);
        waitRoute.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = turnout.Id, RequiredState = TurnoutState.Diverge });

        var blockerRoute = new RouteDefinition
        {
            Id = "r_blocker_timeout_turnout",
            FromBlockId = blockerSourceBlock.Id,
            ToBlockId = sharedBlock.Id,
            StartNavigationDirection = RouteDirection.Right
        };
        blockerRoute.BlockIds.AddRange(new[] { blockerSourceBlock.Id, sharedBlock.Id });
        blockerRoute.PathElementIds.Add(turnout.Id);
        blockerRoute.TurnoutSettings.Add(new RouteTurnoutSetting { TurnoutId = turnout.Id, RequiredState = TurnoutState.Diverge });

        layout.Routes.Add(waitRoute);
        layout.Routes.Add(blockerRoute);
        return (layout, waitRoute, blockerRoute, sourceBlock, sharedBlock, blockerSourceBlock, turnout);
    }

    private static (TrackLayout Layout, RouteDefinition Route1, RouteDefinition Route2, BlockElement Source1, BlockElement Target1, BlockElement Source2, BlockElement Target2) CreateParallelRuntimeLayout()
    {
        var layout = new TrackLayout();
        var signal1 = new SignalElement { Id = "sig_parallel_1", MarkerKey = "Signal", DccAddress = 3, Aspect = SignalAspect.Stop, SignalProfile = "2-aspect-main" };
        var signal2 = new SignalElement { Id = "sig_parallel_2", MarkerKey = "Signal", DccAddress = 4, Aspect = SignalAspect.Stop, SignalProfile = "2-aspect-main" };
        var source1 = new BlockElement { Id = "blk_parallel_a", MarkerKey = "Block", Label = "Parallel A", SignalRightId = signal1.Id };
        var target1 = new BlockElement { Id = "blk_parallel_b", MarkerKey = "Block", Label = "Parallel B" };
        var source2 = new BlockElement { Id = "blk_parallel_c", MarkerKey = "Block", Label = "Parallel C", SignalRightId = signal2.Id };
        var target2 = new BlockElement { Id = "blk_parallel_d", MarkerKey = "Block", Label = "Parallel D" };

        layout.Elements.AddRange(new LayoutElement[] { source1, target1, source2, target2, signal1, signal2 });

        var route1 = new RouteDefinition { Id = "r_parallel_1", FromBlockId = source1.Id, ToBlockId = target1.Id, StartNavigationDirection = RouteDirection.Right };
        route1.BlockIds.AddRange(new[] { source1.Id, target1.Id });

        var route2 = new RouteDefinition { Id = "r_parallel_2", FromBlockId = source2.Id, ToBlockId = target2.Id, StartNavigationDirection = RouteDirection.Right };
        route2.BlockIds.AddRange(new[] { source2.Id, target2.Id });

        layout.Routes.Add(route1);
        layout.Routes.Add(route2);
        return (layout, route1, route2, source1, target1, source2, target2);
    }

    private sealed class Box<T>
        where T : class
    {
        public T? Value { get; set; }
    }

    private static object GetPrivateField(object instance, string fieldName)
    {
        var value = instance.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
        Assert.NotNull(value);
        return value;
    }

    private static object GetRuntimeRegistry(OperationViewModel vm)
        => GetPrivateField(vm, "_runtimeRegistry");

    private static object GetWaitCoordinator(OperationViewModel vm)
        => GetPrivateField(vm, "_waitCoordinator");

    private static object? InvokeInstanceMethod(object instance, string methodName, params object?[] args)
        => instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args);

    private static object? GetRuntimeState(OperationViewModel vm, string routeId)
        => InvokeInstanceMethod(GetRuntimeRegistry(vm), "GetRuntime", routeId);

    private static void AddActiveRoute(OperationViewModel vm, string routeId)
        => InvokeInstanceMethod(GetRuntimeRegistry(vm), "RegisterOrCreateRuntime", routeId, null, null, 0, null);

    private static IDictionary GetWaitStateMap(OperationViewModel vm)
    {
        var waitingRouteIds = Assert.IsAssignableFrom<IEnumerable>(GetRuntimeRegistry(vm).GetType().GetProperty("WaitingRouteIds")!.GetValue(GetRuntimeRegistry(vm)));
        var result = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var routeId in waitingRouteIds.Cast<object>().Select(v => v?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            var runtime = GetRuntimeState(vm, routeId!);
            if (runtime != null)
                result[routeId!] = runtime;
        }

        return result;
    }

    private static IDictionary GetRouteRuntimeMap(OperationViewModel vm)
    {
        var registry = GetRuntimeRegistry(vm);
        var activeRouteIds = Assert.IsAssignableFrom<IEnumerable>(registry.GetType().GetProperty("ActiveRouteIds")!.GetValue(registry));
        var result = new Hashtable(StringComparer.OrdinalIgnoreCase);

        foreach (var routeId in activeRouteIds.Cast<object>().Select(v => v?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            var runtime = GetRuntimeState(vm, routeId!);
            if (runtime == null)
                continue;

            result[routeId!] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["RouteId"] = routeId!,
                ["State"] = runtime.GetType().GetProperty("LifecycleState")?.GetValue(runtime)?.ToString() ?? string.Empty,
                ["SegmentIndex"] = (int?)runtime.GetType().GetProperty("CurrentTraversalIndex")?.GetValue(runtime) ?? 0,
                ["CurrentBlockId"] = runtime.GetType().GetProperty("CurrentBlockId")?.GetValue(runtime)?.ToString(),
                ["WaitingBlockId"] = runtime.GetType().GetProperty("WaitingBlockId")?.GetValue(runtime)?.ToString(),
                ["WaitingReason"] = runtime.GetType().GetProperty("WaitingReason")?.GetValue(runtime)?.ToString(),
                ["WaitingSinceUtc"] = (DateTime?)runtime.GetType().GetProperty("WaitingSinceUtc")?.GetValue(runtime)
            };
        }

        return result;
    }

    private static IDictionary GetTurnoutReservationMap(OperationViewModel vm)
    {
        var field = typeof(OperationViewModel).GetField("_turnoutRuntimeReservations", BindingFlags.NonPublic | BindingFlags.Instance);
        return Assert.IsAssignableFrom<IDictionary>(field?.GetValue(vm));
    }

    private static IDictionary GetWaitingResourceMap(OperationViewModel vm)
    {
        return Assert.IsAssignableFrom<IDictionary>(GetPrivateField(GetWaitCoordinator(vm), "_waitingResourceByRouteId"));
    }

    private static IDictionary GetWaitRegistrationMap(OperationViewModel vm)
    {
        return Assert.IsAssignableFrom<IDictionary>(GetPrivateField(GetWaitCoordinator(vm), "_waitRegistrationsByResourceKey"));
    }

    private static IDictionary GetStickyWaitGrantMap(OperationViewModel vm)
    {
        return Assert.IsAssignableFrom<IDictionary>(GetPrivateField(GetWaitCoordinator(vm), "_stickyWaitGrantsByResourceKey"));
    }

    private static IDictionary GetDeadlockYieldMap(OperationViewModel vm)
    {
        return Assert.IsAssignableFrom<IDictionary>(GetPrivateField(GetWaitCoordinator(vm), "_deadlockYieldByRouteId"));
    }

    private static IDictionary GetRouteActiveWindowMap(OperationViewModel vm)
    {
        var registry = GetRuntimeRegistry(vm);
        var activeRouteIds = Assert.IsAssignableFrom<IEnumerable>(registry.GetType().GetProperty("ActiveRouteIds")!.GetValue(registry));
        var result = new Hashtable(StringComparer.OrdinalIgnoreCase);

        foreach (var routeId in activeRouteIds.Cast<object>().Select(v => v?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            var runtime = GetRuntimeState(vm, routeId!);
            var reservationWindow = runtime?.GetType().GetProperty("ReservationWindow")?.GetValue(runtime);
            if (reservationWindow == null)
                continue;

            var blockIds = ((IEnumerable?)reservationWindow.GetType().GetProperty("BlockIds")?.GetValue(reservationWindow))?.Cast<object>().Select(v => v?.ToString() ?? string.Empty).Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pathIds = ((IEnumerable?)reservationWindow.GetType().GetProperty("PathElementIds")?.GetValue(reservationWindow))?.Cast<object>().Select(v => v?.ToString() ?? string.Empty).Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (blockIds.Count == 0 && pathIds.Count == 0)
                continue;

            result[routeId!] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["BlockIds"] = blockIds
            };
        }

        return result;
    }

    private static HashSet<string> GetRouteActiveWindowBlockIds(OperationViewModel vm, string routeId)
    {
        var map = GetRouteActiveWindowMap(vm);
        Assert.True(map.Contains(routeId));
        var window = map[routeId];
        Assert.NotNull(window);
        if (window is IDictionary dict && dict["BlockIds"] is IEnumerable<string> blockSet)
            return blockSet.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var property = window.GetType().GetProperty("BlockIds", BindingFlags.Public | BindingFlags.Instance);
        var blockIds = Assert.IsAssignableFrom<IEnumerable<string>>(property?.GetValue(window));
        return blockIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static object AssertRuntimeExists(OperationViewModel vm, string routeId)
    {
        var map = GetRouteRuntimeMap(vm);
        Assert.True(map.Contains(routeId));
        var runtimeInfo = map[routeId];
        Assert.NotNull(runtimeInfo);
        return runtimeInfo;
    }

    private static T? GetRuntimeProperty<T>(object runtimeInfo, string propertyName)
    {
        if (runtimeInfo is IDictionary dict && dict.Contains(propertyName))
        {
            var value = dict[propertyName];
            return value is null ? default : (T?)value;
        }

        var property = runtimeInfo.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return (T?)property?.GetValue(runtimeInfo);
    }

    private static void SetRouteRuntime(
        OperationViewModel vm,
        string routeId,
        string stateName,
        int segmentIndex,
        string? currentBlockId,
        IEnumerable<string>? traversalBlockIds = null,
        string? waitingBlockId = null,
        string? waitingReason = null,
        DateTime? waitingSinceUtc = null)
    {
        var registry = GetRuntimeRegistry(vm);
        InvokeInstanceMethod(registry, "RegisterOrCreateRuntime", routeId, null, traversalBlockIds, segmentIndex, currentBlockId);

        if (!string.IsNullOrWhiteSpace(waitingBlockId) && !string.IsNullOrWhiteSpace(waitingReason))
            InvokeInstanceMethod(registry, "EnterWaitState", routeId, waitingBlockId, waitingReason, waitingSinceUtc ?? DateTime.UtcNow);

        var lifecycleType = Type.GetType("TrackFlow.Runtime.RouteRuntimeLifecycleState, TrackFlow")!;
        var lifecycleValue = Enum.Parse(lifecycleType, stateName);
        InvokeInstanceMethod(registry, "SetLifecycleState", routeId, lifecycleValue);
    }

    private static void SetRouteActiveWindow(
        OperationViewModel vm,
        string routeId,
        IEnumerable<string> pathElementIds,
        IEnumerable<string> blockIds)
    {
        InvokeInstanceMethod(
            GetRuntimeRegistry(vm),
            "SetReservationWindow",
            routeId,
            pathElementIds,
            blockIds,
            null,
            false);
    }

    private static void SetWaitState(OperationViewModel vm, string routeId, string blockId, string reason)
    {
        var runtime = GetRuntimeState(vm, routeId);
        var waitingSinceUtc = (DateTime?)runtime?.GetType().GetProperty("WaitingSinceUtc")?.GetValue(runtime) ?? DateTime.UtcNow;
        InvokeInstanceMethod(GetRuntimeRegistry(vm), "EnterWaitState", routeId, blockId, reason, waitingSinceUtc);
    }

    private static void InvokeTrackWaitingResource(OperationViewModel vm, string routeId, string resourceKindName, string resourceId)
    {
        var coordinator = GetWaitCoordinator(vm);
        var method = coordinator.GetType().GetMethod("TrackWaitingResource", BindingFlags.NonPublic | BindingFlags.Instance);
        var resourceKindType = coordinator.GetType().GetNestedType("StickyWaitResourceKind", BindingFlags.NonPublic)!;
        var resourceKind = Enum.Parse(resourceKindType, resourceKindName);
        method!.Invoke(coordinator, new object?[] { routeId, resourceKind, resourceId });
    }

    private static void InvokeAssignStickyWaitWinner(OperationViewModel vm, TrackLayout layout, string resourceKindName, string resourceId)
    {
        var methodName = string.Equals(resourceKindName, "Turnout", StringComparison.OrdinalIgnoreCase)
            ? "AssignStickyWaitWinnerForTurnout"
            : "AssignStickyWaitWinnerForBlock";
        typeof(OperationViewModel).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(vm, new object[] { layout, resourceId });
    }

    private static (bool IsReserved, bool IsCriticalFailure) InvokeReserveNextBlockInternal(
        OperationViewModel vm,
        BlockElement next,
        string locoCode,
        string fromBlockId,
        TrackLayout layout)
    {
        var method = typeof(OperationViewModel).GetMethod("ReserveNextBlockInternal", BindingFlags.NonPublic | BindingFlags.Instance);
        var args = new object?[]
        {
            next,
            locoCode,
            true,
            locoCode,
            layout,
            fromBlockId,
            NavigationDirection.Right,
            false,
            true,
            true,
            true
        };

        var result = (bool)method!.Invoke(vm, args)!;
        return (result, (bool)args[7]!);
    }

    private static bool InvokeProcessDeadlockYieldState(OperationViewModel vm, TrackLayout layout, string routeId)
    {
        var method = typeof(OperationViewModel).GetMethod("ProcessDeadlockYieldState", BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)method!.Invoke(vm, new object[] { layout, routeId })!;
    }

    private static async Task<string?> InvokeWaitForNextBlockReservationAsync(
        OperationViewModel vm,
        TrackLayout layout,
        RouteDefinition route,
        IReadOnlyList<string> traversalBlockIds,
        int segmentIndex,
        BlockElement segmentTarget,
        string locoCode,
        bool orientationForward,
        NavigationDirection travelDirection,
        CancellationToken cancellationToken)
    {
        var method = typeof(OperationViewModel).GetMethod("WaitForNextBlockReservationAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method!.Invoke(vm, new object?[]
        {
            layout,
            route,
            traversalBlockIds,
            segmentIndex,
            segmentTarget,
            locoCode,
            orientationForward,
            travelDirection,
            null,
            cancellationToken
        })!;

        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task)?.ToString();
    }

    private static async Task<(bool IsReady, string? WaitReason)> InvokeTryEnsureTurnoutsForSegmentAsync(
        OperationViewModel vm,
        TrackLayout layout,
        RouteDefinition route,
        string fromBlockId,
        string toBlockId,
        TestDccCentralClient? dcc)
    {
        var method = typeof(OperationViewModel).GetMethod("TryEnsureTurnoutsForSegmentAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method!.Invoke(vm, new object?[]
        {
            layout,
            route,
            fromBlockId,
            toBlockId,
            dcc,
            CancellationToken.None
        })!;

        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var isReady = (bool)result.GetType().GetField("Item1")!.GetValue(result)!;
        var waitReason = (string?)result.GetType().GetField("Item2")!.GetValue(result);
        return (isReady, waitReason);
    }

    private static async Task InvokeDeactivateRouteInternalAsync(OperationViewModel vm, string routeId)
    {
        var method = typeof(OperationViewModel).GetMethod("DeactivateRouteInternalAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method!.Invoke(vm, new object?[]
        {
            routeId,
            false,
            null,
            default(CancellationToken),
            null,
            DiagnosticLevel.Info
        })!;

        await task;
    }

    private static void InvokeApplyBoundaryEntryState(
        OperationViewModel vm,
        TrackLayout layout,
        BlockElement sourceBlock,
        BlockElement targetBlock,
        Locomotive loco,
        string locoCode)
    {
        var method = typeof(OperationViewModel).GetMethod("ApplyBoundaryEntryState", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(vm, new object?[] { layout, sourceBlock, targetBlock, loco, locoCode, Array.Empty<object?>() });
    }

    private static void InvokeInitializeRouteRuntime(
        OperationViewModel vm,
        string routeId,
        string currentBlockId,
        IEnumerable<string> traversalBlockIds,
        string ownerLocomotiveId)
    {
        var method = typeof(OperationViewModel).GetMethod("InitializeRouteRuntime", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(vm, new object?[] { routeId, currentBlockId, traversalBlockIds, ownerLocomotiveId });
    }

    private static void InvokeSetTraversalSegmentWindow(
        OperationViewModel vm,
        TrackLayout layout,
        RouteDefinition route,
        IReadOnlyList<string> traversalBlockIds,
        int leadSegmentIndex)
    {
        var method = typeof(OperationViewModel).GetMethod("SetTraversalSegmentWindow", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(vm, new object?[] { layout, route, traversalBlockIds, leadSegmentIndex, false });
    }

    private static async Task<int> InvokeOnBlockOccupiedAsync(
        OperationViewModel vm,
        TrackLayout layout,
        string occupiedBlockId)
    {
        var method = typeof(OperationViewModel).GetMethod("OnBlockOccupiedAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task<int>)method!.Invoke(vm, new object?[] { layout, occupiedBlockId, null, CancellationToken.None, false })!;
        return await task;
    }

    private static async Task InvokeApplyTailClearStateAsync(
        OperationViewModel vm,
        TrackLayout layout,
        RouteDefinition route,
        BlockElement sourceBlock,
        BlockElement targetBlock)
    {
        var method = typeof(OperationViewModel).GetMethod("ApplyTailClearStateAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method!.Invoke(vm, new object?[] { layout, route, sourceBlock, targetBlock, null, CancellationToken.None })!;
        await task;
    }

    private static void AssertRouteLeakCleared(OperationViewModel vm, string routeId)
    {
        Assert.DoesNotContain(routeId, vm.ActiveRouteIds);
        Assert.False(GetWaitStateMap(vm).Contains(routeId));
        Assert.False(GetRouteRuntimeMap(vm).Contains(routeId));
        Assert.False(GetRouteActiveWindowMap(vm).Contains(routeId));
        Assert.False(GetWaitingResourceMap(vm).Contains(routeId));
        Assert.False(GetDeadlockYieldMap(vm).Contains(routeId));
        var turnoutReservationValues = GetTurnoutReservationMap(vm).Values.Cast<object?>().ToList();
        Assert.DoesNotContain(
            turnoutReservationValues,
            value => string.Equals(value?.ToString(), routeId, StringComparison.OrdinalIgnoreCase));
        var stickyWaitGrantValues = GetStickyWaitGrantMap(vm).Values.Cast<object?>().ToList();
        Assert.DoesNotContain(
            stickyWaitGrantValues,
            value =>
            {
                var winner = value?.GetType().GetProperty("WinnerRouteId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(value)?.ToString();
                return string.Equals(winner, routeId, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static void AssertNoGlobalRuntimeLeaks(OperationViewModel vm)
    {
        Assert.Empty(vm.ActiveRouteIds);
        Assert.Empty(GetWaitStateMap(vm));
        Assert.Empty(GetRouteRuntimeMap(vm));
        Assert.Empty(GetRouteActiveWindowMap(vm));
        Assert.Empty(GetTurnoutReservationMap(vm));
        Assert.Empty(GetWaitingResourceMap(vm));
        Assert.Empty(GetWaitRegistrationMap(vm));
        Assert.Empty(GetStickyWaitGrantMap(vm));
        Assert.Empty(GetDeadlockYieldMap(vm));
    }
}


using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.ViewModels.Operation;
using Xunit;

namespace TrackFlow.Tests;

public class OperationViewModelSignalSafetyTests
{
    [Fact]
    public async Task HandleSignalClickAsync_OccupiedProtectedBlock_ForceStopAndSendDcc()
    {
        var (vm, block, signal) = CreateVmWithSingleSignal();
        var client = new TestDccCentralClient { IsConnected = true };

        block.IsOccupied = true;
        signal.Aspect = SignalAspect.Proceed;

        await vm.HandleSignalClickAsync(signal, client);

        Assert.Equal(SignalAspect.Stop, signal.Aspect);
        Assert.Equal("signal-protected-block-occupied", vm.RouteActivationMessage);
        Assert.Equal("Navestidlo sa neda prepnut: chraneny blok je obsadeny.", vm.RouteActivationMessageText);
        Assert.Single(client.TurnoutCommands);
        Assert.Contains((signal.DccAddress * 4 + 1, false, true), client.TurnoutCommands);
    }

    [Fact]
    public async Task HandleSignalClickAsync_FreeProtectedBlock_RejectsManualToggleAndKeepsStop()
    {
        var (vm, block, signal) = CreateVmWithSingleSignal();
        var client = new TestDccCentralClient { IsConnected = true };

        block.IsOccupied = false;
        signal.Aspect = SignalAspect.Stop;

        await vm.HandleSignalClickAsync(signal, client);

        Assert.Equal(SignalAspect.Stop, signal.Aspect);
        Assert.Equal("signal-change-requires-route", vm.RouteActivationMessage);
        Assert.Empty(client.TurnoutCommands);
    }

    [Fact]
    public async Task RefreshSignalStatusAsync_OccupiedProtectedBlock_NezhadzujeNavestidloPredReleased()
    {
        var (vm, block, signal) = CreateVmWithSingleSignal();
        var client = new TestDccCentralClient { IsConnected = true };

        block.IsOccupied = true;
        signal.Aspect = SignalAspect.Proceed; // manual override before occupancy refresh

        var changed = await vm.RefreshSignalStatusAsync(client);

        Assert.Equal(0, changed);
        Assert.Equal(SignalAspect.Proceed, signal.Aspect);
        Assert.Empty(client.TurnoutCommands);
    }

    [Fact]
    public async Task HandleExternalOccupancyUpdateAsync_OccupiedProtectedBlock_NezhadzujeNavestidloPredReleased()
    {
        var (vm, block, signal) = CreateVmWithSingleSignal();
        var client = new TestDccCentralClient { IsConnected = true };

        signal.Aspect = SignalAspect.Proceed;
        block.IsOccupied = true;

        var changed = await vm.HandleExternalOccupancyUpdateAsync(client);

        Assert.Equal(0, changed);
        Assert.Equal(SignalAspect.Proceed, signal.Aspect);
        Assert.Empty(client.TurnoutCommands);
    }

    [Fact]
    public async Task HandleExternalOccupancyUpdateAsync_OccupiedProtectedBlock_WithDisconnectedDcc_NezhadzujePredReleased()
    {
        var (vm, block, signal) = CreateVmWithSingleSignal();
        var client = new TestDccCentralClient { IsConnected = false };

        signal.Aspect = SignalAspect.Proceed;
        block.IsOccupied = true;

        var changed = await vm.HandleExternalOccupancyUpdateAsync(client);

        Assert.Equal(0, changed);
        Assert.Equal(SignalAspect.Proceed, signal.Aspect);
        Assert.Empty(client.TurnoutCommands);
    }

    [Fact]
    public async Task HandleSignalClickAsync_FreeProtectedBlock_InfoMessageSaPoTtlSkryje()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var block = new BlockElement { Id = "blk_1", MarkerKey = "Block" };
        var signal = new SignalElement
        {
            Id = "sig_1",
            MarkerKey = "Signal",
            ProtectsBlockId = block.Id,
            DccAddress = 50,
            Aspect = SignalAspect.Stop,
            IsBasicMode = true
        };

        var layout = new TrackLayout();
        layout.Elements.Add(block);
        layout.Elements.Add(signal);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>(), transientRouteMessageTtlMs: 50);

        block.IsOccupied = false;
        await vm.HandleSignalClickAsync(signal, dccClient: null);

        Assert.Equal("signal-change-requires-route", vm.RouteActivationMessage);
        await Task.Delay(150);
        Assert.Equal(string.Empty, vm.RouteActivationMessage);
    }

    [Fact]
    public async Task HandleSignalClickAsync_FreeProtectedBlock_RespektujeInfoTtlZAppNastaveni()
    {
        var settings = new SettingsManager();
        settings.LoadApp();
        settings.NewProject();
        settings.App.EnableTransientRouteMessages = true;
        settings.App.RouteMessageTtlInfoMs = 40;

        var block = new BlockElement { Id = "blk_1", MarkerKey = "Block" };
        var signal = new SignalElement
        {
            Id = "sig_1",
            MarkerKey = "Signal",
            ProtectsBlockId = block.Id,
            DccAddress = 50,
            Aspect = SignalAspect.Stop,
            IsBasicMode = true
        };

        var layout = new TrackLayout();
        layout.Elements.Add(block);
        layout.Elements.Add(signal);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        block.IsOccupied = false;

        await vm.HandleSignalClickAsync(signal, dccClient: null);

        Assert.Equal("signal-change-requires-route", vm.RouteActivationMessage);
        await Task.Delay(140);
        Assert.Equal(string.Empty, vm.RouteActivationMessage);
    }

    [Fact]
    public async Task HandleSignalClickAsync_FreeProtectedBlock_KedSuTransientHlaseniaVypnute_NezmizneAutomaticky()
    {
        var settings = new SettingsManager();
        settings.LoadApp();
        settings.NewProject();
        settings.App.EnableTransientRouteMessages = false;
        settings.App.RouteMessageTtlInfoMs = 20;

        var block = new BlockElement { Id = "blk_1", MarkerKey = "Block" };
        var signal = new SignalElement
        {
            Id = "sig_1",
            MarkerKey = "Signal",
            ProtectsBlockId = block.Id,
            DccAddress = 50,
            Aspect = SignalAspect.Stop,
            IsBasicMode = true
        };

        var layout = new TrackLayout();
        layout.Elements.Add(block);
        layout.Elements.Add(signal);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        block.IsOccupied = false;

        await vm.HandleSignalClickAsync(signal, dccClient: null);

        Assert.Equal("signal-change-requires-route", vm.RouteActivationMessage);
        await Task.Delay(120);
        Assert.Equal("signal-change-requires-route", vm.RouteActivationMessage);
    }

    [Fact]
    public async Task RefreshSignalStatusAsync_OccupiedProtectedBlock_NeposielaForceStopPredReleased()
    {
        var (vm, block, signal) = CreateVmWithSingleSignal();
        var client = new TestDccCentralClient { IsConnected = true };

        block.IsOccupied = true;
        signal.Aspect = SignalAspect.Stop;

        var changed = await vm.RefreshSignalStatusAsync(client);

        Assert.Equal(0, changed);
        Assert.Equal(SignalAspect.Stop, signal.Aspect);
        Assert.Empty(client.TurnoutCommands);
    }

    [Fact]
    public async Task HandleExternalOccupancyUpdateAsync_ReleasedProtectedBlock_ZhodiNaStojAPosleDcc()
    {
        var (vm, block, signal) = CreateVmWithSingleSignal();
        var client = new TestDccCentralClient { IsConnected = true };

        signal.Aspect = SignalAspect.Proceed;
        block.IsOccupied = true;

        // Prvé volanie len zapamätá obsadený blok; nové pravidlo nezhadzuje pri Occupied.
        var occupiedChanged = await vm.HandleExternalOccupancyUpdateAsync(client);
        Assert.Equal(0, occupiedChanged);
        Assert.Equal(SignalAspect.Proceed, signal.Aspect);
        Assert.Empty(client.TurnoutCommands);

        block.IsOccupied = false;
        var releasedChanged = await vm.HandleExternalOccupancyUpdateAsync(client);

        Assert.True(releasedChanged > 0);
        Assert.Equal(SignalAspect.Stop, signal.Aspect);
        Assert.Single(client.TurnoutCommands);
        Assert.Contains((signal.DccAddress * 4 + 1, false, true), client.TurnoutCommands);
    }

    [Fact]
    public void IsSimulationMode_SwitchToLive_PreservesOccupancyFromActiveContactIndicator()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var block = new BlockElement
        {
            Id = "blk_live_1",
            MarkerKey = "Block",
            Label = "Blok Live 1",
            IsOccupied = true,
            AssignedLocoId = "754",
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    ModuleAddress = 1,
                    PortNumber = 7,
                    IsActive = true
                }
            }
        };

        var layout = new TrackLayout();
        layout.Elements.Add(block);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());

        vm.IsSimulationMode = false;

        Assert.True(block.IsOccupied);
        Assert.Null(block.AssignedLocoId);
    }

    [Fact]
    public void RefreshLayoutFromProject_ActiveContactIndicatorRestoresStaleOccupancy()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var block = new BlockElement
        {
            Id = "blk_live_2",
            MarkerKey = "Block",
            Label = "Blok Live 2",
            IsOccupied = false,
            Indicators =
            {
                new BlockIndicator
                {
                    Type = BlockIndicatorType.Contact,
                    ModuleAddress = 1,
                    PortNumber = 8,
                    IsActive = true
                }
            }
        };

        var layout = new TrackLayout();
        layout.Elements.Add(block);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());

        vm.RefreshLayoutFromProject();

        Assert.True(block.IsOccupied);
    }

    [Fact]
    public async Task HandleExternalOccupancyUpdateAsync_OccupiedFirstBlockBehindRouteStartSignal_DropsStartSignalToStop()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var blockA = new BlockElement { Id = "blk_a", MarkerKey = "Block", SignalRightId = "sig_start" };
        var blockB = new BlockElement { Id = "blk_b", MarkerKey = "Block" };
        var signalStart = new SignalElement
        {
            Id = "sig_start",
            MarkerKey = "Signal",
            DccAddress = 77,
            Aspect = SignalAspect.Stop,
            IsBasicMode = true
        };

        var route = new RouteDefinition
        {
            Id = "r_occ",
            Name = "Occ route",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            StartNavigationDirection = RouteDirection.Right,
            SafetyFallbackAspect = "Stop"
        };
        route.BlockIds.AddRange(new[] { "blk_a", "blk_b" });

        var layout = new TrackLayout();
        layout.Elements.AddRange(new LayoutElement[] { blockA, blockB, signalStart });
        layout.Routes.Add(route);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        var client = new TestDccCentralClient { IsConnected = true };

        var activation = await vm.RequestRouteAsync("r_occ", client);
        Assert.True(activation.IsSuccess);
        Assert.Equal(SignalAspect.Proceed, signalStart.Aspect);

        blockB.IsOccupied = true;
        await vm.HandleExternalOccupancyUpdateAsync(client);

        Assert.Equal(SignalAspect.Stop, signalStart.Aspect);
        Assert.DoesNotContain("r_occ", vm.ActiveRouteIds);
        Assert.Contains((signalStart.DccAddress * 4 + 1, false, true), client.TurnoutCommands);
    }

    private static (OperationViewModel Vm, BlockElement Block, SignalElement Signal) CreateVmWithSingleSignal()
    {
        var settings = new SettingsManager();
        settings.NewProject();

        var block = new BlockElement { Id = "blk_1", MarkerKey = "Block" };
        var signal = new SignalElement
        {
            Id = "sig_1",
            MarkerKey = "Signal",
            ProtectsBlockId = block.Id,
            DccAddress = 50,
            Aspect = SignalAspect.Stop,
            IsBasicMode = true
        };

        var layout = new TrackLayout();
        layout.Elements.Add(block);
        layout.Elements.Add(signal);
        settings.CurrentProject!.Layout = layout;

        var vm = new OperationViewModel(settings, new ObservableCollection<Locomotive>());
        return (vm, block, signal);
    }
}


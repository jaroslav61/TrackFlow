using TrackFlow.Models;
using TrackFlow.ViewModels;
using Xunit;

namespace TrackFlow.Tests;

public class MainWindowSimulationStateTests
{
    [Fact]
    public void Startup_DefaultsSimulationStateToFalse()
    {
        using var vm = new MainWindowViewModel();

        Assert.False(vm.IsSimulationActive);
        Assert.False(vm.Tabs.Operation.IsSimulationMode);
    }

    [Fact]
    public void Startup_DefaultModeIsEdit()
    {
        using var vm = new MainWindowViewModel();

        Assert.Equal(OperationMode.Edit, vm.ModeManager.CurrentMode);
        Assert.True(vm.IsEditorMode);
        Assert.False(vm.IsSimulationActive);
    }

    [Fact]
    public void ForceSimulationMode_UpdatesIsSimulationActiveAndOperationVm()
    {
        using var vm = new MainWindowViewModel();

        vm.ModeManager.ForceMode(OperationMode.Simulation);

        Assert.True(vm.IsSimulationActive);
        Assert.True(vm.Tabs.Operation.IsSimulationMode);
    }

    [Fact]
    public void StopSimulation_UpdatesIsSimulationActiveAndOperationVm()
    {
        using var vm = new MainWindowViewModel();
        vm.ModeManager.ForceMode(OperationMode.Simulation);

        vm.ModeManager.StopSimulation();

        Assert.False(vm.IsSimulationActive);
        Assert.False(vm.Tabs.Operation.IsSimulationMode);
    }

    [Fact]
    public void OperationSimulationToggle_SyncsToModeManager()
    {
        using var vm = new MainWindowViewModel();

        vm.Tabs.Operation.IsSimulationMode = true;
        Assert.True(vm.IsSimulationActive);
        Assert.Equal(OperationMode.Simulation, vm.ModeManager.CurrentMode);

        vm.Tabs.Operation.IsSimulationMode = false;
        Assert.False(vm.IsSimulationActive);
        Assert.NotEqual(OperationMode.Simulation, vm.ModeManager.CurrentMode);
    }

    [Fact]
    public void CanConnectDcc_FalseWhenSimulationActive()
    {
        using var vm = new MainWindowViewModel();
        vm.ModeManager.ForceMode(OperationMode.Simulation);

        Assert.False(vm.CanConnectDcc);
    }

    [Fact]
    public void CanConnectDcc_TrueWhenSimulationInactive()
    {
        using var vm = new MainWindowViewModel();
        vm.ModeManager.ForceMode(OperationMode.Offline);

        Assert.True(vm.CanConnectDcc);
    }

    [Fact]
    public void SimulatorButtonText_OffWhenNotSimulating()
    {
        using var vm = new MainWindowViewModel();
        vm.ModeManager.ForceMode(OperationMode.Offline);

        Assert.Equal("Spustiť simulátor", vm.OperationModeDisplayTitle);
    }

    [Fact]
    public void SimulatorButtonText_OnWhenSimulating()
    {
        using var vm = new MainWindowViewModel();
        vm.ModeManager.ForceMode(OperationMode.Simulation);

        Assert.Equal("Simulátor: RUNNING", vm.OperationModeDisplayTitle);
    }
}

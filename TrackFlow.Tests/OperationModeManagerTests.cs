using System.Threading.Tasks;
using TrackFlow.Models;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

/// <summary>
/// Unit testy pre OperationModeManager.
/// Testy nepoužívajú Avalonia UI – všetky závislosti sú injektované ako lambdy.
/// ConfirmAsync = null → manager predpokladá súhlas (vhodné pre pozitívne scenáre).
/// ConfirmAsync = () => false → simuluje klik NIE v dialógu.
/// </summary>
public class OperationModeManagerTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static OperationModeManager CreateManager(
        bool confirmResult = true,
        bool disconnectResult = true)
    {
        var mgr = new OperationModeManager();
        mgr.ConfirmAsync = (_, _) => Task.FromResult(confirmResult);
        mgr.DisconnectAllCentrals = () => disconnectResult;
        mgr.SwitchToOperationTab = () => { };
        return mgr;
    }

    // ── Počiatočný stav ──────────────────────────────────────────────────────

    [Fact]
    public void InitialMode_IsOffline()
    {
        var mgr = new OperationModeManager();
        Assert.Equal(OperationMode.Offline, mgr.CurrentMode);
    }

    [Fact]
    public void ForceMode_SetsMode()
    {
        var mgr = new OperationModeManager();
        mgr.ForceMode(OperationMode.Edit);
        Assert.Equal(OperationMode.Edit, mgr.CurrentMode);
    }

    // ── ModeChanged event ────────────────────────────────────────────────────

    [Fact]
    public void ModeChanged_FiredWithCorrectValues()
    {
        var mgr = new OperationModeManager();
        OperationMode? firedPrev = null;
        OperationMode? firedCurr = null;
        mgr.ModeChanged += (prev, curr) => { firedPrev = prev; firedCurr = curr; };

        mgr.ForceMode(OperationMode.Simulation);

        Assert.Equal(OperationMode.Offline, firedPrev);
        Assert.Equal(OperationMode.Simulation, firedCurr);
    }

    [Fact]
    public void ModeChanged_NotFired_WhenModeUnchanged()
    {
        var mgr = new OperationModeManager();
        var count = 0;
        mgr.ModeChanged += (_, _) => count++;

        mgr.ForceMode(OperationMode.Offline); // rovnaký stav
        Assert.Equal(0, count);
    }

    // ── Scenár A: čistý štart ────────────────────────────────────────────────

    [Fact]
    public async Task ScenarioA_OfflineToSimulation_Succeeds()
    {
        var mgr = CreateManager();
        mgr.ForceMode(OperationMode.Offline);

        var result = await mgr.TryStartSimulationAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationMode.Simulation, mgr.CurrentMode);
        Assert.True(mgr.IsSimulation);
    }

    [Fact]
    public async Task ScenarioA_AlreadySimulation_ReturnsSuccess()
    {
        var mgr = CreateManager();
        mgr.ForceMode(OperationMode.Simulation);

        var result = await mgr.TryStartSimulationAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationMode.Simulation, mgr.CurrentMode);
    }

    // ── Scenár B: Edit, centrála odpojená ────────────────────────────────────

    [Fact]
    public async Task ScenarioB_EditOffline_UserConfirms_StartsSimulation()
    {
        var mgr = CreateManager(confirmResult: true);
        mgr.ForceMode(OperationMode.Edit);

        var result = await mgr.TryStartSimulationAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationMode.Simulation, mgr.CurrentMode);
    }

    [Fact]
    public async Task ScenarioB_EditOffline_UserCancels_StaysInEdit()
    {
        var mgr = CreateManager(confirmResult: false);
        mgr.ForceMode(OperationMode.Edit);

        var result = await mgr.TryStartSimulationAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationMode.Edit, mgr.CurrentMode);
    }

    // ── Scenár C: Prevádzka, centrála pripojená ──────────────────────────────

    [Fact]
    public async Task ScenarioC_OnlineUserConfirms_DisconnectsAndStartsSimulation()
    {
        var disconnectCalled = false;
        var mgr = CreateManager(confirmResult: true);
        mgr.DisconnectAllCentrals = () => { disconnectCalled = true; return true; };
        mgr.ForceMode(OperationMode.Online);

        var result = await mgr.TryStartSimulationAsync();

        Assert.True(result.IsSuccess);
        Assert.True(disconnectCalled);
        Assert.Equal(OperationMode.Simulation, mgr.CurrentMode);
    }

    [Fact]
    public async Task ScenarioC_OnlineUserCancels_StaysOnline()
    {
        var mgr = CreateManager(confirmResult: false);
        mgr.ForceMode(OperationMode.Online);

        var result = await mgr.TryStartSimulationAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationMode.Online, mgr.CurrentMode);
    }

    [Fact]
    public async Task ScenarioC_DisconnectFails_ReturnsFailure()
    {
        var mgr = CreateManager(confirmResult: true, disconnectResult: false);
        mgr.ForceMode(OperationMode.Online);

        var result = await mgr.TryStartSimulationAsync();

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.FailureReason);
        Assert.NotEqual(OperationMode.Simulation, mgr.CurrentMode);
    }

    // ── StopSimulation ───────────────────────────────────────────────────────

    [Fact]
    public void StopSimulation_FromSimulation_GoesOffline()
    {
        var mgr = new OperationModeManager();
        mgr.ForceMode(OperationMode.Simulation);

        var result = mgr.StopSimulation();

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationMode.Offline, mgr.CurrentMode);
    }

    [Fact]
    public void StopSimulation_WhenNotSimulating_ReturnsSuccess_NoChange()
    {
        var mgr = new OperationModeManager();
        mgr.ForceMode(OperationMode.Offline);

        var result = mgr.StopSimulation();

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationMode.Offline, mgr.CurrentMode);
    }

    // ── TryEnterEdit ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TryEnterEdit_FromOffline_Succeeds()
    {
        var mgr = CreateManager();
        mgr.ForceMode(OperationMode.Offline);

        var result = await mgr.TryEnterEditAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationMode.Edit, mgr.CurrentMode);
    }

    [Fact]
    public async Task TryEnterEdit_DuringSimulation_UserConfirms_StopsSimAndEntersEdit()
    {
        var mgr = CreateManager(confirmResult: true);
        mgr.ForceMode(OperationMode.Simulation);

        var result = await mgr.TryEnterEditAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationMode.Edit, mgr.CurrentMode);
    }

    [Fact]
    public async Task TryEnterEdit_DuringSimulation_UserCancels_StaysSimulation()
    {
        var mgr = CreateManager(confirmResult: false);
        mgr.ForceMode(OperationMode.Simulation);

        var result = await mgr.TryEnterEditAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationMode.Simulation, mgr.CurrentMode);
    }

    // ── NotifyCentralConnected / Disconnected ────────────────────────────────

    [Fact]
    public void NotifyCentralConnected_FromOffline_GoesOnline()
    {
        var mgr = new OperationModeManager();
        mgr.ForceMode(OperationMode.Offline);

        var result = mgr.NotifyCentralConnected();

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationMode.Online, mgr.CurrentMode);
    }

    [Fact]
    public void NotifyCentralConnected_DuringSimulation_ReturnsFailure()
    {
        var mgr = new OperationModeManager();
        mgr.ForceMode(OperationMode.Simulation);

        var result = mgr.NotifyCentralConnected();

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationMode.Simulation, mgr.CurrentMode);
    }

    [Fact]
    public void NotifyCentralDisconnected_FromOnline_GoesOffline()
    {
        var mgr = new OperationModeManager();
        mgr.ForceMode(OperationMode.Online);

        mgr.NotifyCentralDisconnected();

        Assert.Equal(OperationMode.Offline, mgr.CurrentMode);
    }

    [Fact]
    public void NotifyCentralDisconnected_FromSimulation_NoChange()
    {
        var mgr = new OperationModeManager();
        mgr.ForceMode(OperationMode.Simulation);

        mgr.NotifyCentralDisconnected();

        // Počas simulácie disconnect nemení stav – simulátor beží ďalej
        Assert.Equal(OperationMode.Simulation, mgr.CurrentMode);
    }

    // ── SwitchToOffline ──────────────────────────────────────────────────────

    [Fact]
    public void SwitchToOffline_FromEdit_Succeeds()
    {
        var mgr = new OperationModeManager();
        mgr.ForceMode(OperationMode.Edit);

        var result = mgr.SwitchToOffline();

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationMode.Offline, mgr.CurrentMode);
    }

    [Fact]
    public void SwitchToOffline_DuringSimulation_ReturnsFailure()
    {
        var mgr = new OperationModeManager();
        mgr.ForceMode(OperationMode.Simulation);

        var result = mgr.SwitchToOffline();

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationMode.Simulation, mgr.CurrentMode);
    }

    // ── Computed helpers ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(OperationMode.Edit,       true,  false, false, false)]
    [InlineData(OperationMode.Offline,    false, true,  false, false)]
    [InlineData(OperationMode.Online,     false, false, true,  false)]
    [InlineData(OperationMode.Simulation, false, false, false, true)]
    public void ComputedHelpers_MatchCurrentMode(
        OperationMode mode, bool edit, bool offline, bool online, bool sim)
    {
        var mgr = new OperationModeManager();
        mgr.ForceMode(mode);

        Assert.Equal(edit,    mgr.IsEdit);
        Assert.Equal(offline, mgr.IsOffline);
        Assert.Equal(online,  mgr.IsOnline);
        Assert.Equal(sim,     mgr.IsSimulation);
    }
}

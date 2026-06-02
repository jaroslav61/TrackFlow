using TrackFlow.Models.Layout;
using System;
using TrackFlow.Services;
using TrackFlow.ViewModels.Editor;
using Xunit;

namespace TrackFlow.Tests;

/// <summary>
/// Testy pre TurnoutPropertiesViewModel.
/// Kryjú najmä inicializačný scenár, ktorý spôsoboval NullReferenceException
/// pri volaní SaveCommand.NotifyCanExecuteChanged() pred vytvorením príkazu.
/// </summary>
public class TurnoutPropertiesViewModelTests
{
    private static LayoutEditorViewModel CreateLayoutVm() => new(new SettingsManager());

    // ── Inicializácia ────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_Default_DoesNotThrow()
    {
        // Regression: SaveCommand.NotifyCanExecuteChanged() sa volalo pred
        // vytvorením SaveCommand => NullReferenceException pri otvorení vlastností.
        var ex = Record.Exception(() => new TurnoutPropertiesViewModel());
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_WithTurnout_DoesNotThrow()
    {
        var turnout = new TurnoutElement { DccAddress = 0, DccAddress2 = 0 };
        var ex = Record.Exception(() => new TurnoutPropertiesViewModel(turnout, null, null));
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_WithNonZeroDccAddress_DoesNotThrow()
    {
        var turnout = new TurnoutElement { DccAddress = 42, DccAddress2 = 0 };
        var ex = Record.Exception(() => new TurnoutPropertiesViewModel(turnout, null, null));
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_WithMaxDccAddress_DoesNotThrow()
    {
        var turnout = new TurnoutElement { DccAddress = 2048, DccAddress2 = 2048 };
        var ex = Record.Exception(() => new TurnoutPropertiesViewModel(turnout, null, null));
        Assert.Null(ex);
    }

    // ── SaveCommand ───────────────────────────────────────────────────────────

    [Fact]
    public void SaveCommand_IsNotNull_AfterConstruction()
    {
        var vm = new TurnoutPropertiesViewModel();
        Assert.NotNull(vm.SaveCommand);
    }

    [Fact]
    public void SaveCommand_CanExecute_IsTrueForValidAddresses()
    {
        var turnout = new TurnoutElement { DccAddress = 1, DccAddress2 = 0 };
        var vm = new TurnoutPropertiesViewModel(turnout, null, null);
        Assert.True(vm.CanSave);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    // ── DCC adresa validácia ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(2048)]
    public void DccAddress_ValidRange_HasNoError(int address)
    {
        var vm = new TurnoutPropertiesViewModel();
        vm.DccAddress = address;
        Assert.Empty(vm.DccAddressError);
        Assert.False(vm.HasDccAddressError);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2049)]
    [InlineData(9999)]
    public void DccAddress_InvalidRange_HasError(int address)
    {
        var vm = new TurnoutPropertiesViewModel();
        vm.DccAddress = address;
        Assert.NotEmpty(vm.DccAddressError);
        Assert.True(vm.HasDccAddressError);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2049)]
    public void DccAddress_InvalidValue_CanSaveIsFalse(int address)
    {
        var vm = new TurnoutPropertiesViewModel();
        vm.DccAddress = address;
        Assert.False(vm.CanSave);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void DccAddress_SetToValid_AfterInvalid_CanSaveBecomesTrue()
    {
        var vm = new TurnoutPropertiesViewModel();
        vm.DccAddress = 9999;   // neplatná
        Assert.False(vm.CanSave);

        vm.DccAddress = 100;    // platná
        Assert.True(vm.CanSave);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    // ── DCC adresa 2 validácia ────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(2048)]
    public void DccAddressTwo_ValidRange_HasNoError(int address)
    {
        var vm = new TurnoutPropertiesViewModel();
        vm.DccAddressTwo = address;
        Assert.Empty(vm.DccAddressTwoError);
        Assert.False(vm.HasDccAddressTwoError);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2049)]
    public void DccAddressTwo_InvalidRange_CanSaveIsFalse(int address)
    {
        var vm = new TurnoutPropertiesViewModel();
        vm.DccAddressTwo = address;
        Assert.False(vm.CanSave);
    }

    // ── Načítanie hodnôt z modelu ─────────────────────────────────────────────

    [Fact]
    public void Constructor_LoadsModelValues_Correctly()
    {
        var turnout = new TurnoutElement
        {
            Label = "Test výhybka",
            Description = "Popis",
            DccAddress = 42,
            DccAddress2 = 7,
            InitialState = TurnoutState.Diverge,
            PulseLength = 150,
            UseDefaultPulse = false,
            ReverseLogic = true,
            MaxSpeed = 80,
            LimitedSpeed = 30,
            RequestYellow = true
        };

        var vm = new TurnoutPropertiesViewModel(turnout, null, null);

        Assert.Equal("Test výhybka", vm.TurnoutName);
        Assert.Equal("Popis", vm.Description);
        Assert.Equal(42, vm.DccAddress);
        Assert.Equal(7, vm.DccAddressTwo);
        Assert.Equal(TurnoutState.Diverge, vm.InitialState);
        Assert.Equal(150, vm.PulseLength);
        Assert.False(vm.UseDefaultPulse);
        Assert.True(vm.ReverseLogic);
        Assert.Equal(80, vm.MaxSpeed);
        Assert.Equal(30, vm.LimitedSpeed);
        Assert.True(vm.RequestYellow);
    }

    [Fact]
    public void Constructor_LoadsOnlyEnabledDccCentrals_AndPreselectsSavedProfile()
    {
        var disabledProfile = new TrackFlow.Models.DccCentralProfile
        {
            Id = Guid.NewGuid(),
            IsEnabled = false,
            Type = TrackFlow.Models.DccCentralType.Z21Legacy,
            Host = "192.168.0.10",
            Port = 21105
        };

        var selectedProfile = new TrackFlow.Models.DccCentralProfile
        {
            Id = Guid.NewGuid(),
            IsEnabled = true,
            Type = TrackFlow.Models.DccCentralType.NanoX_S88,
            SerialPort = "COM9",
            BaudRate = 19200
        };

        var settings = new SettingsManager();
        settings.LoadApp();
        settings.App.DccCentralProfiles.Clear();
        settings.App.DccCentralProfiles.Add(disabledProfile);
        settings.App.DccCentralProfiles.Add(selectedProfile);

        var turnout = new TurnoutElement
        {
            DccCentralProfileId = selectedProfile.Id,
            DccAddress = 42
        };

        var vm = new TurnoutPropertiesViewModel(turnout, null, settings);

        Assert.Equal(2, vm.DccSystems.Count);
        Assert.Equal("Bez pripojenia", vm.DccSystems[0].Name);
        Assert.DoesNotContain(vm.DccSystems, x => x.ProfileId == disabledProfile.Id);
        Assert.Equal(selectedProfile.Id, vm.SelectedDccSystem?.ProfileId);
    }

    // ── ShowSecondAddress ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Turnout_3W", true)]
    [InlineData("DoubleSlip", true)]
    [InlineData("Turnout_L", false)]
    [InlineData("Turnout_R", false)]
    [InlineData("Turnout_Y", false)]
    public void ShowSecondAddress_DependsOnMarkerKey(string markerKey, bool expected)
    {
        var turnout = new TurnoutElement { MarkerKey = markerKey };
        var vm = new TurnoutPropertiesViewModel(turnout, null, null);
        Assert.Equal(expected, vm.ShowSecondAddress);
    }

    // ── Find voľnej DCC adresy ───────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task FindAddress_UsesNearestFree_AndRespectsDccAddress2Conflicts()
    {
        var layoutVm = CreateLayoutVm();
        var current = new TurnoutElement { DccAddress = 10 };

        layoutVm.Elements.Add(current);
        layoutVm.Elements.Add(new TurnoutElement { DccAddress = 10 });
        layoutVm.Elements.Add(new TurnoutElement { DccAddress2 = 11 });

        var vm = new TurnoutPropertiesViewModel(current, layoutVm, null);
        await vm.FindAddressCommand.ExecuteAsync(null);

        Assert.Equal(12, vm.DccAddress);
    }

    [Fact]
    public async System.Threading.Tasks.Task FindAddress_IgnoresCurrentTurnoutOwnAddresses()
    {
        var layoutVm = CreateLayoutVm();
        var current = new TurnoutElement { DccAddress = 50, DccAddress2 = 51 };

        layoutVm.Elements.Add(current);

        var vm = new TurnoutPropertiesViewModel(current, layoutVm, null);
        await vm.FindAddressCommand.ExecuteAsync(null);

        // Aktuálna výhybka sa má ignorovať, teda 50 môže zostať vybraná.
        Assert.Equal(50, vm.DccAddress);
    }

    [Fact]
    public async System.Threading.Tasks.Task FindAddress_WrapsAround_From2048_ToFirstFree()
    {
        var layoutVm = CreateLayoutVm();
        var current = new TurnoutElement { DccAddress = 2048 };

        layoutVm.Elements.Add(current);
        layoutVm.Elements.Add(new TurnoutElement { DccAddress = 2048 });
        layoutVm.Elements.Add(new TurnoutElement { DccAddress = 1 });
        layoutVm.Elements.Add(new TurnoutElement { DccAddress = 2 });

        var vm = new TurnoutPropertiesViewModel(current, layoutVm, null);
        await vm.FindAddressCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.DccAddress);
    }

    [Fact]
    public async System.Threading.Tasks.Task FindAddress_WhenAllAddressesUsed_DoesNotChangeCurrentValue()
    {
        var layoutVm = CreateLayoutVm();
        var current = new TurnoutElement { DccAddress = 777 };
        layoutVm.Elements.Add(current);

        // Zaplníme celý rozsah 1..2048 inými výhybkami.
        for (var i = 1; i <= 2048; i++)
            layoutVm.Elements.Add(new TurnoutElement { DccAddress = i });

        var vm = new TurnoutPropertiesViewModel(current, layoutVm, null);
        await vm.FindAddressCommand.ExecuteAsync(null);

        Assert.Equal(777, vm.DccAddress);
    }

    [Fact]
    public async System.Threading.Tasks.Task FindAddress_WithNullLayoutVm_DoesNotChangeValue()
    {
        var turnout = new TurnoutElement { DccAddress = 123 };
        var vm = new TurnoutPropertiesViewModel(turnout, null, null);

        await vm.FindAddressCommand.ExecuteAsync(null);

        Assert.Equal(123, vm.DccAddress);
    }
}


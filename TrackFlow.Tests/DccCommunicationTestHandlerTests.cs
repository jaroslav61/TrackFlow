using System;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.Services.Dcc;
using TrackFlow.ViewModels.Settings;
using Xunit;

namespace TrackFlow.Tests;

public sealed class DccCommunicationTestHandlerTests
{
    [Fact]
    public async Task TestCommand_PriUspechuNastaviVysledokAUvolniBeziaciStav()
    {
        var service = new FakeDccCommunicationTestService
        {
            OnTestAsync = (request, _) => Task.FromResult($"OK {request.DecoderTimeoutMs} {request.ProgrammingMode}")
        };
        var handler = new DccCommunicationTestHandler(service, new FakeDccConnectionService());

        await handler.TestCommand.ExecuteAsync(null);

        Assert.False(handler.IsTestingCommunication);
        Assert.Equal("OK 3000 ServiceTrack", handler.TestResult);
        Assert.NotNull(service.LastRequest);
        Assert.Equal(3_000, service.LastRequest!.DecoderTimeoutMs);
        Assert.Equal(DccProgrammingTestMode.ServiceTrack, service.LastRequest.ProgrammingMode);
        Assert.Equal(3, service.LastRequest.LocoAddress);
    }

    [Fact]
    public async Task TestCommand_PriVynimkeNastaviChybovyTextAUvolniBeziaciStav()
    {
        var service = new FakeDccCommunicationTestService
        {
            OnTestAsync = (_, _) => throw new TimeoutException("Timeout")
        };
        var handler = new DccCommunicationTestHandler(service, new FakeDccConnectionService())
        {
            IsServiceTrackProgrammingMode = true
        };

        await handler.TestCommand.ExecuteAsync(null);

        Assert.False(handler.IsTestingCommunication);
        Assert.Equal("Centrála neodpovedá v limite. Skontrolujte napájanie trafa.", handler.TestResult);
    }

    [Fact]
    public void TestCommand_JeDisabledKymNieJeGlobalnaDccCentralaPripojena()
    {
        var connection = new FakeDccConnectionService(false);
        var handler = new DccCommunicationTestHandler(new FakeDccCommunicationTestService(), connection);

        Assert.False(handler.TestCommand.CanExecute(null));
        Assert.False(handler.IsTestButtonEnabled);
        Assert.Equal("DCC centrála musí byť pripojená.", handler.DisabledConnectionToolTip);

        connection.SetConnected(true);

        Assert.True(handler.TestCommand.CanExecute(null));
        Assert.True(handler.IsTestButtonEnabled);
    }

    [Fact]
    public void TestCommand_JeDisabled_PreCentraluBezCvProgramovania()
    {
        var connection = new FakeDccConnectionService(true, new FakeNonProgrammingDccCentralClient());
        var handler = new DccCommunicationTestHandler(new FakeDccCommunicationTestService(), connection)
        {
            ConfiguredCentralType = DccCentralType.NanoX_S88
        };

        Assert.False(handler.SupportsProgrammingTest);
        Assert.False(handler.TestCommand.CanExecute(null));
        Assert.Equal("Čítanie CV pre túto centrálu zatiaľ nie je implementované.", handler.DisabledConnectionToolTip);
        Assert.Equal("Čítanie CV pre túto centrálu zatiaľ nie je implementované.", handler.DisabledConnectionHint);
        Assert.True(handler.HasDisabledTestHint);
    }

    [Fact]
    public void SettingsViewModel_ExponujeTestHandlerSubProperty()
    {
        var vm = new SettingsViewModel(new SettingsManager(), new FakeDccCommunicationTestService(), new FakeDccConnectionService());

        Assert.NotNull(vm.TestHandler);
        Assert.Equal(3_000, vm.TestHandler.DecoderTimeoutMs);
    }

    [Fact]
    public void IsZ21Start_VratiTrue_AleServiceTrackZostavaDostupny()
    {
        // z21 start síce nemá fyzický PROG výstup, ale Service Mode zvláda cez
        // prepnutie hlavnej trate – preto Service Track NEZAKAZUJEME a NEPREPÍNAME
        // automaticky na POM. Zostáva to na voľbe používateľa.
        var z21 = new Z21Client();
        z21.SetHardwareTypeForTest(Z21HardwareType.Z21Start);

        var connection = new FakeDccConnectionService(isConnected: false, client: z21);
        var handler = new DccCommunicationTestHandler(new FakeDccCommunicationTestService(), connection);

        Assert.True(handler.IsServiceTrackProgrammingMode); // default

        connection.SetConnected(true);

        Assert.True(handler.IsZ21Start);
        Assert.False(handler.IsServiceTrackUnavailable);
        Assert.True(handler.IsServiceTrackAvailable);
        Assert.True(handler.IsServiceTrackProgrammingMode); // NEzmenené – ostáva Service Track
        Assert.False(handler.IsPomProgrammingMode);
        Assert.Equal(string.Empty, handler.ServiceTrackDisabledTooltip);
    }

    [Fact]
    public void IsZ21Start_VratiFalse_PreCiernuZ21()
    {
        var z21 = new Z21Client();
        z21.SetHardwareTypeForTest(Z21HardwareType.Z21New); // čierna Z21

        var connection = new FakeDccConnectionService(isConnected: true, client: z21);
        var handler = new DccCommunicationTestHandler(new FakeDccCommunicationTestService(), connection);

        Assert.False(handler.IsZ21Start);
        Assert.False(handler.IsServiceTrackUnavailable);
        Assert.True(handler.IsServiceTrackAvailable);
        Assert.True(handler.IsServiceTrackProgrammingMode); // ostáva default Service Track
        Assert.Equal(string.Empty, handler.ServiceTrackDisabledTooltip);
    }

    [Fact]
    public void TestCommand_JeDisabled_VRežimePom()
    {
        var handler = new DccCommunicationTestHandler(new FakeDccCommunicationTestService(), new FakeDccConnectionService())
        {
            IsServiceTrackProgrammingMode = false,
            TestLocoAddress = 1234
        };

        Assert.True(handler.IsPomProgrammingMode);
        Assert.False(handler.IsCvReadAvailableForSelectedMode);
        Assert.False(handler.IsTestButtonEnabled);
        Assert.False(handler.TestCommand.CanExecute(null));
        Assert.Equal("Čítanie CV je dostupné iba v režime Service Track.", handler.DisabledConnectionToolTip);
        Assert.Equal("Režim POM nepodporuje čítanie CV.", handler.DisabledConnectionHint);
        Assert.True(handler.HasDisabledTestHint);

        handler.IsServiceTrackProgrammingMode = true;

        Assert.True(handler.IsCvReadAvailableForSelectedMode);
        Assert.True(handler.IsTestButtonEnabled);
        Assert.True(handler.TestCommand.CanExecute(null));
    }

    [Fact]
    public async Task TestCommand_VRežimePom_SaNespustiAniSCommandParameterLocomotive()
    {
        var service = new FakeDccCommunicationTestService();
        var handler = new DccCommunicationTestHandler(service, new FakeDccConnectionService())
        {
            IsServiceTrackProgrammingMode = false,
            TestLocoAddress = 3
        };
        var selectedLoco = new Locomotive("L123", "Test") { DccAddress = 1234 };

        await handler.TestCommand.ExecuteAsync(selectedLoco);

        Assert.False(handler.TestCommand.CanExecute(selectedLoco));
        Assert.Null(service.LastRequest);
        Assert.Equal(3, handler.TestLocoAddress);
    }

    [Fact]
    public async Task TestCommand_VRežimePom_SaNespustiAniSCommandParameterLocoRecord()
    {
        var service = new FakeDccCommunicationTestService();
        var handler = new DccCommunicationTestHandler(service, new FakeDccConnectionService())
        {
            IsServiceTrackProgrammingMode = false,
            TestLocoAddress = 3
        };
        var selectedLoco = new LocoRecord { Address = 77 };

        await handler.TestCommand.ExecuteAsync(selectedLoco);

        Assert.False(handler.TestCommand.CanExecute(selectedLoco));
        Assert.Null(service.LastRequest);
        Assert.Equal(3, handler.TestLocoAddress);
    }

    [Fact]
    public async Task TestCommand_VRežimePom_SaNespustiAniSPriamouAdresou()
    {
        var service = new FakeDccCommunicationTestService();
        var handler = new DccCommunicationTestHandler(service, new FakeDccConnectionService())
        {
            IsServiceTrackProgrammingMode = false,
            TestLocoAddress = 3
        };

        await handler.TestCommand.ExecuteAsync(55);

        Assert.False(handler.TestCommand.CanExecute(55));
        Assert.Null(service.LastRequest);
        Assert.Equal(3, handler.TestLocoAddress);
    }

    [Fact]
    public void SettingsViewModel_ExponujeSelectedLocomotiveForDccTestZProvidera()
    {
        var vm = new SettingsViewModel(new SettingsManager(), new FakeDccCommunicationTestService(), new FakeDccConnectionService());
        var selectedLoco = new Locomotive("L9", "Vybraná") { DccAddress = 9 };

        vm.SetDccTestLocomotiveProvider(() => selectedLoco);

        Assert.Same(selectedLoco, vm.SelectedLocomotiveForDccTest);
    }

    [Fact]
    public void SettingsViewModel_OpenDccCentralTabForLocomotive_VyberieDccTabAPrenesieAdresu()
    {
        var vm = new SettingsViewModel(new SettingsManager(), new FakeDccCommunicationTestService(), new FakeDccConnectionService());
        var selectedLoco = new LocoRecord { Address = 88 };

        vm.OpenDccCentralTabForLocomotive(selectedLoco);

        Assert.Equal(1, vm.SelectedSettingsTabIndex);
        Assert.Same(selectedLoco, vm.SelectedLocomotiveForDccTest);
        Assert.Equal(88, vm.TestHandler.TestLocoAddress);
    }

    [Fact]
    public void SettingsViewModel_ClearDccTestLocomotiveOverride_VratiProvider()
    {
        var vm = new SettingsViewModel(new SettingsManager(), new FakeDccCommunicationTestService(), new FakeDccConnectionService());
        var providerLoco = new Locomotive("P", "Provider") { DccAddress = 12 };
        var overrideLoco = new LocoRecord { Address = 88 };
        vm.SetDccTestLocomotiveProvider(() => providerLoco);

        vm.OpenDccCentralTabForLocomotive(overrideLoco);
        vm.ClearDccTestLocomotiveOverride();

        Assert.Same(providerLoco, vm.SelectedLocomotiveForDccTest);
    }

    [Fact]
    public void SettingsViewModel_DruheOtvorenieBezPripojenia_PomHintZostaneSpravny()
    {
        var manager = new SettingsManager();
        var connection = new FakeDccConnectionService(false);

        using var firstOpen = new SettingsViewModel(manager, new FakeDccCommunicationTestService(), connection);
        firstOpen.TestHandler.IsServiceTrackProgrammingMode = false;
        Assert.Equal("Režim POM nepodporuje čítanie CV.", firstOpen.TestHandler.DisabledConnectionHint);

        using var secondOpen = new SettingsViewModel(manager, new FakeDccCommunicationTestService(), connection);
        Assert.Equal("DCC centrála musí byť pripojená.", secondOpen.TestHandler.DisabledConnectionHint);

        secondOpen.TestHandler.IsServiceTrackProgrammingMode = false;

        Assert.Equal("Režim POM nepodporuje čítanie CV.", secondOpen.TestHandler.DisabledConnectionHint);
        Assert.True(secondOpen.TestHandler.HasDisabledTestHint);
    }

    private sealed class FakeDccCommunicationTestService : IDccCommunicationTestService
    {
        public Func<DccCommunicationTestRequest, CancellationToken, Task<string>>? OnTestAsync { get; init; }

        public DccCommunicationTestRequest? LastRequest { get; private set; }

        public Task<string> TestReadCv1Async(DccCommunicationTestRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            if (OnTestAsync is not null)
            {
                return OnTestAsync(request, ct);
            }

            return Task.FromResult("OK");
        }
    }

    private sealed class FakeDccConnectionService : IDccConnectionService
    {
        public FakeDccConnectionService(bool isConnected = true, IDccCentralClient? client = null)
        {
            IsConnected = isConnected;
            Client = client ?? new FakeDccCentralClient();
        }

        public bool IsConnected { get; private set; }

        public IDccCentralClient Client { get; }

        public event Action<bool>? IsConnectedChanged;

        public void SetConnected(bool isConnected)
        {
            IsConnected = isConnected;
            IsConnectedChanged?.Invoke(isConnected);
        }
    }

    private sealed class FakeDccCentralClient : IDccCentralClient, IDccProgrammingClient
    {
        public bool IsConnected => true;
        public uint? SerialNumber => 1234;
        public Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default) => Task.FromResult(true);
        public void Disconnect() { }
        public Task SetLocomotiveSpeedAsync(int address, int speed, bool forward, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetLocomotiveFunctionAsync(int address, int functionIndex, bool active, CancellationToken ct = default) => Task.CompletedTask;
        public Task EmergencyStopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task TrackPowerOnAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetTurnoutAsync(int address, bool branch, bool activate, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> ReadCvAsync(int cvAddress, DccProgrammingTestMode programmingMode, int timeoutMs, int locoAddress, CancellationToken ct = default)
            => Task.FromResult(4);
        public Task WriteCvAsync(int cvAddress, int value, DccProgrammingTestMode programmingMode, int timeoutMs, int locoAddress, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeNonProgrammingDccCentralClient : IDccCentralClient
    {
        public bool IsConnected => true;
        public uint? SerialNumber => null;
        public Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default) => Task.FromResult(true);
        public void Disconnect() { }
        public Task SetLocomotiveSpeedAsync(int address, int speed, bool forward, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetLocomotiveFunctionAsync(int address, int functionIndex, bool active, CancellationToken ct = default) => Task.CompletedTask;
        public Task EmergencyStopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task TrackPowerOnAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetTurnoutAsync(int address, bool branch, bool activate, CancellationToken ct = default) => Task.CompletedTask;
    }
}


using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.Services.Dcc;
using TrackFlow.ViewModels.Library;
using Xunit;

namespace TrackFlow.Tests;

public sealed class LocomotiveAddressProgrammingTests
{
    [Fact]
    public async Task ReadAddressCommand_PreKratkuAdresuNacitaCv29ACv1DoEditora()
    {
        var (vm, client, _) = CreateViewModel();
        client.CvValues[29] = 0x06;
        client.CvValues[1] = 55;

        await vm.ReadAddressCommand.ExecuteAsync(null);

        Assert.Equal(new[] { 29, 1 }, client.ReadRequests);
        Assert.Equal(new[] { 0, 0 }, client.ReadRequestAddresses);
        Assert.Equal(55, vm.SelectedLocomotive?.DccAddress);
        Assert.Equal("55", vm.AddressText);
        Assert.Equal(0x06, vm.SelectedLocomotive?.Cv29Value);
    }

    [Fact]
    public async Task ReadAddressCommand_PreDlhuAdresuNacitaCv29Cv17Cv18ADekodujeNmraAdresu()
    {
        var (vm, client, _) = CreateViewModel();
        var (cv17, cv18) = DccAddressCodec.EncodeLongAddress(1234);
        client.CvValues[29] = DccAddressCodec.SetLongAddressFlag(0x06, useLongAddress: true);
        client.CvValues[17] = cv17;
        client.CvValues[18] = cv18;

        await vm.ReadAddressCommand.ExecuteAsync(null);

        Assert.Equal(new[] { 29, 17, 18 }, client.ReadRequests);
        Assert.Equal(new[] { 0, 0, 0 }, client.ReadRequestAddresses);
        Assert.Equal(1234, vm.SelectedLocomotive?.DccAddress);
        Assert.Equal("1234", vm.AddressText);
    }

    [Fact]
    public async Task WriteAddressCommand_PreKratkuAdresuZapiseCv1AResetujeBit5VCv29()
    {
        var (vm, client, _) = CreateViewModel();
        client.CvValues[29] = 0x26;
        vm.AddressText = "87";

        await vm.WriteAddressCommand.ExecuteAsync(null);

        Assert.Equal(new[] { 29 }, client.ReadRequests);
        Assert.Equal(new[] { 0 }, client.ReadRequestAddresses);
        Assert.Equal((1, 87), client.WriteRequests[0]);
        Assert.Equal((29, 0x06), client.WriteRequests[1]);
        Assert.Equal(new[] { 0, 0 }, client.WriteRequestAddresses);
        Assert.Equal(87, vm.SelectedLocomotive?.DccAddress);
        Assert.Equal(0x06, vm.SelectedLocomotive?.Cv29Value);
    }

    [Fact]
    public async Task WriteAddressCommand_PreDlhuAdresuZapiseCv17Cv18AZapneBit5VCv29()
    {
        var (vm, client, _) = CreateViewModel();
        client.CvValues[29] = 0x06;
        vm.AddressText = "1234";

        await vm.WriteAddressCommand.ExecuteAsync(null);

        var (cv17, cv18) = DccAddressCodec.EncodeLongAddress(1234);
        Assert.Equal(new[] { 29 }, client.ReadRequests);
        Assert.Equal(new[] { 0 }, client.ReadRequestAddresses);
        Assert.Equal((17, cv17), client.WriteRequests[0]);
        Assert.Equal((18, cv18), client.WriteRequests[1]);
        Assert.Equal((29, 0x26), client.WriteRequests[2]);
        Assert.Equal(new[] { 0, 0, 0 }, client.WriteRequestAddresses);
        Assert.Equal(1234, vm.SelectedLocomotive?.DccAddress);
        Assert.Equal(0x26, vm.SelectedLocomotive?.Cv29Value);
    }

    [Fact]
    public void AddressProgrammingButtonsSuPovoleneLenPriPripojenejProgramovacejCentralne()
    {
        var disconnectedConnection = new FakeDccConnectionService(isConnected: false, client: new FakeProgrammingClient());
        var disconnectedVm = CreateViewModel(disconnectedConnection).ViewModel;
        Assert.False(disconnectedVm.CanReadDecoderAddress);
        Assert.False(disconnectedVm.CanWriteDecoderAddress);

        var unsupportedConnection = new FakeDccConnectionService(isConnected: true, client: new FakeNonProgrammingClient());
        var unsupportedVm = CreateViewModel(unsupportedConnection).ViewModel;
        Assert.False(unsupportedVm.CanReadDecoderAddress);
        Assert.False(unsupportedVm.CanWriteDecoderAddress);

        var supportedConnection = new FakeDccConnectionService(isConnected: true, client: new FakeProgrammingClient());
        var supportedVm = CreateViewModel(supportedConnection).ViewModel;
        Assert.True(supportedVm.CanReadDecoderAddress);
        Assert.True(supportedVm.CanWriteDecoderAddress);
    }

    [Fact]
    public void GlobalneDccTlacidlaSuPovoleneLenPriZaskrtnutomDccAPripojenejCentralne()
    {
        var disconnectedConnection = new FakeDccConnectionService(isConnected: false, client: new FakeProgrammingClient());
        var disconnectedVm = CreateViewModel(disconnectedConnection).ViewModel;
        Assert.False(disconnectedVm.IsGlobalDccProgrammingAvailable);

        var connectedConnection = new FakeDccConnectionService(isConnected: true, client: new FakeProgrammingClient());
        var connectedVm = CreateViewModel(connectedConnection).ViewModel;
        Assert.True(connectedVm.IsGlobalDccProgrammingAvailable);

        connectedVm.IsDccProgrammingEnabled = false;
        Assert.False(connectedVm.IsGlobalDccProgrammingAvailable);

        connectedVm.IsDccProgrammingEnabled = true;
        Assert.True(connectedVm.IsGlobalDccProgrammingAvailable);

        connectedConnection.SetConnected(false);
        Assert.False(connectedVm.IsGlobalDccProgrammingAvailable);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(1234)]
    [InlineData(9999)]
    public void DccAddressCodec_DlhaAdresaSaZakodujeAdekodujeBezStraty(int address)
    {
        var (cv17, cv18) = DccAddressCodec.EncodeLongAddress(address);
        var decoded = DccAddressCodec.DecodeLongAddress(cv17, cv18);

        Assert.Equal(address, decoded);
        Assert.True((cv17 & 0xC0) == 0xC0);
    }

    private static (LocomotivesWindowViewModel ViewModel, FakeProgrammingClient Client, FakeDccConnectionService Connection) CreateViewModel()
    {
        var connection = new FakeDccConnectionService(isConnected: true, client: new FakeProgrammingClient());
        var result = CreateViewModel(connection);
        return (result.ViewModel, (FakeProgrammingClient)connection.Client, connection);
    }

    private static (LocomotivesWindowViewModel ViewModel, FakeDccConnectionService Connection) CreateViewModel(FakeDccConnectionService connection)
    {
        var settings = new SettingsManager();
        settings.ProjectLocomotives.Add(new LocoRecord
        {
            Name = "Test loco",
            Address = 3,
            Cv29Value = 0x06,
            IsDccProgrammingEnabled = true
        });

        var vm = new LocomotivesWindowViewModel(settings, dccConnectionService: connection);
        return (vm, connection);
    }

    private sealed class FakeDccConnectionService : IDccConnectionService
    {
        public FakeDccConnectionService(bool isConnected, IDccCentralClient client)
        {
            IsConnected = isConnected;
            Client = client;
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

    private sealed class FakeProgrammingClient : IDccCentralClient, IDccProgrammingClient
    {
        public Dictionary<int, int> CvValues { get; } = new();
        public List<int> ReadRequests { get; } = new();
        public List<int> ReadRequestAddresses { get; } = new();
        public List<(int Cv, int Value)> WriteRequests { get; } = new();
        public List<int> WriteRequestAddresses { get; } = new();

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
        {
            ReadRequests.Add(cvAddress);
            ReadRequestAddresses.Add(locoAddress);
            return Task.FromResult(CvValues.TryGetValue(cvAddress, out var value) ? value : 0);
        }

        public Task WriteCvAsync(int cvAddress, int value, DccProgrammingTestMode programmingMode, int timeoutMs, int locoAddress, CancellationToken ct = default)
        {
            WriteRequests.Add((cvAddress, value));
            WriteRequestAddresses.Add(locoAddress);
            CvValues[cvAddress] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNonProgrammingClient : IDccCentralClient
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



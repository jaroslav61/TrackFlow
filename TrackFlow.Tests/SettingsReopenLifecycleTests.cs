using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.Services.Dcc;
using TrackFlow.ViewModels.Settings;
using Xunit;

namespace TrackFlow.Tests;

public sealed class SettingsReopenLifecycleTests
{
    [Fact]
    public void Load_DetachesTelemetryFromPreviousConfiguredCentralItems()
    {
        var manager = new SettingsManager();
        manager.LoadApp();

        var telemetry = new FakeTelemetrySource();
        var connection = new FakeDccConnectionServiceWithTelemetry(telemetry);
        var vm = new SettingsViewModel(manager, dccConnectionService: connection);

        var profile = new DccCentralProfile
        {
            Id = Guid.NewGuid(),
            Type = DccCentralType.Z21,
            Host = "192.168.0.111",
            Port = 21105,
            IsEnabled = true
        };

        manager.App.DccCentralProfiles.Clear();
        manager.App.DccCentralProfiles.Add(profile);
        manager.App.SelectedDccCentralProfileId = profile.Id;

        vm.Load();

        Assert.Single(vm.ConfiguredCentrals);
        var oldItem = vm.ConfiguredCentrals[0];
        Assert.NotNull(oldItem);
        Assert.Equal(1, telemetry.HandlerCount);

        vm.Load();

        Assert.Equal(1, telemetry.HandlerCount);

        telemetry.RaiseMainVoltageChanged();

        Assert.Single(vm.ConfiguredCentrals);
        Assert.NotSame(oldItem, vm.ConfiguredCentrals[0]);
    }

    private sealed class FakeTelemetrySource : IDccTelemetry
    {
        private PropertyChangedEventHandler? _propertyChanged;

        public int HandlerCount => _propertyChanged?.GetInvocationList().Length ?? 0;

        public bool IsTelemetrySupported => true;
        public bool IsBlackZ21 => false;
        public double? MainVoltage => 15.5;
        public double? ProgVoltage => 15.2;
        public double? TrackCurrent => 0.7;
        public double? ProgTrackCurrent => 0.1;
        public double? CentralTemperature => 35;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => _propertyChanged += value;
            remove => _propertyChanged -= value;
        }

        public void RaiseMainVoltageChanged()
            => _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IDccTelemetry.MainVoltage)));
    }

    private sealed class FakeDccConnectionServiceWithTelemetry : IDccConnectionService
    {
        private readonly IDccTelemetry _telemetry;

        public FakeDccConnectionServiceWithTelemetry(IDccTelemetry telemetry)
        {
            _telemetry = telemetry;
            Client = new FakeTelemetryClient(telemetry);
        }

        public bool IsConnected => true;
        public IDccCentralClient Client { get; }
        public event Action<bool>? IsConnectedChanged
        {
            add { }
            remove { }
        }

        public Task<(bool Ok, DccCentralType Type, uint? Serial)> ConnectAsync(CancellationToken ct = default)
            => Task.FromResult((true, DccCentralType.Z21, (uint?)1234));

        public void Disconnect(string reason = "user") { }

        private sealed class FakeTelemetryClient : IDccCentralClient, IDccTelemetry
        {
            private readonly IDccTelemetry _inner;

            public FakeTelemetryClient(IDccTelemetry inner) => _inner = inner;

            public bool IsConnected => true;
            public uint? SerialNumber => 1234;
            public bool IsTelemetrySupported => _inner.IsTelemetrySupported;
            public bool IsBlackZ21 => _inner.IsBlackZ21;
            public double? MainVoltage => _inner.MainVoltage;
            public double? ProgVoltage => _inner.ProgVoltage;
            public double? TrackCurrent => _inner.TrackCurrent;
            public double? ProgTrackCurrent => _inner.ProgTrackCurrent;
            public double? CentralTemperature => _inner.CentralTemperature;
            public event PropertyChangedEventHandler? PropertyChanged
            {
                add => _inner.PropertyChanged += value;
                remove => _inner.PropertyChanged -= value;
            }

            public Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default) => Task.FromResult(true);
            public void Disconnect() { }
            public Task SetLocomotiveSpeedAsync(int address, int speed, bool forward, CancellationToken ct = default) => Task.CompletedTask;
            public Task SetLocomotiveFunctionAsync(int address, int functionIndex, bool active, CancellationToken ct = default) => Task.CompletedTask;
            public Task EmergencyStopAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task TrackPowerOnAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task SetTurnoutAsync(int address, bool branch, bool activate, CancellationToken ct = default) => Task.CompletedTask;
        }
    }
}


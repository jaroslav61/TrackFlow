using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.Services.Dcc;
using Xunit;

namespace TrackFlow.Tests;

public class DccConnectionServiceTests
{
    [Fact]
    public async Task ConnectAsync_WhenFailedAndAutoConnectEnabled_RaisesOnlyConnectFailed_OnInitialFailure()
    {
        var settings = CreateSettings(autoConnect: true);
        var fake = new FakeKeepAliveClient();
        fake.EnqueueConnectResult(false);

        var service = new DccConnectionService(
            settings,
            _ => fake,
            new DccMonitorOptions(1000, 2, 1000, 1, 1000, 2000),
            clientMatchesOverride: (_, _) => true);

        var events = new List<DccConnectionStateChange>();
        var eventsLock = new object();
        service.ConnectionStateChanged += e =>
        {
            lock (eventsLock)
                events.Add(e);
        };

        var (ok, type, _) = await service.ConnectAsync();

        Assert.False(ok);
        Assert.Equal(DccCentralType.Z21, type);
        Assert.Contains(events, e => e.Kind == DccConnectionChangeKind.ConnectFailed && e.Reason == "timeout/no-response");
        Assert.DoesNotContain(events, e => e.Kind == DccConnectionChangeKind.Disconnected);
        Assert.DoesNotContain(events, e => e.Kind == DccConnectionChangeKind.Reconnecting);

        service.Disconnect("test-end");
    }

    [Fact]
    public async Task MonitorLoop_OnPingFailures_Raises_Disconnected_Then_Reconnecting_Then_Connected()
    {
        var settings = CreateSettings(autoConnect: true);
        var fake = new FakeKeepAliveClient();

        // Initial connect succeeds.
        fake.EnqueueConnectResult(true);

        // Keepalive fails twice => connection lost.
        fake.EnqueuePingResult(false);
        fake.EnqueuePingResult(false);

        // Auto-reconnect first attempt succeeds.
        fake.EnqueueConnectResult(true);

        var service = new DccConnectionService(
            settings,
            _ => fake,
            new DccMonitorOptions(20, 2, 20, 1, 20, 400),
            clientMatchesOverride: (_, _) => true);

        var events = new List<DccConnectionStateChange>();
        var connectedAfterReconnect = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        service.ConnectionStateChanged += e =>
        {
            events.Add(e);
            if (e.Kind == DccConnectionChangeKind.Connected && e.Reason == "auto-reconnect")
                connectedAfterReconnect.TrySetResult(true);
        };

        var (ok, _, _) = await service.ConnectAsync();
        Assert.True(ok);

        var completed = await Task.WhenAny(connectedAfterReconnect.Task, Task.Delay(2000));
        Assert.True(connectedAfterReconnect.Task.IsCompleted, "Did not observe Connected(auto-reconnect) within timeout.");

        var disconnectedIdx = events.FindIndex(e => e.Kind == DccConnectionChangeKind.Disconnected && e.Reason == "connection-lost");
        var reconnectingIdx = events.FindIndex(e => e.Kind == DccConnectionChangeKind.Reconnecting && e.Reason == "auto-reconnect");
        var connectedIdx = events.FindIndex(e => e.Kind == DccConnectionChangeKind.Connected && e.Reason == "auto-reconnect");

        Assert.True(disconnectedIdx >= 0, "Disconnected(connection-lost) event missing.");
        Assert.True(reconnectingIdx > disconnectedIdx, "Reconnecting event should come after Disconnected(connection-lost).");
        Assert.True(connectedIdx > reconnectingIdx, "Connected(auto-reconnect) event should come after Reconnecting.");

        service.Disconnect("test-end");
    }

    [Fact]
    public async Task MonitorLoop_WhenReconnectWindowExpires_Raises_AutoReconnectTimeout()
    {
        var settings = CreateSettings(autoConnect: true);
        var fake = new FakeKeepAliveClient();

        // Initial connect succeeds, then keepalive loss starts the reconnect window.
        fake.EnqueueConnectResult(true);
        fake.EnqueuePingResult(false);
        fake.EnqueuePingResult(false);

        // Then keep auto-reconnect attempts failing.
        fake.DefaultConnectResult = false;

        var service = new DccConnectionService(
            settings,
            _ => fake,
            new DccMonitorOptions(10, 2, 10, 1, 10, 80),
            clientMatchesOverride: (_, _) => true);

        var timeoutSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        service.ConnectionStateChanged += e =>
        {
            if (e.Kind == DccConnectionChangeKind.ConnectFailed && e.Reason == "auto-reconnect-timeout")
                timeoutSeen.TrySetResult(true);
        };

        var (ok, _, _) = await service.ConnectAsync();
        Assert.True(ok);

        var completed = await Task.WhenAny(timeoutSeen.Task, Task.Delay(3000));
        Assert.True(timeoutSeen.Task.IsCompleted, "Did not observe auto-reconnect-timeout event within timeout.");

        service.Disconnect("test-end");
    }

    [Fact]
    public async Task ConnectAsync_ForNanoXWithoutKeepAlive_DoesNotRaiseReconnecting()
    {
        var settings = new SettingsManager();
        settings.App.DefaultDccCentralType = DccCentralType.NanoX_S88;
        settings.App.DefaultDccSerialPort = "COM7";
        settings.App.DefaultDccBaudRate = 19200;
        settings.App.DefaultAutoConnect = true;

        var fake = new FakeSerialClient { DefaultConnectResult = false };

        var service = new DccConnectionService(
            settings,
            _ => fake,
            new DccMonitorOptions(20, 2, 20, 1, 20, 100),
            clientMatchesOverride: (_, _) => true);

        var events = new List<DccConnectionStateChange>();
        var eventsLock = new object();
        service.ConnectionStateChanged += e =>
        {
            lock (eventsLock)
                events.Add(e);
        };

        var (ok, type, _) = await service.ConnectAsync();

        Assert.False(ok);
        Assert.Equal(DccCentralType.NanoX_S88, type);
        Assert.Contains(events, e => e.Kind == DccConnectionChangeKind.ConnectFailed && e.Reason == "timeout/no-response");
        Assert.DoesNotContain(events, e => e.Kind == DccConnectionChangeKind.Reconnecting);
        Assert.Equal("COM7", fake.LastHost);
        Assert.Equal(19200, fake.LastPort);
    }

    [Fact]
    public async Task ConnectAsync_ForNanoX_PassesSerialPortAndBaudRateToClient()
    {
        var settings = new SettingsManager();
        settings.App.DefaultDccCentralType = DccCentralType.NanoX_S88;
        settings.App.DefaultDccSerialPort = "COM5";
        settings.App.DefaultDccBaudRate = 38400;

        var fake = new FakeSerialClient { DefaultConnectResult = true };
        var service = new DccConnectionService(
            settings,
            _ => fake,
            clientMatchesOverride: (_, _) => true);

        var (ok, type, _) = await service.ConnectAsync();

        Assert.True(ok);
        Assert.Equal(DccCentralType.NanoX_S88, type);
        Assert.Equal("COM5", fake.LastHost);
        Assert.Equal(38400, fake.LastPort);
    }

    [Fact]
    public async Task Disconnect_SingleCentral_RaisesDisconnectedEventWithSelectedProfileId()
    {
        var settings = CreateSettings(autoConnect: false);
        var selectedProfileId = Guid.NewGuid();
        settings.App.DccCentralProfiles.Add(new DccCentralProfile
        {
            Id = selectedProfileId,
            Type = DccCentralType.Z21,
            Host = "127.0.0.1",
            Port = 21105,
            IsEnabled = true,
            AutoConnect = false
        });
        settings.App.SelectedDccCentralProfileId = selectedProfileId;

        var fake = new FakeKeepAliveClient();
        fake.EnqueueConnectResult(true);

        var service = new DccConnectionService(
            settings,
            _ => fake,
            clientMatchesOverride: (_, _) => true);

        var events = new List<DccConnectionStateChange>();
        service.ConnectionStateChanged += e => events.Add(e);

        var (ok, _, _) = await service.ConnectAsync();
        Assert.True(ok);

        service.Disconnect("user");

        Assert.Contains(events, e => e.Kind == DccConnectionChangeKind.Connected && e.ProfileId == selectedProfileId);
        Assert.Contains(events, e => e.Kind == DccConnectionChangeKind.Disconnected && e.Reason == "user" && e.ProfileId == selectedProfileId);
    }

    [Fact]
    public async Task ConnectMissingAsync_WhenProfileAutoConnectIsFalse_DoesNotStartReconnectLoop()
    {
        var settings = new SettingsManager();

        var profile = new DccCentralProfile
        {
            Id = Guid.NewGuid(),
            Type = DccCentralType.Z21,
            Host = "127.0.0.1",
            Port = 21105,
            AutoConnect = false
        };

        var fake = new FakeKeepAliveClient();
        fake.EnqueueConnectResult(false);

        var service = new DccConnectionService(
            settings,
            _ => fake,
            new DccMonitorOptions(20, 2, 20, 1, 20, 150),
            clientMatchesOverride: (_, _) => true);

        var events = new List<DccConnectionStateChange>();
        var eventsLock = new object();
        service.ConnectionStateChanged += e =>
        {
            lock (eventsLock)
                events.Add(e);
        };

        await service.ConnectMissingAsync(new[] { profile });
        await Task.Delay(180);

        Assert.Contains(events, e => e.ProfileId == profile.Id && e.Kind == DccConnectionChangeKind.ConnectFailed);
        Assert.DoesNotContain(events, e => e.ProfileId == profile.Id && e.Kind == DccConnectionChangeKind.Reconnecting);
        Assert.DoesNotContain(service.ReconnectingProfileIds, id => id == profile.Id);

        service.DisconnectAll("test-end");
    }

    [Fact]
    public async Task ConnectMissingAsync_AfterConnectionLoss_StartsReconnectOnlyForProfilesWithAutoConnectEnabled()
    {
        var settings = new SettingsManager();

        var noAutoProfile = new DccCentralProfile
        {
            Id = Guid.NewGuid(),
            Type = DccCentralType.Z21,
            Host = "127.0.0.1",
            Port = 21105,
            AutoConnect = false
        };

        var autoProfile = new DccCentralProfile
        {
            Id = Guid.NewGuid(),
            Type = DccCentralType.GenericIpUdp,
            Host = "127.0.0.2",
            Port = 21106,
            AutoConnect = true
        };

        var noAutoClient = new FakeKeepAliveClient();
        noAutoClient.EnqueueConnectResult(true);
        noAutoClient.EnqueuePingResult(false);
        noAutoClient.EnqueuePingResult(false);

        var autoClient = new FakeKeepAliveClient();
        autoClient.EnqueueConnectResult(true);
        autoClient.EnqueuePingResult(false);
        autoClient.EnqueuePingResult(false);
        autoClient.DefaultConnectResult = false;

        var service = new DccConnectionService(
            settings,
            type => type == DccCentralType.GenericIpUdp ? autoClient : noAutoClient,
            new DccMonitorOptions(20, 2, 20, 1, 20, 150),
            clientMatchesOverride: (_, _) => true);

        var events = new List<DccConnectionStateChange>();
        var eventsLock = new object();
        service.ConnectionStateChanged += e =>
        {
            lock (eventsLock)
                events.Add(e);
        };

        await service.ConnectMissingAsync(new[] { noAutoProfile, autoProfile });

        var deadline = DateTime.UtcNow.AddSeconds(2);
        bool HasAutoReconnectEvent()
        {
            lock (eventsLock)
                return events.Any(e => e.ProfileId == autoProfile.Id && e.Kind == DccConnectionChangeKind.Reconnecting);
        }

        DccConnectionStateChange[] SnapshotEvents()
        {
            lock (eventsLock)
                return events.ToArray();
        }

        while (DateTime.UtcNow < deadline && !HasAutoReconnectEvent())
            await Task.Delay(20);

        var snapshot = SnapshotEvents();

        Assert.Contains(snapshot, e => e.ProfileId == autoProfile.Id && e.Kind == DccConnectionChangeKind.Reconnecting);
        Assert.DoesNotContain(snapshot, e => e.ProfileId == noAutoProfile.Id && e.Kind == DccConnectionChangeKind.Reconnecting);
        Assert.Contains(service.ReconnectingProfileIds, id => id == autoProfile.Id);
        Assert.DoesNotContain(service.ReconnectingProfileIds, id => id == noAutoProfile.Id);

        service.DisconnectAll("test-end");
    }

    private static SettingsManager CreateSettings(bool autoConnect)
    {
        var settings = new SettingsManager();
        settings.App.DefaultDccCentralType = DccCentralType.Z21;
        settings.App.DefaultDccCentralHost = "127.0.0.1";
        settings.App.DefaultDccCentralPort = 21105;
        settings.App.DefaultAutoConnect = autoConnect;
        return settings;
    }

    private static void AssertEventSequenceContainsInOrder(
        IReadOnlyList<DccConnectionStateChange> events,
        params (DccConnectionChangeKind Kind, string Reason)[] expected)
    {
        var idx = 0;
        foreach (var e in events)
        {
            if (idx >= expected.Length)
                break;

            var next = expected[idx];
            if (e.Kind == next.Kind && string.Equals(e.Reason, next.Reason, StringComparison.Ordinal))
                idx++;
        }

        Assert.True(
            idx == expected.Length,
            $"Expected ordered event sequence not found. Expected={string.Join(" -> ", expected.Select(x => $"{x.Kind}:{x.Reason}"))}; Actual={string.Join(" | ", events.Select(x => $"{x.Kind}:{x.Reason}"))}");
    }

    private sealed class FakeKeepAliveClient : IDccCentralClient, IDccKeepAliveClient
    {
        private readonly ConcurrentQueue<bool> _connectResults = new();
        private readonly ConcurrentQueue<bool> _pingResults = new();

        public bool IsConnected { get; private set; }
        public uint? SerialNumber { get; private set; } = 1234;

        public bool DefaultConnectResult { get; set; }
        public bool DefaultPingResult { get; set; } = true;

        public void EnqueueConnectResult(bool result) => _connectResults.Enqueue(result);
        public void EnqueuePingResult(bool result) => _pingResults.Enqueue(result);

        public Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
        {
            bool ok = _connectResults.TryDequeue(out var scripted) ? scripted : DefaultConnectResult;
            IsConnected = ok;
            return Task.FromResult(ok);
        }

        public void Disconnect() => IsConnected = false;

        public Task<bool> PingAsync(CancellationToken ct = default)
        {
            bool ok = _pingResults.TryDequeue(out var scripted) ? scripted : DefaultPingResult;
            return Task.FromResult(ok);
        }

        public Task SetLocomotiveSpeedAsync(int address, int speed, bool forward, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetLocomotiveFunctionAsync(int address, int functionIndex, bool active, CancellationToken ct = default) => Task.CompletedTask;
        public Task EmergencyStopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task TrackPowerOnAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetTurnoutAsync(int address, bool branch, bool activate, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeSerialClient : IDccCentralClient
    {
        public bool IsConnected { get; private set; }
        public uint? SerialNumber => null;
        public bool DefaultConnectResult { get; set; }
        public string? LastHost { get; private set; }
        public int LastPort { get; private set; }

        public Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
        {
            LastHost = host;
            LastPort = port;
            IsConnected = DefaultConnectResult;
            return Task.FromResult(DefaultConnectResult);
        }

        public void Disconnect() => IsConnected = false;
        public Task SetLocomotiveSpeedAsync(int address, int speed, bool forward, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetLocomotiveFunctionAsync(int address, int functionIndex, bool active, CancellationToken ct = default) => Task.CompletedTask;
        public Task EmergencyStopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task TrackPowerOnAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetTurnoutAsync(int address, bool branch, bool activate, CancellationToken ct = default) => Task.CompletedTask;
    }
}




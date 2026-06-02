using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Services.Dcc;

namespace TrackFlow.Tests;

internal sealed class TestDccCentralClient : IDccCentralClient
{
    public bool IsConnected { get; set; }
    public uint? SerialNumber => 1;
    public int EmergencyStopCalls { get; private set; }
    public List<(int Address, bool Branch, bool Activate)> TurnoutCommands { get; } = new();
    public List<(int Address, int AspectNumber)> ExtendedAccessoryCommands { get; } = new();
    public List<(int Address, int Speed, bool Forward)> LocomotiveSpeedCommands { get; } = new();

    public Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default) => Task.FromResult(true);
    public void Disconnect() { }
    public Task SetLocomotiveSpeedAsync(int address, int speed, bool forward, CancellationToken ct = default)
    {
        LocomotiveSpeedCommands.Add((address, speed, forward));
        return Task.CompletedTask;
    }
    public Task SetLocomotiveFunctionAsync(int address, int functionIndex, bool active, CancellationToken ct = default) => Task.CompletedTask;
    public Task EmergencyStopAsync(CancellationToken ct = default)
    {
        EmergencyStopCalls++;
        return Task.CompletedTask;
    }
    public Task TrackPowerOnAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task SetTurnoutAsync(int address, bool branch, bool activate, CancellationToken ct = default)
    {
        TurnoutCommands.Add((address, branch, activate));
        return Task.CompletedTask;
    }

    public Task SetExtendedAccessoryAspectAsync(int address, int aspectNumber, CancellationToken ct = default)
    {
        ExtendedAccessoryCommands.Add((address, aspectNumber));
        return Task.CompletedTask;
    }
}


using System.Threading;
using System.Threading.Tasks;

namespace TrackFlow.Services.Dcc;

/// <summary>
/// Bezpečný placeholder klient pre centrály, ktoré už majú pripravené UI / Settings model,
/// ale ešte nemajú implementovanú skutočnú transportnú vrstvu.
/// </summary>
public sealed class UnsupportedDccCentralClient : IDccCentralClient
{
    public bool IsConnected => false;
    public uint? SerialNumber => null;

    public Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
        => Task.FromResult(false);

    public void Disconnect()
    {
    }

    public Task SetLocomotiveSpeedAsync(int address, int speed, bool forward, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SetLocomotiveFunctionAsync(int address, int functionIndex, bool active, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task EmergencyStopAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public Task TrackPowerOnAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SetTurnoutAsync(int address, bool branch, bool activate, CancellationToken ct = default)
        => Task.CompletedTask;
}

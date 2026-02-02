using System.Threading;
using System.Threading.Tasks;

namespace TrackFlow.Services.Dcc;

public interface IDccCentralClient
{
    bool IsConnected { get; }
    uint? SerialNumber { get; }

    Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default);
    void Disconnect();
}

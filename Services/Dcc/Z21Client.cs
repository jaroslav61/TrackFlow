using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TrackFlow.Services.Dcc;

public sealed class Z21Client : IDccCentralClient, IDccKeepAliveClient
{
    public bool IsConnected { get; private set; }
    public uint? SerialNumber { get; private set; }

    private string? _lastHost;
    private int _lastPort;

    public async Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        _lastHost = host;
        _lastPort = port;

        // Z21 "connect" = overenie odpovede na LAN_GET_SERIAL_NUMBER (UDP).
        // UDP je bezstavové; stav držíme logicky.
        var serial = await TryGetSerialAsync(host, port, ct);

        if (!serial.HasValue)
        {
            IsConnected = false;
            SerialNumber = null;
            return false;
        }

        SerialNumber = serial.Value;
        IsConnected = true;
        return true;
    }

    public void Disconnect()
    {
        // UDP je bezstavové – odpojenie je zatiaľ len logické.
        IsConnected = false;
        SerialNumber = null;
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        // Reálny ping pre Z21: pokus o získanie serial number.
        if (string.IsNullOrWhiteSpace(_lastHost) || _lastPort <= 0)
            return false;

        var serial = await TryGetSerialAsync(_lastHost, _lastPort, ct);
        if (!serial.HasValue)
            return false;

        SerialNumber = serial.Value;
        return true;
    }

    private static async Task<uint?> TryGetSerialAsync(string host, int port, CancellationToken ct)
    {
        // Z21 LAN protocol: LAN_GET_SERIAL_NUMBER request = 04 00 10 00
        // Response: 08 00 10 00 + 4B serial (LE)
        var payload = new byte[] { 0x04, 0x00, 0x10, 0x00 };

        var addresses = await Dns.GetHostAddressesAsync(host);
        if (addresses.Length == 0)
            return null;

        // Preferuj IPv4 (ak existuje), inak vezmi prvú adresu.
        var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];

        using var udp = new UdpClient();
        udp.Connect(ip, port);
        await udp.SendAsync(payload, payload.Length);

        var recvTask = udp.ReceiveAsync();
        var completed = await Task.WhenAny(recvTask, Task.Delay(800, ct));
        if (completed != recvTask)
            return null;

        var data = recvTask.Result.Buffer;
        if (data.Length < 8)
            return null;

        if (data[0] != 0x08 || data[1] != 0x00 || data[2] != 0x10 || data[3] != 0x00)
            return null;

        uint serial = (uint)(data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24));
        return serial;
    }
}

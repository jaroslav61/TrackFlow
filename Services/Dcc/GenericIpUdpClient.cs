﻿using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TrackFlow.Services.Dcc;

public sealed class GenericIpUdpClient : IDccCentralClient, IDccTelemetry, IDisposable
{
    private UdpClient? _udp;

    // ── IDccTelemetry: generický UDP klient nemá definovaný telemetrický protokol ──
    public bool IsTelemetrySupported => false;
    public bool IsBlackZ21 => false;
    public double? MainVoltage => null;
    public double? ProgVoltage => null;
    public double? TrackCurrent => null;
    public double? ProgTrackCurrent => null;
    public double? CentralTemperature => null;
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { /* no-op: generic UDP telemetry not implemented */ }
        remove { /* no-op */ }
    }

    public bool IsConnected { get; private set; }

    // Zadanie: zatiaľ len základ, serial = null
    public uint? SerialNumber => null;

    public async Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host nesmie byť prázdny.", nameof(host));

        if (port < 1 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port musí byť v rozsahu 1..65535.");

        Disconnect();

        var addrs = await Dns.GetHostAddressesAsync(host, ct);
        if (addrs.Length == 0)
            return false;

        // preferuj IPv4, ak je dostupné (kvôli kompatibilite a jednoduchosti)
        var addr = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs[0];

        _udp = new UdpClient();
        _udp.Connect(addrs[0], port);
        _udp.Connect(addr, port);

        // UDP je bezstavové – zatiaľ len logický stav bez handshake
        IsConnected = true;
        return true;
    }

    public void Disconnect()
    {
        IsConnected = false;

        try { _udp?.Dispose(); }
        catch { /* ignore */ }
        finally { _udp = null; }
    }

    // Generický klient zatiaľ nemá definovaný protokol – metódy sú prázdne stubs.
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

    public void Dispose() => Disconnect();
}

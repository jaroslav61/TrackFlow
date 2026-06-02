﻿using System.Threading;
using System.Threading.Tasks;

namespace TrackFlow.Services.Dcc;

public interface IDccCentralClient
{
    bool IsConnected { get; }
    uint? SerialNumber { get; }

    Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default);
    void Disconnect();

    /// <summary>Nastaví rýchlosť a smer lokomotívy. speed: 0..126, forward: true=vpred.</summary>
    Task SetLocomotiveSpeedAsync(int address, int speed, bool forward, CancellationToken ct = default);

    /// <summary>Nastaví stav funkcie lokomotívy. functionIndex: 0=F0, 1=F1 … 28=F28.</summary>
    Task SetLocomotiveFunctionAsync(int address, int functionIndex, bool active, CancellationToken ct = default);

    /// <summary>Núdzové zastavenie všetkých lokomotív.</summary>
    Task EmergencyStopAsync(CancellationToken ct = default);

    /// <summary>Zapne napájanie koľajiska (po E-Stop ho obnoví).</summary>
    Task TrackPowerOnAsync(CancellationToken ct = default);

    /// <summary>
    /// Ovláda výhybku / accessory decoder.
    /// address: 1..2048 (DCC accessory adresa)
    /// branch: false = výstup 0 / priamo, true = výstup 1 / odbočka
    /// activate: true = energizovať zvolený výstup, false = de-energizovať ho
    /// </summary>
    Task SetTurnoutAsync(int address, bool branch, bool activate, CancellationToken ct = default);

    /// <summary>
    /// Ovláda accessory v rozšírenom režime (1 adresa + číslo aspektu).
    /// Predvolené správanie fallbackne na SetTurnoutAsync pre klientov bez natívnej podpory.
    /// </summary>
    Task SetExtendedAccessoryAspectAsync(int address, int aspectNumber, CancellationToken ct = default)
        => SetTurnoutAsync(address, aspectNumber > 0, activate: true, ct);
}

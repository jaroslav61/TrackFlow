using System;

namespace TrackFlow.Models;

/// <summary>
/// Jedna uložená konfigurácia DCC centrály (typ + adresa/port alebo COM).
/// </summary>
public sealed class DccCentralProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsEnabled { get; set; } = true;
    public DccCentralType Type { get; set; } = DccCentralType.Z21Legacy;
    public string Host { get; set; } = "192.168.0.111";
    public int Port { get; set; } = 21105;
    public string SerialPort { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 19200;
    /// <summary>
    /// Povoliť automatické pripájanie / auto-reconnect pre tento KONKRÉTNY profil centrály.
    /// V multi-central režime má prednosť pred legacy globálnym DefaultAutoConnect.
    /// </summary>
    public bool AutoConnect { get; set; } = false;

    /// <summary>
    /// Voliteľný override maximálneho odberu hlavnej koľaje [A].
    /// Null = použiť predvolený limit podľa detegovaného hardvéru centrály.
    /// </summary>
    public double? MainTrackCurrentLimitAmperes { get; set; }

    public StartupFunctionBehavior StartupBehavior { get; set; } = StartupFunctionBehavior.SendAllFunctions;
}


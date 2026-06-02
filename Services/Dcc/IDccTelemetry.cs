using System.ComponentModel;

namespace TrackFlow.Services.Dcc;

/// <summary>
/// Rozhranie pre DCC centrály, ktoré dokážu poskytnúť telemetrické údaje
/// (napätie, prúd, teplota) o stave systému.
/// Implementácie musia oznamovať zmeny hodnôt cez <see cref="INotifyPropertyChanged"/>.
/// </summary>
public interface IDccTelemetry : INotifyPropertyChanged
{
    /// <summary>True, ak centrála vôbec podporuje telemetriu (napr. z21). False pre XpressNet NanoX.</summary>
    bool IsTelemetrySupported { get; }

    /// <summary>
    /// True, ak ide o plnohodnotnú „veľkú“ čiernu Z21/Z21 rodinu.
    /// False pre z21 start / malé varianty a pre klientov bez z21 hardvéru.
    /// </summary>
    bool IsBlackZ21 { get; }

    /// <summary>Hlavné napätie v koľajach [V]. Null kým prvý paket nedorazí, alebo ak nepodporované.</summary>
    double? MainVoltage { get; }

    /// <summary>Napätie na programovacej koľaji [V]. Null kým prvý paket nedorazí, alebo ak nepodporované.</summary>
    double? ProgVoltage { get; }

    /// <summary>Odber prúdu z hlavnej koľaje [A].</summary>
    double? TrackCurrent { get; }

    /// <summary>Odber prúdu programovacej koľaje [A].</summary>
    double? ProgTrackCurrent { get; }

    /// <summary>Teplota centrály [°C].</summary>
    double? CentralTemperature { get; }
}


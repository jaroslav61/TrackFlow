namespace TrackFlow.Services.Dcc;

/// <summary>
/// Voliteľné runtime-preferences pre DCC klientov, ktoré vedia znížiť alebo vypnúť
/// telemetrický polling podľa globálnych nastavení UI.
/// </summary>
public interface ITelemetryPreferenceAwareClient
{
    /// <summary>
    /// True = telemetria je povolená (polling môže bežať).
    /// False = klient má sieťový polling pozastaviť, ale môže ostať pripojený.
    /// </summary>
    bool IsTelemetryEnabled { get; set; }
}


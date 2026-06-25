using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TrackFlow.Services.Dcc;

public interface IDccProgrammingClient
{
    /// <summary>
    ///     Prečíta hodnotu CV. Pre <see cref="DccProgrammingTestMode.ServiceTrack" /> sa
    ///     <paramref name="locoAddress" /> ignoruje (volajúci má odovzdať explicitný placeholder,
    ///     napr. 0); pre <see cref="DccProgrammingTestMode.ProgramOnMain" /> je adresa
    ///     lokomotívy povinná (cez ňu sa adresuje POM paket).
    /// </summary>
    Task<int> ReadCvAsync(int cvAddress, DccProgrammingTestMode programmingMode, int timeoutMs, int locoAddress,
        CancellationToken ct = default);

    /// <summary>
    ///     Zapíše hodnotu CV. Pre <see cref="DccProgrammingTestMode.ServiceTrack" /> sa
    ///     <paramref name="locoAddress" /> ignoruje (volajúci má odovzdať explicitný placeholder,
    ///     napr. 0); pre <see cref="DccProgrammingTestMode.ProgramOnMain" /> je adresa
    ///     lokomotívy povinná.
    /// </summary>
    Task WriteCvAsync(int cvAddress, int value, DccProgrammingTestMode programmingMode, int timeoutMs, int locoAddress,
        CancellationToken ct = default);

    /// <summary>
    ///     Číta viacero CV v jednej service-mode session — jeden socket, jedna registrácia,
    ///     žiadne ExitServiceMode medzi CV. Rýchlejšie a spoľahlivejšie ako opakované ReadCvAsync.
    /// </summary>
    Task ReadMultipleCvsAsync(
        IReadOnlyList<int> cvAddresses,
        int timeoutMsPerCv,
        int interCvDelayMs,
        Action<int, int> onCvRead,
        Action<int, int, int>? onCvReading = null,
        CancellationToken ct = default);
}
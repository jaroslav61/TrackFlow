using System.Threading;
using System.Threading.Tasks;

namespace TrackFlow.Services.Dcc;

public enum DccProgrammingTestMode
{
    ServiceTrack,
    ProgramOnMain
}

/// <summary>
/// Požiadavka na test komunikácie s dekodérom.
/// </summary>
/// <param name="DecoderTimeoutMs">Maximálny čas na odpoveď dekodéra (ms).</param>
/// <param name="ProgrammingMode">Service Track alebo POM.</param>
/// <param name="LocoAddress">DCC adresa lokomotívy – používa sa iba pri POM. Pre Service Track ignorované.</param>
public sealed record DccCommunicationTestRequest(
    int DecoderTimeoutMs,
    DccProgrammingTestMode ProgrammingMode,
    int LocoAddress = 3);

public interface IDccCommunicationTestService
{
    Task<string> TestReadCv1Async(DccCommunicationTestRequest request, CancellationToken ct = default);
}




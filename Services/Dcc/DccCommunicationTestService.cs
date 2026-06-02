using System;
using System.Threading;
using System.Threading.Tasks;
namespace TrackFlow.Services.Dcc;

public sealed class DccCommunicationTestService : IDccCommunicationTestService
{
    private readonly IDccConnectionService _dccConnectionService;
    private readonly SettingsManager _settingsManager;

    public DccCommunicationTestService(
        SettingsManager settingsManager,
        IDccConnectionService dccConnectionService)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _dccConnectionService = dccConnectionService ?? throw new ArgumentNullException(nameof(dccConnectionService));
    }

    public async Task<string> TestReadCv1Async(DccCommunicationTestRequest request, CancellationToken ct = default)
    {
        if (!_dccConnectionService.IsConnected)
            throw new InvalidOperationException("DCC centrála musí byť pripojená.");

        var effective = _settingsManager.GetEffective();
        var timeoutMs = Math.Clamp(request.DecoderTimeoutMs, 1_000, 5_000);

        if (_dccConnectionService.Client is not IDccProgrammingClient programmingClient)
            throw new NotSupportedException($"Čítanie CV pre centrálu {DccCentralDisplayName.Get(effective.DccCentralType)} zatiaľ nie je implementované.");

        var cv1 = await programmingClient.ReadCvAsync(1, request.ProgrammingMode, timeoutMs, request.LocoAddress, ct);
        var modeText = request.ProgrammingMode == DccProgrammingTestMode.ServiceTrack
            ? "Programovacia koľaj"
            : $"POM na hlavnej trati, loco {request.LocoAddress}";

        return $"Úspešne:  CV1 = {cv1} ({modeText})";
    }
}



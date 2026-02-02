using TrackFlow.Models;

namespace TrackFlow.Services.Dcc;

public static class DccClientFactory
{
    public static IDccCentralClient Create(DccCentralType type)
    {
        return type switch
        {
            DccCentralType.Z21Legacy => new Z21Client(),
            DccCentralType.Z21 => new Z21Client(),

            DccCentralType.GenericIpUdp => new GenericIpUdpClient(),

            _ => new Z21Client()
        };
    }
}

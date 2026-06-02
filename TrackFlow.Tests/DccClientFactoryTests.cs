using TrackFlow.Models;
using TrackFlow.Services.Dcc;
using Xunit;

namespace TrackFlow.Tests;

public sealed class DccClientFactoryTests
{
    [Fact]
    public void Create_ForNanoXS88_ReturnsSerialClient()
    {
        var client = DccClientFactory.Create(DccCentralType.NanoX_S88);

        Assert.IsType<SerialDccClient>(client);
    }
}



using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Services.Dcc;
using Xunit;

namespace TrackFlow.Tests;

public sealed class Z21ClientDisconnectTests
{
    [Fact]
    public void Disconnect_WhenSendSemaphoreIsHeld_ReturnsImmediately()
    {
        using var client = new Z21Client();

        var sendLockField = typeof(Z21Client).GetField("_sendLock", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(sendLockField);

        var sendLock = Assert.IsType<SemaphoreSlim>(sendLockField!.GetValue(client));
        sendLock.Wait();

        try
        {
            var start = DateTime.UtcNow;
            client.Disconnect();
            var elapsed = DateTime.UtcNow - start;

            Assert.True(elapsed < TimeSpan.FromMilliseconds(100),
                $"Disconnect blocked for {elapsed.TotalMilliseconds:F0} ms while send lock was held.");
        }
        finally
        {
            sendLock.Release();
        }
    }

    [Fact]
    public async Task Disconnect_CanBeCalledRepeatedly_WithoutThrowing()
    {
        using var client = new Z21Client();

        client.Disconnect();
        client.Disconnect();

        await Task.Delay(50);

        client.Disconnect();
    }
}


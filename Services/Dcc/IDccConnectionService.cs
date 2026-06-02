using System;

namespace TrackFlow.Services.Dcc;

public interface IDccConnectionService
{
    bool IsConnected { get; }
    IDccCentralClient Client { get; }
    event Action<bool>? IsConnectedChanged;
}


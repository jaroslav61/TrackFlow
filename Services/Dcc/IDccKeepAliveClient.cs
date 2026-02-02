using System.Threading;
using System.Threading.Tasks;

namespace TrackFlow.Services.Dcc;

/// <summary>
/// Voliteľné rozšírenie klienta pre monitorovanie „živosti“ spojenia.
/// Implementujte len tam, kde viete urobiť reálny ping/keepalive.
/// </summary>
public interface IDccKeepAliveClient
{
    Task<bool> PingAsync(CancellationToken ct = default);
}

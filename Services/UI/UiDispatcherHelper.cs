using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace TrackFlow.Services.UI;

/// <summary>
/// Stateless UI helper na bezpečné vyvolanie callbacku na UI vlákne.
/// Extrahované 1:1 z <see cref="TrackFlow.ViewModels.Operation.OperationViewModel"/> –
/// mechanický presun bez zmeny správania.
/// </summary>
internal static class UiDispatcherHelper
{
    /// <summary>
    /// Ak sme na UI vlákne, callback vykoná synchrónne; inak ho posunie cez
    /// <see cref="Dispatcher.UIThread"/> s prioritou <see cref="DispatcherPriority.Render"/>.
    /// Pri akejkoľvek výnimke z dispatchera fallback-uje na priame volanie –
    /// zhodne s pôvodnou OVM implementáciou.
    /// </summary>
    public static Task InvokeLayoutRefreshAsync(Action onLayoutRefresh)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                onLayoutRefresh();
            }
            else
            {
                Dispatcher.UIThread.Post(onLayoutRefresh, DispatcherPriority.Render);
            }
        }
        catch
        {
            onLayoutRefresh();
        }

        return Task.CompletedTask;
    }
}


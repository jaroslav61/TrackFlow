using System;
using System.IO;
using System.Linq;
using Xunit;

namespace TrackFlow.Tests;

public class AuditPriorityMarkupTests
{
    [Fact]
    public void OperationViewModel_NeobsahujeSyncOverAsyncGetResultVPrioritnychCestach()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("ViewModels", "Operation", "OperationViewModel.cs"));

        Assert.DoesNotContain(".GetAwaiter().GetResult()", code, StringComparison.Ordinal);
        Assert.Contains("QueueRefreshSignalStatus();", code, StringComparison.Ordinal);
        Assert.Contains("private async Task RefreshSignalStatusDeferredAsync()", code, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(() => LayoutRefreshRequested?.Invoke(), DispatcherPriority.Background);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservationEngine_MaSynchronnyAdvancePreDispatcherCestu()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Services", "Runtime", "ReservationEngine.cs"));

        Assert.Contains("public void Advance(AdvanceReservationWindowRequest request)", code, StringComparison.Ordinal);
        Assert.Contains("public Task AdvanceAsync(AdvanceReservationWindowRequest request)", code, StringComparison.Ordinal);
        Assert.Contains("Advance(request);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Z21Client_NetworkLoopsLogujuPrechodneChybyNamiestoTichehoSwallow()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Services", "Dcc", "Z21Client.cs"));

        Assert.Contains("Z21 telemetria send chyba (LAN_SYSTEMSTATE_GETDATA)", code, StringComparison.Ordinal);
        Assert.Contains("Z21 telemetria send chyba (LAN_RMBUS_GETDATA group=0)", code, StringComparison.Ordinal);
        Assert.Contains("MainReceiveLoop socket chyba:", code, StringComparison.Ordinal);
        Assert.Contains("MainReceiveLoop chyba:", code, StringComparison.Ordinal);
    }

    private static string GetWorkspaceFilePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "TrackFlow.sln")))
            directory = directory.Parent;

        if (directory == null)
            throw new InvalidOperationException("Nepodarilo sa nájsť koreň workspace TrackFlow.");

        return Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
    }
}


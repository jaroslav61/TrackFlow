using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TrackFlow.Models.Layout;
using TrackFlow.Runtime;
using TrackFlow.Services.Dcc;
using TrackFlow.Services.Signal;
using TrackFlow.Services.Signals;

namespace TrackFlow.Services.Runtime;

internal enum TraversalWaitOutcome
{
    Reserved,
    RouteInactive,
    RouteMissing,
    LayoutMissing,
    LocoMissing,
    TimedOut
}

internal sealed record WaitForNextBlockReservationRequest(
    TrackLayout Layout,
    RouteDefinition Route,
    IReadOnlyList<string> TraversalBlockIds,
    int SegmentIndex,
    BlockElement SegmentTarget,
    string LocoCode,
    bool OrientationForward,
    NavigationDirection TravelDirection,
    IDccCentralClient? DccClient,
    CancellationToken CancellationToken);

internal sealed class TraversalWaitCoordinatorCallbacks
{
    public required Func<string, string> ResolveTrainDisplayName { get; init; }
    public required Func<TrackLayout, string, string> ResolveRouteDisplayName { get; init; }
    public required Func<TrackLayout, string?, string?, string?> ResolveOwningRouteForBlock { get; init; }
    public required Func<string?, string?> ResolveTurnoutOwnerRouteId { get; init; }
    public required Func<TrackLayout, RouteDefinition, string, string, string?> ResolveConflictingTurnoutId { get; init; }
    public required Func<TrackLayout, string, TraversalWaitOutcome?> ResolveInvalidRouteOutcome { get; init; }
    public required Func<string, bool> IsTraversalLocoStillValid { get; init; }
    public required Func<string, CancellationToken, Task> StopLocomotiveDisplayAsync { get; init; }
    public required Func<TrackLayout, RouteDefinition, string, string, IDccCentralClient?, CancellationToken, Task<(bool IsReady, string? WaitReason)>> TryEnsureTurnoutsForSegmentAsync { get; init; }
    public required Func<int, CancellationToken, Task> MovementDelayAsync { get; init; }
    public required Func<TrackLayout, string, (int ReleasedBlocks, int ReleasedTurnouts)> ReleasePendingSharedReservationsForYield { get; init; }
    public required Action RequestLayoutRefresh { get; init; }
    public required Action<string, string, string, string?, int?, DateTime?, DiagnosticLevel> DiagnoseWaitState { get; init; }
    public required Action<TrackLayout, RouteDefinition, string, string, string?, string?, int, DateTime?, string, DiagnosticLevel> DiagnoseWaitRetry { get; init; }
    public required Action<TrackLayout, string, string?, string, string, string, string, DiagnosticLevel> DiagnoseArbiter { get; init; }
    public required Action<TrackLayout, string, string?, string?, string, DiagnosticLevel, string?> DiagnoseDeadlock { get; init; }
    public required Action<TrackLayout, string, string, string, string, DiagnosticLevel> DiagnoseDuplicateOrchestration { get; init; }
    public required Action<TrackLayout, string?, string?, SignalElement?, string, DiagnosticLevel> DiagnoseSignalRuntime { get; init; }
}

internal sealed class TraversalWaitCoordinator
{
    private const int WaitRetryIntervalMs = 500;
    private const int StickyWaitWinnerWindowMs = 700;

    private enum StickyWaitResourceKind
    {
        Block,
        Turnout
    }

    private sealed record WaitResourceRegistration(string RouteId, long Order);

    private sealed record StickyWaitGrant(
        string ResourceId,
        string WinnerRouteId,
        DateTime ExpiresAtUtc);

    private sealed record DeadlockYieldState(
        string RouteId,
        string WinnerRouteId,
        string WaitingResourceKey,
        DateTime EnteredAtUtc);

    private sealed record DeadlockCycleInfo(
        string WinnerRouteId,
        string LoserRouteId,
        string WinnerWaitingResourceKey,
        string LoserWaitingResourceKey,
        string WinnerBlockedByRouteId,
        string LoserBlockedByRouteId);

    private readonly RuntimeStateRegistry _runtimeRegistry;
    private readonly ReservationEngine _reservationEngine;
    private readonly TraversalEngine _traversalEngine;
    private readonly SignalSafetyEngine _signalSafetyEngine;
    private readonly TraversalWaitCoordinatorCallbacks _callbacks;
    private readonly TimeSpan _waitMaxDuration;
    private readonly object _waitArbiterSync = new();
    private readonly Dictionary<string, DeadlockYieldState> _deadlockYieldByRouteId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _waitingResourceByRouteId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<WaitResourceRegistration>> _waitRegistrationsByResourceKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StickyWaitGrant> _stickyWaitGrantsByResourceKey = new(StringComparer.OrdinalIgnoreCase);
    private long _waitRegistrationSequence;

    public TraversalWaitCoordinator(
        RuntimeStateRegistry runtimeRegistry,
        ReservationEngine reservationEngine,
        TraversalEngine traversalEngine,
        SignalSafetyEngine signalSafetyEngine,
        TraversalWaitCoordinatorCallbacks callbacks,
        TimeSpan waitMaxDuration)
    {
        _runtimeRegistry = runtimeRegistry;
        _reservationEngine = reservationEngine;
        _traversalEngine = traversalEngine;
        _signalSafetyEngine = signalSafetyEngine;
        _callbacks = callbacks;
        _waitMaxDuration = waitMaxDuration;
    }

    public void ClearState()
    {
        lock (_waitArbiterSync)
        {
            _waitingResourceByRouteId.Clear();
            _waitRegistrationsByResourceKey.Clear();
            _stickyWaitGrantsByResourceKey.Clear();
            _waitRegistrationSequence = 0;
        }

        _deadlockYieldByRouteId.Clear();
    }

    public bool EnterTraversalWait(string routeId, string blockId, string reason)
        => EnterTraversalWaitCore(routeId, blockId, reason);

    public bool ExitTraversalWait(string routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return false;

        var removed = _runtimeRegistry.ExitWaitState(routeId);
        ClearWaitingResource(routeId);
        _deadlockYieldByRouteId.Remove(routeId);
        return removed;
    }

    public bool ExitTraversalWaitSuccess(string routeId, string blockId)
        => ExitTraversalWaitSuccessCore(routeId, blockId);

    public bool ProcessDeadlockYieldState(TrackLayout layout, string routeId)
    {
        if (_deadlockYieldByRouteId.TryGetValue(routeId, out var yieldState))
        {
            if (DateTime.UtcNow - yieldState.EnteredAtUtc >= _waitMaxDuration)
            {
                ExitDeadlockYield(layout, routeId, "časový-limit", DiagnosticLevel.Warning);
                return false;
            }

            if (ShouldKeepDeadlockYield(layout, yieldState))
                return true;

            ExitDeadlockYield(layout, routeId, "vyriešený", DiagnosticLevel.Success);
            return false;
        }

        var cycle = TryDetectCircularWaitDeadlock(layout, routeId);
        if (cycle == null)
            return false;

        _callbacks.DiagnoseDeadlock(layout, cycle.WinnerRouteId, cycle.WinnerBlockedByRouteId, cycle.WinnerWaitingResourceKey, "detegovaný", DiagnosticLevel.Warning, null);
        _callbacks.DiagnoseDeadlock(layout, cycle.LoserRouteId, cycle.LoserBlockedByRouteId, cycle.LoserWaitingResourceKey, "detegovaný", DiagnosticLevel.Warning, null);

        if (!string.Equals(cycle.LoserRouteId, routeId, StringComparison.OrdinalIgnoreCase))
            return false;

        return EnterDeadlockYield(layout, routeId, cycle.WinnerRouteId, cycle.LoserWaitingResourceKey);
    }

    public async Task<TraversalWaitOutcome> WaitForNextBlockReservationAsync(WaitForNextBlockReservationRequest request)
    {
        var reason = ResolveWaitReason(request.SegmentTarget);
        var waitEntered = EnterTraversalWaitCore(request.Route.Id, request.SegmentTarget.Id, reason);

        await _signalSafetyEngine.ApplyStopBeforeSegmentAsync(
            request.Layout,
            request.Route,
            request.TraversalBlockIds,
            request.SegmentIndex,
            request.DccClient,
            request.CancellationToken);

        await _callbacks.StopLocomotiveDisplayAsync(request.LocoCode, request.CancellationToken);

        if (waitEntered)
        {
            _callbacks.RequestLayoutRefresh();
        }
        else
        {
            _callbacks.DiagnoseDuplicateOrchestration(
                request.Layout,
                request.Route.Id,
                request.SegmentTarget.Id,
                "duplicate visual refresh trigger",
                "WAIT stav je už aktívny pre rovnaký blok a dôvod; refresh sekvencia ostáva behavior-preserving bez cleanupu.",
                DiagnosticLevel.Info);
        }

        try
        {
            var retryCount = 0;
            while (true)
            {
                request.CancellationToken.ThrowIfCancellationRequested();

                if (!_runtimeRegistry.IsRouteActive(request.Route.Id))
                {
                    ExitTraversalWait(request.Route.Id);
                    return TraversalWaitOutcome.RouteInactive;
                }

                var invalidRouteOutcome = _callbacks.ResolveInvalidRouteOutcome(request.Layout, request.Route.Id);
                if (invalidRouteOutcome.HasValue)
                {
                    ExitTraversalWait(request.Route.Id);
                    return invalidRouteOutcome.Value;
                }

                if (!_callbacks.IsTraversalLocoStillValid(request.LocoCode))
                {
                    ExitTraversalWait(request.Route.Id);
                    return TraversalWaitOutcome.LocoMissing;
                }

                if (IsWaitTimedOut(request.Route.Id))
                {
                    _callbacks.DiagnoseWaitRetry(
                        request.Layout,
                        request.Route,
                        request.LocoCode,
                        request.SegmentTarget.Id,
                        reason,
                        _callbacks.ResolveOwningRouteForBlock(request.Layout, request.SegmentTarget.Id, request.Route.Id),
                        retryCount,
                        _runtimeRegistry.GetRuntime(request.Route.Id)?.WaitState?.EnteredAtUtc,
                        "retry-časový-limit",
                        DiagnosticLevel.Warning);
                    ExitTraversalWaitTimeout(request.Route.Id, request.SegmentTarget.Id);
                    return TraversalWaitOutcome.TimedOut;
                }

                if (ProcessDeadlockYieldState(request.Layout, request.Route.Id))
                {
                    await _signalSafetyEngine.ApplyStopBeforeSegmentAsync(
                        request.Layout,
                        request.Route,
                        request.TraversalBlockIds,
                        request.SegmentIndex,
                        request.DccClient,
                        request.CancellationToken);
                    await _callbacks.MovementDelayAsync(WaitRetryIntervalMs, request.CancellationToken);
                    continue;
                }

                retryCount++;

                var turnoutReady = await _callbacks.TryEnsureTurnoutsForSegmentAsync(
                    request.Layout,
                    request.Route,
                    request.TraversalBlockIds[request.SegmentIndex],
                    request.TraversalBlockIds[request.SegmentIndex + 1],
                    request.DccClient,
                    request.CancellationToken);
                if (!turnoutReady.IsReady)
                {
                    var conflictingTurnoutId = _callbacks.ResolveConflictingTurnoutId(
                        request.Layout,
                        request.Route,
                        request.TraversalBlockIds[request.SegmentIndex],
                        request.TraversalBlockIds[request.SegmentIndex + 1]);
                    if (!string.IsNullOrWhiteSpace(conflictingTurnoutId))
                        TrackWaitingResource(request.Route.Id, StickyWaitResourceKind.Turnout, conflictingTurnoutId);

                    if (ProcessDeadlockYieldState(request.Layout, request.Route.Id))
                    {
                        await _signalSafetyEngine.ApplyStopBeforeSegmentAsync(
                            request.Layout,
                            request.Route,
                            request.TraversalBlockIds,
                            request.SegmentIndex,
                            request.DccClient,
                            request.CancellationToken);
                        await _callbacks.MovementDelayAsync(WaitRetryIntervalMs, request.CancellationToken);
                        continue;
                    }

                    var turnoutOwnerRouteId = _callbacks.ResolveTurnoutOwnerRouteId(conflictingTurnoutId);
                    if (ShouldEmitWaitRetryDiagnostic(retryCount))
                    {
                        _callbacks.DiagnoseWaitRetry(
                            request.Layout,
                            request.Route,
                            request.LocoCode,
                            conflictingTurnoutId ?? request.SegmentTarget.Id,
                            turnoutReady.WaitReason ?? "konflikt-vyhybky",
                            turnoutOwnerRouteId,
                            retryCount,
                            _runtimeRegistry.GetRuntime(request.Route.Id)?.WaitState?.EnteredAtUtc,
                            "retry-štart",
                            DiagnosticLevel.Info);
                        _callbacks.DiagnoseWaitRetry(
                            request.Layout,
                            request.Route,
                            request.LocoCode,
                            conflictingTurnoutId ?? request.SegmentTarget.Id,
                            turnoutReady.WaitReason ?? "konflikt-vyhybky",
                            turnoutOwnerRouteId,
                            retryCount,
                            _runtimeRegistry.GetRuntime(request.Route.Id)?.WaitState?.EnteredAtUtc,
                            "retry-odmietnutý",
                            DiagnosticLevel.Warning);
                    }

                    if (EnterTraversalWaitCore(request.Route.Id, request.SegmentTarget.Id, turnoutReady.WaitReason ?? "konflikt-vyhybky"))
                        _callbacks.RequestLayoutRefresh();

                    await _signalSafetyEngine.ApplyStopBeforeSegmentAsync(
                        request.Layout,
                        request.Route,
                        request.TraversalBlockIds,
                        request.SegmentIndex,
                        request.DccClient,
                        request.CancellationToken);
                    await _callbacks.MovementDelayAsync(WaitRetryIntervalMs, request.CancellationToken);
                    continue;
                }

                TrackWaitingResource(request.Route.Id, StickyWaitResourceKind.Block, request.SegmentTarget.Id);
                if (ProcessDeadlockYieldState(request.Layout, request.Route.Id))
                {
                    await _signalSafetyEngine.ApplyStopBeforeSegmentAsync(
                        request.Layout,
                        request.Route,
                        request.TraversalBlockIds,
                        request.SegmentIndex,
                        request.DccClient,
                        request.CancellationToken);
                    await _callbacks.MovementDelayAsync(WaitRetryIntervalMs, request.CancellationToken);
                    continue;
                }

                if (!request.SegmentTarget.IsTailClearing
                    && request.SegmentTarget.IsShadowSet
                    && string.Equals(request.SegmentTarget.ReservedLocoId, request.LocoCode, StringComparison.OrdinalIgnoreCase))
                {
                    _callbacks.DiagnoseDuplicateOrchestration(
                        request.Layout,
                        request.Route.Id,
                        request.SegmentTarget.Id,
                        "duplicate reservation apply",
                        "WAIT recovery ide cez idempotentný branch; reservation apply flow ostáva zámerne nezmenený.",
                        DiagnosticLevel.Info);

                    _callbacks.DiagnoseWaitRetry(
                        request.Layout,
                        request.Route,
                        request.LocoCode,
                        request.SegmentTarget.Id,
                        reason,
                            _callbacks.ResolveOwningRouteForBlock(request.Layout, request.SegmentTarget.Id, request.Route.Id),
                        retryCount,
                        _runtimeRegistry.GetRuntime(request.Route.Id)?.WaitState?.EnteredAtUtc,
                        "retry-úspech",
                        DiagnosticLevel.Success);
                    Log.Information("WAIT retry rezervácie úspešný bez nového pokusu: cesta=[{Cesta}], blok=[{Blok}]", request.Route.Id, request.SegmentTarget.Id);
                    return await CompleteWaitRecoveryAsync(request, "voľno-obnovené-po-wait");
                }

                var retrySignal = RouteSegmentSignalResolver.ResolveSegmentStartSignal(
                    request.Layout,
                    request.TraversalBlockIds[request.SegmentIndex],
                    request.TravelDirection,
                    request.TraversalBlockIds[request.SegmentIndex + 1]);
                if (ShouldEmitWaitRetryDiagnostic(retryCount))
                {
                    _callbacks.DiagnoseWaitRetry(
                        request.Layout,
                        request.Route,
                        request.LocoCode,
                        request.SegmentTarget.Id,
                        reason,
                        _callbacks.ResolveOwningRouteForBlock(request.Layout, request.SegmentTarget.Id, request.Route.Id),
                        retryCount,
                        _runtimeRegistry.GetRuntime(request.Route.Id)?.WaitState?.EnteredAtUtc,
                        "retry-štart",
                        DiagnosticLevel.Info);
                }

                Log.Information(
                    "WAIT retry rezervácie: cesta=[{Cesta}], blok=[{Blok}], návestidlo=[{Návestidlo}], aspekt=[{Aspekt}]",
                    request.Route.Id,
                    request.SegmentTarget.Id,
                    retrySignal?.Id ?? "žiadne",
                    retrySignal?.Aspect.ToString() ?? "žiadny");

                _ = _reservationEngine.TryReserveNextBlock(
                    new ReserveNextBlockRequest(
                        request.SegmentTarget,
                        request.LocoCode,
                        request.OrientationForward,
                        _callbacks.ResolveTrainDisplayName(request.LocoCode),
                        request.Layout,
                        request.TraversalBlockIds[request.SegmentIndex],
                        request.TravelDirection,
                        SuppressFailureDiagnostics: true,
                        SuppressSuccessRefresh: true,
                        IgnoreProtectingSignalAspect: true),
                    out _);

                if (!request.SegmentTarget.IsTailClearing
                    && request.SegmentTarget.IsShadowSet
                    && string.Equals(request.SegmentTarget.ReservedLocoId, request.LocoCode, StringComparison.OrdinalIgnoreCase))
                {
                    _callbacks.DiagnoseWaitRetry(
                        request.Layout,
                        request.Route,
                        request.LocoCode,
                        request.SegmentTarget.Id,
                        reason,
                        _callbacks.ResolveOwningRouteForBlock(request.Layout, request.SegmentTarget.Id, request.Route.Id),
                        retryCount,
                        _runtimeRegistry.GetRuntime(request.Route.Id)?.WaitState?.EnteredAtUtc,
                        "retry-úspech",
                        DiagnosticLevel.Success);
                    Log.Information("WAIT retry rezervácie úspešný: cesta=[{Cesta}], blok=[{Blok}]", request.Route.Id, request.SegmentTarget.Id);
                    return await CompleteWaitRecoveryAsync(request, "obnova-po-retry");
                }

                if (ShouldEmitWaitRetryDiagnostic(retryCount))
                {
                    _callbacks.DiagnoseWaitRetry(
                        request.Layout,
                        request.Route,
                        request.LocoCode,
                        request.SegmentTarget.Id,
                        reason,
                        _callbacks.ResolveOwningRouteForBlock(request.Layout, request.SegmentTarget.Id, request.Route.Id),
                        retryCount,
                        _runtimeRegistry.GetRuntime(request.Route.Id)?.WaitState?.EnteredAtUtc,
                        "retry-odmietnutý",
                        DiagnosticLevel.Warning);
                }

                await _signalSafetyEngine.ApplyStopBeforeSegmentAsync(
                    request.Layout,
                    request.Route,
                    request.TraversalBlockIds,
                    request.SegmentIndex,
                    request.DccClient,
                    request.CancellationToken);
                await _callbacks.MovementDelayAsync(WaitRetryIntervalMs, request.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warning("WAIT cancel detegovaný: cesta=[{Cesta}], blok=[{Blok}]", request.Route.Id, request.SegmentTarget.Id);
            ExitTraversalWait(request.Route.Id);
            throw;
        }
    }

    public void AssignStickyWaitWinnerForBlock(TrackLayout layout, string resourceId)
        => AssignStickyWaitWinner(layout, StickyWaitResourceKind.Block, resourceId);

    public void AssignStickyWaitWinnerForTurnout(TrackLayout layout, string resourceId)
        => AssignStickyWaitWinner(layout, StickyWaitResourceKind.Turnout, resourceId);

    public string? TryGetStickyBlockingWinnerRouteIdForBlock(TrackLayout layout, string resourceId, string? requestingRouteId)
        => TryGetStickyBlockingWinnerRouteId(layout, StickyWaitResourceKind.Block, resourceId, requestingRouteId);

    public string? TryGetStickyBlockingWinnerRouteIdForTurnout(TrackLayout layout, string resourceId, string? requestingRouteId)
        => TryGetStickyBlockingWinnerRouteId(layout, StickyWaitResourceKind.Turnout, resourceId, requestingRouteId);

    public void ConsumeStickyWaitWinnerForBlock(string? requestingRouteId, string resourceId)
        => ConsumeStickyWaitWinner(requestingRouteId, StickyWaitResourceKind.Block, resourceId);

    public void ConsumeStickyWaitWinnerForTurnout(string? requestingRouteId, string resourceId)
        => ConsumeStickyWaitWinner(requestingRouteId, StickyWaitResourceKind.Turnout, resourceId);

    public string? ResolveStickyWaitWinnerRouteIdForBlock(string? blockId)
        => ResolveStickyWaitWinnerRouteId(StickyWaitResourceKind.Block, blockId);

    public string? ResolveStickyWaitWinnerRouteIdForTurnout(string? turnoutId)
        => ResolveStickyWaitWinnerRouteId(StickyWaitResourceKind.Turnout, turnoutId);

    private static string ResolveWaitReason(BlockElement next)
    {
        if (next.IsTailClearing)
            return "tail-clear-blok";
        if (next.IsOccupied)
            return "obsadený-blok";
        if (next.IsShadowSet)
            return "rezervované-iným";
        return "bezpečnostné-obmedzenie-za-jazdy";
    }

    private bool EnterTraversalWaitCore(string routeId, string blockId, string reason)
    {
        if (string.IsNullOrWhiteSpace(routeId) || string.IsNullOrWhiteSpace(blockId))
            return false;

        var existing = _runtimeRegistry.GetRuntime(routeId)?.WaitState;
        if (existing != null
            && string.Equals(existing.BlockId, blockId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.Reason, reason, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var waitingSinceUtc = DateTime.UtcNow;
        _runtimeRegistry.EnterWaitState(routeId, blockId, reason, waitingSinceUtc);
        _callbacks.DiagnoseWaitState(routeId, blockId, "wait-enter", reason, 0, waitingSinceUtc, DiagnosticLevel.Info);
        _callbacks.DiagnoseWaitState(routeId, blockId, "vstup", reason, 0, waitingSinceUtc, DiagnosticLevel.Info);
        Log.Information("WAIT vstup: cesta=[{Cesta}], blok=[{Blok}], dôvod=[{Dôvod}]", routeId, blockId, reason);
        return true;
    }

    private bool ExitTraversalWaitSuccessCore(string routeId, string blockId)
    {
        if (string.IsNullOrWhiteSpace(routeId) || string.IsNullOrWhiteSpace(blockId))
            return false;

        if (!ExitTraversalWait(routeId))
            return false;

        _runtimeRegistry.ResetWaitStateToActive(routeId);
        _callbacks.DiagnoseWaitState(routeId, blockId, "winner-consume", null, null, null, DiagnosticLevel.Success);
        Log.Information("WAIT opakovaný pokus úspešný: cesta=[{Cesta}], blok=[{Blok}]", routeId, blockId);
        return true;
    }

    private void ExitTraversalWaitTimeout(string routeId, string blockId)
    {
        if (string.IsNullOrWhiteSpace(routeId) || string.IsNullOrWhiteSpace(blockId))
            return;

        if (!ExitTraversalWait(routeId))
            return;

        _callbacks.DiagnoseWaitState(routeId, blockId, "timeout", null, null, null, DiagnosticLevel.Warning);
        Log.Warning(
            "ČAKANIE - časový limit po [{Minúty:0.##}] minútach: cesta=[{Cesta}], blok=[{Blok}]",
            _waitMaxDuration.TotalMinutes,
            routeId,
            blockId);
    }

    private bool IsWaitTimedOut(string routeId)
    {
        var waitState = _runtimeRegistry.GetRuntime(routeId)?.WaitState;
        if (waitState == null)
            return false;

        return DateTime.UtcNow - waitState.EnteredAtUtc >= _waitMaxDuration;
    }

    private async Task<TraversalWaitOutcome> CompleteWaitRecoveryAsync(WaitForNextBlockReservationRequest request, string signalState)
    {
        var exited = ExitTraversalWaitSuccessCore(request.Route.Id, request.SegmentTarget.Id);

        if (IsDuplicateTraversalWindowRestore(request.Route.Id, request.TraversalBlockIds, request.SegmentIndex, keepPreviousSegmentActive: false))
        {
            // DUPLIKÁT zistený len diagnosticky; behavior-preserving signal restore flow sa nemení.
            _callbacks.DiagnoseDuplicateOrchestration(
                request.Layout,
                request.Route.Id,
                request.SegmentTarget.Id,
                "duplicate signal recompute",
                "WAIT recovery opakuje traversal window + signal recompute nad identickým segmentovým oknom; sequencing ostáva zachovaný.",
                DiagnosticLevel.Info);
        }

        _traversalEngine.SetTraversalWindow(new TraversalWindowRequest(
            request.Layout,
            request.Route,
            request.TraversalBlockIds,
            request.SegmentIndex,
            KeepPreviousSegmentActive: false));
        await _signalSafetyEngine.UpdateTraversalSignalWindowAsync(new UpdateTraversalSignalWindowRequest(
            request.Layout,
            request.Route,
            request.TraversalBlockIds,
            request.SegmentIndex,
            KeepPreviousSegmentActive: false,
            request.DccClient,
            request.CancellationToken));

        var recoveredSignal = RouteSegmentSignalResolver.ResolveSegmentStartSignal(
            request.Layout,
            request.TraversalBlockIds[request.SegmentIndex],
            request.TravelDirection,
            request.TraversalBlockIds[request.SegmentIndex + 1]);
        Log.Information(
            "WAIT recovery stav: cesta=[{Cesta}], blok=[{Blok}], návestidlo=[{Návestidlo}], aspekt=[{Aspekt}], okno=[{Okno}], pokračovanie=[{Pokračovanie}]",
            request.Route.Id,
            request.SegmentTarget.Id,
            recoveredSignal?.Id ?? "žiadne",
            recoveredSignal?.Aspect.ToString() ?? "žiadny",
            _runtimeRegistry.TryGetReservationWindow(request.Route.Id, out var windowAfterRetrySuccess) && windowAfterRetrySuccess != null
                ? string.Join(",", windowAfterRetrySuccess.BlockIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
                : string.Empty,
            request.SegmentTarget.IsShadowSet && string.Equals(request.SegmentTarget.ReservedLocoId, request.LocoCode, StringComparison.OrdinalIgnoreCase));
        _callbacks.DiagnoseSignalRuntime(request.Layout, request.Route.Id, request.LocoCode, recoveredSignal, signalState, DiagnosticLevel.Success);
        if (exited)
            _callbacks.RequestLayoutRefresh();

        return TraversalWaitOutcome.Reserved;
    }

    private bool IsDuplicateTraversalWindowRestore(string routeId, IReadOnlyList<string> traversalBlockIds, int leadSegmentIndex, bool keepPreviousSegmentActive)
    {
        if (!_runtimeRegistry.TryGetReservationWindow(routeId, out var window) || window == null)
            return false;

        var expectedBlockIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddSegment(int segmentIndex)
        {
            if (segmentIndex < 0 || segmentIndex >= traversalBlockIds.Count - 1)
                return;

            expectedBlockIds.Add(traversalBlockIds[segmentIndex]);
            expectedBlockIds.Add(traversalBlockIds[segmentIndex + 1]);
        }

        AddSegment(leadSegmentIndex);
        if (keepPreviousSegmentActive)
            AddSegment(leadSegmentIndex - 1);

        return window.LeadSegmentIndex == leadSegmentIndex
               && window.KeepPreviousSegmentActive == keepPreviousSegmentActive
               && window.BlockIds.SetEquals(expectedBlockIds);
    }

    private static bool ShouldEmitWaitRetryDiagnostic(int retryCount)
        => retryCount == 1 || retryCount % 5 == 0;

    private static string BuildWaitResourceKey(StickyWaitResourceKind resourceKind, string resourceId)
        => $"{resourceKind}:{resourceId}";

    private void TrackWaitingResource(string routeId, StickyWaitResourceKind resourceKind, string resourceId)
    {
        if (string.IsNullOrWhiteSpace(routeId) || string.IsNullOrWhiteSpace(resourceId))
            return;

        var resourceKey = BuildWaitResourceKey(resourceKind, resourceId);

        lock (_waitArbiterSync)
        {
            if (_waitingResourceByRouteId.TryGetValue(routeId, out var existingKey)
                && string.Equals(existingKey, resourceKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            RemoveWaitingResourceRegistrationNoLock(routeId);

            if (!_waitRegistrationsByResourceKey.TryGetValue(resourceKey, out var registrations))
            {
                registrations = new List<WaitResourceRegistration>();
                _waitRegistrationsByResourceKey[resourceKey] = registrations;
            }

            registrations.Add(new WaitResourceRegistration(routeId, ++_waitRegistrationSequence));
            registrations.Sort((a, b) => a.Order.CompareTo(b.Order));
            _waitingResourceByRouteId[routeId] = resourceKey;
        }
    }

    private void ClearWaitingResource(string routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return;

        lock (_waitArbiterSync)
        {
            RemoveWaitingResourceRegistrationNoLock(routeId);
        }
    }

    private void RemoveWaitingResourceRegistrationNoLock(string routeId)
    {
        if (!_waitingResourceByRouteId.TryGetValue(routeId, out var existingKey))
            return;

        _waitingResourceByRouteId.Remove(routeId);

        if (!_waitRegistrationsByResourceKey.TryGetValue(existingKey, out var registrations))
            return;

        registrations.RemoveAll(r => string.Equals(r.RouteId, routeId, StringComparison.OrdinalIgnoreCase));
        if (registrations.Count == 0)
            _waitRegistrationsByResourceKey.Remove(existingKey);
    }

    private List<WaitResourceRegistration> GetEligibleWaitersNoLock(string resourceKey)
    {
        if (!_waitRegistrationsByResourceKey.TryGetValue(resourceKey, out var registrations))
            return new List<WaitResourceRegistration>();

        registrations.RemoveAll(r => !_runtimeRegistry.IsRouteActive(r.RouteId)
                                     || !_runtimeRegistry.HasWaitState(r.RouteId)
                                     || !_waitingResourceByRouteId.TryGetValue(r.RouteId, out var trackedKey)
                                     || !string.Equals(trackedKey, resourceKey, StringComparison.OrdinalIgnoreCase));

        if (registrations.Count == 0)
            _waitRegistrationsByResourceKey.Remove(resourceKey);

        return registrations
            .OrderBy(r => r.Order)
            .ToList();
    }

    private string FormatWaitOrder(TrackLayout layout, IEnumerable<string> routeIds)
    {
        var labels = routeIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => _callbacks.ResolveRouteDisplayName(layout, id))
            .ToList();

        return labels.Count > 0 ? string.Join(" > ", labels) : "žiadne";
    }

    private void AssignStickyWaitWinner(TrackLayout layout, StickyWaitResourceKind resourceKind, string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            return;

        StickyWaitGrant grant;
        string waitOrder;
        var resourceKey = BuildWaitResourceKey(resourceKind, resourceId);

        lock (_waitArbiterSync)
        {
            var eligibleWaiters = GetEligibleWaitersNoLock(resourceKey);
            waitOrder = FormatWaitOrder(layout, eligibleWaiters.Select(w => w.RouteId));

            if (eligibleWaiters.Count == 0)
            {
                _stickyWaitGrantsByResourceKey.Remove(resourceKey);
                return;
            }

            var winner = eligibleWaiters[0];
            grant = new StickyWaitGrant(resourceId, winner.RouteId, DateTime.UtcNow.AddMilliseconds(StickyWaitWinnerWindowMs));
            _stickyWaitGrantsByResourceKey[resourceKey] = grant;
        }

        _callbacks.DiagnoseWaitState(grant.WinnerRouteId, resourceId, "winner-assign", resourceKind.ToString(), null, null, DiagnosticLevel.Info);
        _callbacks.DiagnoseArbiter(layout, resourceKind.ToString(), resourceId, grant.WinnerRouteId, waitOrder, $"{StickyWaitWinnerWindowMs}ms", "udelený", DiagnosticLevel.Info);
    }

    private string? TryGetStickyBlockingWinnerRouteId(TrackLayout layout, StickyWaitResourceKind resourceKind, string resourceId, string? requestingRouteId)
    {
        if (string.IsNullOrWhiteSpace(resourceId) || string.IsNullOrWhiteSpace(requestingRouteId))
            return null;

        var resourceKey = BuildWaitResourceKey(resourceKind, resourceId);

        while (true)
        {
            StickyWaitGrant? expiredGrant = null;
            string? blockingWinner = null;
            string blockingWaitOrder = "žiadne";
            string stickyWindow = "0ms";
            StickyWaitGrant? reassignedGrant = null;
            string waitOrder;

            lock (_waitArbiterSync)
            {
                if (!_stickyWaitGrantsByResourceKey.TryGetValue(resourceKey, out var grant))
                    return null;

                var eligibleWaiters = GetEligibleWaitersNoLock(resourceKey);
                waitOrder = FormatWaitOrder(layout, eligibleWaiters.Select(w => w.RouteId));

                if (grant.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    expiredGrant = grant;
                    _stickyWaitGrantsByResourceKey.Remove(resourceKey);
                }
                else if (!eligibleWaiters.Any(w => string.Equals(w.RouteId, grant.WinnerRouteId, StringComparison.OrdinalIgnoreCase)))
                {
                    _stickyWaitGrantsByResourceKey.Remove(resourceKey);
                    if (eligibleWaiters.Count > 0)
                    {
                        var newWinner = eligibleWaiters[0];
                        reassignedGrant = new StickyWaitGrant(resourceId, newWinner.RouteId, DateTime.UtcNow.AddMilliseconds(StickyWaitWinnerWindowMs));
                        _stickyWaitGrantsByResourceKey[resourceKey] = reassignedGrant;
                    }
                }
                else if (!string.Equals(grant.WinnerRouteId, requestingRouteId, StringComparison.OrdinalIgnoreCase))
                {
                    blockingWinner = grant.WinnerRouteId;
                    blockingWaitOrder = waitOrder;
                    stickyWindow = $"{Math.Max(0, (int)Math.Ceiling((grant.ExpiresAtUtc - DateTime.UtcNow).TotalMilliseconds))}ms";
                }
                else
                {
                    return null;
                }
            }

            if (expiredGrant != null)
            {
                _callbacks.DiagnoseWaitState(expiredGrant.WinnerRouteId, expiredGrant.ResourceId, "winner-expire", resourceKind.ToString(), null, null, DiagnosticLevel.Info);
                _callbacks.DiagnoseArbiter(layout, resourceKind.ToString(), resourceId, expiredGrant.WinnerRouteId, waitOrder, "0ms", "expiroval", DiagnosticLevel.Info);
                continue;
            }

            if (reassignedGrant != null)
            {
                _callbacks.DiagnoseWaitState(reassignedGrant.WinnerRouteId, reassignedGrant.ResourceId, "winner-assign", resourceKind.ToString(), null, null, DiagnosticLevel.Info);
                _callbacks.DiagnoseArbiter(layout, resourceKind.ToString(), resourceId, reassignedGrant.WinnerRouteId, waitOrder, $"{StickyWaitWinnerWindowMs}ms", "udelený", DiagnosticLevel.Info);
                continue;
            }

            if (blockingWinner != null)
            {
                _callbacks.DiagnoseArbiter(layout, resourceKind.ToString(), resourceId, blockingWinner, blockingWaitOrder, stickyWindow, "odmietnutý", DiagnosticLevel.Warning);
                return blockingWinner;
            }

            return null;
        }
    }

    private void ConsumeStickyWaitWinner(string? requestingRouteId, StickyWaitResourceKind resourceKind, string resourceId)
    {
        if (string.IsNullOrWhiteSpace(requestingRouteId) || string.IsNullOrWhiteSpace(resourceId))
            return;

        var resourceKey = BuildWaitResourceKey(resourceKind, resourceId);

        lock (_waitArbiterSync)
        {
            if (_stickyWaitGrantsByResourceKey.TryGetValue(resourceKey, out var grant)
                && string.Equals(grant.WinnerRouteId, requestingRouteId, StringComparison.OrdinalIgnoreCase))
            {
                _stickyWaitGrantsByResourceKey.Remove(resourceKey);
            }
        }

        _callbacks.DiagnoseWaitState(requestingRouteId, resourceId, "winner-consume", resourceKind.ToString(), null, null, DiagnosticLevel.Success);
    }

    private string? ResolveStickyWaitWinnerRouteId(StickyWaitResourceKind resourceKind, string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            return null;

        var resourceKey = BuildWaitResourceKey(resourceKind, resourceId);

        lock (_waitArbiterSync)
        {
            if (!_stickyWaitGrantsByResourceKey.TryGetValue(resourceKey, out var grant))
                return null;

            if (grant.ExpiresAtUtc <= DateTime.UtcNow)
            {
                _stickyWaitGrantsByResourceKey.Remove(resourceKey);
                return null;
            }

            return grant.WinnerRouteId;
        }
    }

    private static bool TryParseWaitResourceKey(string resourceKey, out StickyWaitResourceKind resourceKind, out string resourceId)
    {
        resourceKind = StickyWaitResourceKind.Block;
        resourceId = string.Empty;

        if (string.IsNullOrWhiteSpace(resourceKey))
            return false;

        var separatorIndex = resourceKey.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= resourceKey.Length - 1)
            return false;

        if (!Enum.TryParse(resourceKey[..separatorIndex], ignoreCase: true, out resourceKind))
            return false;

        resourceId = resourceKey[(separatorIndex + 1)..];
        return !string.IsNullOrWhiteSpace(resourceId);
    }

    private string? GetTrackedWaitingResourceKey(string routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return null;

        lock (_waitArbiterSync)
        {
            if (_waitingResourceByRouteId.TryGetValue(routeId, out var resourceKey))
                return resourceKey;
        }

        var waitState = _runtimeRegistry.GetRuntime(routeId)?.WaitState;
        return waitState != null
            ? BuildWaitResourceKey(StickyWaitResourceKind.Block, waitState.BlockId)
            : null;
    }

    private DateTime? ResolveRouteWaitSinceUtc(string routeId)
        => _runtimeRegistry.GetWaitSinceUtc(routeId)
           ?? _runtimeRegistry.GetRuntime(routeId)?.WaitingSinceUtc;

    private string? ResolveResourceOwnerRouteId(TrackLayout layout, StickyWaitResourceKind resourceKind, string resourceId, string? excludeRouteId = null)
    {
        if (resourceKind == StickyWaitResourceKind.Block)
        {
            return _callbacks.ResolveOwningRouteForBlock(layout, resourceId, excludeRouteId);
        }

        var turnoutOwnerRouteId = _callbacks.ResolveTurnoutOwnerRouteId(resourceId);
        if (!string.IsNullOrWhiteSpace(excludeRouteId)
            && string.Equals(turnoutOwnerRouteId, excludeRouteId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return turnoutOwnerRouteId;
    }

    private DeadlockCycleInfo? TryDetectCircularWaitDeadlock(TrackLayout layout, string routeId)
    {
        var waitingResourceKey = GetTrackedWaitingResourceKey(routeId);
        if (!TryParseWaitResourceKey(waitingResourceKey ?? string.Empty, out var resourceKind, out var resourceId))
            return null;

        var blockedByRouteId = ResolveResourceOwnerRouteId(layout, resourceKind, resourceId, routeId);
        if (string.IsNullOrWhiteSpace(blockedByRouteId)
            || !_runtimeRegistry.HasWaitState(blockedByRouteId))
        {
            return null;
        }

        var otherWaitingResourceKey = GetTrackedWaitingResourceKey(blockedByRouteId);
        if (!TryParseWaitResourceKey(otherWaitingResourceKey ?? string.Empty, out var otherResourceKind, out var otherResourceId))
            return null;

        var otherBlockedByRouteId = ResolveResourceOwnerRouteId(layout, otherResourceKind, otherResourceId, blockedByRouteId);
        if (!string.Equals(otherBlockedByRouteId, routeId, StringComparison.OrdinalIgnoreCase))
            return null;

        var routeWaitSince = ResolveRouteWaitSinceUtc(routeId) ?? DateTime.UtcNow;
        var otherWaitSince = ResolveRouteWaitSinceUtc(blockedByRouteId) ?? DateTime.UtcNow;

        var winnerRouteId = routeWaitSince <= otherWaitSince
            ? routeId
            : blockedByRouteId;
        var loserRouteId = string.Equals(winnerRouteId, routeId, StringComparison.OrdinalIgnoreCase)
            ? blockedByRouteId
            : routeId;

        return new DeadlockCycleInfo(
            winnerRouteId,
            loserRouteId,
            string.Equals(winnerRouteId, routeId, StringComparison.OrdinalIgnoreCase) ? waitingResourceKey! : otherWaitingResourceKey!,
            string.Equals(loserRouteId, routeId, StringComparison.OrdinalIgnoreCase) ? waitingResourceKey! : otherWaitingResourceKey!,
            string.Equals(winnerRouteId, routeId, StringComparison.OrdinalIgnoreCase) ? blockedByRouteId : otherBlockedByRouteId!,
            string.Equals(loserRouteId, routeId, StringComparison.OrdinalIgnoreCase) ? blockedByRouteId : otherBlockedByRouteId!);
    }

    private bool EnterDeadlockYield(TrackLayout layout, string routeId, string winnerRouteId, string waitingResourceKey)
    {
        if (string.IsNullOrWhiteSpace(routeId) || string.IsNullOrWhiteSpace(winnerRouteId) || string.IsNullOrWhiteSpace(waitingResourceKey))
            return false;

        if (_deadlockYieldByRouteId.TryGetValue(routeId, out var existing)
            && string.Equals(existing.WinnerRouteId, winnerRouteId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.WaitingResourceKey, waitingResourceKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _deadlockYieldByRouteId[routeId] = new DeadlockYieldState(routeId, winnerRouteId, waitingResourceKey, DateTime.UtcNow);
        _traversalEngine.SetTraversalLifecycleState(routeId, RouteRuntimeLifecycleState.Yielding);

        var released = _callbacks.ReleasePendingSharedReservationsForYield(layout, routeId);
        var releasedSticky = ReleaseStickyGrantsForRoute(routeId);
        _callbacks.DiagnoseWaitState(routeId, waitingResourceKey, "yield", null, null, null, DiagnosticLevel.Warning);
        _callbacks.DiagnoseDeadlock(
            layout,
            routeId,
            winnerRouteId,
            waitingResourceKey,
            "ustúpenie",
            DiagnosticLevel.Warning,
            $", uvoľnené=[bloky:{released.ReleasedBlocks}, výhybky:{released.ReleasedTurnouts}, priority:{releasedSticky}]");
        return true;
    }

    private int ReleaseStickyGrantsForRoute(string routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return 0;

        lock (_waitArbiterSync)
        {
            var resourceKeys = _stickyWaitGrantsByResourceKey
                .Where(kv => string.Equals(kv.Value.WinnerRouteId, routeId, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var resourceKey in resourceKeys)
                _stickyWaitGrantsByResourceKey.Remove(resourceKey);

            return resourceKeys.Count;
        }
    }

    private void ExitDeadlockYield(TrackLayout layout, string routeId, string state, DiagnosticLevel level)
    {
        if (!_deadlockYieldByRouteId.Remove(routeId, out var yieldState))
            return;

        RestoreRouteRuntimeYieldToWaiting(routeId);
        _callbacks.DiagnoseDeadlock(layout, routeId, yieldState.WinnerRouteId, yieldState.WaitingResourceKey, state, level, null);
    }

    private void RestoreRouteRuntimeYieldToWaiting(string routeId)
    {
        var runtime = _runtimeRegistry.GetRuntime(routeId);
        if (runtime != null
            && runtime.LifecycleState == RouteRuntimeLifecycleState.Yielding)
        {
            _traversalEngine.SetTraversalLifecycleState(routeId, RouteRuntimeLifecycleState.Waiting);
        }
    }

    private bool ShouldKeepDeadlockYield(TrackLayout layout, DeadlockYieldState yieldState)
    {
        if (!_runtimeRegistry.IsRouteActive(yieldState.WinnerRouteId))
            return false;

        if (!TryParseWaitResourceKey(yieldState.WaitingResourceKey, out var resourceKind, out var resourceId))
            return false;

        var blockingRouteId = ResolveResourceOwnerRouteId(layout, resourceKind, resourceId, yieldState.RouteId);
        return string.Equals(blockingRouteId, yieldState.WinnerRouteId, StringComparison.OrdinalIgnoreCase);
    }
}









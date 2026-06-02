using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TrackFlow.Extensions;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Runtime;
using TrackFlow.Services.Dcc;
using TrackFlow.Services.Operation;
using TrackFlow.Services.Signal;
using TrackFlow.ViewModels.Operation;

namespace TrackFlow.Services.Runtime;

internal sealed class ReservationEngineCallbacks
{
    public required Func<string, string> ResolveTrainDisplayName { get; init; }
    public required Func<TrackLayout, string?, string> ResolveRouteDisplayName { get; init; }
    public required Func<TrackLayout, RouteDefinition, string?> ResolvePrimaryRouteLocoId { get; init; }
    public required Func<string, RouteDefinition, List<string>> GetActivationBlockOrder { get; init; }
    public required Func<TrackLayout, string, RouteDefinition, IEnumerable<string>> ResolveRouteRuntimeOwnedBlockIds { get; init; }
    public required Func<TrackLayout, string, string, bool> IsBlockUsedByAnotherActiveRoute { get; init; }
    public required Func<TrackLayout, string, string, bool> IsTurnoutStillRequiredByAnotherRoute { get; init; }
    public required Func<TrackLayout, string, string, string?> ResolveActiveRouteForSegment { get; init; }
    public required Func<TrackLayout, string, string?, string?> ResolveOwningRouteForBlock { get; init; }
    public required Func<TrackLayout, RouteDefinition, string, string, NavigationDirection> ResolveSegmentTravelDirection { get; init; }
    public required Func<TrackLayout, RouteDefinition, string, string, IReadOnlyCollection<string>> ResolveTraversedTurnoutIds { get; init; }
    public required Func<TrackLayout, string, string?, string?> TryGetStickyBlockingWinnerRouteIdForBlock { get; init; }
    public required Action<string?, string> ConsumeStickyWaitWinnerForBlock { get; init; }
    public required Action<TrackLayout, string> AssignStickyWaitWinnerForBlock { get; init; }
    public required Action<TrackLayout, string> AssignStickyWaitWinnerForTurnout { get; init; }
    public required Action<TrackLayout, string, string, string?, string?, string, DiagnosticLevel> DiagnoseReservationEngine { get; init; }
    public required Action<TrackLayout, string, string, string?, string?, string?, DiagnosticLevel> DiagnoseBlockRuntime { get; init; }
    public required Action<TrackLayout, string?, TurnoutState, string?, string?, string, DiagnosticLevel> DiagnoseTurnoutRuntime { get; init; }
    public required Action<TrackLayout, string?, string?, SignalElement?, string, DiagnosticLevel> DiagnoseSignalRuntime { get; init; }
    public required Action<TrackLayout> ApplyDynamicLockWindow { get; init; }
    public required Action MarkDirty { get; init; }
    public required Action RequestLayoutRefresh { get; init; }
}

internal sealed record ReserveNextBlockRequest(
    BlockElement? NextBlock,
    string LocoCode,
    bool OrientationForward,
    string? TrainName,
    TrackLayout Layout,
    string FromBlockId,
    NavigationDirection TravelDirection,
    bool SuppressFailureDiagnostics = false,
    bool SuppressSuccessRefresh = false,
    bool IgnoreProtectingSignalAspect = false);

internal sealed record ReserveInitialWindowRequest(
    TrackLayout Layout,
    RouteDefinition Route,
    string LocoCode,
    string SourceBlockId,
    string? TargetBlockIdHint,
    bool TravelForward,
    bool OrientationForward);

internal sealed record AdvanceReservationWindowRequest(
    TrackLayout Layout,
    RouteDefinition Route,
    string LocoCode,
    string CurrentBlockId,
    bool OrientationForward,
    string Source);

internal sealed record BoundaryEntryReservationRequest(
    TrackLayout Layout,
    RouteDefinition Route,
    BlockElement SourceBlock,
    BlockElement TargetBlock,
    Locomotive? Loco,
    string LocoCode,
    bool RequestLayoutRefresh = true);

internal sealed record TailClearReleaseRequest(
    TrackLayout Layout,
    RouteDefinition Route,
    BlockElement SourceBlock,
    BlockElement TargetBlock,
    IDccCentralClient? DccClient,
    CancellationToken CancellationToken);

internal sealed class ReservationEngine
{
    private readonly RuntimeStateRegistry _runtimeRegistry;
    private readonly IDictionary<string, string> _turnoutRuntimeReservations;
    private readonly ReservationEngineCallbacks _callbacks;

    public ReservationEngine(
        RuntimeStateRegistry runtimeRegistry,
        IDictionary<string, string> turnoutRuntimeReservations,
        ReservationEngineCallbacks callbacks)
    {
        _runtimeRegistry = runtimeRegistry;
        _turnoutRuntimeReservations = turnoutRuntimeReservations;
        _callbacks = callbacks;
    }

    public void SetReservationWindow(
        string routeId,
        IEnumerable<string> pathElementIds,
        IEnumerable<string> blockIds,
        int? leadSegmentIndex,
        bool keepPreviousSegmentActive)
        => _runtimeRegistry.SetReservationWindow(routeId, pathElementIds, blockIds, leadSegmentIndex, keepPreviousSegmentActive);

    public void ReserveInitialWindow(ReserveInitialWindowRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LocoCode) || string.IsNullOrWhiteSpace(request.SourceBlockId))
            return;

        var trainName = _callbacks.ResolveTrainDisplayName(request.LocoCode);

        var drivingOrder = _callbacks.GetActivationBlockOrder(request.Route.Id, request.Route);
        var nextBlockId = ResolveNextBlockIdInOrder(drivingOrder, request.SourceBlockId);
        if (string.IsNullOrWhiteSpace(nextBlockId))
            nextBlockId = request.TargetBlockIdHint;

        if (string.IsNullOrWhiteSpace(nextBlockId)
            || string.Equals(nextBlockId, request.SourceBlockId, StringComparison.OrdinalIgnoreCase))
        {
            ReleaseStaleShadowsCore(request.Layout, request.LocoCode, keepBlockId: null);
            return;
        }

        ReleaseStaleShadowsCore(request.Layout, request.LocoCode, keepBlockId: nextBlockId);

        var nextBlock = request.Layout.Elements.OfType<BlockElement>()
            .FirstOrDefault(b => string.Equals(b.Id, nextBlockId, StringComparison.OrdinalIgnoreCase));

        var travelDirection = _callbacks.ResolveSegmentTravelDirection(request.Layout, request.Route, request.SourceBlockId, nextBlockId);
        if (!TryReserveNextBlock(
                new ReserveNextBlockRequest(
                    nextBlock,
                    request.LocoCode,
                    request.OrientationForward,
                    trainName,
                    request.Layout,
                    request.SourceBlockId,
                    travelDirection),
                out var isCritical)
            && isCritical)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "Bezpečnosť",
                $"⛔ KRITICKÁ CHYBA: Inicializačná rezervácia bloku {OperationDisplayHelpers.ResolveBlockDisplayName(request.Layout, nextBlockId)} ZLYHALA z bezpečnostných dôvodov!",
                DiagnosticLevel.Critical);
        }
    }

    public void Advance(AdvanceReservationWindowRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LocoCode) || string.IsNullOrWhiteSpace(request.CurrentBlockId))
            return;

        var trainName = _callbacks.ResolveTrainDisplayName(request.LocoCode);
        var drivingOrder = _callbacks.GetActivationBlockOrder(request.Route.Id, request.Route);
        var nextBlockId = ResolveNextBlockIdInOrder(drivingOrder, request.CurrentBlockId);

        if (!TryBeginReservationAdvance(request.Layout, request.Route.Id, request.Source, request.CurrentBlockId, nextBlockId))
            return;

        try
        {
            _callbacks.DiagnoseReservationEngine(request.Layout, request.Route.Id, request.Source, request.CurrentBlockId, nextBlockId, "advance", DiagnosticLevel.Info);

            if (string.IsNullOrWhiteSpace(nextBlockId))
            {
                _callbacks.DiagnoseReservationEngine(request.Layout, request.Route.Id, request.Source, request.CurrentBlockId, nextBlockId, "skip", DiagnosticLevel.Info);
                ReleaseStaleShadowsCore(request.Layout, request.LocoCode, keepBlockId: null);
                return;
            }

            if (string.Equals(nextBlockId, request.CurrentBlockId, StringComparison.OrdinalIgnoreCase))
            {
                _callbacks.DiagnoseReservationEngine(request.Layout, request.Route.Id, request.Source, request.CurrentBlockId, nextBlockId, "skip", DiagnosticLevel.Info);
                return;
            }

            if (IsReservationBehindFrontier(request.Route.Id, request.CurrentBlockId, nextBlockId))
            {
                _callbacks.DiagnoseReservationEngine(request.Layout, request.Route.Id, request.Source, request.CurrentBlockId, nextBlockId, "deny-behind-frontier", DiagnosticLevel.Warning);
                return;
            }

            ReleaseStaleShadowsCore(request.Layout, request.LocoCode, keepBlockId: nextBlockId);

            var nextBlock = request.Layout.Elements.OfType<BlockElement>()
                .FirstOrDefault(b => string.Equals(b.Id, nextBlockId, StringComparison.OrdinalIgnoreCase));
            var travelDirection = _callbacks.ResolveSegmentTravelDirection(request.Layout, request.Route, request.CurrentBlockId, nextBlockId);

            if (!TryReserveNextBlock(
                    new ReserveNextBlockRequest(
                        nextBlock,
                        request.LocoCode,
                        request.OrientationForward,
                        trainName,
                        request.Layout,
                        request.CurrentBlockId,
                        travelDirection),
                    out var isCritical))
            {
                _callbacks.DiagnoseReservationEngine(request.Layout, request.Route.Id, request.Source, request.CurrentBlockId, nextBlockId, "deny", isCritical ? DiagnosticLevel.Critical : DiagnosticLevel.Info);
                if (isCritical)
                {
                    TrackFlowDoctorService.Instance.Diagnose(
                        "Bezpečnosť",
                        $"⛔ KRITICKÁ CHYBA: Advance rezervácia bloku {OperationDisplayHelpers.ResolveBlockDisplayName(request.Layout, nextBlockId)} ZLYHALA - návestidlo nie je na Voľno!",
                        DiagnosticLevel.Critical);
                }
            }
            else
            {
                // BEZPEČNOSŤ: po posune frontier-a vyčisti zaostávajúce shadow rezervácie
                // za vlakom v rámci traversal-u tejto cesty (idempotentné, rieši degradovaný
                // stav, ktorý by mohol pretrvať mimo loco-wide ReleaseStaleShadowsCore vyššie).
                ClearShadowsBehindFrontier(request.Layout, request.Route, request.LocoCode, request.CurrentBlockId);
            }
        }
        finally
        {
            _runtimeRegistry.EndReservationAdvance(request.Route.Id);
        }
    }

    public Task AdvanceAsync(AdvanceReservationWindowRequest request)
    {
        Advance(request);
        return Task.CompletedTask;
    }

    public void ApplyBoundaryEntry(BoundaryEntryReservationRequest request)
    {
        ValidateMovementPreCommitReservationOrThrow(request.Layout, request.Route.Id, request.TargetBlock, request.LocoCode);

        var targetAssignedForward = request.SourceBlock.AssignedLocoIsForward;
        var isReserved = string.Equals(request.TargetBlock.ReservedLocoId, request.LocoCode, StringComparison.OrdinalIgnoreCase);
        var isUnchanged = string.Equals(request.TargetBlock.AssignedLocoId, request.LocoCode, StringComparison.OrdinalIgnoreCase)
            && request.TargetBlock.IsOccupied
            && request.TargetBlock.AssignedLocoIsForward == targetAssignedForward
            && !request.TargetBlock.IsTailClearing
            && !request.TargetBlock.IsShadowSet
            && string.IsNullOrWhiteSpace(request.TargetBlock.ReservedLocoId)
            && !request.TargetBlock.IsDragOverActive
            && string.Equals(request.SourceBlock.AssignedLocoId, request.LocoCode, StringComparison.OrdinalIgnoreCase)
            && request.SourceBlock.IsOccupied
            && request.SourceBlock.IsTailClearing
            && !request.SourceBlock.IsDragOverActive;

        if (isUnchanged)
        {
            if (request.Loco != null)
            {
                request.Loco.IsPlacedOnTrack = true;
                request.Loco.AssignedBlockId = request.TargetBlock.Id;
            }

            return;
        }

        request.TargetBlock.AssignedLocoId = request.LocoCode;
        request.TargetBlock.IsOccupied = true;
        request.TargetBlock.AssignedLocoIsForward = targetAssignedForward;
        request.TargetBlock.IsTailClearing = false;
        ClearShadowReservation(request.TargetBlock);
        request.TargetBlock.IsDragOverActive = false;

        request.SourceBlock.AssignedLocoId = request.LocoCode;
        request.SourceBlock.IsOccupied = true;
        request.SourceBlock.IsDragOverActive = false;
        request.SourceBlock.IsTailClearing = true;

        TrackFlowDoctorService.Instance.Diagnose(
            "Senzor",
            $"Blok {OperationDisplayHelpers.BlockDisplayName(request.SourceBlock)} čaká na tail-clear (vlak vstúpil do {OperationDisplayHelpers.BlockDisplayName(request.TargetBlock)})",
            DiagnosticLevel.Info);

        var ownerRouteId = _callbacks.ResolveOwningRouteForBlock(request.Layout, request.TargetBlock.Id, null);
        _callbacks.DiagnoseBlockRuntime(
            request.Layout,
            request.TargetBlock.Id,
            "obsadený",
            request.LocoCode,
            ownerRouteId,
            ownerRouteId,
            isReserved ? DiagnosticLevel.Info : DiagnosticLevel.Warning);

        TrackFlowDoctorService.Instance.Diagnose(
            "Senzor",
            $"Blok {OperationDisplayHelpers.BlockDisplayName(request.TargetBlock)} OBSADENÝ (Vlak: {(!string.IsNullOrWhiteSpace(request.Loco?.DisplayName) ? request.Loco!.DisplayName : _callbacks.ResolveTrainDisplayName(request.LocoCode))})",
            isReserved ? DiagnosticLevel.Info : DiagnosticLevel.Warning);

        if (request.Loco != null)
        {
            request.Loco.IsPlacedOnTrack = true;
            request.Loco.AssignedBlockId = request.TargetBlock.Id;
        }

        // BEZPEČNOSŤ: po vstupe vlaku do nového bloku invalidovať akékoľvek
        // zaostávajúce shadow rezervácie ZA vlakom (bloky pred targetom v traversal-e
        // tej istej cesty, ktoré stále nesú IsShadowSet pre túto loko). Frontier sa
        // posunul; rezervácia je povolená iba pred vlakom.
        ClearShadowsBehindFrontier(request.Layout, request.Route, request.LocoCode, request.TargetBlock.Id);

        _callbacks.ApplyDynamicLockWindow(request.Layout);
        _callbacks.MarkDirty();
        if (request.RequestLayoutRefresh)
            _callbacks.RequestLayoutRefresh();
    }

    private void ClearShadowsBehindFrontier(TrackLayout layout, RouteDefinition route, string locoCode, string frontierBlockId)
    {
        if (string.IsNullOrWhiteSpace(locoCode) || string.IsNullOrWhiteSpace(frontierBlockId))
            return;

        var runtime = _runtimeRegistry.GetRuntime(route.Id);
        if (runtime == null || runtime.TraversalBlockIds.Count == 0)
            return;

        var frontierIndex = FindTraversalIndex(runtime.TraversalBlockIds, frontierBlockId);
        if (frontierIndex <= 0)
            return;

        var blocksById = layout.Elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < frontierIndex; i++)
        {
            var bid = runtime.TraversalBlockIds[i];
            if (!blocksById.TryGetValue(bid, out var block))
                continue;
            if (!block.IsShadowSet && string.IsNullOrWhiteSpace(block.ReservedLocoId))
                continue;
            // Neclearuj cudzie rezervácie (iný vlak/cesta).
            if (!string.IsNullOrWhiteSpace(block.ReservedLocoId)
                && !string.Equals(block.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase))
                continue;
            // Obsadený blok zachovaj – jeho stav rieši tail-clear.
            if (block.IsOccupied)
                continue;

            TrackFlowDoctorService.Instance.Diagnose(
                "Dispečer",
                $"[BEZPEČNOSŤ] uvoľňujem zaostávajúcu shadow rezerváciu ZA vlakom: blok=[{OperationDisplayHelpers.BlockDisplayName(block)}], vlak=[{locoCode}], cesta=[{_callbacks.ResolveRouteDisplayName(layout, route.Id)}]",
                DiagnosticLevel.Warning);
            ClearShadowReservation(block);
        }
    }

    public async Task ReleaseAsync(TailClearReleaseRequest request)
    {
        request.SourceBlock.AssignedLocoId = null;
        request.SourceBlock.IsOccupied = false;
        request.SourceBlock.AssignedLocoIsForward = true;
        request.SourceBlock.IsDragOverActive = false;
        request.SourceBlock.IsLocked = false;
        request.SourceBlock.IsTailClearing = false;

        _callbacks.DiagnoseReservationEngine(request.Layout, request.Route.Id, "tail-clear", request.SourceBlock.Id, request.TargetBlock.Id, "skip", DiagnosticLevel.Info);

        // BEZPEČNOSŤ: ak medzičasom rezerváciu na tomto zdieľanom bloku
        // získala iná aktívna/waiting cesta, nesmieme ju zmazať tail-clearom
        // našej cesty. Kontrola je route-first, nie iba loco-first: rovnaká
        // lokomotíva môže mať nadväznú trasu a jej čerstvá rezervácia nesmie
        // byť zničená starším tail-clearom.
        var routeLocoId = _callbacks.ResolvePrimaryRouteLocoId(request.Layout, request.Route);
        var currentReservedLocoId = request.SourceBlock.ReservedLocoId;
        var reservationOwnerRouteId = !string.IsNullOrWhiteSpace(currentReservedLocoId) || request.SourceBlock.IsShadowSet
            ? _callbacks.ResolveOwningRouteForBlock(request.Layout, request.SourceBlock.Id, null)
            : null;
        bool reservationOwnedByAnotherRoute =
            !string.IsNullOrWhiteSpace(reservationOwnerRouteId)
            && !string.Equals(reservationOwnerRouteId, request.Route.Id, StringComparison.OrdinalIgnoreCase);
        bool reservationOwnedByAnotherLoco =
            !string.IsNullOrWhiteSpace(currentReservedLocoId)
            && !string.IsNullOrWhiteSpace(routeLocoId)
            && !string.Equals(currentReservedLocoId, routeLocoId, StringComparison.OrdinalIgnoreCase);
        bool foreignReservation = reservationOwnedByAnotherRoute || reservationOwnedByAnotherLoco;

        if (foreignReservation)
        {
            Log.Information(
                "Tail-clear skip foreign reservation owner: route={Route} block={Block} reservedLoco={ReservedLoco} routeLoco={RouteLoco} reservationOwner={ReservationOwner}",
                _callbacks.ResolveRouteDisplayName(request.Layout, request.Route.Id),
                OperationDisplayHelpers.BlockDisplayName(request.SourceBlock),
                currentReservedLocoId ?? "-",
                routeLocoId ?? "-",
                _callbacks.ResolveRouteDisplayName(request.Layout, reservationOwnerRouteId));
        }
        else
        {
            ClearShadowReservation(request.SourceBlock);
        }
        _callbacks.AssignStickyWaitWinnerForBlock(request.Layout, request.SourceBlock.Id);
        _callbacks.DiagnoseBlockRuntime(
            request.Layout,
            request.SourceBlock.Id,
            "tail-clear",
            _callbacks.ResolvePrimaryRouteLocoId(request.Layout, request.Route),
            request.Route.Id,
            request.Route.Id,
            DiagnosticLevel.Info);

        var signalController = new SignalController(request.Layout.Elements, request.DccClient);
        var travelDirection = _callbacks.ResolveSegmentTravelDirection(request.Layout, request.Route, request.SourceBlock.Id, request.TargetBlock.Id);
        var signal = RouteSegmentSignalResolver.ResolveSegmentStartSignal(request.Layout, request.SourceBlock.Id, travelDirection, request.TargetBlock.Id);
        if (signal != null)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "Senzor",
                $"Senzor [{OperationDisplayHelpers.BlockDisplayName(request.SourceBlock)}] tail clear - zhadzujem návestidlo {OperationDisplayHelpers.SignalDisplayName(signal)} na STOJ",
                DiagnosticLevel.Info);
            _callbacks.DiagnoseSignalRuntime(request.Layout, request.Route.Id, _callbacks.ResolvePrimaryRouteLocoId(request.Layout, request.Route), signal, "stoj-po-tail-clear", DiagnosticLevel.Info);

            signal.Aspect = SignalAspect.Stop;
            if (request.DccClient != null)
                await signalController.SendCurrentStateToCentral(signal, request.DccClient, request.CancellationToken, reason: "tail-clear-force-stop");
        }

        ReleaseTraversedTurnouts(request.Layout, request.Route, request.SourceBlock.Id, request.TargetBlock.Id);
        _callbacks.ApplyDynamicLockWindow(request.Layout);
        _callbacks.MarkDirty();
        _callbacks.RequestLayoutRefresh();
    }

    public void ClearLocoReservations(TrackLayout layout, string locoCode)
    {
        if (string.IsNullOrWhiteSpace(locoCode))
            return;

        foreach (var block in layout.Elements.OfType<BlockElement>())
        {
            if (!string.Equals(block.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase))
                continue;

            ClearShadowReservation(block);
        }
    }

    public void ReleaseStaleShadows(TrackLayout layout, string locoCode, string? keepBlockId)
        => ReleaseStaleShadowsCore(layout, locoCode, keepBlockId);

    public void ClearShadowReservation(BlockElement block)
    {
        var reservedLocoId = block.ReservedLocoId;
        var hadReservation = block.IsShadowSet || !string.IsNullOrWhiteSpace(reservedLocoId);

        if (hadReservation)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "Dispečer",
                $"Vlak [{reservedLocoId ?? "Neznámy"}] uvoľnil blok [{OperationDisplayHelpers.BlockDisplayName(block)}]",
                DiagnosticLevel.Info);
        }

        block.ReservedLocoId = null;
        block.ReservedLocoIsForward = true;
        block.IsShadowSet = false;
    }

    public void ResetStuckShadowsBeforeActivation(TrackLayout layout)
    {
        var activeRouteBlockIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var routeRuntime in _runtimeRegistry.EnumerateActiveRuntimes())
        {
            var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeRuntime.RouteId, StringComparison.OrdinalIgnoreCase));
            if (route == null)
                continue;

            foreach (var blockId in _callbacks.ResolveRouteRuntimeOwnedBlockIds(layout, routeRuntime.RouteId, route))
                activeRouteBlockIds.Add(blockId);
        }

        foreach (var block in layout.Elements.OfType<BlockElement>())
        {
            if (block.IsOccupied)
                continue;
            if (!block.IsShadowSet && string.IsNullOrWhiteSpace(block.ReservedLocoId))
                continue;
            if (activeRouteBlockIds.Contains(block.Id))
                continue;

            ClearShadowReservation(block);
            block.IsLocked = false;
        }
    }

    public void ClearRouteReservations(TrackLayout layout, RouteDefinition route)
    {
        if (route.BlockIds.Count == 0)
            return;

        var routeLocoId = _callbacks.ResolvePrimaryRouteLocoId(layout, route);
        if (string.IsNullOrWhiteSpace(routeLocoId))
            return;

        var blockIds = _callbacks.ResolveRouteRuntimeOwnedBlockIds(layout, route.Id, route)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (blockIds.Count == 0)
            return;

        foreach (var block in layout.Elements.OfType<BlockElement>())
        {
            if (!blockIds.Contains(block.Id))
                continue;
            if (_callbacks.IsBlockUsedByAnotherActiveRoute(layout, route.Id, block.Id))
                continue;
            if (string.IsNullOrWhiteSpace(block.ReservedLocoId))
                continue;
            if (!string.Equals(block.ReservedLocoId, routeLocoId, StringComparison.OrdinalIgnoreCase))
                continue;

            ClearShadowReservation(block);
            _callbacks.AssignStickyWaitWinnerForBlock(layout, block.Id);
            _callbacks.DiagnoseBlockRuntime(layout, block.Id, "uvoľnený", routeLocoId, route.Id, route.Id, DiagnosticLevel.Info);
        }
    }

    public int CountRouteOwnedReservations(TrackLayout layout, RouteDefinition route)
    {
        var routeLocoId = _callbacks.ResolvePrimaryRouteLocoId(layout, route);
        if (string.IsNullOrWhiteSpace(routeLocoId))
            return 0;

        var blockIds = _callbacks.ResolveRouteRuntimeOwnedBlockIds(layout, route.Id, route)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return layout.Elements.OfType<BlockElement>()
            .Count(block => blockIds.Contains(block.Id)
                            && block.IsShadowSet
                            && string.Equals(block.ReservedLocoId, routeLocoId, StringComparison.OrdinalIgnoreCase));
    }

    public void ValidateMovementPreCommitReservationOrThrow(TrackLayout layout, string? routeId, BlockElement targetBlock, string locoCode)
    {
        var reservationValid = !string.IsNullOrWhiteSpace(locoCode)
            && targetBlock.IsShadowSet
            && string.Equals(targetBlock.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase);
        var owner = DescribeMovementReservationOwner(targetBlock);
        var routeName = _callbacks.ResolveRouteDisplayName(layout, routeId);
        var trainName = _callbacks.ResolveTrainDisplayName(locoCode);

        if (reservationValid)
            return;

        Log.Warning(
            "Movement pre-commit validation failed: target={Target} owner={Owner} route={Route} loco={Loco}",
            OperationDisplayHelpers.BlockDisplayName(targetBlock),
            owner,
            routeName,
            locoCode);

        throw new OperationViewModel.MovementCommitValidationException(targetBlock.Id, locoCode, owner);
    }

    public bool TryReserveNextBlock(ReserveNextBlockRequest request, out bool isCriticalFailure)
    {
        isCriticalFailure = false;
        if (request.NextBlock == null || string.IsNullOrWhiteSpace(request.LocoCode))
            return false;

        var requestingRouteId = _callbacks.ResolveActiveRouteForSegment(request.Layout, request.FromBlockId, request.NextBlock.Id);

        if (IsReservationBehindFrontier(requestingRouteId, request.FromBlockId, request.NextBlock.Id))
        {
            _callbacks.DiagnoseReservationEngine(
                request.Layout,
                requestingRouteId ?? string.Empty,
                "frontier-guard",
                request.FromBlockId,
                request.NextBlock.Id,
                "deny-behind-frontier",
                DiagnosticLevel.Warning);
            _callbacks.DiagnoseBlockRuntime(
                request.Layout,
                request.NextBlock.Id,
                "rezervácia-odmietnutá-za-vlakom",
                request.LocoCode,
                requestingRouteId,
                _callbacks.ResolveOwningRouteForBlock(request.Layout, request.NextBlock.Id, requestingRouteId),
                DiagnosticLevel.Warning);
            return false;
        }

        if (request.NextBlock.IsTailClearing)
        {
            if (!request.SuppressFailureDiagnostics)
            {
                TrackFlowDoctorService.Instance.Diagnose(
                    "Dispečer",
                    $"Vlak [{request.TrainName ?? request.LocoCode}] nedokázal rezervovať blok [{OperationDisplayHelpers.BlockDisplayName(request.NextBlock)}]",
                    DiagnosticLevel.Warning);
            }

            _callbacks.DiagnoseBlockRuntime(
                request.Layout,
                request.NextBlock.Id,
                "rezervácia-odmietnutá-tail-clear",
                request.LocoCode,
                requestingRouteId,
                _callbacks.ResolveOwningRouteForBlock(request.Layout, request.NextBlock.Id, requestingRouteId),
                DiagnosticLevel.Warning);
            return false;
        }

        if (request.NextBlock.IsShadowSet || request.NextBlock.IsOccupied)
        {
            if (string.Equals(request.NextBlock.ReservedLocoId, request.LocoCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!request.SuppressFailureDiagnostics)
            {
                TrackFlowDoctorService.Instance.Diagnose(
                    "Dispečer",
                    $"Vlak [{request.TrainName ?? request.LocoCode}] nedokázal rezervovať blok [{OperationDisplayHelpers.BlockDisplayName(request.NextBlock)}]",
                    DiagnosticLevel.Warning);
            }

            _callbacks.DiagnoseBlockRuntime(
                request.Layout,
                request.NextBlock.Id,
                "rezervácia-odmietnutá",
                request.LocoCode,
                requestingRouteId,
                _callbacks.ResolveOwningRouteForBlock(request.Layout, request.NextBlock.Id, requestingRouteId),
                DiagnosticLevel.Warning);
            return false;
        }

        var stickyWinnerRouteId = _callbacks.TryGetStickyBlockingWinnerRouteIdForBlock(request.Layout, request.NextBlock.Id, requestingRouteId);
        if (!string.IsNullOrWhiteSpace(stickyWinnerRouteId))
        {
            _callbacks.DiagnoseBlockRuntime(
                request.Layout,
                request.NextBlock.Id,
                "sticky-odmietnutá",
                request.LocoCode,
                requestingRouteId,
                stickyWinnerRouteId,
                DiagnosticLevel.Warning);
            return false;
        }

        SignalElement? protectingSignal = null;
        var fromBlock = request.Layout.Elements.OfType<BlockElement>()
            .FirstOrDefault(b => string.Equals(b.Id, request.FromBlockId, StringComparison.OrdinalIgnoreCase));
        if (fromBlock != null)
        {
            var assignedSignalId = fromBlock.GetSignalForDirection(request.TravelDirection);
            if (!string.IsNullOrWhiteSpace(assignedSignalId))
            {
                protectingSignal = request.Layout.Elements.OfType<SignalElement>()
                    .FirstOrDefault(s => string.Equals(s.Id, assignedSignalId, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!request.IgnoreProtectingSignalAspect && protectingSignal != null && protectingSignal.HasValidDccAddress())
        {
            var guardingSignal = protectingSignal;
            var isPermissive = guardingSignal.Aspect != SignalAspect.Stop;
            if (!isPermissive)
            {
                isCriticalFailure = true;
                if (!request.SuppressFailureDiagnostics)
                {
                    TrackFlowDoctorService.Instance.Diagnose(
                        "Dispečer",
                        $"Vlak [{request.TrainName ?? request.LocoCode}] nedokázal rezervovať blok [{OperationDisplayHelpers.BlockDisplayName(request.NextBlock)}]",
                        DiagnosticLevel.Warning);
                    TrackFlowDoctorService.Instance.Diagnose(
                        "Bezpečnosť",
                        $"⛔ STOP: Návestidlo {guardingSignal.Label} v smere jazdy je na Stoj!",
                        DiagnosticLevel.Critical);
                }

                _callbacks.DiagnoseBlockRuntime(
                    request.Layout,
                    request.NextBlock.Id,
                    "rezervácia-stop-gate",
                    request.LocoCode,
                    requestingRouteId,
                    _callbacks.ResolveOwningRouteForBlock(request.Layout, request.NextBlock.Id, requestingRouteId),
                    DiagnosticLevel.Warning);
                return false;
            }
        }

        request.NextBlock.ReservedLocoId = request.LocoCode;
        request.NextBlock.ReservedLocoIsForward = request.OrientationForward;
        request.NextBlock.IsShadowSet = true;
        _callbacks.ConsumeStickyWaitWinnerForBlock(requestingRouteId, request.NextBlock.Id);
        TrackFlowDoctorService.Instance.Diagnose(
            "Dispečer",
            $"Blok [{OperationDisplayHelpers.BlockDisplayName(request.NextBlock)}] rezervovaný [{request.TrainName ?? request.LocoCode}]",
            DiagnosticLevel.Info);
        TrackFlowDoctorService.Instance.Diagnose(
            "Senzor",
            $"Blok {OperationDisplayHelpers.BlockDisplayName(request.NextBlock)} REZERVOVANÝ (Vlak: {request.TrainName ?? "Neznámy"})",
            DiagnosticLevel.Info);
        _callbacks.DiagnoseBlockRuntime(request.Layout, request.NextBlock.Id, "rezervovaný", request.LocoCode, requestingRouteId, requestingRouteId, DiagnosticLevel.Success);
        if (!request.SuppressSuccessRefresh)
            _callbacks.RequestLayoutRefresh();
        return true;
    }

    private bool TryBeginReservationAdvance(TrackLayout layout, string routeId, string source, string? currentBlockId, string? nextBlockId)
    {
        var runtime = _runtimeRegistry.GetRuntime(routeId);
        if (runtime == null)
        {
            _callbacks.DiagnoseReservationEngine(layout, routeId, source, currentBlockId, nextBlockId, "deny", DiagnosticLevel.Warning);
            return false;
        }

        if (runtime.ReservationAdvanceInProgress)
        {
            _callbacks.DiagnoseReservationEngine(layout, routeId, source, currentBlockId, nextBlockId, "deny", DiagnosticLevel.Critical);
            return false;
        }

        if (!_runtimeRegistry.TryBeginReservationAdvance(routeId, currentBlockId, nextBlockId))
        {
            _callbacks.DiagnoseReservationEngine(layout, routeId, source, currentBlockId, nextBlockId, "skip-duplicate", DiagnosticLevel.Info);
            return false;
        }

        return true;
    }

    private bool IsReservationBehindFrontier(string? routeId, string fromBlockId, string nextBlockId)
    {
        if (string.IsNullOrWhiteSpace(routeId)
            || string.IsNullOrWhiteSpace(fromBlockId)
            || string.IsNullOrWhiteSpace(nextBlockId))
        {
            return false;
        }

        var runtime = _runtimeRegistry.GetRuntime(routeId);
        if (runtime == null || runtime.TraversalBlockIds.Count == 0)
            return false;

        var fromIndex = FindTraversalIndex(runtime.TraversalBlockIds, fromBlockId);
        var nextIndex = FindTraversalIndex(runtime.TraversalBlockIds, nextBlockId);
        if (fromIndex < 0 || nextIndex < 0)
            return false;

        var frontierIndex = Math.Clamp(runtime.CurrentTraversalIndex, 0, runtime.TraversalBlockIds.Count - 1);
        if (!string.IsNullOrWhiteSpace(runtime.CurrentBlockId))
        {
            var currentBlockIndex = FindTraversalIndex(runtime.TraversalBlockIds, runtime.CurrentBlockId);
            if (currentBlockIndex >= 0)
                frontierIndex = Math.Max(frontierIndex, currentBlockIndex);
        }

        if (runtime.ReservationWindow.LeadSegmentIndex is int leadIndex)
            frontierIndex = Math.Max(frontierIndex, Math.Clamp(leadIndex, 0, runtime.TraversalBlockIds.Count - 1));

        return fromIndex < frontierIndex || nextIndex <= frontierIndex;
    }

    private static int FindTraversalIndex(IReadOnlyList<string> traversalBlockIds, string blockId)
    {
        for (var i = 0; i < traversalBlockIds.Count; i++)
        {
            if (string.Equals(traversalBlockIds[i], blockId, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void ReleaseStaleShadowsCore(TrackLayout layout, string locoCode, string? keepBlockId)
    {
        if (string.IsNullOrWhiteSpace(locoCode))
            return;

        foreach (var block in layout.Elements.OfType<BlockElement>())
        {
            if (!string.Equals(block.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(keepBlockId)
                && string.Equals(block.Id, keepBlockId, StringComparison.OrdinalIgnoreCase))
                continue;

            ClearShadowReservation(block);
        }
    }

    private void ReleaseTraversedTurnouts(TrackLayout layout, RouteDefinition route, string fromBlockId, string toBlockId)
    {
        if (route.TurnoutSettings.Count == 0)
            return;

        var turnoutIdsToRelease = _callbacks.ResolveTraversedTurnoutIds(layout, route, fromBlockId, toBlockId);
        if (turnoutIdsToRelease.Count == 0)
            return;

        var turnoutById = layout.Elements.OfType<TurnoutElement>()
            .ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);

        foreach (var turnoutId in turnoutIdsToRelease)
        {
            if (!turnoutById.TryGetValue(turnoutId, out var targetTurnout))
                continue;

            if (_turnoutRuntimeReservations.TryGetValue(turnoutId, out var ownerRouteId)
                && !string.Equals(ownerRouteId, route.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _turnoutRuntimeReservations.Remove(turnoutId);
            _callbacks.AssignStickyWaitWinnerForTurnout(layout, turnoutId);
            _callbacks.DiagnoseTurnoutRuntime(layout, turnoutId, targetTurnout.State, route.Id, route.Id, "uvoľnená", DiagnosticLevel.Info);

            if (_callbacks.IsTurnoutStillRequiredByAnotherRoute(layout, route.Id, turnoutId))
                continue;

            targetTurnout.State = TurnoutState.Straight;
        }
    }

    private string DescribeMovementReservationOwner(BlockElement targetBlock)
    {
        if (targetBlock.IsShadowSet && !string.IsNullOrWhiteSpace(targetBlock.ReservedLocoId))
            return _callbacks.ResolveTrainDisplayName(targetBlock.ReservedLocoId);

        return "žiadna";
    }

    private static string? ResolveNextBlockIdInOrder(IReadOnlyList<string> drivingOrder, string currentBlockId)
    {
        if (drivingOrder == null || drivingOrder.Count == 0 || string.IsNullOrWhiteSpace(currentBlockId))
            return null;

        for (int i = 0; i < drivingOrder.Count - 1; i++)
        {
            if (string.Equals(drivingOrder[i], currentBlockId, StringComparison.OrdinalIgnoreCase))
                return drivingOrder[i + 1];
        }

        return null;
    }
}





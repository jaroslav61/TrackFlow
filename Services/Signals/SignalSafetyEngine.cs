using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Extensions;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Runtime;
using TrackFlow.Services.Dcc;
using TrackFlow.Services.Operation;
using TrackFlow.Services.Runtime;
using TrackFlow.Services.Signal;

namespace TrackFlow.Services.Signals;

internal sealed class SignalSafetyEngineCallbacks
{
    public required Func<ProjectSettingsData?> GetProjectSettings { get; init; }
    public required Func<TrackLayout, RouteDefinition, string?> ResolvePrimaryRouteLocoId { get; init; }
    public required Func<IDccCentralClient?, bool> ShouldSendDcc { get; init; }
    public required Action<TrackLayout, string?, string?, SignalElement?, string, DiagnosticLevel> DiagnoseSignalRuntime { get; init; }
}

internal sealed record UpdateTraversalSignalWindowRequest(
    TrackLayout Layout,
    RouteDefinition Route,
    IReadOnlyList<string> TraversalBlockIds,
    int LeadSegmentIndex,
    bool KeepPreviousSegmentActive,
    IDccCentralClient? DccClient,
    CancellationToken CancellationToken);

internal sealed class SignalSafetyEngine
{
    private readonly RuntimeStateRegistry _runtimeRegistry;
    private readonly TraversalEngine _traversalEngine;
    private readonly OperationRuntimeSafetyService _runtimeSafetyService;
    private readonly SignalSafetyEngineCallbacks _callbacks;

    public SignalSafetyEngine(
        RuntimeStateRegistry runtimeRegistry,
        ReservationEngine reservationEngine,
        TraversalEngine traversalEngine,
        OperationRuntimeSafetyService runtimeSafetyService,
        SignalSafetyEngineCallbacks callbacks)
    {
        _runtimeRegistry = runtimeRegistry;
        _traversalEngine = traversalEngine;
        _runtimeSafetyService = runtimeSafetyService;
        _callbacks = callbacks;

        ArgumentNullException.ThrowIfNull(reservationEngine);
    }

    public void NormalizeInvalidSignalsToStop(TrackLayout layout)
    {
        foreach (var signal in layout.Elements.OfType<SignalElement>())
        {
            if (!signal.HasValidDccAddress())
                signal.Aspect = SignalAspect.Stop;
        }
    }

    public void SetAllSignalsRed(TrackLayout layout)
    {
        foreach (var signal in layout.Elements.OfType<SignalElement>())
            signal.Aspect = SignalAspect.Stop;
    }

    public async Task<int> SetAllSignalsRedAndPushAsync(
        TrackLayout layout,
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string? syncId = null)
    {
        SetAllSignalsRed(layout);
        return await _runtimeSafetyService.SendAllSignalStatesAsync(layout.Elements, dccClient, ct, reason: "connect-all-red", syncId: syncId);
    }

    public async Task<bool> ForceSignalStopAsync(
        SignalElement signal,
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string reason = "runtime-stop")
    {
        var changed = signal.Aspect != SignalAspect.Stop;
        signal.Aspect = SignalAspect.Stop;
        await _runtimeSafetyService.SendCurrentSignalStateAsync(signal, dccClient, ct, reason: reason);
        return changed;
    }

    public async Task<int> ApplyReleasedBlockSignalStopsAsync(
        TrackLayout layout,
        IReadOnlyCollection<string> releasedIds,
        Action<BlockElement> cleanupReleasedBlock,
        IDccCentralClient? dccClient,
        CancellationToken ct,
        bool sendDcc)
    {
        if (releasedIds.Count == 0)
            return 0;

        var blocksById = layout.Elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);
        var changedSignals = new List<SignalElement>();

        // SYSTÉMOVÁ OCHRANA: zachované behavior-preserving; momentálne slúži len ako auditná mapa.
        _ = GetActiveRouteProtectedSignalIds(layout);

        foreach (var releasedId in releasedIds)
        {
            if (!blocksById.TryGetValue(releasedId, out var block))
                continue;

            foreach (var signal in layout.Elements.OfType<SignalElement>())
            {
                if (!string.Equals(signal.ProtectsBlockId, releasedId, StringComparison.OrdinalIgnoreCase))
                    continue;

                TrackFlowDoctorService.Instance.Diagnose(
                    "Senzor",
                    $"Senzor [{OperationDisplayHelpers.BlockDisplayName(block)}] uvoľnený - zhadzujem návestidlo {OperationDisplayHelpers.SignalDisplayName(signal)} na STOJ",
                    DiagnosticLevel.Info);

                if (signal.Aspect != SignalAspect.Stop)
                {
                    signal.Aspect = SignalAspect.Stop;
                    if (signal.HasValidDccAddress())
                        changedSignals.Add(signal);
                }
            }

            if (!layout.Elements.OfType<SignalElement>()
                .Any(s => string.Equals(s.ProtectsBlockId, releasedId, StringComparison.OrdinalIgnoreCase)))
            {
                TrackFlowDoctorService.Instance.Diagnose(
                    "Senzor",
                    $"Senzor [{OperationDisplayHelpers.BlockDisplayName(block)}] uvoľnený - žiadne chrániace návestidlo nie je definované",
                    DiagnosticLevel.Info);
            }

            cleanupReleasedBlock(block);
        }

        if (sendDcc && _callbacks.ShouldSendDcc(dccClient))
        {
            foreach (var signal in changedSignals)
                await _runtimeSafetyService.SendCurrentSignalStateAsync(signal, dccClient, ct, reason: "released-force-stop");
        }

        return releasedIds.Count + changedSignals.Count;
    }

    public async Task ApplyStopBeforeSegmentAsync(
        TrackLayout layout,
        RouteDefinition route,
        IReadOnlyList<string> traversalBlockIds,
        int segmentIndex,
        IDccCentralClient? dccClient,
        CancellationToken ct)
    {
        if (segmentIndex < 0 || segmentIndex >= traversalBlockIds.Count - 1)
            return;

        var signalController = new SignalController(layout.Elements, dccClient);

        var fromBlockId = traversalBlockIds[segmentIndex];
        var toBlockId = traversalBlockIds[segmentIndex + 1];
        var direction = RouteSegmentSignalResolver.ResolveSegmentTravelDirection(layout, route, fromBlockId, toBlockId);
        var stopSignal = RouteSegmentSignalResolver.ResolveSegmentStartSignal(layout, fromBlockId, direction, toBlockId);

        if (stopSignal != null && stopSignal.Aspect != SignalAspect.Stop)
        {
            stopSignal.Aspect = SignalAspect.Stop;
            _callbacks.DiagnoseSignalRuntime(layout, route.Id, _callbacks.ResolvePrimaryRouteLocoId(layout, route), stopSignal, "stoj-pred-segmentom", DiagnosticLevel.Info);
            if (stopSignal.HasValidDccAddress() && _callbacks.ShouldSendDcc(dccClient))
                await signalController.SendCurrentStateToCentral(stopSignal, dccClient, ct, reason: "wait-stop");
        }
    }

    public async Task UpdateTraversalSignalWindowAsync(UpdateTraversalSignalWindowRequest request)
    {
        var traversalBlockIds = request.TraversalBlockIds.Count > 0
            ? request.TraversalBlockIds
            : _traversalEngine.GetTraversalBlockOrder(request.Route.Id, request.Route);
        if (traversalBlockIds.Count < 2)
            return;

        var signalController = new SignalController(request.Layout.Elements, request.DccClient);

        bool TryResolveNextMainSignalAspectForLookAhead(
            int currentSegmentIndex,
            HashSet<int> resolvingSegments,
            out SignalAspect nextSignalAspect)
        {
            nextSignalAspect = SignalAspect.Stop;

            if (currentSegmentIndex < 0 || currentSegmentIndex >= traversalBlockIds.Count - 1)
                return false;

            var currentBlockId = traversalBlockIds[currentSegmentIndex];
            var nextBlockId = traversalBlockIds[currentSegmentIndex + 1];
            var currentDirection = RouteSegmentSignalResolver.ResolveSegmentTravelDirection(request.Layout, request.Route, currentBlockId, nextBlockId);
            var nextDirection = currentSegmentIndex + 2 < traversalBlockIds.Count
                ? RouteSegmentSignalResolver.ResolveSegmentTravelDirection(request.Layout, request.Route, nextBlockId, traversalBlockIds[currentSegmentIndex + 2])
                : currentDirection;

            var nextMainSignal = RouteSegmentSignalResolver.ResolveSegmentStartSignal(request.Layout, nextBlockId, nextDirection);
            if (nextMainSignal == null)
                return false;

            if (currentSegmentIndex + 2 < traversalBlockIds.Count
                && TryResolvePlannedFinalAspectForSegment(currentSegmentIndex + 1, resolvingSegments, out var plannedAspect))
            {
                nextSignalAspect = plannedAspect;
                return true;
            }

            nextSignalAspect = nextMainSignal.Aspect;
            return true;
        }

        bool TryResolvePlannedFinalAspectForSegment(
            int segmentIndex,
            HashSet<int> resolvingSegments,
            out SignalAspect finalAspect)
        {
            finalAspect = SignalAspect.Stop;

            if (segmentIndex < 0 || segmentIndex >= traversalBlockIds.Count - 1)
                return false;
            if (!resolvingSegments.Add(segmentIndex))
                return false;

            try
            {
                var fromBlockId = traversalBlockIds[segmentIndex];
                var direction = RouteSegmentSignalResolver.ResolveSegmentTravelDirection(request.Layout, request.Route, fromBlockId, traversalBlockIds[segmentIndex + 1]);
                var signal = RouteSegmentSignalResolver.ResolveSegmentStartSignal(request.Layout, fromBlockId, direction);
                if (signal == null)
                    return false;

                // BEZPEČNOSŤ: ak nasledujúci blok v traverzálnom okne je už
                // rezervovaný/obsadený cudzím vlakom, plánovaný aspekt musí
                // byť Stoj (inak by predošlé návestidlo dostalo NextSignalAspect=Voľno
                // a chybne by sa promotovalo na Voľno aj keď cesta nie je voľná).
                var planningRouteLocoId = _callbacks.ResolvePrimaryRouteLocoId(request.Layout, request.Route);
                var planningNextBlock = request.Layout.Elements.OfType<BlockElement>()
                    .FirstOrDefault(b => string.Equals(b.Id, traversalBlockIds[segmentIndex + 1], StringComparison.OrdinalIgnoreCase));
                if (planningNextBlock != null && !string.IsNullOrWhiteSpace(planningRouteLocoId))
                {
                    bool foreignReservedPlan =
                        !string.IsNullOrWhiteSpace(planningNextBlock.ReservedLocoId)
                        && !string.Equals(planningNextBlock.ReservedLocoId, planningRouteLocoId, StringComparison.OrdinalIgnoreCase);
                    bool foreignOccupiedPlan =
                        planningNextBlock.IsOccupied
                        && !string.IsNullOrWhiteSpace(planningNextBlock.AssignedLocoId)
                        && !string.Equals(planningNextBlock.AssignedLocoId, planningRouteLocoId, StringComparison.OrdinalIgnoreCase);
                    if (foreignReservedPlan || foreignOccupiedPlan)
                    {
                        finalAspect = SignalAspect.Stop;
                        return true;
                    }
                }

                var baseAspect = signalController.CalculateRouteAspect(request.Route);
                var isStartSegment = segmentIndex == 0;
                var isDiverged = isStartSegment
                              && (baseAspect == SignalAspect.SlowProceed
                                  || baseAspect == SignalAspect.SlowCaution);

                var nextExists = TryResolveNextMainSignalAspectForLookAhead(segmentIndex, resolvingSegments, out var nextAspect);
                var warnNextStop = nextExists && SignalAspectLogic.IsStopAspect(nextAspect);

                finalAspect = SignalAspectLogic.SynthesizeLookAheadAspect(
                    signal,
                    baseAspect,
                    isStartSegment,
                    isDiverged,
                    warnNextStop);
                return true;
            }
            finally
            {
                resolvingSegments.Remove(segmentIndex);
            }
        }

        async Task ApplyStopForSegmentAsync(int segmentIndex)
        {
            if (segmentIndex < 0 || segmentIndex >= traversalBlockIds.Count - 1)
                return;

            var fromBlockId = traversalBlockIds[segmentIndex];
            var direction = RouteSegmentSignalResolver.ResolveSegmentTravelDirection(request.Layout, request.Route, fromBlockId, traversalBlockIds[segmentIndex + 1]);
            var stopSignal = RouteSegmentSignalResolver.ResolveSegmentStartSignal(request.Layout, fromBlockId, direction);

            if (stopSignal != null && stopSignal.Aspect != SignalAspect.Stop)
            {
                stopSignal.Aspect = SignalAspect.Stop;
                _callbacks.DiagnoseSignalRuntime(
                    request.Layout,
                    request.Route.Id,
                    _callbacks.ResolvePrimaryRouteLocoId(request.Layout, request.Route),
                    stopSignal,
                    "stoj-po-prejazde",
                    DiagnosticLevel.Info);
                if (stopSignal.HasValidDccAddress() && _callbacks.ShouldSendDcc(request.DccClient))
                    await signalController.SendCurrentStateToCentral(stopSignal, request.DccClient, request.CancellationToken, reason: "traversal-stop");
            }
        }

        async Task ApplyPermissiveForSegmentAsync(int segmentIndex)
        {
            if (segmentIndex < 0 || segmentIndex >= traversalBlockIds.Count - 1)
                return;

            var fromBlockId = traversalBlockIds[segmentIndex];
            var fromBlock = request.Layout.Elements.OfType<BlockElement>()
                .FirstOrDefault(b => string.Equals(b.Id, fromBlockId, StringComparison.OrdinalIgnoreCase));
            if (fromBlock == null)
                return;

            var direction = RouteSegmentSignalResolver.ResolveSegmentTravelDirection(request.Layout, request.Route, fromBlockId, traversalBlockIds[segmentIndex + 1]);
            var permissiveSignal = RouteSegmentSignalResolver.ResolveSegmentStartSignal(request.Layout, fromBlockId, direction);

            if (permissiveSignal == null)
                return;

            // BEZPEČNOSŤ: ak nasledujúci blok v traverzálnom okne je momentálne
            // rezervovaný alebo obsadený CUDZÍM vlakom (inou aktívnou cestou),
            // toto návestidlo musí ostať na STOJ. Bez tejto kontroly by sa
            // base=Výstraha bez ďalšieho návestidla promotovalo na Voľno a
            // pustilo vlak do bloku, ktorý vlastní niekto iný.
            var routeLocoIdForSafety = _callbacks.ResolvePrimaryRouteLocoId(request.Layout, request.Route);
            var nextBlockId = traversalBlockIds[segmentIndex + 1];
            var nextBlock = request.Layout.Elements.OfType<BlockElement>()
                .FirstOrDefault(b => string.Equals(b.Id, nextBlockId, StringComparison.OrdinalIgnoreCase));
            if (nextBlock != null && !string.IsNullOrWhiteSpace(routeLocoIdForSafety))
            {
                bool foreignReserved =
                    !string.IsNullOrWhiteSpace(nextBlock.ReservedLocoId)
                    && !string.Equals(nextBlock.ReservedLocoId, routeLocoIdForSafety, StringComparison.OrdinalIgnoreCase);
                bool foreignOccupied =
                    nextBlock.IsOccupied
                    && !string.IsNullOrWhiteSpace(nextBlock.AssignedLocoId)
                    && !string.Equals(nextBlock.AssignedLocoId, routeLocoIdForSafety, StringComparison.OrdinalIgnoreCase);

                if (foreignReserved || foreignOccupied)
                {
                    TrackFlowDoctorService.Instance.Diagnose(
                        "\u004E\u00E1vestidlo",
                        $"Syntéza aspektu pre {OperationDisplayHelpers.SignalDisplayName(permissiveSignal)}: " +
                        $"vynucujem STOJ - nasledujúci blok [{OperationDisplayHelpers.BlockDisplayName(nextBlock)}] " +
                        $"vlastní iný vlak (rezervácia=[{nextBlock.ReservedLocoId ?? "-"}], obsadenie=[{nextBlock.AssignedLocoId ?? "-"}], cesta-vlak=[{routeLocoIdForSafety}])",
                        DiagnosticLevel.Info);

                    if (permissiveSignal.Aspect != SignalAspect.Stop)
                    {
                        permissiveSignal.Aspect = SignalAspect.Stop;
                        TrackFlowDoctorService.Instance.Diagnose(
                            "\u004E\u00E1vestidlo",
                            $"Nahadzujem návestidlo {OperationDisplayHelpers.SignalDisplayName(permissiveSignal)} na Stoj (cudzí vlastník ďalšieho bloku)",
                            DiagnosticLevel.Info);
                        if (permissiveSignal.HasValidDccAddress() && _callbacks.ShouldSendDcc(request.DccClient))
                            await signalController.SendCurrentStateToCentral(permissiveSignal, request.DccClient, request.CancellationToken, reason: "traversal-stop-foreign-owner");
                    }
                    return;
                }
            }

            var baseAspect = signalController.CalculateRouteAspect(request.Route);
            bool isStartSegment = segmentIndex == 0;
            bool isDiverged = isStartSegment
                              && (baseAspect == SignalAspect.SlowProceed
                                  || baseAspect == SignalAspect.SlowCaution);

            bool nextMainSignalExists = TryResolveNextMainSignalAspectForLookAhead(
                segmentIndex,
                new HashSet<int>(),
                out var nextSignalAspect);
            bool nextMainSignalWillStop = nextMainSignalExists && SignalAspectLogic.IsStopAspect(nextSignalAspect);

            bool warnNextStop = nextMainSignalExists && nextMainSignalWillStop;

            var finalAspect = SignalAspectLogic.SynthesizeLookAheadAspect(
                permissiveSignal,
                baseAspect,
                isStartSegment,
                isDiverged,
                warnNextStop);

            TrackFlowDoctorService.Instance.Diagnose(
                "\u004E\u00E1vestidlo",
                $"Syntéza aspektu pre {OperationDisplayHelpers.SignalDisplayName(permissiveSignal)}: " +
                $"baseAspect={baseAspect.ToSlovakName()}, " +
                $"NextSignalAspect={(nextMainSignalExists ? nextSignalAspect.ToSlovakName() : "žiadne")}, " +
                $"NextSignalStop={nextMainSignalWillStop}, " +
                $"Výsledok={finalAspect.ToSlovakName()}",
                DiagnosticLevel.Info);

            if (permissiveSignal.Aspect != finalAspect)
            {
                permissiveSignal.Aspect = finalAspect;
                TrackFlowDoctorService.Instance.Diagnose(
                    "\u004E\u00E1vestidlo",
                    $"nahadzujem návestidlo {OperationDisplayHelpers.SignalDisplayName(permissiveSignal)} na {finalAspect.ToSlovakName()}",
                    DiagnosticLevel.Info);
                if (permissiveSignal.HasValidDccAddress() && _callbacks.ShouldSendDcc(request.DccClient))
                    await signalController.SendCurrentStateToCentral(permissiveSignal, request.DccClient, request.CancellationToken, reason: "traversal-go");
            }
        }

        if (request.KeepPreviousSegmentActive)
            await ApplyPermissiveForSegmentAsync(request.LeadSegmentIndex - 1);
        else if (request.LeadSegmentIndex > 0)
            await ApplyStopForSegmentAsync(request.LeadSegmentIndex - 1);

        await ApplyPermissiveForSegmentAsync(request.LeadSegmentIndex);
    }

    public void SetSignalsRedRespectingActiveRoutes(TrackLayout layout, string ownerRouteId)
    {
        foreach (var signal in layout.Elements.OfType<SignalElement>())
        {
            if (IsSignalUsedByAnotherActiveRoute(layout, ownerRouteId, signal.Id))
                continue;

            signal.Aspect = SignalAspect.Stop;
        }
    }

    public void ApplyRouteSafetyFallback(TrackLayout layout, RouteDefinition route)
    {
        var fallbackAspect = ResolveRouteSafetyFallbackAspect(route);
        var signalsById = layout.Elements.OfType<SignalElement>()
            .ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);

        var signalIds = ResolveRouteSignalIds(layout, route);

        foreach (var signalId in signalIds)
        {
            if (IsSignalUsedByAnotherActiveRoute(layout, route.Id, signalId))
                continue;

            if (signalsById.TryGetValue(signalId, out var signal) && signal.HasValidDccAddress())
                signal.Aspect = fallbackAspect;
        }
    }

    public HashSet<string> GetActiveRouteProtectedSignalIds(TrackLayout layout)
    {
        var protectedSignalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var routeId in _runtimeRegistry.ActiveRouteIds)
        {
            var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
            if (route == null)
                continue;

            foreach (var signalId in route.RouteSignalIds)
            {
                if (!string.IsNullOrWhiteSpace(signalId))
                    protectedSignalIds.Add(signalId);
            }
        }

        return protectedSignalIds;
    }

    private HashSet<string> ResolveRouteSignalIds(TrackLayout layout, RouteDefinition route)
    {
        var signalsById = layout.Elements.OfType<SignalElement>()
            .ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
        var signalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var signalId in route.RouteSignalIds)
        {
            if (!string.IsNullOrWhiteSpace(signalId))
                signalIds.Add(signalId);
        }

        if (!string.IsNullOrWhiteSpace(route.FromBlockId))
        {
            var fromBlock = layout.Elements.OfType<BlockElement>()
                .FirstOrDefault(b => string.Equals(b.Id, route.FromBlockId, StringComparison.OrdinalIgnoreCase));
            if (fromBlock != null)
            {
                var normalizedDirection = RouteDirection.NormalizeOrDefault(route.StartNavigationDirection, RouteDirection.Right,
                    $"Route[{route.Id}].{nameof(RouteDefinition.StartNavigationDirection)}");
                var direction = RouteSegmentSignalResolver.MapRouteDirectionToNavigationDirection(normalizedDirection);
                var signalId = fromBlock.GetSignalForDirection(direction);
                if (!string.IsNullOrWhiteSpace(signalId)
                    && signalsById.ContainsKey(signalId))
                {
                    signalIds.Add(signalId);
                }
            }
        }

        var routeTurnouts = route.TurnoutSettings
            .ToDictionary(t => t.TurnoutId, t => t.RequiredState, StringComparer.OrdinalIgnoreCase);
        var blocksById = layout.Elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);
        var foundRoutes = new RoutePathfinder(layout).FindAllRoutes();
        for (int i = 0; i < route.BlockIds.Count - 1; i++)
        {
            var fromBlockId = route.BlockIds[i];
            var toBlockId = route.BlockIds[i + 1];
            if (string.IsNullOrWhiteSpace(fromBlockId) || string.IsNullOrWhiteSpace(toBlockId))
                continue;
            if (!blocksById.TryGetValue(fromBlockId, out var fromBlock))
                continue;

            var edge = foundRoutes.FirstOrDefault(f =>
                string.Equals(f.FromBlockId, fromBlockId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.ToBlockId, toBlockId, StringComparison.OrdinalIgnoreCase)
                && f.TurnoutStates.All(kv => routeTurnouts.TryGetValue(kv.Key, out var state) && state == kv.Value));

            if (edge == null)
                continue;

            var direction = RouteSegmentSignalResolver.ResolveNavigationDirectionFromBlockPort(fromBlock, edge.FromBlockExitPort);
            var signalId = fromBlock.GetSignalForDirection(direction);
            if (!string.IsNullOrWhiteSpace(signalId)
                && signalsById.ContainsKey(signalId))
            {
                signalIds.Add(signalId);
            }
        }

        return signalIds;
    }

    private bool IsSignalUsedByAnotherActiveRoute(TrackLayout layout, string ownerRouteId, string signalId)
    {
        if (string.IsNullOrWhiteSpace(signalId))
            return false;

        foreach (var routeId in _runtimeRegistry.ActiveRouteIds)
        {
            if (string.Equals(routeId, ownerRouteId, StringComparison.OrdinalIgnoreCase))
                continue;

            var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
            if (route == null)
                continue;

            if (ResolveRouteSignalIds(layout, route).Contains(signalId))
                return true;
        }

        return false;
    }

    private static SignalAspect ResolveRouteSafetyFallbackAspect(RouteDefinition route)
        => SignalAspect.Stop;
}




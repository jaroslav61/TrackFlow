using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Models.Layout;
using TrackFlow.Services.Dcc;

namespace TrackFlow.Services;

/// <summary>
/// Runtime orchestration pre safety stav v operation režime.
/// View vrstva iba renderuje stav, bezpečnostná logika a refresh signálov žije tu.
/// </summary>
public sealed class OperationRuntimeSafetyService
{
    private readonly CollisionDetectionService _collisionDetectionService = new();
    private readonly SignalController _signalController = new();

    public CollisionCheckResult EvaluateBlockEntry(
        IEnumerable<LayoutElement> layoutElements,
        string targetBlockId,
        string locoCode,
        int safetyDistanceBlocks = 1)
    {
        return _collisionDetectionService.EvaluateEntry(layoutElements, targetBlockId, locoCode, safetyDistanceBlocks);
    }

    public CollisionCheckResult EvaluateBlockEntry(
        TrackLayout layout,
        string targetBlockId,
        string locoCode,
        RouteDefinition? candidateRoute,
        int safetyDistanceBlocks = 1)
    {
        return _collisionDetectionService.EvaluateEntry(layout, targetBlockId, locoCode, candidateRoute, safetyDistanceBlocks);
    }

    public int RefreshSignals(
        IEnumerable<LayoutElement> layoutElements,
        IReadOnlyCollection<string>? activeRouteIds = null,
        IEnumerable<RouteDefinition>? allRoutes = null)
    {
        if (layoutElements == null) throw new ArgumentNullException(nameof(layoutElements));
        return _signalController.RefreshAspects(layoutElements, activeRouteIds, allRoutes).Count;
    }

    public Task<int> RefreshSignalsAsync(
        IEnumerable<LayoutElement> layoutElements,
        IDccCentralClient? dccClient,
        IReadOnlyCollection<string>? activeRouteIds = null,
        IEnumerable<RouteDefinition>? allRoutes = null,
        CancellationToken ct = default,
        string reason = "refresh",
        string? syncId = null)
    {
        if (layoutElements == null) throw new ArgumentNullException(nameof(layoutElements));
        return _signalController.RefreshAllAsync(layoutElements, dccClient, activeRouteIds, allRoutes, ct, reason, syncId);
    }

    public Task<bool> SendCurrentSignalStateAsync(
        SignalElement signal,
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string reason = "runtime",
        string? syncId = null)
    {
        if (signal == null) throw new ArgumentNullException(nameof(signal));
        return _signalController.SendCurrentStateToCentral(signal, dccClient, ct, reason, syncId);
    }

    public Task<int> SendAllSignalStatesAsync(
        IEnumerable<LayoutElement> layoutElements,
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string reason = "force",
        string? syncId = null)
    {
        if (layoutElements == null) throw new ArgumentNullException(nameof(layoutElements));
        return _signalController.SendAllCurrentStatesToCentralAsync(layoutElements, dccClient, ct, reason, syncId);
    }
}


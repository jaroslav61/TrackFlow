using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Text;
using Avalonia.Threading;
using Serilog;
using TrackFlow.Runtime;
using TrackFlow.Extensions;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.Services.Dcc;
using TrackFlow.Services.Operation;
using static TrackFlow.Services.Operation.RouteDirectionAnalyzer;
using TrackFlow.Services.Signal;
using TrackFlow.Services.Signals;
using TrackFlow.Services.Simulation;
using TrackFlow.Services.UI;
using TrackFlow.Services.Runtime;

namespace TrackFlow.ViewModels.Operation;

public partial class OperationViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    private readonly CancellationTokenSource _globalCts = new();
    private CancellationTokenSource _panicStopCts = new();

    /// <summary>
    /// Interná výnimka používaná na okamžité ukončenie simulačného segmentu pri porušení
    /// bezpečnostných invariantov (STOJ/ČERVENÁ alebo chýbajúca rezervácia).
    /// </summary>
    private sealed class SimulationSafetyException : Exception
    {
        public string SafetyReason { get; }

        public SimulationSafetyException(string safetyReason, string message)
            : base(message)
        {
            SafetyReason = safetyReason;
        }
    }

    private sealed class ActiveSimulationContext
    {
        public LocomotiveSimulationEngine? Engine { get; set; }
        public string? TargetBlockId { get; set; }
        public double PreEntryDistanceMm { get; set; }
        public bool BoundaryEntryTriggered { get; set; }
    }
    
    private const int MovementHeartbeatMs = 100;
    private const double DisplayRampKmhPerSecond = 10.0;
    private const double SimulationFallbackBrakingRatio = 0.60;
    private const double SimulationFallbackStopRatio = 0.90;
    private const double SimulationStopSnapMm = 20.0;

    private readonly SettingsManager _settings;
    private readonly RouteActivationService _routeActivationService = new();
    private readonly OperationRuntimeSafetyService _runtimeSafetyService = new();
    private readonly RuntimeStateRegistry _runtimeRegistry = new();
    private readonly TraversalEngine _traversalEngine;
    private readonly ReservationEngine _reservationEngine;
    private readonly SignalSafetyEngine _signalSafetyEngine;
    private readonly TraversalWaitCoordinator _waitCoordinator;
    private readonly ActiveRouteVisualScopeResolver _activeRouteVisualScopeResolver;
    private readonly HashSet<string> _lastOccupiedBlockIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _turnoutRuntimeReservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastTraversalSignalSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastTraversalWindowSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lastTraversalWindowLeadIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _lastTraversalWindowKeepPrevious = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastLayoutRefreshSnapshot;
    private bool _warnedAboutLiveFeedbackWhileSimulationMode;

    private enum ActiveRouteRuntimeState
    {
        Active,
        Waiting,
        Yielding,
        Completed,
        Failed,
        EmergencyStopped
    }

    private sealed class ActiveRouteRuntimeInfo
    {
        public string RouteId { get; init; } = string.Empty;

        public ActiveRouteRuntimeState State { get; set; }
            = ActiveRouteRuntimeState.Active;

        public int SegmentIndex { get; set; }

        public string? CurrentBlockId { get; set; }

        public string? WaitingBlockId { get; set; }

        public string? WaitingReason { get; set; }

        public DateTime? WaitingSinceUtc { get; set; }

        public bool ReservationAdvanceInProgress { get; set; }

        public string? LastAdvanceCurrentBlockId { get; set; }

        public string? LastAdvanceNextBlockId { get; set; }
    }

    internal sealed class MovementCommitValidationException : Exception
    {
        public MovementCommitValidationException(string targetBlockId, string locoCode, string owner)
            : base($"movement-commit-blocked: target={targetBlockId}, loco={locoCode}, owner={owner}")
        {
            TargetBlockId = targetBlockId;
            LocoCode = locoCode;
            Owner = owner;
        }

        public string TargetBlockId { get; }
        public string LocoCode { get; }
        public string Owner { get; }
    }

    private static readonly TimeSpan DefaultWaitMaxDuration = TimeSpan.FromMinutes(2);
    private readonly int? _transientRouteMessageTtlOverrideMs;
    private readonly Func<int, CancellationToken, Task> _movementDelayAsync;
    private CancellationTokenSource? _routeMessageCts;
    
    // FÁZA 3: Real-time Sync & Safety - tracking aktívneho simulation enginu pre real-time korekciu
    private readonly Dictionary<string, ActiveSimulationContext> _activeSimulations = new(StringComparer.OrdinalIgnoreCase);

    private enum RouteMessageTtlType
    {
        None,
        Success,
        Info,
        Warning
    }
    
    /// <summary>Prístup ku SettingsManager pre View (potrebný pre CabAssignments).</summary>
    public SettingsManager Settings => _settings;

    /// <summary>
    /// Runtime Locomotive objekty s vagónmi.
    /// Zdieľané so SmartStripsViewModel pre synchronizáciu stavu.
    /// </summary>
    public ObservableCollection<Locomotive> Locomotives { get; private set; }
    
    /// <summary>Elementy layoutu pre zobrazenie v Prevádzke.</summary>
    public ObservableCollection<LayoutElement> LayoutElements { get; } = new();

    [ObservableProperty]
    private Locomotive? selectedLoco;

    [ObservableProperty]
    private string routeActivationMessage = string.Empty;
    
    /// <summary>
    /// OPERATION MODE: Simulation (trenažér) vs Live (ostrá prevádzka).
    /// true = Simulator (bez DCC centrály, simulované senzory)
    /// false = Live (reálna centrála, reálne senzory)
    /// </summary>
    private bool _isSimulationMode = true;
    
    public bool IsSimulationMode
    {
        get => _isSimulationMode;
        set
        {
            if (SetProperty(ref _isSimulationMode, value))
            {
                OnIsSimulationModeChanged(value);
            }
        }
    }

    public string RouteActivationMessageText => TranslateRouteActivationMessage(RouteActivationMessage);
    public bool HasRouteActivationMessage => !string.IsNullOrWhiteSpace(RouteActivationMessageText);

    public bool HasProject => _settings.CurrentProject != null;
    public IReadOnlyCollection<string> ActiveRouteIds => _runtimeRegistry.ActiveRouteIds;

    public bool IsElementOnActiveRoutePath(string elementId)
        => _activeRouteVisualScopeResolver.IsElementInActiveRouteVisualScope(_settings.CurrentProject?.Layout, _runtimeRegistry, elementId);

    public bool IsRouteUiActivationEnabled(RouteElement? routeElement, out string reason, bool emitDiagnostic = false)
    {
        var layout = _settings.CurrentProject?.Layout;
        RouteDefinition? preferred = null;

        if (routeElement == null)
            return FinishRouteUiActivationCheck(layout, routeElement, preferred, false, "route-marker-null", emitDiagnostic, out reason);

        if (layout == null)
            return FinishRouteUiActivationCheck(layout, routeElement, preferred, false, "projekt-neotvorený", emitDiagnostic, out reason);

        if (string.IsNullOrWhiteSpace(routeElement.SelectedRouteDefinitionId))
            return FinishRouteUiActivationCheck(layout, routeElement, preferred, false, "marker-bez-vybratej-cesty", emitDiagnostic, out reason);

        preferred = layout.Routes.FirstOrDefault(r =>
            string.Equals(r.Id, routeElement.SelectedRouteDefinitionId, StringComparison.OrdinalIgnoreCase));
        if (preferred == null)
            return FinishRouteUiActivationCheck(layout, routeElement, preferred, false, "definícia-cesty-nenájdená", emitDiagnostic, out reason);

        if (!preferred.IsEnabled)
            return FinishRouteUiActivationCheck(layout, routeElement, preferred, false, "cesta-disabled", emitDiagnostic, out reason);

        string from = !string.IsNullOrWhiteSpace(preferred.FromBlockId)
            ? preferred.FromBlockId
            : preferred.BlockIds.FirstOrDefault() ?? string.Empty;
        string to = !string.IsNullOrWhiteSpace(preferred.ToBlockId)
            ? preferred.ToBlockId
            : preferred.BlockIds.LastOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return FinishRouteUiActivationCheck(layout, routeElement, preferred, false, "cesta-zle-nakonfigurovaná", emitDiagnostic, out reason);

        var blocks = layout.Elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        if (!blocks.TryGetValue(from, out var fromBlock) || !blocks.TryGetValue(to, out var toBlock))
            return FinishRouteUiActivationCheck(layout, routeElement, preferred, false, "koncový-blok-neexistuje", emitDiagnostic, out reason);

        var selectedCode = SelectedLoco?.Code;
        var hasEndpointLoco = !string.IsNullOrWhiteSpace(fromBlock.AssignedLocoId)
                              || !string.IsNullOrWhiteSpace(toBlock.AssignedLocoId)
                              || (!string.IsNullOrWhiteSpace(selectedCode)
                                  && (string.Equals(fromBlock.AssignedLocoId, selectedCode, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(toBlock.AssignedLocoId, selectedCode, StringComparison.OrdinalIgnoreCase)));

        if (!hasEndpointLoco)
            return FinishRouteUiActivationCheck(layout, routeElement, preferred, false, "na-koncoch-cesty-nie-je-vlak", emitDiagnostic, out reason);

        return FinishRouteUiActivationCheck(layout, routeElement, preferred, true, _runtimeRegistry.ActiveRouteCount > 0
            ? $"povolené-počas-{_runtimeRegistry.ActiveRouteCount}-aktívnych-ciest"
            : "povolené", emitDiagnostic, out reason);
    }

    private bool FinishRouteUiActivationCheck(
        TrackLayout? layout,
        RouteElement? routeElement,
        RouteDefinition? route,
        bool enabled,
        string finalReason,
        bool emitDiagnostic,
        out string reason)
    {
        reason = finalReason;

        return enabled;
    }
    
    /// <summary>Event ktorý sa zavolá keď sa má schéma prekresliť (View odpočúva).</summary>
    public event System.Action? LayoutRefreshRequested;

    partial void OnRouteActivationMessageChanged(string value)
    {
        _ = value;
        OnPropertyChanged(nameof(RouteActivationMessageText));
        OnPropertyChanged(nameof(HasRouteActivationMessage));
    }

    /// <summary>
    /// Konštruktor s injekciou zdieľanej Locomotives kolekcie zo SmartStripsViewModel.
    /// </summary>
    public OperationViewModel(
        SettingsManager settingsManager,
        ObservableCollection<Locomotive> sharedLocomotives,
        int? transientRouteMessageTtlMs = null,
        Func<int, CancellationToken, Task>? movementDelayAsync = null,
        TimeSpan? waitMaxDuration = null)
    {
        _settings = settingsManager;
        Locomotives = sharedLocomotives;
        _transientRouteMessageTtlOverrideMs = transientRouteMessageTtlMs;
        _movementDelayAsync = movementDelayAsync ?? Task.Delay;
        var effectiveWaitMaxDuration = waitMaxDuration ?? DefaultWaitMaxDuration;
        _traversalEngine = new TraversalEngine(
            _runtimeRegistry,
            new TraversalEngineCallbacks
            {
                ResolveSegmentPathIds = ResolveSegmentPathIds
            });
        _activeRouteVisualScopeResolver = new ActiveRouteVisualScopeResolver(ResolveSegmentPathIds);
        _reservationEngine = new ReservationEngine(
            _runtimeRegistry,
            _turnoutRuntimeReservations,
            new ReservationEngineCallbacks
            {
                ResolveTrainDisplayName = ResolveTrainDisplayName,
                ResolveRouteDisplayName = ResolveRouteDisplayName,
                ResolvePrimaryRouteLocoId = ResolvePrimaryRouteLocoId,
                GetActivationBlockOrder = _traversalEngine.GetTraversalBlockOrder,
                ResolveRouteRuntimeOwnedBlockIds = ResolveRouteRuntimeOwnedBlockIds,
                IsBlockUsedByAnotherActiveRoute = IsBlockUsedByAnotherActiveRoute,
                IsTurnoutStillRequiredByAnotherRoute = IsTurnoutStillRequiredByAnotherRoute,
                ResolveActiveRouteForSegment = _traversalEngine.ResolveActiveRouteForSegment,
                ResolveOwningRouteForBlock = ResolveOwningRouteForBlock,
                ResolveSegmentTravelDirection = RouteSegmentSignalResolver.ResolveSegmentTravelDirection,
                ResolveTraversedTurnoutIds = ResolveTraversedTurnoutIds,
                TryGetStickyBlockingWinnerRouteIdForBlock = TryGetStickyBlockingWinnerRouteIdForBlock,
                ConsumeStickyWaitWinnerForBlock = ConsumeStickyWaitWinnerForBlock,
                AssignStickyWaitWinnerForBlock = AssignStickyWaitWinnerForBlock,
                AssignStickyWaitWinnerForTurnout = AssignStickyWaitWinnerForTurnout,
                DiagnoseReservationEngine = DiagnoseReservationEngine,
                DiagnoseBlockRuntime = DiagnoseBlockRuntime,
                DiagnoseTurnoutRuntime = DiagnoseTurnoutRuntime,
                DiagnoseSignalRuntime = DiagnoseSignalRuntime,
                ApplyDynamicLockWindow = ApplyDynamicLockWindow,
                MarkDirty = MarkDirty,
                RequestLayoutRefresh = () => RequestLayoutRefreshIfChanged(_settings.CurrentProject?.Layout, "reservation-engine")
            });
        _signalSafetyEngine = new SignalSafetyEngine(
            _runtimeRegistry,
            _reservationEngine,
            _traversalEngine,
            _runtimeSafetyService,
            new SignalSafetyEngineCallbacks
            {
                GetProjectSettings = () => _settings.CurrentProject?.Settings,
                ResolvePrimaryRouteLocoId = ResolvePrimaryRouteLocoId,
                ShouldSendDcc = ShouldSendDcc,
                DiagnoseSignalRuntime = DiagnoseSignalRuntime
            });
        _waitCoordinator = new TraversalWaitCoordinator(
            _runtimeRegistry,
            _reservationEngine,
            _traversalEngine,
            _signalSafetyEngine,
            new TraversalWaitCoordinatorCallbacks
            {
                ResolveTrainDisplayName = ResolveTrainDisplayName,
                ResolveRouteDisplayName = ResolveRouteDisplayName,
                ResolveOwningRouteForBlock = ResolveOwningRouteForBlock,
                ResolveTurnoutOwnerRouteId = ResolveTurnoutOwnerRouteId,
                ResolveConflictingTurnoutId = ResolveConflictingTurnoutId,
                ResolveInvalidRouteOutcome = ResolveTraversalInvalidRouteOutcome,
                IsTraversalLocoStillValid = IsTraversalLocoStillValid,
                StopLocomotiveDisplayAsync = StopLocomotiveDisplayAsync,
                TryEnsureTurnoutsForSegmentAsync = TryEnsureTurnoutsForSegmentAsync,
                MovementDelayAsync = _movementDelayAsync,
                ReleasePendingSharedReservationsForYield = ReleasePendingSharedReservationsForYield,
                RequestLayoutRefresh = () => RequestLayoutRefreshIfChanged(_settings.CurrentProject?.Layout, "wait-coordinator"),
                DiagnoseWaitState = DiagnoseWaitState,
                DiagnoseWaitRetry = DiagnoseWaitRetry,
                DiagnoseArbiter = DiagnoseArbiter,
                DiagnoseDeadlock = DiagnoseDeadlock,
                DiagnoseDuplicateOrchestration = DiagnoseDuplicateWaitOrchestration,
                DiagnoseSignalRuntime = DiagnoseSignalRuntime
            },
            effectiveWaitMaxDuration);
        RefreshFromProject();
    }

    public void RefreshFromProject()
    {
        SelectedLoco = Locomotives.FirstOrDefault();
        SetRouteActivationMessage(string.Empty, autoHide: false);
        _runtimeRegistry.Clear();
        _activeRouteVisualScopeResolver.Clear();
        _lastTraversalSignalSnapshots.Clear();
        _lastTraversalWindowSnapshots.Clear();
        _lastTraversalWindowLeadIndex.Clear();
        _lastTraversalWindowKeepPrevious.Clear();
        _lastOccupiedBlockIds.Clear();
        _turnoutRuntimeReservations.Clear();
        ClearWaitArbiterState();
        OnPropertyChanged(nameof(ActiveRouteIds));

        OnPropertyChanged(nameof(HasProject));

        // Načítať aj layout elementy
        RefreshLayoutFromProject();
    }

    private static bool IsSoftRuntimeActivationConflict(string reason)
        => string.Equals(reason, "target-block-locked", StringComparison.OrdinalIgnoreCase)
           || string.Equals(reason, "target-block-reserved", StringComparison.OrdinalIgnoreCase)
           || string.Equals(reason, "konflikt-vyhybky", StringComparison.OrdinalIgnoreCase);

    private void ReleaseTurnoutReservationsForRoute(string routeId, TrackLayout? layout = null)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return;

        foreach (var turnoutId in _turnoutRuntimeReservations
                     .Where(kv => string.Equals(kv.Value, routeId, StringComparison.OrdinalIgnoreCase))
                     .Select(kv => kv.Key)
                     .ToList())
        {
            _turnoutRuntimeReservations.Remove(turnoutId);
            if (layout != null)
                AssignStickyWaitWinnerForTurnout(layout, turnoutId);
        }
    }

    private void ClearWaitArbiterState()
        => _waitCoordinator.ClearState();

    private void AssignStickyWaitWinnerForBlock(TrackLayout layout, string resourceId)
        => _waitCoordinator.AssignStickyWaitWinnerForBlock(layout, resourceId);

    private void AssignStickyWaitWinnerForTurnout(TrackLayout layout, string resourceId)
        => _waitCoordinator.AssignStickyWaitWinnerForTurnout(layout, resourceId);

    private string? TryGetStickyBlockingWinnerRouteIdForBlock(TrackLayout layout, string resourceId, string? requestingRouteId)
        => _waitCoordinator.TryGetStickyBlockingWinnerRouteIdForBlock(layout, resourceId, requestingRouteId);

    private string? TryGetStickyBlockingWinnerRouteIdForTurnout(TrackLayout layout, string resourceId, string? requestingRouteId)
        => _waitCoordinator.TryGetStickyBlockingWinnerRouteIdForTurnout(layout, resourceId, requestingRouteId);

    private void ConsumeStickyWaitWinnerForBlock(string? requestingRouteId, string resourceId)
        => _waitCoordinator.ConsumeStickyWaitWinnerForBlock(requestingRouteId, resourceId);

    private void ConsumeStickyWaitWinnerForTurnout(string? requestingRouteId, string resourceId)
        => _waitCoordinator.ConsumeStickyWaitWinnerForTurnout(requestingRouteId, resourceId);

    private string? ResolveStickyWaitWinnerRouteIdForBlock(string? blockId)
        => _waitCoordinator.ResolveStickyWaitWinnerRouteIdForBlock(blockId);

    private string? ResolveStickyWaitWinnerRouteIdForTurnout(string? turnoutId)
        => _waitCoordinator.ResolveStickyWaitWinnerRouteIdForTurnout(turnoutId);

    private static bool TryResolveWaitingResourceId(string? resourceKey, out string resourceId)
    {
        resourceId = string.Empty;
        if (string.IsNullOrWhiteSpace(resourceKey))
            return false;

        var separatorIndex = resourceKey.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= resourceKey.Length - 1)
            return false;

        resourceId = resourceKey[(separatorIndex + 1)..];
        return !string.IsNullOrWhiteSpace(resourceId);
    }

    private string FormatRouteHeldResources(TrackLayout layout, string routeId)
    {
        var blocks = GetRouteActiveBlockIds(layout, routeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => ResolveLayoutResourceDisplayName(layout, id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var turnouts = _turnoutRuntimeReservations
            .Where(kv => string.Equals(kv.Value, routeId, StringComparison.OrdinalIgnoreCase))
            .Select(kv => ResolveLayoutResourceDisplayName(layout, kv.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parts = new List<string>();
        if (blocks.Count > 0)
            parts.Add($"bloky: {string.Join(", ", blocks)}");
        if (turnouts.Count > 0)
            parts.Add($"výhybky: {string.Join(", ", turnouts)}");

        return parts.Count > 0 ? string.Join(" | ", parts) : "žiadne";
    }

    private string FormatWaitingResource(TrackLayout layout, string? resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
            return "žiadne";

        return TryResolveWaitingResourceId(resourceKey, out var resourceId)
            ? ResolveLayoutResourceDisplayName(layout, resourceId)
            : resourceKey;
    }

    private string FormatBlockingRoute(TrackLayout layout, string? routeId)
        => FormatRouteTagValue(layout, routeId);

    private (int ReleasedBlocks, int ReleasedTurnouts) ReleasePendingSharedReservationsForYield(TrackLayout layout, string routeId)
    {
        var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
        if (route == null)
            return (0, 0);

        var routeLocoId = ResolvePrimaryRouteLocoId(layout, route);
        var releasedBlocks = 0;
        if (!string.IsNullOrWhiteSpace(routeLocoId))
        {
            foreach (var block in layout.Elements.OfType<BlockElement>())
            {
                if (!block.IsShadowSet || !string.Equals(block.ReservedLocoId, routeLocoId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (block.IsOccupied && string.Equals(block.AssignedLocoId, routeLocoId, StringComparison.OrdinalIgnoreCase))
                    continue;

                ClearShadowReservation(block);
                AssignStickyWaitWinnerForBlock(layout, block.Id);
                DiagnoseBlockRuntime(layout, block.Id, "deadlock-yield-uvoľnený", routeLocoId, routeId, routeId, DiagnosticLevel.Info);
                releasedBlocks++;
            }
        }

        var releasedTurnouts = 0;
        foreach (var turnoutId in _turnoutRuntimeReservations
                     .Where(kv => string.Equals(kv.Value, routeId, StringComparison.OrdinalIgnoreCase))
                     .Select(kv => kv.Key)
                     .ToList())
        {
            _turnoutRuntimeReservations.Remove(turnoutId);
            AssignStickyWaitWinnerForTurnout(layout, turnoutId);
            releasedTurnouts++;

            var turnout = layout.Elements.OfType<TurnoutElement>().FirstOrDefault(t => string.Equals(t.Id, turnoutId, StringComparison.OrdinalIgnoreCase));
            if (turnout != null)
                DiagnoseTurnoutRuntime(layout, turnoutId, turnout.State, routeId, routeId, "deadlock-yield-uvoľnená", DiagnosticLevel.Info);
        }

        return (releasedBlocks, releasedTurnouts);
    }

    private bool ProcessDeadlockYieldState(TrackLayout layout, string routeId)
        => _waitCoordinator.ProcessDeadlockYieldState(layout, routeId);

    private IReadOnlyDictionary<string, TurnoutState> ResolveSegmentTurnoutRequirements(
        TrackLayout layout,
        RouteDefinition route,
        string fromBlockId,
        string toBlockId)
    {
        var routeTurnoutStates = route.TurnoutSettings
            .Where(t => !string.IsNullOrWhiteSpace(t.TurnoutId))
            .ToDictionary(t => t.TurnoutId, t => t.RequiredState, StringComparer.OrdinalIgnoreCase);
        if (routeTurnoutStates.Count == 0)
            return new Dictionary<string, TurnoutState>(StringComparer.OrdinalIgnoreCase);

        var turnoutIds = ResolveTraversedTurnoutIds(layout, route, fromBlockId, toBlockId);
        return turnoutIds
            .Where(turnoutId => routeTurnoutStates.ContainsKey(turnoutId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(turnoutId => turnoutId, turnoutId => routeTurnoutStates[turnoutId], StringComparer.OrdinalIgnoreCase);
    }

    private bool IsTurnoutAvailableForRoute(string routeId, string turnoutId)
    {
        if (string.IsNullOrWhiteSpace(routeId) || string.IsNullOrWhiteSpace(turnoutId))
            return false;

        if (!_turnoutRuntimeReservations.TryGetValue(turnoutId, out var ownerRouteId))
            return true;

        return string.Equals(ownerRouteId, routeId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(bool IsReady, string? WaitReason)> TryEnsureTurnoutsForSegmentAsync(
        TrackLayout layout,
        RouteDefinition route,
        string fromBlockId,
        string toBlockId,
        IDccCentralClient? dccClient,
        CancellationToken ct)
    {
        var turnoutRequirements = ResolveSegmentTurnoutRequirements(layout, route, fromBlockId, toBlockId);
        if (turnoutRequirements.Count == 0)
            return (true, null);

        foreach (var turnoutId in turnoutRequirements.Keys)
        {
            if (!IsTurnoutAvailableForRoute(route.Id, turnoutId))
            {
                DiagnoseTurnoutRuntime(layout, turnoutId, turnoutRequirements[turnoutId], route.Id, ResolveTurnoutOwnerRouteId(turnoutId), "odmietnutá", DiagnosticLevel.Warning);
                return (false, "konflikt-vyhybky");
            }

            var stickyWinnerRouteId = TryGetStickyBlockingWinnerRouteIdForTurnout(layout, turnoutId, route.Id);
            if (!string.IsNullOrWhiteSpace(stickyWinnerRouteId))
            {
                DiagnoseTurnoutRuntime(layout, turnoutId, turnoutRequirements[turnoutId], route.Id, stickyWinnerRouteId, "sticky-odmietnutá", DiagnosticLevel.Warning);
                return (false, "konflikt-vyhybky");
            }
        }

        var turnoutById = layout.Elements.OfType<TurnoutElement>()
            .ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);
        var effectiveDccClient = GetEffectiveDccClient(dccClient);
        var changed = false;

        foreach (var requirement in turnoutRequirements)
        {
            if (!turnoutById.TryGetValue(requirement.Key, out var turnout))
                continue;

            _turnoutRuntimeReservations[requirement.Key] = route.Id;
            ConsumeStickyWaitWinnerForTurnout(route.Id, requirement.Key);

            if (turnout.State == requirement.Value)
            {
                DiagnoseTurnoutRuntime(layout, requirement.Key, requirement.Value, route.Id, route.Id, "pripravená", DiagnosticLevel.Info);
                continue;
            }

            turnout.State = requirement.Value;
            changed = true;
            DiagnoseTurnoutRuntime(layout, requirement.Key, requirement.Value, route.Id, route.Id, "prestavená", DiagnosticLevel.Info);

            if (ShouldSendDcc(effectiveDccClient) && turnout.DccAddress > 0)
            {
                bool branch = RouteActivationService.MapTurnoutStateToBranch(requirement.Value);
                await effectiveDccClient!.SetTurnoutAsync(turnout.DccAddress, branch, activate: true, ct);
            }
        }

        if (changed)
            LayoutRefreshRequested?.Invoke();

        return (true, null);
    }

    /// <summary>
    /// Pri aktivácii cesty: prejde všetkými segmentmi cesty a postupne preklopí
    /// každú výhybku do požadovanej polohy (v schéme aj v Live režime na centrále)
    /// ešte predtým, než sa spustí samotná jazda. Cieľom je, aby používateľ v demo
    /// režime videl celú cestu "postavenú" naraz, a nie aby sa výhybky prepínali
    /// až tesne pred vstupom vlaku do daného segmentu.
    /// </summary>
    private async Task PreSwitchRouteTurnoutsAsync(
        TrackLayout layout,
        RouteDefinition route,
        IDccCentralClient? dccClient,
        CancellationToken ct)
    {
        if (layout == null || route == null || route.TurnoutSettings.Count == 0)
            return;
        if (route.BlockIds.Count < 2)
            return;

        for (int i = 0; i < route.BlockIds.Count - 1; i++)
        {
            ct.ThrowIfCancellationRequested();

            var fromId = route.BlockIds[i];
            var toId = route.BlockIds[i + 1];
            if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
                continue;

            // Ignorujeme prípadné konflikty/výsledok: aktivácia už bola schválená a
            // toto je iba "vizuálne predprestavenie" cesty. Per-segment kontrola
            // pred vstupom vlaku stále prebehne v TryEnsureTurnoutsForSegmentAsync.
            await TryEnsureTurnoutsForSegmentAsync(layout, route, fromId, toId, dccClient, ct);
        }
    }

    private void InitializeRouteRuntime(string routeId, string? currentBlockId, IEnumerable<string>? traversalBlockIds = null, string? ownerLocomotiveId = null)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return;

        _traversalEngine.InitializeTraversal(new InitializeTraversalRequest(routeId, currentBlockId, traversalBlockIds, ownerLocomotiveId));
    }

    private void UpdateRouteRuntimeForSegment(string routeId, int segmentIndex, string currentBlockId, string? ownerLocomotiveId = null, IEnumerable<string>? traversalBlockIds = null)
    {
        if (string.IsNullOrWhiteSpace(routeId) || string.IsNullOrWhiteSpace(currentBlockId))
            return;

        _traversalEngine.AdvanceTraversal(new AdvanceTraversalRequest(routeId, segmentIndex, currentBlockId, ownerLocomotiveId, traversalBlockIds));
    }

    private void UpdateRouteRuntimeWaiting(string routeId, string blockId, string reason, DateTime waitingSinceUtc)
        => _runtimeRegistry.EnterWaitState(routeId, blockId, reason, waitingSinceUtc);

    private void ResetRouteRuntimeWaitingToActive(string routeId)
        => _runtimeRegistry.ResetWaitStateToActive(routeId);

    private void SetRouteRuntimeState(string routeId, RouteRuntimeLifecycleState state)
        => _traversalEngine.SetTraversalLifecycleState(routeId, state);

    private void ResetTailClearTraversalState(string routeId, string? sourceBlockId, string? targetBlockId)
        => _traversalEngine.ResetTailClearState(routeId, sourceBlockId, targetBlockId);

    private void MarkTraversalBoundaryEntry(string routeId, string? sourceBlockId, string? targetBlockId)
        => _traversalEngine.MarkBoundaryEntry(routeId, sourceBlockId, targetBlockId, DateTime.UtcNow);

    private void MarkTraversalTailClear(string routeId, string? sourceBlockId, string? targetBlockId)
        => _traversalEngine.MarkTailClear(routeId, sourceBlockId, targetBlockId, DateTime.UtcNow);

    private bool EnterTraversalWait(string routeId, string blockId, string reason)
        => _waitCoordinator.EnterTraversalWait(routeId, blockId, reason);

    private bool ExitTraversalWait(string routeId)
        => _waitCoordinator.ExitTraversalWait(routeId);

    private bool ExitTraversalWaitSuccess(string routeId, string blockId)
        => _waitCoordinator.ExitTraversalWaitSuccess(routeId, blockId);

    private bool IsTraversalRouteStillValid(TrackLayout layout, string routeId)
    {
        if (_settings.CurrentProject?.Layout != layout)
            return false;

        return layout.Routes.Any(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
    }

    private TraversalWaitOutcome? ResolveTraversalInvalidRouteOutcome(TrackLayout layout, string routeId)
    {
        if (_settings.CurrentProject?.Layout != layout)
            return TraversalWaitOutcome.LayoutMissing;

        return layout.Routes.Any(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase))
            ? null
            : TraversalWaitOutcome.RouteMissing;
    }

    private bool IsTraversalLocoStillValid(string locoCode)
        => !string.IsNullOrWhiteSpace(locoCode)
           && Locomotives.Any(l => string.Equals(l.Code, locoCode, StringComparison.OrdinalIgnoreCase));

    private Task StopLocomotiveDisplayAsync(string locoCode, CancellationToken ct)
    {
        _ = ct;

        var loco = Locomotives.FirstOrDefault(l => string.Equals(l.Code, locoCode, StringComparison.OrdinalIgnoreCase));
        if (loco != null)
        {
            loco.TargetSpeed = 0;
            loco.CurrentDisplaySpeed = 0;
        }

        return Task.CompletedTask;
    }

    private async Task ApplyStopBeforeSegmentAsync(
        TrackLayout layout,
        RouteDefinition route,
        IReadOnlyList<string> traversalBlockIds,
        int segmentIndex,
        IDccCentralClient? dccClient,
        CancellationToken ct)
        => await _signalSafetyEngine.ApplyStopBeforeSegmentAsync(layout, route, traversalBlockIds, segmentIndex, GetEffectiveDccClient(dccClient), ct);

    private async Task<TraversalWaitOutcome> WaitForNextBlockReservationAsync(
        TrackLayout layout,
        RouteDefinition route,
        IReadOnlyList<string> traversalBlockIds,
        int segmentIndex,
        BlockElement segmentTarget,
        string locoCode,
        bool orientationForward,
        NavigationDirection travelDirection,
        IDccCentralClient? dccClient,
        CancellationToken ct)
        => await _waitCoordinator.WaitForNextBlockReservationAsync(new WaitForNextBlockReservationRequest(
            layout,
            route,
            traversalBlockIds,
            segmentIndex,
            segmentTarget,
            locoCode,
            orientationForward,
            travelDirection,
            GetEffectiveDccClient(dccClient),
            ct));

    private async Task<RouteActivationResult> HandleRouteLocalCancellationAsync(
        RouteDefinition route,
        Locomotive? loco,
        IDccCentralClient? dccClient,
        string cancellationSource)
    {
        Log.Warning(
            "Route-local cleanup štart: cesta=[{Cesta}], zdroj=[{Zdroj}]",
            route.Id,
            cancellationSource);

        SetRouteRuntimeState(route.Id, RouteRuntimeLifecycleState.EmergencyStopped);

        if (_settings.CurrentProject?.Layout != null)
        {
            await DeactivateRouteInternalAsync(
                route.Id,
                updateMessage: true,
                dccClient: dccClient,
                ct: CancellationToken.None,
                diagnosticReason: "Jazda bola zrušená",
                diagnosticLevel: DiagnosticLevel.Warning);
        }
        else
        {
            _runtimeRegistry.RemoveRuntime(route.Id);
            _activeRouteVisualScopeResolver.ResetRoute(route.Id);
            ReleaseTurnoutReservationsForRoute(route.Id);
            ExitTraversalWait(route.Id);
            OnPropertyChanged(nameof(ActiveRouteIds));
        }

        if (loco != null)
        {
            loco.TargetSpeed = 0;
            loco.CurrentDisplaySpeed = 0;
        }

        Log.Information(
            "Route-local cleanup dokončený: cesta=[{Cesta}], zostávajúce aktívne cesty=[{Počet}]",
            route.Id,
            _runtimeRegistry.ActiveRouteCount);

        LayoutRefreshRequested?.Invoke();
        return RouteActivationResult.Failed("cancelled");
    }

    // ------------------ /WAIT mode (step 1) ---------------------
    
    /// <summary>Načíta elementy schémy z projektu a požiada View o prekreslenie.</summary>
    public void RefreshLayoutFromProject()
    {
        LayoutElements.Clear();
        
        var layout = _settings.CurrentProject?.Layout;
        if (layout == null) return;
        
        foreach (var element in layout.Elements)
        {
            // Po nacitani rezimu prevadzka resetneme runtime locky, tie riadi RouteActivationService.
            if (element is BlockElement block)
            {
                block.IsLocked = false;
                ClearShadowReservation(block);
            }

            LayoutElements.Add(element);
        }

        var resyncedBlocks = DccFeedbackLayoutApplier.SynchronizeOccupancyFromIndicators(layout);
        if (resyncedBlocks.Count > 0)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"RBUS operation-enter resync: restoredBlocks={string.Join(", ", resyncedBlocks.Select(block => block.Id))}");
        }

        _signalSafetyEngine.NormalizeInvalidSignalsToStop(layout);

        QueueRefreshSignalStatus();
        
        // Notifikovať View že má prekresliť canvas
        LayoutRefreshRequested?.Invoke();
    }
    
    /// <summary>
    /// OPERATION MODE: Bezpečnostný reset pri prepnutí medzi Simulation a Live režimom.
    /// Zabezpečí, aby sa zastavili všetky lokomotívy a zhodili všetky aktívne cesty.
    /// </summary>
    private void OnIsSimulationModeChanged(bool value)
    {
        var layout = _settings.CurrentProject?.Layout;
        if (layout == null)
            return;
        
        var modeName = value ? "SIMULÁTOR (Trenažér)" : "LIVE (Ostrá prevádzka)";
        
        TrackFlowDoctorService.Instance.Diagnose(
            "Prevádzkový režim",
            $" Prepnutie režimu: {modeName}. Vykonávam bezpečnostný reset...",
            DiagnosticLevel.Warning);
        
        // 1. Zastav všetky lokomotívy (len v pamäti, bez DCC príkazov)
        foreach (var loco in Locomotives)
        {
            loco.TargetSpeed = 0;
            loco.CurrentDisplaySpeed = 0;
        }
        
        // 2. Zhoď všetky aktívne cesty
        DeactivateAllRoutes();
        
        // 3. Cleanup simulation contexts
        _activeSimulations.Clear();
        
        // 4. Reset layout elementov
        foreach (var element in layout.Elements)
        {
            if (element is BlockElement block)
            {
                block.IsLocked = false;
                ClearShadowReservation(block);
                block.AssignedLocoId = null;
            }
            
        }

        var resyncedBlocks = DccFeedbackLayoutApplier.SynchronizeOccupancyFromIndicators(layout);
        TrackFlowDoctorService.Instance.Diagnose(
            "Prevádzkový režim",
            $"Resync po prepnutí režimu: activeContactBlocks={resyncedBlocks.Count}, ids={(resyncedBlocks.Count == 0 ? "-" : string.Join(", ", resyncedBlocks.Select(block => block.Id)))}",
            resyncedBlocks.Count > 0 ? DiagnosticLevel.Info : DiagnosticLevel.Success);

        SetAllSignalsRed(layout);
        
        TrackFlowDoctorService.Instance.Diagnose(
            "Prevádzkový režim",
            $"✅ Režim {modeName} aktivovaný. Všetky cesty uvoľnené, lokomotívy zastavené.",
            DiagnosticLevel.Success);
        
        LayoutRefreshRequested?.Invoke();
    }

    public void RefreshSignalStatus()
        => QueueRefreshSignalStatus();

    private void QueueRefreshSignalStatus()
        => _ = RefreshSignalStatusDeferredAsync();

    private async Task RefreshSignalStatusDeferredAsync()
    {
        try
        {
            var changed = await RefreshSignalStatusAsync(dccClient: null).ConfigureAwait(false);
            if (changed > 0)
                Dispatcher.UIThread.Post(() => LayoutRefreshRequested?.Invoke(), DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("OperationViewModel.RefreshSignalStatusDeferredAsync", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Prevádzkový režim",
                $"⚠️ Asynchrónny refresh návestidiel zlyhal: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);
        }
    }

    public async Task<int> RefreshSignalStatusAsync(IDccCentralClient? dccClient, CancellationToken ct = default)
    {
        var layout = _settings.CurrentProject?.Layout;
        if (layout == null)
            return 0;

        var effectiveDccClient = GetEffectiveDccClient(dccClient);
        return await HandleOccupiedBlocks(layout, effectiveDccClient, ct, sendDcc: effectiveDccClient != null);
    }

    public async Task<int> RefreshSignalsAndPushToCentralAsync(
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string? syncId = null)
    {
        var layout = _settings.CurrentProject?.Layout;
        if (layout == null)
            return 0;

        // Dorovnaj safety stav (obsadene bloky -> Stop + release ciest), potom pushni aktualny snapshot.
        var effectiveDccClient = GetEffectiveDccClient(dccClient);
        await RefreshSignalStatusAsync(effectiveDccClient, ct);
        var sent = await _runtimeSafetyService.SendAllSignalStatesAsync(layout.Elements, effectiveDccClient, ct, reason: "refresh-snapshot", syncId: syncId);
        LayoutRefreshRequested?.Invoke();
        return sent;
    }

    /// <summary>
    /// Force update: odošle aktuálny aspekt každého návestidla do centrály
    /// bez ohľadu na to, či sa aspekt v modeli menil.
    /// </summary>
    public async Task<int> ForceSendCurrentSignalStatesAsync(
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string? syncId = null)
    {
        var layout = _settings.CurrentProject?.Layout;
        if (layout == null)
            return 0;

        var effectiveDccClient = GetEffectiveDccClient(dccClient);
        var sent = await _runtimeSafetyService.SendAllSignalStatesAsync(layout.Elements, effectiveDccClient, ct, reason: "connect-force-snapshot", syncId: syncId);
        LayoutRefreshRequested?.Invoke();
        return sent;
    }

    public async Task<int> SetAllSignalsRedAndPushAsync(
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string? syncId = null)
    {
        var layout = _settings.CurrentProject?.Layout;
        if (layout == null)
            return 0;

        var effectiveDccClient = GetEffectiveDccClient(dccClient);
        var sent = await _signalSafetyEngine.SetAllSignalsRedAndPushAsync(layout, effectiveDccClient, ct, syncId);
        LayoutRefreshRequested?.Invoke();
        return sent;
    }

    public async Task HandleSignalClickAsync(
        SignalElement signal,
        IDccCentralClient? dccClient = null,
        CancellationToken ct = default)
    {
        // Skip návestidlá bez platnej DCC adresy.
        if (!signal.HasValidDccAddress())
        {
            Log.Debug("Signal click ignored: invalid DCC address {Address} (signal={SignalId})", signal.DccAddress, signal.Id);
            return;
        }

        if (IsProtectedBlockOccupied(signal))
        {
            var changed = await _signalSafetyEngine.ForceSignalStopAsync(signal, GetEffectiveDccClient(dccClient), ct, reason: "signal-click-protected");
            SetRouteActivationMessage("signal-protected-block-occupied");
            if (changed)
                MarkDirty();

            LayoutRefreshRequested?.Invoke();
            return;
        }

        // Manualne prepinanie navestidla je vypnute - povolujuca navest je viazana iba na RequestRoute().
        SetRouteActivationMessage("signal-change-requires-route", autoHide: true);
    }

    /// <summary>
    /// Vstupný bod pre externé zmeny obsadenosti (napr. senzory).
    /// Po aktualizácii stavov blokov dorovná návestidlá, odošle DCC a vyžiada prekreslenie.
    /// </summary>
    public async Task<int> HandleExternalOccupancyUpdateAsync(
        IDccCentralClient? dccClient = null,
        CancellationToken ct = default)
    {
        var layout = _settings.CurrentProject?.Layout;
        if (dccClient != null && IsSimulationMode && !_warnedAboutLiveFeedbackWhileSimulationMode)
        {
            _warnedAboutLiveFeedbackWhileSimulationMode = true;
            TrackFlowDoctorService.Instance.Diagnose(
                "Prevádzkový režim",
                "⚠️ Prišla živá DCC obsadenosť, ale Prevádzka je stále v režime SIMULÁTOR. Feedback sa spracuje, no prepínač môže po safety resete zneprehľadniť stav blokov.",
                DiagnosticLevel.Warning);
        }

        if (!IsSimulationMode)
            _warnedAboutLiveFeedbackWhileSimulationMode = false;

        if (layout != null)
        {
            DiagnoseOrchestrationPass(layout, routeId: null, "reconcile-pass", "flow=[external-occupancy-update]");

            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"RBUS external-reconcile-start: mode={(IsSimulationMode ? "simulation" : "live")}, occupiedBefore={string.Join(", ", layout.Elements.OfType<BlockElement>().Where(static block => block.IsOccupied).Select(static block => block.Id))}");
        }

        var changed = layout != null
            ? await HandleOccupiedBlocks(layout, dccClient, ct, sendDcc: dccClient != null)
            : 0;

        if (layout != null)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "DCC",
                $"RBUS external-reconcile-end: changed={changed}, occupiedAfter={string.Join(", ", layout.Elements.OfType<BlockElement>().Where(static block => block.IsOccupied).Select(static block => block.Id))}");
        }

        RequestLayoutRefreshIfChanged(layout, "external-occupancy-update");
        return changed;
    }

    public Task<RouteActivationResult> RequestRouteAsync(
        string routeId,
        IDccCentralClient? dccClient = null,
        CancellationToken ct = default)
    {
        return ActivateRouteAsync(routeId, dccClient, ct);
    }

    private void MarkDirty()
    {
        _settings.Dirty.MarkDirty("operation");
    }

    private IDccCentralClient? GetEffectiveDccClient(IDccCentralClient? dccClient)
        => dccClient;

    private bool ShouldSendDcc(IDccCentralClient? dccClient)
        => dccClient != null;

    public async Task<RouteActivationResult> ActivateRouteAsync(
        string routeId,
        IDccCentralClient? dccClient = null,
        CancellationToken ct = default)
    {
        var layout = _settings.CurrentProject?.Layout;
        if (layout == null)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "RouteActivation",
                "Zlyhanie aktivácie: projekt nie je otvorený",
                DiagnosticLevel.Warning);
            var noProject = RouteActivationResult.Failed("no-project");
            SetRouteActivationMessage(noProject.Reason);
            return noProject;
        }

        // STRIKTNÁ smerová validácia: pred aktiváciou over, že smer jazdy je povolený vo všetkých blokoch cesty.
        var precheckRoute = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
        if (precheckRoute == null)
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "RouteActivation",
                $"Cesta nebola nájdená: [{LayoutElementDisplayHelper.ShortId(routeId)}]",
                DiagnosticLevel.Warning);
        }
        if (precheckRoute != null)
        {
            var activationPrecheckRoute = TrackFlow.Services.Operation.RouteActivationOrder.ResolveActivationRouteOrder(layout, precheckRoute, SelectedLoco);
            var blocksById = layout.Elements.OfType<BlockElement>()
                .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < activationPrecheckRoute.BlockIds.Count - 1; i++)
            {
                var fromId = activationPrecheckRoute.BlockIds[i];
                var toId = activationPrecheckRoute.BlockIds[i + 1];
                if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
                    continue;
                if (!blocksById.TryGetValue(fromId, out var fromBlock) || !blocksById.TryGetValue(toId, out var toBlock))
                    continue;

                var travelDirection = RouteSegmentSignalResolver.ResolveSegmentTravelDirection(layout, activationPrecheckRoute, fromId, toId);

                if (!fromBlock.IsTravelDirectionAllowed(travelDirection))
                {
                    var fail = RouteActivationResult.Failed($"Smer jazdy v bloku {OperationDisplayHelpers.BlockDisplayName(fromBlock)} nie je povolený");
                    TrackFlowDoctorService.Instance.Diagnose(
                        "RouteActivation",
                        $"Zlyhanie predkontroly: smer jazdy NIE JE POVOLENÝ v bloku {OperationDisplayHelpers.BlockDisplayName(fromBlock)} " +
                        $"pre segment {OperationDisplayHelpers.ResolveBlockDisplayName(layout, fromId)} → {OperationDisplayHelpers.ResolveBlockDisplayName(layout, toId)}, travelDirection='{travelDirection}'",
                        DiagnosticLevel.Warning);
                    SetRouteActivationMessage(fail.Reason);
                    return fail;
                }
                if (!toBlock.IsTravelDirectionAllowed(travelDirection))
                {
                    var fail = RouteActivationResult.Failed($"Smer jazdy v bloku {OperationDisplayHelpers.BlockDisplayName(toBlock)} nie je povolený");
                    TrackFlowDoctorService.Instance.Diagnose(
                        "RouteActivation",
                        $"Zlyhanie predkontroly: smer jazdy NIE JE POVOLENÝ v bloku {OperationDisplayHelpers.BlockDisplayName(toBlock)} " +
                        $"pre segment {OperationDisplayHelpers.ResolveBlockDisplayName(layout, fromId)} → {OperationDisplayHelpers.ResolveBlockDisplayName(layout, toId)}, travelDirection='{travelDirection}'",
                        DiagnosticLevel.Warning);
                    SetRouteActivationMessage(fail.Reason);
                    return fail;
                }
            }

            var precheckSignalController = new SignalController(layout.Elements, GetEffectiveDccClient(dccClient));
            if (!precheckSignalController.TryValidateRouteSignalSupport(activationPrecheckRoute, layout.Elements, out var incompatibleSignalReason))
            {
                var fail = RouteActivationResult.Failed(incompatibleSignalReason ?? "Vybraný typ návestidla nie je správny.");
                TrackFlowDoctorService.Instance.Diagnose(
                    "RouteActivation",
                    incompatibleSignalReason ?? "Vybraný typ návestidla nie je správny pre požadovanú návesť.",
                    DiagnosticLevel.Warning);
                SetRouteActivationMessage(fail.Reason);
                return fail;
            }
        }

        // Pred štartom novej cesty: vyčisti všetky 'zaseknuté' Shadow stavy z predchádzajúcich jázd
        // (bloky, ktoré majú nastavený IsShadowSet/ReservedLocoId, ale nepatria k žiadnej aktívnej ceste
        // ani k aktuálnemu obsadeniu).
        ResetStuckShadowsBeforeActivation(layout);

        var effectiveDccClient = GetEffectiveDccClient(dccClient);
        var result = await _routeActivationService.TryActivateAsync(layout, routeId, _runtimeRegistry.MutableActiveRouteIds, effectiveDccClient, ct);
        SetRouteActivationMessage(result.IsSuccess ? "route-activated" : result.Reason);

        if (!result.IsSuccess)
        {
            if (result.Conflict != null && result.Conflict.HasConflict)
            {
                TrackFlowDoctorService.Instance.Diagnose(
                    "RouteActivation",
                    $"Tvrdý konflikt: cesta=[{ResolveRouteDisplayName(layout, routeId)}], dôvod=[{result.Reason}]",
                    DiagnosticLevel.Warning);
            }
        }

        if (result.IsSuccess)
        {
            var activatedRoute = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
            
            if (activatedRoute != null)
            {
                var activationRoute = TrackFlow.Services.Operation.RouteActivationOrder.ResolveActivationRouteOrder(layout, activatedRoute, SelectedLoco);

                InitializeRouteRuntime(
                    activatedRoute.Id,
                    activationRoute.BlockIds.FirstOrDefault(),
                    activationRoute.BlockIds,
                    ResolvePrimaryRouteLocoId(layout, activationRoute) ?? SelectedLoco?.Code);
                DiagnoseActivationRouteOrder(layout, activationRoute);
                DiagnoseRouteStarted(layout, activationRoute);

                // UX: Pri aktivácii cesty preklop VŠETKY výhybky na trase do požadovanej polohy
                // okamžite (a v Live režime aj na centrále), nie až tesne pred vstupom vlaku do
                // segmentu. Vďaka tomu sa v schéme schematicky najprv prestavia celá cesta a
                // následne sa rozsvietia návestidlá / spustí jazda.
                await PreSwitchRouteTurnoutsAsync(layout, activationRoute, effectiveDccClient, ct);

                var selectedDirectionAnalysis = AnalyzeSelectedLocoDirectionForRoute(activationRoute, layout.Elements);
                if (selectedDirectionAnalysis.HasDirection && SelectedLoco != null)
                {
                    TryApplyAutomaticDirectionIfStopped(SelectedLoco, selectedDirectionAnalysis.DesiredForward);
                    await SynchronizeSelectedLocoDirectionForActivatedRouteAsync(activationRoute, layout.Elements, selectedDirectionAnalysis.DesiredForward, effectiveDccClient, ct);
                    LayoutRefreshRequested?.Invoke();
                }

                var signalController = new SignalController(layout.Elements, effectiveDccClient);

                // KROK 1 (model-only): Vyčisti aspekty všetkých návestidiel v modeli na STOJ.
                // ŽIADNE DCC sa zatiaľ neposiela - eliminujeme „command storm" do centrály
                // (Stop → Permissive na tú istú adresu v ms vzdialenosti spôsobuje, že Z21 / PeLi
                // dekodér nestihne spracovať príkazy a fyzické návestidlo zostane v zlom stave).
                SetSignalsRedRespectingActiveRoutes(layout, activatedRoute.Id);

                // KROK 2: Vypočítaj/validuj iba baseAspect štartového návestidla.
                // DÔLEŽITÉ: baseAspect sa tu NESMIE zapísať do modelu/UI ani odoslať do DCC.
                // Finálny zápis a jediné DCC odoslanie robí až UpdateTraversalSignalWindowAsync
                // po kompletnej look-ahead syntéze.
                var routeSignalApplied = await signalController.ApplySignalAspectsForRouteAsync(
                    activationRoute, ct, reason: "route-activate-base", sendDcc: false);

                if (!routeSignalApplied)
                {
                    SetSignalsRedRespectingActiveRoutes(layout, activatedRoute.Id);
                }
                else
                {
                    // Pri aktivácii nastavujeme iba štartový segment. BaseAspect sa nezapisuje;
                    // ApplyPermissiveForSegmentAsync najprv spraví look-ahead syntézu a až potom
                    // zapíše/odošle jeden finálny aspekt.
                    await UpdateTraversalSignalWindowAsync(
                        layout,
                        activationRoute,
                        activationRoute.BlockIds,
                        leadSegmentIndex: 0,
                        keepPreviousSegmentActive: false,
                        dccClient: effectiveDccClient,
                        ct);
                }

                // Aktivacna sekvencia: po návestidle potvrď Shadow rezerváciu a až potom nastav dashboard target speed.
                if (selectedDirectionAnalysis.HasDirection && SelectedLoco != null)
                {
                    ReserveInitialWindowForSelectedLoco(layout, activationRoute, SelectedLoco);
                    ApplyActivationLockWindow(layout, activationRoute);
                }

                // Pri aktivacii cesty vizualne aktivujeme iba prvy segment.
                SetTraversalSegmentWindow(layout, activationRoute, activationRoute.BlockIds, leadSegmentIndex: 0, keepPreviousSegmentActive: false);

            }
            MarkDirty();
            OnPropertyChanged(nameof(ActiveRouteIds));
            LayoutRefreshRequested?.Invoke();
        }

        return result;
    }

    public void DeactivateRoute(string routeId)
    {
        _ = DeactivateRouteInternalAsync(
            routeId, 
            updateMessage: true, 
            dccClient: null,
            CancellationToken.None, 
            diagnosticReason: "manuálne vypnutie");
    }

    private async Task DeactivateRouteInternalAsync(
        string routeId,
        bool updateMessage,
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string? diagnosticReason = null,
        DiagnosticLevel diagnosticLevel = DiagnosticLevel.Info)
    {
        var layout = _settings.CurrentProject?.Layout;
        if (layout == null)
            return;

        var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
        var releasedTurnoutCount = _turnoutRuntimeReservations.Count(kv => string.Equals(kv.Value, routeId, StringComparison.OrdinalIgnoreCase));
        var releasedReservationCount = route != null ? CountRouteOwnedReservations(layout, route) : 0;
        DiagnoseCleanupRuntime(layout, routeId, route != null ? ResolvePrimaryRouteLocoId(layout, route) : null, "štart", releasedReservationCount, releasedTurnoutCount, DiagnosticLevel.Info);
        _routeActivationService.Deactivate(layout, routeId, _runtimeRegistry.MutableActiveRouteIds);
        // OPRAVA: RouteActivationService.Deactivate volá RebuildBlockLocks, ktorá nastaví IsLocked=true
        // na prvé 2 bloky každej ZOSTÁVAJÚCEJ aktívnej cesty bez ohľadu na aktuálnu pozíciu vlaku.
        // Tým sa zničí sliding window a bloky, ktoré vlak už dávno prešiel (napr. štart bloku A
        // a prvý zdieľaný X), sa znovu zafarbia na svetlomodrú (locked). Prepočítajme lock window
        // podľa aktuálneho stavu obsadenia, aby zostali správne odomknuté.
        ApplyDynamicLockWindow(layout);
        _runtimeRegistry.RemoveRuntime(routeId);
        _activeRouteVisualScopeResolver.ResetRoute(routeId);
        _lastTraversalSignalSnapshots.Remove(routeId);
        _lastTraversalWindowSnapshots.Remove(routeId);
        _lastTraversalWindowLeadIndex.Remove(routeId);
        _lastTraversalWindowKeepPrevious.Remove(routeId);
        ReleaseTurnoutReservationsForRoute(routeId, layout);
        ExitTraversalWait(routeId);
        if (route != null)
        {
            ApplyRouteSafetyFallback(layout, route);
            ClearRouteReservations(layout, route);

            if (!string.IsNullOrWhiteSpace(diagnosticReason))
                DiagnoseRouteEnded(layout, route, diagnosticReason, diagnosticLevel);
        }

        // VYMAZANIE STALE REZERVÁCIÍ: vynúť ClearShadowReservation pre VŠETKY bloky, ktoré
        // ešte držia Shadow/ReservedLocoId a nepatria do žiadnej inej aktívnej cesty.
        // Po skončení cesty nesmie v žiadnom bloku zostať visieť "modrý tieň".
        if (_runtimeRegistry.ActiveRouteCount == 0)
            ResetStuckShadowsBeforeActivation(layout);

        SetSignalsRedRespectingActiveRoutes(layout, routeId);

        await _runtimeSafetyService.SendAllSignalStatesAsync(
            layout.Elements, 
            GetEffectiveDccClient(dccClient), 
            ct, 
            reason: "force-stop-on-deactivation");

        if (updateMessage)
            SetRouteActivationMessage("route-deactivated");
        DiagnoseCleanupRuntime(layout, routeId, route != null ? ResolvePrimaryRouteLocoId(layout, route) : null, "hotovo", releasedReservationCount, releasedTurnoutCount, DiagnosticLevel.Info);
        MarkDirty();
        OnPropertyChanged(nameof(ActiveRouteIds));
        LayoutRefreshRequested?.Invoke();
    }

    public void DeactivateAllRoutes()
    {
        var layout = _settings.CurrentProject?.Layout;
        if (layout == null)
            return;

        var routesToFallback = _runtimeRegistry.ActiveRouteIds
            .Select(id => layout.Routes.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(r => r != null)
            .Cast<RouteDefinition>()
            .ToList();

        _routeActivationService.DeactivateAll(layout, _runtimeRegistry.MutableActiveRouteIds);
        _turnoutRuntimeReservations.Clear();
        foreach (var routeId in _runtimeRegistry.WaitingRouteIds.ToList())
            ExitTraversalWait(routeId);
        _runtimeRegistry.Clear();
        _activeRouteVisualScopeResolver.Clear();
        _lastTraversalSignalSnapshots.Clear();
        _lastTraversalWindowSnapshots.Clear();
        _lastTraversalWindowLeadIndex.Clear();
        _lastTraversalWindowKeepPrevious.Clear();
        ClearWaitArbiterState();
        foreach (var route in routesToFallback)
        {
            ApplyRouteSafetyFallback(layout, route);
            ClearRouteReservations(layout, route);
            DiagnoseRouteEnded(layout, route, "Núdzové zastavenie", DiagnosticLevel.Info);
        }

        SetAllSignalsRed(layout);

        SetRouteActivationMessage("routes-deactivated-all");
        MarkDirty();
        OnPropertyChanged(nameof(ActiveRouteIds));
        LayoutRefreshRequested?.Invoke();
    }

    // =====================================================================================
    // Emergency Stop (global)
    // =====================================================================================

    private void TriggerPanicCancellation()
    {
        try
        {
            _panicStopCts.Cancel();
        }
        catch
        {
            // ignore
        }
        finally
        {
            try { _panicStopCts.Dispose(); } catch { }
            _panicStopCts = new CancellationTokenSource();
        }
    }

    /// <summary>
    /// Okamžité zastavenie celej prevádzky:
    /// - zruší bežiace simulácie/presuny (CancellationToken)
    /// - deaktivuje všetky aktívne cesty
    /// - nastaví návestidlá na STOJ
    /// - v Live režime odošle EmergencyStop + speed=0 pre všetky lokomotívy + refresh stavov návestidiel
    /// </summary>
    public async Task EmergencyStopAsync(
        IDccCentralClient? dccClient = null,
        bool sendDcc = true,
        CancellationToken ct = default)
    {
        // 1) Zruš bežiace simulácie (okamžite zastaví SimulateMoveHeartbeatAsync v ďalšom tiku)
        TriggerPanicCancellation();

        var layout = _settings.CurrentProject?.Layout;

        // 2) Zastav všetky lokomotívy v modeli (UI sa hneď zosynchronizuje)
        foreach (var loco in Locomotives)
        {
            loco.TargetSpeed = 0;
            loco.CurrentDisplaySpeed = 0;
        }

        // 3) Deaktivuj všetky aktívne cesty a nastav signály na STOJ (model-only)
        DeactivateAllRoutes();
        if (layout != null)
            SetAllSignalsRed(layout);

        // 4) Live: odošli príkazy do centrálky
        var effectiveDccClient = GetEffectiveDccClient(dccClient);
        var shouldSend = sendDcc
                         && !IsSimulationMode
                         && ShouldSendDcc(effectiveDccClient)
                         && effectiveDccClient != null
                         && effectiveDccClient.IsConnected;

        if (shouldSend && layout != null)
        {
            try
            {
                await effectiveDccClient!.EmergencyStopAsync(ct);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EmergencyStopAsync failed");
            }

            // Extra safety: explicitne speed=0 pre všetky známe lokomotívy
            foreach (var loco in Locomotives.Where(l => l.DccAddress > 0))
            {
                try
                {
                    var forward = !loco.IsReverse;
                    await effectiveDccClient!.SetLocomotiveSpeedAsync(loco.DccAddress, 0, forward, ct);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Emergency stop: speed=0 send failed for loco address {Address}", loco.DccAddress);
                }
            }

            // A nakoniec vynúť odoslanie všetkých návestidiel (stop) pre konzistenciu
            try
            {
                await _runtimeSafetyService.SendAllSignalStatesAsync(layout.Elements, effectiveDccClient, ct, reason: "e-stop");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Emergency stop: SendAllSignalStatesAsync failed");
            }
        }

        // 5) Cleanup
        _activeSimulations.Clear();
        MarkDirty();
        LayoutRefreshRequested?.Invoke();
    }

    public async Task<RouteActivationResult> MoveLocomotiveBetweenBlocksAsync(
        string locoCode,
        string sourceBlockId,
        string targetBlockId,
        IDccCentralClient? dccClient = null,
        CancellationToken ct = default,
        string? preferredRouteDefinitionId = null)
    {
        var requestedTrainName = ResolveTrainDisplayName(locoCode);

        if (string.IsNullOrWhiteSpace(locoCode))
        {
            TrackFlowDoctorService.Instance.Diagnose("RouteActivation", "Presun vlaku zlyhal: lokomotíva nie je vybraná.", DiagnosticLevel.Warning);
            return RouteActivationResult.Failed("loco-not-selected");
        }

        var layout = _settings.CurrentProject?.Layout;
        if (layout == null)
        {
            TrackFlowDoctorService.Instance.Diagnose("RouteActivation", "Presun vlaku zlyhal: projekt nie je otvorený.", DiagnosticLevel.Warning);
            return RouteActivationResult.Failed("no-project");
        }

        var effectiveDccClient = GetEffectiveDccClient(dccClient);

        if (string.Equals(sourceBlockId, targetBlockId, System.StringComparison.OrdinalIgnoreCase))
        {
            TrackFlowDoctorService.Instance.Diagnose("RouteActivation",
                $"Presun vlaku zlyhal: východzí a cieľový blok sú rovnaké ([{LayoutElementDisplayHelper.ShortId(sourceBlockId)}]).",
                DiagnosticLevel.Warning);
            return RouteActivationResult.Failed("source-target-same");
        }

        var blocks = layout.Elements.OfType<BlockElement>().ToDictionary(b => b.Id, b => b, System.StringComparer.OrdinalIgnoreCase);
        if (!blocks.TryGetValue(sourceBlockId, out var sourceBlock))
        {
            var allBlocks = string.Join(", ", blocks.Values.Select(OperationDisplayHelpers.BlockDisplayName));
            TrackFlowDoctorService.Instance.Diagnose("RouteActivation",
                $"Presun vlaku zlyhal: východzí blok neexistuje ([{LayoutElementDisplayHelper.ShortId(sourceBlockId)}]). Existujúce bloky: [{allBlocks}]",
                DiagnosticLevel.Warning);
            return RouteActivationResult.Failed("source-block-not-found");
        }
        if (!blocks.TryGetValue(targetBlockId, out var targetBlock))
        {
            var allBlocks = string.Join(", ", blocks.Values.Select(OperationDisplayHelpers.BlockDisplayName));
            TrackFlowDoctorService.Instance.Diagnose("RouteActivation",
                $"Presun vlaku zlyhal: cieľový blok neexistuje ([{LayoutElementDisplayHelper.ShortId(targetBlockId)}]). Existujúce bloky: [{allBlocks}]",
                DiagnosticLevel.Warning);
            return RouteActivationResult.Failed("target-block-not-found");
        }

        var sourceName = OperationDisplayHelpers.BlockDisplayName(sourceBlock);
        var targetName = OperationDisplayHelpers.BlockDisplayName(targetBlock);

        if (!string.Equals(sourceBlock.AssignedLocoId, locoCode, System.StringComparison.OrdinalIgnoreCase))
        {
            TrackFlowDoctorService.Instance.Diagnose("RouteActivation",
                $"Presun vlaku zlyhal: vo východzom bloku {sourceName} je vlak '{ResolveTrainDisplayName(sourceBlock.AssignedLocoId ?? string.Empty)}', očakával sa vlak '{requestedTrainName}'.",
                DiagnosticLevel.Warning);
            return RouteActivationResult.Failed("source-block-loco-mismatch");
        }

        RouteDefinition? route = null;

        if (!string.IsNullOrWhiteSpace(preferredRouteDefinitionId))
        {
            route = layout.Routes.FirstOrDefault(r =>
                string.Equals(r.Id, preferredRouteDefinitionId, System.StringComparison.OrdinalIgnoreCase)
                && RouteMatches(r, sourceBlockId, targetBlockId, allowReverse: true));
        }

        route ??= layout.Routes.FirstOrDefault(r => RouteMatches(r, sourceBlockId, targetBlockId, allowReverse: false));
        if (route == null)
        {
            route = layout.Routes.FirstOrDefault(r => RouteMatches(r, sourceBlockId, targetBlockId, allowReverse: true));
        }
        if (route == null)
        {
            TrackFlowDoctorService.Instance.Diagnose("RouteActivation",
                $"Cesta nebola nájdená: [{sourceName} → {targetName}]",
                DiagnosticLevel.Warning);
            return RouteActivationResult.Failed("route-not-found");
        }

        var collision = _runtimeSafetyService.EvaluateBlockEntry(layout, targetBlock.Id, locoCode, route, safetyDistanceBlocks: 1);
        if (!collision.IsSafe)
        {
            if (IsSoftRuntimeActivationConflict(collision.Reason))
            {
                TrackFlowDoctorService.Instance.Diagnose("Bezpečnosť",
                    $"soft runtime konflikt pri aktivácii: vlak=[{requestedTrainName}], cieľ=[{targetName}], dôvod=[{collision.Reason}], blokujúci=[{OperationDisplayHelpers.ResolveBlockDisplayName(layout, collision.BlockingBlockId)}]",
                    DiagnosticLevel.Info);
            }
            else
            {
            TrackFlowDoctorService.Instance.Diagnose("Bezpečnosť",
                $"nebezpečný vstup do bloku: vlak=[{requestedTrainName}], cieľ=[{targetName}], dôvod=[{collision.Reason}], blokujúci=[{OperationDisplayHelpers.ResolveBlockDisplayName(layout, collision.BlockingBlockId)}]",
                DiagnosticLevel.Warning);

            SetRouteActivationMessage(collision.Reason);
            return RouteActivationResult.Failed(collision.Reason);
            }
        }

        var activation = await ActivateRouteAsync(route.Id, dccClient, ct);
        if (!activation.IsSuccess)
        {
            TrackFlowDoctorService.Instance.Diagnose("RouteActivation",
                $"Presun vlaku zlyhal: aktivácia cesty nebola úspešná (dôvod='{activation.Reason}'). Pozri predchádzajúce záznamy aktivácie.",
                DiagnosticLevel.Warning);
            return activation;
        }

        var loco = Locomotives.FirstOrDefault(l => string.Equals(l.Code, locoCode, System.StringComparison.OrdinalIgnoreCase));
        var directionAnalysis = AnalyzeDirectionForMove(route, sourceBlockId, targetBlockId);
        if (loco != null && directionAnalysis.HasDirection)
        {
            TryApplyAutomaticDirectionIfStopped(loco, directionAnalysis.DesiredForward);
        }

        var travelForward = directionAnalysis.HasDirection
            ? directionAnalysis.DesiredForward
            : sourceBlock.AssignedLocoIsForward;

        var traversalBlockIds = BuildTraversalBlockSequence(route, sourceBlockId, targetBlockId);
        if (traversalBlockIds.Count < 2)
            return RouteActivationResult.Failed("route-not-configured");

        InitializeRouteRuntime(route.Id, traversalBlockIds[0], traversalBlockIds, locoCode);

        var routeIndexForward = string.Equals(traversalBlockIds[0], route.BlockIds.FirstOrDefault(), StringComparison.OrdinalIgnoreCase)
            || route.BlockIds.FindIndex(id => string.Equals(id, traversalBlockIds[0], StringComparison.OrdinalIgnoreCase))
               < route.BlockIds.FindIndex(id => string.Equals(id, traversalBlockIds[^1], StringComparison.OrdinalIgnoreCase));

        // FIX: NAJPRV nastaviť návestidlá, POTOM vytvoriť rezervácie
        SetTraversalSegmentWindow(layout, route, traversalBlockIds, leadSegmentIndex: 0, keepPreviousSegmentActive: false);
        await UpdateTraversalSignalWindowAsync(layout, route, traversalBlockIds, leadSegmentIndex: 0, keepPreviousSegmentActive: false, effectiveDccClient, ct);
        
        // Teraz až rezervácie (návestidlá sú už nastavené)
        ReserveInitialWindow(
            layout,
            route,
            locoCode,
            traversalBlockIds[0],
            traversalBlockIds[1],
            routeIndexForward,
            sourceBlock.AssignedLocoIsForward);
        
        LayoutRefreshRequested?.Invoke();

        // Fáza 2.4: heartbeat simuluje prejdenú vzdialenosť + ramping CurrentDisplaySpeed.
        var routeStartAspect = ResolveRouteStartSignalAspect(route, layout.Elements);

        static bool IsReservedFor(string code, BlockElement b)
            => !string.IsNullOrWhiteSpace(code)
               && b.IsShadowSet
               && string.Equals(b.ReservedLocoId, code, StringComparison.OrdinalIgnoreCase);

        async Task<RouteActivationResult?> WaitForSegmentReservationRecoveryAsync(
            int recoverySegmentIndex,
            BlockElement recoverySource,
            BlockElement recoveryTarget,
            string cancellationSource)
        {
            var waitTravelDirection = RouteSegmentSignalResolver.ResolveSegmentTravelDirection(layout, route, recoverySource.Id, recoveryTarget.Id);
            TraversalWaitOutcome waitOutcome;
            try
            {
                waitOutcome = await WaitForNextBlockReservationAsync(
                    layout,
                    route,
                    traversalBlockIds,
                    recoverySegmentIndex,
                    recoveryTarget,
                    locoCode,
                    orientationForward: recoverySource.AssignedLocoIsForward,
                    travelDirection: waitTravelDirection,
                    effectiveDccClient,
                    ct);
            }
            catch (OperationCanceledException)
            {
                return await HandleRouteLocalCancellationAsync(route, loco, effectiveDccClient, cancellationSource);
            }

            if (waitOutcome == TraversalWaitOutcome.Reserved)
                return null;

            if ((waitOutcome == TraversalWaitOutcome.TimedOut
                 || waitOutcome == TraversalWaitOutcome.LocoMissing
                 || waitOutcome == TraversalWaitOutcome.RouteMissing
                 || waitOutcome == TraversalWaitOutcome.LayoutMissing)
                && _runtimeRegistry.IsRouteActive(route.Id))
            {
                SetRouteRuntimeState(route.Id, RouteRuntimeLifecycleState.Failed);
                await DeactivateRouteInternalAsync(
                    route.Id,
                    updateMessage: true,
                    dccClient: effectiveDccClient,
                    ct: CancellationToken.None,
                    diagnosticReason: waitOutcome switch
                    {
                        TraversalWaitOutcome.TimedOut => $"ČAKANIE ukončené: časový limit v bloku {OperationDisplayHelpers.BlockDisplayName(recoveryTarget)}",
                        TraversalWaitOutcome.LocoMissing => $"ČAKANIE ukončené: vlak {ResolveTrainDisplayName(locoCode)} už nie je dostupný",
                        TraversalWaitOutcome.RouteMissing => $"ČAKANIE ukončené: cesta {FormatActiveRouteDiagnosticLabel(layout, route)} už neexistuje",
                        TraversalWaitOutcome.LayoutMissing => "ČAKANIE ukončené: rozloženie bolo zmenené počas čakania",
                        _ => "ČAKANIE ukončené"
                    },
                    diagnosticLevel: waitOutcome == TraversalWaitOutcome.TimedOut ? DiagnosticLevel.Warning : DiagnosticLevel.Info);
            }

            return RouteActivationResult.Failed(waitOutcome switch
            {
                TraversalWaitOutcome.RouteInactive => "route-inactive",
                TraversalWaitOutcome.RouteMissing => "route-missing",
                TraversalWaitOutcome.LayoutMissing => "layout-missing",
                TraversalWaitOutcome.LocoMissing => "loco-missing",
                TraversalWaitOutcome.TimedOut => "wait-timeout",
                _ => "reservation-missing"
            });
        }

        for (int segmentIndex = 0; segmentIndex < traversalBlockIds.Count - 1; segmentIndex++)
        {
            ct.ThrowIfCancellationRequested();

            if (!blocks.TryGetValue(traversalBlockIds[segmentIndex], out var segmentSource))
                return RouteActivationResult.Failed("source-block-not-found");
            if (!blocks.TryGetValue(traversalBlockIds[segmentIndex + 1], out var segmentTarget))
                return RouteActivationResult.Failed("target-block-not-found");

            UpdateRouteRuntimeForSegment(route.Id, segmentIndex, segmentSource.Id, locoCode, traversalBlockIds);
            ResetTailClearTraversalState(route.Id, segmentSource.Id, segmentTarget.Id);

            var turnoutReady = await TryEnsureTurnoutsForSegmentAsync(
                layout,
                route,
                segmentSource.Id,
                segmentTarget.Id,
                effectiveDccClient,
                ct);
            if (!turnoutReady.IsReady)
            {
                var waitFailure = await WaitForSegmentReservationRecoveryAsync(segmentIndex, segmentSource, segmentTarget, "wait-turnout");
                if (waitFailure != null)
                    return waitFailure;
            }

            // STRIKTNÁ BEZPEČNOSŤ: segment sa nesmie spustiť bez Shadow rezervácie cieľového bloku.
            if (!IsReservedFor(locoCode, segmentTarget))
            {
                // WAIT mode: next block is not reservable right now. Route MUST stay active.
                // Dispatcher enforces STOJ before the conflicting segment and retries reservation periodically.
                var waitFailure = await WaitForSegmentReservationRecoveryAsync(segmentIndex, segmentSource, segmentTarget, "wait-rezervácia");
                if (waitFailure != null)
                    return waitFailure;
            }

            // Segmentový aspekt + speed limit (per segment).
            // Smerovo-striktný výber návestidla: iba signál priradený pre smer jazdy segmentu.
            var segmentTravelDirection = RouteSegmentSignalResolver.ResolveSegmentTravelDirection(layout, route, segmentSource.Id, segmentTarget.Id);
            var segmentSignal = RouteSegmentSignalResolver.ResolveSegmentStartSignal(layout, segmentSource.Id, segmentTravelDirection, segmentTarget.Id);
            var segmentEntryAspect = segmentSignal?.Aspect ?? routeStartAspect;
            var segmentSpeedLimit = SignalController.ResolveSpeedLimitForAspect(segmentEntryAspect);

            var boundaryEntered = false;
            var tailCleared = false;

            if (_runtimeRegistry.IsRouteActive(route.Id) && loco != null)
            {
                // Určíme či je to posledný segment cesty (kde vlak musí brzdiť)
                var isLastSegment = (segmentIndex == traversalBlockIds.Count - 2);

                // FÁZA 3: Vytvor simulationContext pre real-time sync
                var simulationContext = new ActiveSimulationContext();
                _activeSimulations[loco.Code] = simulationContext;

                // Prepojiť CancellationToken: ak sa aplikácia ukončuje ALEBO užívateľ stlačí Cancel
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _globalCts.Token, _panicStopCts.Token);
                var linkedToken = linkedCts.Token;

                try
                {
                    var simulationScale = SimulationScaleResolver.ResolveScaleDivisor(_settings.GetEffective().Scale);
                    await SimulateMoveHeartbeatAsync(
                        loco,
                        route,
                        segmentSource,
                        segmentTarget,
                        segmentEntryAspect,
                        segmentSignal,
                        segmentSpeedLimit,
                        blocks,
                        traversalBlockIds,
                        isLastSegment,
                        simulationScale,
                        linkedToken,
                        isSimulationMode: IsSimulationMode,
                        layout: layout,
                        simulationContext: simulationContext,
                        onSimulatedSensorOccupied: async (blockId) =>
                        {
                            // OPERATION MODE: Simulovaný senzor v Trenažéri
                            // Automaticky zavolá OnBlockOccupiedAsync, aby sa model aktualizoval
                            await OnBlockOccupiedAsync(layout, blockId, effectiveDccClient, linkedToken, sendDcc: false);
                        },
                        onTargetBoundaryEntry: async () =>
                        {
                            if (boundaryEntered)
                                return;

                            boundaryEntered = true;
                            MarkTraversalBoundaryEntry(route.Id, segmentSource.Id, segmentTarget.Id);
                            DiagnoseOrchestrationPass(
                                layout,
                                route.Id,
                                "progression-authoritative-pass",
                                $"flow=[boundary-entry], source=[{OperationDisplayHelpers.BlockDisplayName(segmentSource)}], target=[{OperationDisplayHelpers.BlockDisplayName(segmentTarget)}], lead=[{segmentIndex + 1}]");
                            ApplyBoundaryEntryStateWithoutImmediateRefresh(layout, route, segmentSource, segmentTarget, loco, locoCode);
                            SetTraversalSegmentWindow(layout, route, traversalBlockIds, segmentIndex + 1, keepPreviousSegmentActive: false);
                            LayoutRefreshRequested?.Invoke();
                            await UpdateTraversalSignalWindowAsync(layout, route, traversalBlockIds, segmentIndex + 1, keepPreviousSegmentActive: false, effectiveDccClient, linkedToken);
                            // TraversalEngine kandidát: reservation advance ostáva zatiaľ sekvenovaný traversal boundary-entry callbackom.
                            await AdvanceReservationWindowInternalAsync(layout, route, locoCode, segmentTarget.Id, segmentTarget.AssignedLocoIsForward, source: "ApplyBoundaryEntryState");
                        },
                        onSourceTailClear: async () =>
                        {
                            if (tailCleared)
                                return;

                            tailCleared = true;
                            MarkTraversalTailClear(route.Id, segmentSource.Id, segmentTarget.Id);
                            DiagnoseOrchestrationPass(
                                layout,
                                route.Id,
                                "traversal-refresh-reconcile",
                                $"flow=[tail-clear-reconcile], source=[{OperationDisplayHelpers.BlockDisplayName(segmentSource)}], target=[{OperationDisplayHelpers.BlockDisplayName(segmentTarget)}], lead=[{segmentIndex + 1}]");
                            await ApplyTailClearStateAsync(layout, route, segmentSource, segmentTarget, effectiveDccClient, linkedToken);
                            await HandleExternalOccupancyUpdateAsync(effectiveDccClient, linkedToken);
                            SetTraversalSegmentWindow(layout, route, traversalBlockIds, segmentIndex + 1, keepPreviousSegmentActive: false);
                            await UpdateTraversalSignalWindowAsync(layout, route, traversalBlockIds, segmentIndex + 1, keepPreviousSegmentActive: false, effectiveDccClient, ct);
                        },
                        delayAsync: _movementDelayAsync,
                        onLayoutRefresh: () => LayoutRefreshRequested?.Invoke());
                }
                catch (MovementCommitValidationException)
                {
                    loco.TargetSpeed = 0;
                    loco.CurrentDisplaySpeed = 0;

                    var waitFailure = await WaitForSegmentReservationRecoveryAsync(segmentIndex, segmentSource, segmentTarget, "wait-pre-commit-validation");
                    if (waitFailure != null)
                        return waitFailure;

                    segmentIndex--;
                    continue;
                }
                catch (SimulationSafetyException ex)
                {
                    loco.TargetSpeed = 0;
                    loco.CurrentDisplaySpeed = 0;
                    SetRouteRuntimeState(route.Id, RouteRuntimeLifecycleState.Failed);

                    TrackFlowDoctorService.Instance.Diagnose(
                        "Bezpečnosť",
                        $"⛔ Núdzové zastavenie: {ex.Message}",
                        DiagnosticLevel.Critical);

                    await DeactivateRouteInternalAsync(
                        route.Id,
                        updateMessage: true,
                        dccClient: effectiveDccClient,
                        ct: CancellationToken.None,
                        diagnosticReason: "Núdzové zastavenie",
                        diagnosticLevel: DiagnosticLevel.Critical);

                    LayoutRefreshRequested?.Invoke();
                    return RouteActivationResult.Failed(ex.SafetyReason);
                }
                catch (OperationCanceledException)
                {
                    // Zrušenie pohybu izoluj iba na aktuálnu cestu.
                    return await HandleRouteLocalCancellationAsync(route, loco, effectiveDccClient, "segment-movement");
                }
                finally
                {
                    // FÁZA 3: Cleanup simulationContext po skončení simulácie
                    _activeSimulations.Remove(loco.Code);
                }
            }
            else
            {
                await _movementDelayAsync(300, ct);
                await _movementDelayAsync(650, ct);
                await _movementDelayAsync(350, ct);

                if (!boundaryEntered)
                {
                    try
                    {
                        boundaryEntered = true;
                        MarkTraversalBoundaryEntry(route.Id, segmentSource.Id, segmentTarget.Id);
                        DiagnoseOrchestrationPass(
                            layout,
                            route.Id,
                            "progression-authoritative-pass",
                            $"flow=[boundary-entry], source=[{OperationDisplayHelpers.BlockDisplayName(segmentSource)}], target=[{OperationDisplayHelpers.BlockDisplayName(segmentTarget)}], lead=[{segmentIndex + 1}]");
                        ApplyBoundaryEntryStateWithoutImmediateRefresh(layout, route, segmentSource, segmentTarget, loco, locoCode);
                        SetTraversalSegmentWindow(layout, route, traversalBlockIds, segmentIndex + 1, keepPreviousSegmentActive: false);
                        LayoutRefreshRequested?.Invoke();
                        await UpdateTraversalSignalWindowAsync(layout, route, traversalBlockIds, segmentIndex + 1, keepPreviousSegmentActive: false, effectiveDccClient, ct);

                        // Jediný autoritatívny trigger reservation advance je boundary-entry occupancy transition.
                        // TraversalEngine kandidát: reservation advance ostáva zatiaľ sekvenovaný traversal boundary-entry callbackom.
                        await AdvanceReservationWindowInternalAsync(layout, route, locoCode, segmentTarget.Id, segmentTarget.AssignedLocoIsForward, source: "ApplyBoundaryEntryState");
                    }
                    catch (MovementCommitValidationException)
                    {
                        if (loco != null)
                        {
                            loco.TargetSpeed = 0;
                            loco.CurrentDisplaySpeed = 0;
                        }

                        var waitFailure = await WaitForSegmentReservationRecoveryAsync(segmentIndex, segmentSource, segmentTarget, "wait-pre-commit-validation");
                        if (waitFailure != null)
                            return waitFailure;

                        segmentIndex--;
                        continue;
                    }
                }

                if (!tailCleared)
                {
                    tailCleared = true;
                    MarkTraversalTailClear(route.Id, segmentSource.Id, segmentTarget.Id);
                    DiagnoseOrchestrationPass(
                        layout,
                        route.Id,
                        "traversal-refresh-reconcile",
                        $"flow=[tail-clear-local], source=[{OperationDisplayHelpers.BlockDisplayName(segmentSource)}], target=[{OperationDisplayHelpers.BlockDisplayName(segmentTarget)}], lead=[{segmentIndex + 1}]");
                    await ApplyTailClearStateAsync(layout, route, segmentSource, segmentTarget, effectiveDccClient, ct);
                    // ODSTRÁNENÉ: HandleExternalOccupancyUpdateAsync - TailClear už poslal všetky DCC príkazy.
                    SetTraversalSegmentWindow(layout, route, traversalBlockIds, segmentIndex + 1, keepPreviousSegmentActive: false);
                    await UpdateTraversalSignalWindowAsync(layout, route, traversalBlockIds, segmentIndex + 1, keepPreviousSegmentActive: false, effectiveDccClient, ct);
                }
            }

            if (!tailCleared)
            {
                tailCleared = true;
                MarkTraversalTailClear(route.Id, segmentSource.Id, segmentTarget.Id);
                DiagnoseOrchestrationPass(
                    layout,
                    route.Id,
                    "traversal-refresh-reconcile",
                    $"flow=[tail-clear-final], source=[{OperationDisplayHelpers.BlockDisplayName(segmentSource)}], target=[{OperationDisplayHelpers.BlockDisplayName(segmentTarget)}], lead=[{segmentIndex + 1}]");
                await ApplyTailClearStateAsync(layout, route, segmentSource, segmentTarget, effectiveDccClient, ct);
                // ODSTRÁNENÉ: HandleExternalOccupancyUpdateAsync - TailClear už poslal všetky DCC príkazy.
                SetTraversalSegmentWindow(layout, route, traversalBlockIds, segmentIndex + 1, keepPreviousSegmentActive: false);
                await UpdateTraversalSignalWindowAsync(layout, route, traversalBlockIds, segmentIndex + 1, keepPreviousSegmentActive: false, effectiveDccClient, ct);
            }

            // OPRAVA: RefreshSignalsAsync sa NESMIE volať počas behu aktívnej cesty.
            // ApplyTailClearStateAsync už poslala Stop na uvoľnené návestidlá (force-send).
            // UpdateTraversalSignalWindowAsync nastaví permissive aspekty pre ďalšie segmenty.
            // Návestidlá aktívnej cesty musia byť pod exkluzívnou kontrolou RouteManager.
        }

        await RefreshSignalStatusAsync(effectiveDccClient, ct);
        if (_traversalEngine.IsTraversalComplete(route.Id, route))
            _traversalEngine.SetTraversalLifecycleState(route.Id, RouteRuntimeLifecycleState.Completed);
        await DeactivateRouteInternalAsync(
            route.Id,
            updateMessage: false,
            effectiveDccClient,
            ct,
            diagnosticReason: $"vlak {ResolveTrainDisplayName(locoCode)} dorazil do bloku {OperationDisplayHelpers.BlockDisplayName(targetBlock)}",
            diagnosticLevel: DiagnosticLevel.Success);
        ClearLocoReservations(layout, locoCode);
        SetRouteActivationMessage("loco-moved");
        MarkDirty();
        LayoutRefreshRequested?.Invoke();

        return RouteActivationResult.Success();
    }

    public async Task<RouteActivationResult> MoveLocomotiveByRouteElementAsync(
        RouteElement routeElement,
        IDccCentralClient? dccClient = null,
        CancellationToken ct = default)
    {
        var layout = _settings.CurrentProject?.Layout;
        if (layout == null)
        {
            TrackFlowDoctorService.Instance.Diagnose("Cesta",
                "Klik na cestu ignorovaný: nie je otvorený projekt.", DiagnosticLevel.Warning);
            return RouteActivationResult.Failed("no-project");
        }

        // Vyriešim cieľovú definíciu cesty z markeru.
        var preferred = string.IsNullOrWhiteSpace(routeElement.SelectedRouteDefinitionId)
            ? null
            : layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeElement.SelectedRouteDefinitionId, StringComparison.OrdinalIgnoreCase));
        if (preferred == null)
        {
            TrackFlowDoctorService.Instance.Diagnose("Cesta",
                "Klik na cestu ignorovaný: marker nemá vybranú definíciu cesty.", DiagnosticLevel.Warning);
            return RouteActivationResult.Failed("route-not-selected");
        }

        string from = !string.IsNullOrWhiteSpace(preferred.FromBlockId) ? preferred.FromBlockId : preferred.BlockIds.FirstOrDefault() ?? string.Empty;
        string to = !string.IsNullOrWhiteSpace(preferred.ToBlockId) ? preferred.ToBlockId : preferred.BlockIds.LastOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            TrackFlowDoctorService.Instance.Diagnose("Cesta",
                "Klik na cestu ignorovaný: cesta nie je správne nakonfigurovaná (chýba From/To).",
                DiagnosticLevel.Warning);
            return RouteActivationResult.Failed("route-not-configured");
        }

        var fromName = OperationDisplayHelpers.ResolveBlockDisplayName(layout, from);
        var toName = OperationDisplayHelpers.ResolveBlockDisplayName(layout, to);

        TrackFlowDoctorService.Instance.Diagnose("Cesta",
            $"Klik na marker cesty: vybraná cesta={FormatRouteNameForDiagnostic(layout, preferred)}, " +
            $"{fromName} → {toName}, " +
            $"blocks=[{string.Join("→", preferred.BlockIds.Select(id => OperationDisplayHelpers.ResolveBlockDisplayName(layout, id)))}]",
            DiagnosticLevel.Info);

        // 1) Skús vybranú lokomotívu zo SmartStripu, ak je v jednom z koncových blokov cesty.
        var blocks = layout.Elements.OfType<BlockElement>().ToList();
        var fromBlock = blocks.FirstOrDefault(b => string.Equals(b.Id, from, StringComparison.OrdinalIgnoreCase));
        var toBlock = blocks.FirstOrDefault(b => string.Equals(b.Id, to, StringComparison.OrdinalIgnoreCase));

        BlockElement? sourceBlock = null;
        string? locoCode = null;

        var selected = SelectedLoco;
        if (selected != null && !string.IsNullOrWhiteSpace(selected.Code))
        {
            if (fromBlock != null && string.Equals(fromBlock.AssignedLocoId, selected.Code, StringComparison.OrdinalIgnoreCase))
            {
                sourceBlock = fromBlock;
                locoCode = selected.Code;
            }
            else if (toBlock != null && string.Equals(toBlock.AssignedLocoId, selected.Code, StringComparison.OrdinalIgnoreCase))
            {
                sourceBlock = toBlock;
                locoCode = selected.Code;
            }
        }

        // 2) Fallback: použi lokomotívu reálne priradenú v štartovom/koncovom bloku cesty.
        //    Týmto klik na cestu funguje aj keď používateľ vybral inú loko v paneli (alebo žiadnu).
        if (sourceBlock == null)
        {
            if (fromBlock != null && !string.IsNullOrWhiteSpace(fromBlock.AssignedLocoId))
            {
                sourceBlock = fromBlock;
                locoCode = fromBlock.AssignedLocoId;
            }
            else if (toBlock != null && !string.IsNullOrWhiteSpace(toBlock.AssignedLocoId))
            {
                sourceBlock = toBlock;
                locoCode = toBlock.AssignedLocoId;
            }
        }

        if (sourceBlock == null || string.IsNullOrWhiteSpace(locoCode))
        {
            TrackFlowDoctorService.Instance.Diagnose("Cesta",
                $"Klik na cestu {fromName} → {toName} ignorovaný: ani v jednom z koncových blokov nie je priradená žiadna lokomotíva.",
                DiagnosticLevel.Warning);
            return RouteActivationResult.Failed("source-block-not-found");
        }

        // Auto-select: zosynchronizuj SmartStrip výber s reálne použitou lokomotívou,
        // aby ďalšie kroky (smer, dashboard) pracovali konzistentne.
        if (selected == null || !string.Equals(selected.Code, locoCode, StringComparison.OrdinalIgnoreCase))
        {
            var resolved = Locomotives.FirstOrDefault(l => string.Equals(l.Code, locoCode, StringComparison.OrdinalIgnoreCase));
            if (resolved != null)
                SelectedLoco = resolved;
        }

        var trainName = ResolveTrainDisplayName(locoCode);
        TrackFlowDoctorService.Instance.Diagnose("Cesta",
            $"Klik na cestu {fromName} → {toName} pre vlak {trainName}.",
            DiagnosticLevel.Info);

        if (string.Equals(sourceBlock.Id, from, StringComparison.OrdinalIgnoreCase))
            return await MoveLocomotiveBetweenBlocksAsync(locoCode, from, to, dccClient, ct, preferred.Id);

        if (string.Equals(sourceBlock.Id, to, StringComparison.OrdinalIgnoreCase))
            return await MoveLocomotiveBetweenBlocksAsync(locoCode, to, from, dccClient, ct, preferred.Id);

        TrackFlowDoctorService.Instance.Diagnose("Cesta",
            $"Vlak {trainName} nestojí v {fromName} ani v {toName} — cestu nie je možné spustiť.",
            DiagnosticLevel.Warning);
        return RouteActivationResult.Failed("source-not-on-route");
    }

    public async Task<CollisionCheckResult> AssignLocomotiveToBlockAsync(
        string locoCode,
        string targetBlockId,
        bool isForward,
        IDccCentralClient? dccClient = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(locoCode))
            return CollisionCheckResult.Blocked("loco-code-missing");

        var layout = _settings.CurrentProject?.Layout;
        if (layout == null)
            return CollisionCheckResult.Blocked("no-project");

        var targetBlock = layout.Elements.OfType<BlockElement>()
            .FirstOrDefault(b => string.Equals(b.Id, targetBlockId, System.StringComparison.OrdinalIgnoreCase));
        if (targetBlock == null)
            return CollisionCheckResult.Blocked("target-block-not-found");

        // Manuálne priradenie lokomotívy v Prevádzke NIE JE jazda po ceste.
        // Preto NEPOUŽÍVAME runtime kolíznu kontrolu (`EvaluateBlockEntry`) – tá je platná pre
        // smerovanie vlaku do cieľového bloku a hlási „cieľový blok je obsadený“ aj v prípade,
        // keď blok len fyzicky obsadil senzor (AssignedLocoId=null). Pri priradení je to však
        // presne ten scenár, kde si používateľ chce „prevziať“ ghost-obsadenie pre svoju loko.
        //
        // Reálne dôvody zamietnuť priradenie sú:
        //   1) blok je zamknutý aktívnou cestou,
        //   2) blok už patrí inej lokomotíve (cez block.AssignedLocoId),
        //   3) v bloku už stojí iná lokomotíva, ktorej `loco.AssignedBlockId` ukazuje na tento
        //      blok (napr. po prepnutí Simulátor/Live sa block.AssignedLocoId vyčistí, ale
        //      lokomotívy si nesú svoju pozíciu naďalej – inak by sa stojaca loko mlčky
        //      „stratila“ a sensor-obsadenosť by bola prepísaná falošným AssignedLocoId).
        if (targetBlock.IsLocked)
        {
            var locked = CollisionCheckResult.Blocked("assign-block-locked", targetBlock.Id);
            SetRouteActivationMessage(locked.Reason);
            return locked;
        }

        if (!string.IsNullOrWhiteSpace(targetBlock.AssignedLocoId)
            && !string.Equals(targetBlock.AssignedLocoId, locoCode, System.StringComparison.OrdinalIgnoreCase))
        {
            var occupiedByOther = CollisionCheckResult.Blocked("assign-block-other-loco", targetBlock.Id);
            SetRouteActivationMessage(occupiedByOther.Reason);
            return occupiedByOther;
        }

        var standingOther = Locomotives.FirstOrDefault(l =>
            l != null
            && !string.Equals(l.Code, locoCode, System.StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(l.AssignedBlockId)
            && string.Equals(l.AssignedBlockId, targetBlock.Id, System.StringComparison.OrdinalIgnoreCase));
        if (standingOther != null)
        {
            var occupiedByStanding = CollisionCheckResult.Blocked("assign-block-other-loco", targetBlock.Id);
            SetRouteActivationMessage(occupiedByStanding.Reason);
            TrackFlowDoctorService.Instance.Diagnose(
                "Senzor",
                $"Priradenie do bloku {OperationDisplayHelpers.BlockDisplayName(targetBlock)} odmietnuté – v bloku už stojí lokomotíva {(!string.IsNullOrWhiteSpace(standingOther.DisplayName) ? standingOther.DisplayName : standingOther.Code)}.",
                DiagnosticLevel.Warning);
            return occupiedByStanding;
        }

        var collision = CollisionCheckResult.Safe();

         foreach (var other in layout.Elements.OfType<BlockElement>())
         {
             if (other == targetBlock || !string.Equals(other.AssignedLocoId, locoCode, System.StringComparison.OrdinalIgnoreCase))
                 continue;

             other.AssignedLocoId = null;
             other.IsOccupied = false;
             other.AssignedLocoIsForward = true;
             ClearShadowReservation(other);
             other.IsDragOverActive = false;
             break;
         }

        targetBlock.AssignedLocoId = locoCode;
        targetBlock.AssignedLocoIsForward = isForward;
        // Manuálne priradenie predstavuje iba logistické umiestnenie vlaku do bloku.
        // Fyzickú obsadenosť potvrdzuje senzor / externý occupancy update – nesmieme však
        // existujúce IsOccupied=true zhodiť na false, lebo by sme tým zmazali platné
        // sensor-detegované obsadenie a blok by sa podfarbil ako „len priradený“ (žltý)
        // namiesto „obsadený“. Ponechávame teda aktuálny stav IsOccupied bez zmeny.
        ClearShadowReservation(targetBlock);
        targetBlock.IsDragOverActive = false;

        var loco = Locomotives.FirstOrDefault(l => string.Equals(l.Code, locoCode, System.StringComparison.OrdinalIgnoreCase));
        if (loco != null)
        {
            loco.IsPlacedOnTrack = true;
            loco.AssignedBlockId = targetBlock.Id;
        }

        string trainDisplayName = !string.IsNullOrWhiteSpace(loco?.DisplayName)
            ? loco!.DisplayName
            : ResolveTrainDisplayName(locoCode);

        var occupancyNote = targetBlock.IsOccupied
            ? "sensor-obsadenosť ponechaná"
            : "bez potvrdenej obsadenosti zo senzora / centrály";
        TrackFlowDoctorService.Instance.Diagnose(
            "Senzor",
            $"Blok {OperationDisplayHelpers.BlockDisplayName(targetBlock)} PRIRADENÝ (Vlak: {trainDisplayName}) – {occupancyNote}.",
            DiagnosticLevel.Info);

        await RefreshSignalStatusAsync(dccClient, ct);
        MarkDirty();
        LayoutRefreshRequested?.Invoke();
        return collision;
    }

    private async Task SimulateMoveHeartbeatAsync(
        Locomotive loco,
        RouteDefinition route,
        BlockElement sourceBlock,
        BlockElement targetBlock,
        SignalAspect entryAspect,
        SignalElement? entrySignal,
        int routeSpeedLimit,
        IReadOnlyDictionary<string, BlockElement> blocks,
        IReadOnlyList<string> drivingOrder,
        bool isLastSegment,
        double distanceScale,
        CancellationToken ct,
        bool isSimulationMode = false,
        TrackLayout? layout = null,
        ActiveSimulationContext? simulationContext = null,
        Func<string, Task>? onSimulatedSensorOccupied = null,
        Func<Task>? onTargetBoundaryEntry = null,
        Func<Task>? onSourceTailClear = null,
        Func<int, CancellationToken, Task>? delayAsync = null,
        Action? onLayoutRefresh = null)
    {
        delayAsync ??= Task.Delay;

        static bool IsReservedFor(string code, BlockElement b)
            => !string.IsNullOrWhiteSpace(code)
               && b.IsShadowSet
               && string.Equals(b.ReservedLocoId, code, StringComparison.OrdinalIgnoreCase);

        void EmergencyBrakeNow(string safetyReason, string doctorMessage)
        {
            loco.TargetSpeed = 0;
            loco.CurrentDisplaySpeed = 0;

            TrackFlowDoctorService.Instance.Diagnose(
                "Bezpečnosť",
                "⛔ " + doctorMessage,
                DiagnosticLevel.Critical);

            throw new SimulationSafetyException(safetyReason, doctorMessage);
        }

        var rawMarkerProfile = ResolveMarkerProfile(route, sourceBlock, targetBlock);
        var blockLengthMm = TrackFlow.Services.Simulation.MovementBlockLength.ResolveMovementBlockLengthMm(targetBlock, isSimulationMode);
        var markerProfile = ResolveEffectiveMarkerProfile(rawMarkerProfile, targetBlock, blockLengthMm, isSimulationMode);
        var hasMarkers = markerProfile.HasAnyMarker;
        var simulationFallbackProfile = CreateSimulationFallbackMarkerProfile(blockLengthMm);
        var useSimulationFallbackMarkers = isSimulationMode && !hasMarkers;

        TimeService.Instance.SimulationSpeedFactor =
            _settings.CurrentProject?.Settings.SimulationSpeedFactor
            ?? ProjectSettingsData.DefaultSimulationSpeedFactor;
        var previousModelTime = TimeService.Instance.CurrentModelTime;

        async Task<double> WaitForNextModelDeltaSecondsAsync()
        {
            await delayAsync(MovementHeartbeatMs, ct);

            TimeService.Instance.SimulationSpeedFactor =
                _settings.CurrentProject?.Settings.SimulationSpeedFactor
                ?? ProjectSettingsData.DefaultSimulationSpeedFactor;

            var currentModelTime = TimeService.Instance.CurrentModelTime;

            // Unit testy používajú nulové oneskorenie pohybu. Ak globálny timer ešte nestihol
            // posunúť modelový čas a hodiny nie sú pauznuté, posuň čas o ekvivalent heartbeatu
            // cez TimeService, aby aj testovací seam ostal deterministický.
            if (currentModelTime <= previousModelTime && !TimeService.Instance.IsPaused)
            {
                TimeService.Instance.AdvanceModelTime(TimeSpan.FromMilliseconds(MovementHeartbeatMs));
                currentModelTime = TimeService.Instance.CurrentModelTime;
            }

            if (currentModelTime <= previousModelTime)
            {
                previousModelTime = currentModelTime;
                return 0.0;
            }

            var dt = (currentModelTime - previousModelTime).TotalSeconds;
            previousModelTime = currentModelTime;
            return dt;
        }

        var maxBlockSpeed = Math.Clamp(targetBlock.MaxSpeedKmh, 0, 100);
        var restrictedSpeed = Math.Clamp(targetBlock.ResSpeedKmh, 0, 100);
        var limitedCruise = Math.Clamp(routeSpeedLimit > 0 ? routeSpeedLimit : maxBlockSpeed, 0, 100);
        
        // KRITICKÁ OPRAVA: Ak je toto posledný segment, vypočítaj maximálnu bezpečnú vstupnú rýchlosť
        // aby vlak mohol bezpečne zabrzdiť v cieľovom bloku
        double boundaryCruiseLimit = limitedCruise;
        if (isLastSegment && !hasMarkers)
        {
            // Maximálna bezpečná vstupná rýchlosť: V_max = sqrt(2 * decel * distance)
            // Pre bezpečnosť použijeme 70% dĺžky bloku ako brzdnú dráhu
            var availableBrakingDistanceMm = blockLengthMm * 0.70;
            var maxSafeEntrySpeed = (int)Math.Round(SimulationBrakingMath.CalculateMaxEntrySpeed(availableBrakingDistanceMm, DisplayRampKmhPerSecond, distanceScale));
            boundaryCruiseLimit = Math.Min((double)limitedCruise, (double)maxSafeEntrySpeed);
            
            TrackFlowDoctorService.Instance.Diagnose(
                "Brzdenie",
                $"Posledný segment: max. bezpečná vstupná rýchlosť {maxSafeEntrySpeed:F1} km/h " +
                $"(blok {blockLengthMm/10:F0}cm, brzdná dráha {availableBrakingDistanceMm/10:F0}cm, mierka 1:{distanceScale:F0})",
                DiagnosticLevel.Info);
        }
        else
        {
            boundaryCruiseLimit = Math.Clamp(Math.Max(restrictedSpeed, loco.CurrentDisplaySpeed), 0, limitedCruise);
        }
        var fallbackCruise = boundaryCruiseLimit;

        // Pre-entry vzdialenosť + cieľový blok = celková dráha
        var preEntryDistanceMm = ResolvePreEntryDistanceMm(sourceBlock, isSimulationMode);
        var totalDistanceMm = preEntryDistanceMm + blockLengthMm;

        // === EVENT-DRIVEN ENGINE: Jeden engine pre celý segment ===
        var engine = new LocomotiveSimulationEngine(
            totalLengthMm: totalDistanceMm,
            accelerationStepKmh: DisplayRampKmhPerSecond,
            initialSpeedKmh: Math.Clamp(loco.CurrentDisplaySpeed, 0, 100),
            distanceScale: distanceScale);

        // FÁZA 3: Registrácia enginu do contextu pre real-time sync z OnBlockOccupiedAsync
        if (simulationContext != null)
        {
            simulationContext.Engine = engine;
            simulationContext.TargetBlockId = targetBlock.Id;
            simulationContext.PreEntryDistanceMm = preEntryDistanceMm;
            simulationContext.BoundaryEntryTriggered = false;
        }

        // Boundary Triggers
        var boundaryEntryTriggered = false;
        var tailClearTriggered = false;
        var tailClearTriggerMm = preEntryDistanceMm + (ResolveTailClearTriggerCm(markerProfile, blockLengthMm / 10.0) * 10.0);

        // Kontrola next block pre isLastSegment override
        var hasNextBlockReserved = TrackFlow.Services.Operation.BlockLookAheadHelper.IsNextBlockReservedForLoco(drivingOrder, targetBlock.Id, loco.Code, blocks);
        var requiresStop = !hasMarkers && isLastSegment && !hasNextBlockReserved;

        // === HLAVNÝ EVENT-DRIVEN CYKLUS: Engine počíta, ViewModel reaguje na hranice ===
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var dtSec = await WaitForNextModelDeltaSecondsAsync();
            if (dtSec <= 0.0)
            {
                if (onLayoutRefresh != null)
                    await InvokeLayoutRefreshAsync(onLayoutRefresh);

                continue;
            }

            // Stop sa vyhodnocuje len ak existuje "reálne" segmentové návestidlo.
            // Návestidlo bez platnej DCC adresy považujeme za model-only placeholder (ne-enforcing).
            var signalAspectNow = entrySignal?.Aspect ?? entryAspect;
            var hasStopSignal = entrySignal != null
                               && entrySignal.HasValidDccAddress()
                               && signalAspectNow == SignalAspect.Stop;

            var currentDistanceMm = engine.CurrentDistanceMm;
            var blockDistanceMm = Math.Max(0.0, currentDistanceMm - preEntryDistanceMm);
            var travelledCm = blockDistanceMm / 10.0;
            
            // === ÚLOHA 2: DYNAMICKÝ FLYING SWITCH ===
            // V každom tiku kontroluj, či sa next block uvoľnil/rezervoval
            hasNextBlockReserved = TrackFlow.Services.Operation.BlockLookAheadHelper.IsNextBlockReservedForLoco(drivingOrder, targetBlock.Id, loco.Code, blocks);
            requiresStop = !hasMarkers && isLastSegment && !hasNextBlockReserved;

            // === BOUNDARY ENTRY TRIGGER: Prvý vstup do targetBlock ===
            if (!boundaryEntryTriggered && currentDistanceMm >= preEntryDistanceMm)
            {
                // FAIL-SAFE: zákaz vstupu pri STOJ/ČERVENÁ.
                if (hasStopSignal)
                {
                    engine.CurrentDistanceMm = Math.Min(engine.CurrentDistanceMm, Math.Max(0.0, preEntryDistanceMm - 0.1));
                    engine.CurrentSpeedKmh = 0;
                    EmergencyBrakeNow(
                        "signal-stop",
                        $"Vstup do {OperationDisplayHelpers.BlockDisplayName(targetBlock)} zablokovaný: návestidlo je na {signalAspectNow.ToSlovakName()}."
                    );
                }

                // FAIL-SAFE: zákaz vstupu bez rezervácie.
                if (!IsReservedFor(loco.Code, targetBlock))
                {
                    engine.CurrentDistanceMm = Math.Min(engine.CurrentDistanceMm, Math.Max(0.0, preEntryDistanceMm - 0.1));
                    engine.CurrentSpeedKmh = 0;
                    ValidateMovementPreCommitReservationOrThrow(layout!, route.Id, targetBlock, loco.Code);
                }

                boundaryEntryTriggered = true;

                // FÁZA 3: Označ v contexto, že boundary entry bol triggered
                if (simulationContext != null)
                    simulationContext.BoundaryEntryTriggered = true;

                // Bezpečnostná kontrola: IsShadowSet má prednosť pred vizuálnym aspektom
                if (entryAspect == SignalAspect.Stop)
                {
                    if (!targetBlock.IsShadowSet)
                    {
                        TrackFlowDoctorService.Instance.Diagnose(
                            "Bezpečnosť",
                            $"VAROVANIE: Vlak vstupuje do bloku {OperationDisplayHelpers.BlockDisplayName(targetBlock)}, ale návestidlo ukazuje {entryAspect.ToSlovakName()}",
                            DiagnosticLevel.Warning);
                    }
                }
                
                // === OPERATION MODE: SIMULÁCIA SENZOROV (Trenažér) ===
                // V Simulation režime automaticky simulujeme fyzický senzor obsadenia
                if (isSimulationMode && onSimulatedSensorOccupied != null && layout != null)
                {
                    TrackFlowDoctorService.Instance.Diagnose(
                        "Simulátor",
                        $"SIMULOVANÝ SENZOR: Blok {OperationDisplayHelpers.BlockDisplayName(targetBlock)} obsadený",
                        DiagnosticLevel.Info);
                    
                    // Fire-and-forget: simulovaný senzor musí bežať na UI threade,
                    // pretože callback mení bloky, lokomotívy a ObservableCollection-bound stav.
                    _ = Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        try 
                        { 
                            await onSimulatedSensorOccupied(targetBlock.Id); 
                        }
                        catch (Exception ex)
                        {
                            TrackFlowDoctorService.Instance.Diagnose(
                                "Simulátor",
                                $"⚠ Chyba pri simulovanom senzore: {ex.Message}",
                                DiagnosticLevel.Warning);
                        }
                    }, DispatcherPriority.Normal);
                }

                // Okamžite spusť boundary entry akciu
                if (onTargetBoundaryEntry != null)
                    await onTargetBoundaryEntry();
            }

            // === TAIL CLEAR TRIGGER: Fire-and-forget ===
            if (!tailClearTriggered && currentDistanceMm >= tailClearTriggerMm && onSourceTailClear != null)
            {
                tailClearTriggered = true;
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try { await onSourceTailClear(); }
                    catch { /* ignore */ }
                }, DispatcherPriority.Normal);
            }

            // === DYNAMICKÁ TARGETSPEED: Reaguje na pozíciu v bloku ===
            double targetSpeed;
            
            // Pred boundary entry: plynulá akcelerácia k limitedCruise
            if (!boundaryEntryTriggered)
            {
                targetSpeed = hasStopSignal ? 0 : limitedCruise;
            }
            // Po boundary entry: marker-based alebo requires-stop logika
            else if (hasMarkers)
            {
                if (markerProfile.StopCm > 0 && travelledCm >= markerProfile.StopCm)
                    targetSpeed = 0;
                else if (markerProfile.BrakingCm > 0 && travelledCm >= markerProfile.BrakingCm)
                    targetSpeed = Math.Clamp(restrictedSpeed, 0, boundaryCruiseLimit);
                else if (markerProfile.DistanceCm > 0 && travelledCm >= markerProfile.DistanceCm)
                    targetSpeed = boundaryCruiseLimit;
                else
                    targetSpeed = boundaryCruiseLimit;
            }
            else if (requiresStop)
            {
                if (useSimulationFallbackMarkers)
                {
                    // Simulation-only fallback: ak používateľ nezadal markery, dosadíme virtuálne body
                    // brzdenia/zastavenia voči konštantnej dĺžke bloku. Nezasahuje to do smerovania,
                    // portov návestidiel, rezervácií ani critical sections.
                    if (simulationFallbackProfile.StopCm > 0 && travelledCm >= simulationFallbackProfile.StopCm)
                    {
                        targetSpeed = 0;
                    }
                    else if (simulationFallbackProfile.BrakingCm > 0 && travelledCm >= simulationFallbackProfile.BrakingCm)
                    {
                        var brakingStartMm = simulationFallbackProfile.BrakingCm * 10.0;
                        var stopMm = Math.Max(brakingStartMm + 1.0, simulationFallbackProfile.StopCm * 10.0);
                        var remainingToStopMm = Math.Max(0.0, stopMm - blockDistanceMm);
                        var brakingLengthMm = Math.Max(1.0, stopMm - brakingStartMm);
                        var ratio = Math.Clamp(remainingToStopMm / brakingLengthMm, 0.0, 1.0);
                        targetSpeed = remainingToStopMm <= SimulationStopSnapMm
                            ? 0
                            : Math.Clamp(boundaryCruiseLimit * ratio, 0, 100);
                    }
                    else
                    {
                        targetSpeed = boundaryCruiseLimit;
                    }
                }
                else
                {
                    // === ÚLOHA 3: INTELIGENTNÉ BRZDENIE (Safety Distance) ===
                    // Posledný blok cesty - progresívne brzdenie

                    // Výpočet brzdnej vzdialenosti pri aktuálnej rýchlosti
                    var currentSpeed = engine.CurrentSpeedKmh;
                    var brakingDistanceMm = SimulationBrakingMath.CalculateBrakingDistanceMm(currentSpeed, DisplayRampKmhPerSecond, distanceScale);
                    var remainingDistanceMm = Math.Max(0.0, blockLengthMm - blockDistanceMm);

                    // FIX: Brzdenie začína dynamicky na základe brzdnej dráhy (nie fixných 70%)
                    // Pridáme bezpečnostnú rezervu 30%
                    var safetyMarginMm = brakingDistanceMm * 1.3;
                    var brakingStartMm = Math.Max(blockLengthMm * 0.50, blockLengthMm - safetyMarginMm);

                    // KRITICKÁ KONTROLA: Ak brzdná dráha > zostávajúca vzdialenosť -> Emergency Stop
                    // Ale hlás len ak je prekročenie väčšie než 20% (drobné odchýlky sú normálne)
                    var overshootPercent = remainingDistanceMm > 0
                        ? ((brakingDistanceMm - remainingDistanceMm) / remainingDistanceMm) * 100.0
                        : 100.0;
                    if (brakingDistanceMm > remainingDistanceMm && currentSpeed > 5.0)
                    {
                        if (overshootPercent > 20.0)
                        {
                            TrackFlowDoctorService.Instance.Diagnose(
                                "Bezpečnosť",
                                $"⚠ KRITICKÁ: Vlak {loco.DisplayName} má nedostatočnú brzdnú dráhu! " +
                                $"Brzdná vzdialenosť: {brakingDistanceMm / 10.0:F1}cm, Zostávajúca: {remainingDistanceMm / 10.0:F1}cm, Prekročenie: {overshootPercent:F0}%. Núdzové brzdenie!",
                                DiagnosticLevel.Critical);
                        }

                        // Núdzové brzdenie - okamžite na nulu
                        targetSpeed = 0;
                    }
                    else if (blockDistanceMm >= brakingStartMm)
                    {
                        // Normálne progresívne brzdenie
                        var brakingLengthMm = Math.Max(1.0, blockLengthMm - brakingStartMm);
                        var ratio = Math.Clamp(remainingDistanceMm / brakingLengthMm, 0.0, 1.0);
                        targetSpeed = Math.Clamp((int)Math.Round(fallbackCruise * ratio), 0, 100);
                    }
                    else
                    {
                        targetSpeed = boundaryCruiseLimit;
                    }
                }
            }
            else
            {
                // FLYING SWITCH: Plynulý prechod medzi blokmi
                targetSpeed = boundaryCruiseLimit;
            }

            // Engine počíta fyziku, ViewModel len nastavuje cieľ
            var result = engine.Update(targetSpeed, dtSec);

            loco.TargetSpeed = (int)Math.Round(targetSpeed);
            loco.CurrentDisplaySpeed = Math.Clamp((int)Math.Round(result.SpeedKmh), 0, 100);

            // Anti-deadlock: ak je návestidlo na STOJ/ČERVENÁ a vlak už stojí pred hranicou,
            // segment musí skončiť fail-safe.
            if (!boundaryEntryTriggered && hasStopSignal && loco.CurrentDisplaySpeed == 0)
            {
                engine.CurrentSpeedKmh = 0;
                EmergencyBrakeNow(
                    "signal-stop",
                    $"Vlak {loco.DisplayName} zastavil pred návestidlom na {signalAspectNow.ToSlovakName()} (pred {OperationDisplayHelpers.BlockDisplayName(targetBlock)})."
                );
            }

            // === OKAMŽITÁ UI SYNCHRONIZÁCIA: Dispatcher zabezpečí render priority ===
            if (onLayoutRefresh != null)
            {
                await InvokeLayoutRefreshAsync(onLayoutRefresh);
            }

            // Podmienky na zastavenie cyklu.
            // Engine má minimálny posun 1mm/tik, preto DeltaMm nie je vhodný indikátor státia.
            if (targetSpeed == 0 && loco.CurrentDisplaySpeed == 0)
            {
                if (hasMarkers && markerProfile.StopCm > 0 && travelledCm >= markerProfile.StopCm)
                    break;
                if (useSimulationFallbackMarkers && simulationFallbackProfile.StopCm > 0 && travelledCm >= simulationFallbackProfile.StopCm)
                    break;
                if (requiresStop)
                    break;
            }

            // Koniec segmentu - vlak prešiel celú dráhu
            if (result.IsAtEnd || blockDistanceMm >= blockLengthMm)
                break;

        }

        // Tail Clear fallback (safety)
        if (!tailClearTriggered && onSourceTailClear != null)
        {
            // FIX: Callback MUSÍ bežať na UI threade
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try { await onSourceTailClear(); }
                catch { /* ignore */ }
            }, DispatcherPriority.Normal);
        }

        // === FINÁLNE ZASTAVENIE: Iba ak je requiresStop ===
        if (requiresStop && loco.CurrentDisplaySpeed > 0)
        {
            while (loco.CurrentDisplaySpeed > 0)
            {
                ct.ThrowIfCancellationRequested();

                var dtSec = await WaitForNextModelDeltaSecondsAsync();
                if (dtSec <= 0.0)
                {
                    if (onLayoutRefresh != null)
                        await InvokeLayoutRefreshAsync(onLayoutRefresh);

                    continue;
                }
                
                var result = engine.Update(0, dtSec);
                loco.TargetSpeed = 0;
                loco.CurrentDisplaySpeed = Math.Max(0, (int)Math.Round(result.SpeedKmh));

                if (onLayoutRefresh != null)
                {
                    await InvokeLayoutRefreshAsync(onLayoutRefresh);
                }
            }
        }
        
        // FÁZA 3: Cleanup simulationContext
        if (simulationContext != null)
        {
            simulationContext.Engine = null;
            simulationContext.TargetBlockId = null;
        }
    }

    // CalculateBrakingDistanceMm / CalculateMaxEntrySpeed / NormalizeSimulationDistanceScale:
    // presunuté do TrackFlow.Services.Simulation.SimulationBrakingMath (1:1, behavior-preserving).
    // IsNextBlockReservedForLoco / GetLookAheadBlocks / BlockLookAheadInfo:
    // presunuté do TrackFlow.Services.Operation.BlockLookAheadHelper (1:1, behavior-preserving).

    private static int ResolveStepTargetSpeed(
        bool hasMarkers,
        MarkerSpeedProfile markerProfile,
        double travelledCm,
        int limitedCruise,
        int restrictedSpeed,
        bool requiresStopFallback,
        double travelledMm,
        double blockLengthMm,
        double fallbackBrakeStartMm,
        int fallbackCruise)
    {
        if (hasMarkers)
        {
            if (markerProfile.StopCm > 0 && travelledCm >= markerProfile.StopCm)
                return 0;

            if (markerProfile.BrakingCm > 0 && travelledCm >= markerProfile.BrakingCm)
                return Math.Clamp(Math.Min(restrictedSpeed, limitedCruise), 0, 100);

            if (markerProfile.DistanceCm > 0 && travelledCm >= markerProfile.DistanceCm)
                return limitedCruise;

            return limitedCruise;
        }

        // PLYNULÁ JAZDA: Ak nie je posledný segment (requiresStopFallback=false), vlak pokračuje plynule
        if (!requiresStopFallback)
            return limitedCruise;

        if (travelledMm < fallbackBrakeStartMm)
            return fallbackCruise;

        var brakingLengthMm = Math.Max(1.0, blockLengthMm - fallbackBrakeStartMm);
        var remainingMm = Math.Max(0.0, blockLengthMm - travelledMm);
        var ratio = Math.Clamp(remainingMm / brakingLengthMm, 0.0, 1.0);
        return Math.Clamp((int)Math.Round(fallbackCruise * ratio), 0, 100);
    }

    /// <summary>
    /// Pred prechodom hranice cieľového bloku počká, kým RouteManager potvrdí Shadow rezerváciu
    /// (alebo kým je blok už fyzicky obsadený samotnou lokomotívou). Bezpečnostný timeout 2 s.
    /// </summary>
    private static async Task WaitForTargetReservationAsync(BlockElement targetBlock, Locomotive loco, CancellationToken ct)
    {
        const int maxWaitMs = 2000;
        var waited = 0;
        while (waited < maxWaitMs)
        {
            ct.ThrowIfCancellationRequested();
            var reservedForLoco = targetBlock.IsShadowSet
                && string.Equals(targetBlock.ReservedLocoId, loco.Code, StringComparison.OrdinalIgnoreCase);
            var alreadyOccupiedByLoco = targetBlock.IsOccupied
                && string.Equals(targetBlock.AssignedLocoId, loco.Code, StringComparison.OrdinalIgnoreCase);
            if (reservedForLoco || alreadyOccupiedByLoco)
                return;
            await Task.Delay(MovementHeartbeatMs, ct);
            waited += MovementHeartbeatMs;
        }

        var trainName = !string.IsNullOrWhiteSpace(loco.DisplayName) ? loco.DisplayName : loco.Code;
        TrackFlowDoctorService.Instance.Diagnose(
            "Dispečer",
            $"Vlak [{trainName}] čaká na blok [{OperationDisplayHelpers.BlockDisplayName(targetBlock)}]",
            DiagnosticLevel.Warning);
    }

    private static double Approach(double current, double target, double maxStep)
    {
        if (current < target)
            return Math.Min(current + maxStep, target);
        if (current > target)
            return Math.Max(current - maxStep, target);
        return current;
    }

    // InvokeLayoutRefreshAsync: implementácia presunutá do
    // TrackFlow.Services.UI.UiDispatcherHelper (1:1, behavior-preserving).
    // Forwarder zachovaný kvôli stabilite call-sites a JetBrains Rider Code Cleanup safety.
    private static Task InvokeLayoutRefreshAsync(Action onLayoutRefresh)
        => UiDispatcherHelper.InvokeLayoutRefreshAsync(onLayoutRefresh);

    // ResolveMovementBlockLengthMm:
    // presunuté do TrackFlow.Services.Simulation.MovementBlockLength (1:1, behavior-preserving).

    private static double ResolvePreEntryDistanceMm(BlockElement sourceBlock, bool isSimulationMode)
    {
        var sourceLengthMm = TrackFlow.Services.Simulation.MovementBlockLength.ResolveMovementBlockLengthMm(sourceBlock, isSimulationMode);
        return Math.Max(200.0, sourceLengthMm * 0.25);
    }

    private static MarkerSpeedProfile ResolveEffectiveMarkerProfile(
        MarkerSpeedProfile markerProfile,
        BlockElement targetBlock,
        double blockLengthMm,
        bool isSimulationMode)
    {
        if (!isSimulationMode)
            return markerProfile;

        var virtualBlockLengthCm = blockLengthMm / 10.0;
        var projectBlockLengthCm = targetBlock.LengthCm > 0 ? targetBlock.LengthCm : 0;

        // V simulátore používame používateľské markery iba pomerovo:
        // markerCm / LengthCm -> rovnaká relatívna poloha na virtuálnom SimBlockLengthMm.
        // Ak dĺžka bloku chýba, absolútne centimetre markerov nemajú spoľahlivý referenčný rámec,
        // preto vrátime prázdny profil a hlavná logika použije fallback 60% brzdenie / 90% stop.
        if (projectBlockLengthCm <= 0 || !markerProfile.HasAnyMarker)
            return default;

        return new MarkerSpeedProfile(
            NormalizeSimulationMarkerCm(markerProfile.DistanceCm, projectBlockLengthCm, virtualBlockLengthCm),
            NormalizeSimulationMarkerCm(markerProfile.BrakingCm, projectBlockLengthCm, virtualBlockLengthCm),
            NormalizeSimulationMarkerCm(markerProfile.StopCm, projectBlockLengthCm, virtualBlockLengthCm));
    }

    private static double NormalizeSimulationMarkerCm(double markerCm, int projectBlockLengthCm, double virtualBlockLengthCm)
    {
        if (markerCm <= 0 || virtualBlockLengthCm <= 0)
            return 0;

        var ratio = markerCm / projectBlockLengthCm;
        return Math.Clamp(ratio * virtualBlockLengthCm, 0.0, virtualBlockLengthCm);
    }


    private static MarkerSpeedProfile CreateSimulationFallbackMarkerProfile(double blockLengthMm)
    {
        var blockLengthCm = Math.Max(0.0, blockLengthMm / 10.0);
        return new MarkerSpeedProfile(
            DistanceCm: 0,
            BrakingCm: blockLengthCm * SimulationFallbackBrakingRatio,
            StopCm: blockLengthCm * SimulationFallbackStopRatio);
    }

    private static MarkerSpeedProfile ResolveMarkerProfile(RouteDefinition route, BlockElement sourceBlock, BlockElement targetBlock)
    {
        var moveUsesForwardMarkers = string.Equals(route.FromBlockId, sourceBlock.Id, StringComparison.OrdinalIgnoreCase);
        if (moveUsesForwardMarkers)
        {
            return new MarkerSpeedProfile(targetBlock.FwdDistanceCm, targetBlock.FwdBrakingCm, targetBlock.FwdStopCm);
        }

        return new MarkerSpeedProfile(targetBlock.BwdDistanceCm, targetBlock.BwdBrakingCm, targetBlock.BwdStopCm);
    }

    private static double ResolveTailClearTriggerCm(MarkerSpeedProfile markerProfile, double blockLengthCm)
    {
        var fallbackTrigger = Math.Max(5.0, blockLengthCm * 0.20);
        if (markerProfile.BrakingCm <= 0)
            return fallbackTrigger;

        return Math.Min(markerProfile.BrakingCm, fallbackTrigger);
    }

    private readonly record struct MarkerSpeedProfile(double DistanceCm, double BrakingCm, double StopCm)
    {
        public bool HasAnyMarker => DistanceCm > 0 || BrakingCm > 0 || StopCm > 0;
    }

    // DirectionAnalysis / OrientationSyncAnalysis: presunuté do
    // TrackFlow.Services.Operation.RouteDirectionAnalyzer (1:1, behavior-preserving).

    private DirectionAnalysis AnalyzeSelectedLocoDirectionForRoute(RouteDefinition route, IEnumerable<LayoutElement> layoutElements)
    {
        var loco = SelectedLoco;
        if (loco == null || string.IsNullOrWhiteSpace(loco.Code))
            return new DirectionAnalysis(false, DesiredForward: true);

        var sourceBlockId = loco.AssignedBlockId;
        if (string.IsNullOrWhiteSpace(sourceBlockId))
        {
            sourceBlockId = layoutElements
                .OfType<BlockElement>()
                .FirstOrDefault(b => string.Equals(b.AssignedLocoId, loco.Code, StringComparison.OrdinalIgnoreCase))
                ?.Id;
        }

        if (string.IsNullOrWhiteSpace(sourceBlockId))
            return new DirectionAnalysis(false, DesiredForward: true);

        var blocksFirst = route.BlockIds.FirstOrDefault() ?? string.Empty;
        var blocksLast = route.BlockIds.LastOrDefault() ?? string.Empty;

        var from = !string.IsNullOrWhiteSpace(blocksFirst)
            ? blocksFirst
            : (route.FromBlockId ?? string.Empty);
        var to = !string.IsNullOrWhiteSpace(blocksLast)
            ? blocksLast
            : (route.ToBlockId ?? string.Empty);

        if (string.Equals(sourceBlockId, from, StringComparison.OrdinalIgnoreCase))
            return new DirectionAnalysis(true, DesiredForward: true);
        if (string.Equals(sourceBlockId, to, StringComparison.OrdinalIgnoreCase))
            return new DirectionAnalysis(true, DesiredForward: false);

        return new DirectionAnalysis(false, DesiredForward: true);
    }

    // AnalyzeDirectionForMove / TryApplyAutomaticDirectionIfStopped: presunuté do
    // TrackFlow.Services.Operation.RouteDirectionAnalyzer (1:1, behavior-preserving).

    private async Task SynchronizeSelectedLocoDirectionForActivatedRouteAsync(
        RouteDefinition route,
        IEnumerable<LayoutElement> layoutElements,
        bool travelDesiredForward,
        IDccCentralClient? dccClient,
        CancellationToken ct)
    {
        var loco = SelectedLoco;
        if (loco == null)
            return;

        // Travel direction (from→to vs to→from) bol už určený traversal analýzou v call-site
        // (AnalyzeSelectedLocoDirectionForRoute → DesiredForward). Odovzdávame ho ako
        // travelDesiredForward, aby orientation-sync NEPREPÍSAL UI/DCC smer fyzickým DCC smerom.

        var sync = AnalyzeOrientationSyncForRoute(route, layoutElements, loco, travelDesiredForward);
        if (!sync.HasData)
            return;

        loco.IsReversedByOrientation = sync.IsReversedByOrientation;

        // Dashboard/travel smer už bol určený traversal analýzou pred volaním tejto metódy.
        // Orientation-sync nesmie prepísať UI smer fyzickým DCC smerom.

        var effectiveDccClient = GetEffectiveDccClient(dccClient);
        if (effectiveDccClient == null || !effectiveDccClient.IsConnected || loco.DccAddress < 1)
            return;

        var speed = Math.Clamp(loco.CurrentDisplaySpeed, 0, 126);
        await effectiveDccClient.SetLocomotiveSpeedAsync(loco.DccAddress, speed, sync.DesiredDccForward, ct);
    }

    // AnalyzeOrientationSyncForRoute: presunuté do
    // TrackFlow.Services.Operation.RouteDirectionAnalyzer (1:1, behavior-preserving).

    // IsForwardRouteDirection: implementácia v TrackFlow.Services.Operation.RouteDirectionUtilities.
    // Tenký forwarder zachovaný kvôli jedinému live call-site nižšie v tomto súbore.
    // (InvertRouteDirection forwarder odstránený – 0 call-sites.)
    private static bool IsForwardRouteDirection(string direction)
        => RouteDirectionUtilities.IsForwardRouteDirection(direction);


    private void ReserveInitialWindowForSelectedLoco(TrackLayout layout, RouteDefinition route, Locomotive loco)
    {
        if (string.IsNullOrWhiteSpace(loco.Code))
            return;

        var sourceBlockId = loco.AssignedBlockId;
        if (string.IsNullOrWhiteSpace(sourceBlockId))
        {
            sourceBlockId = layout.Elements.OfType<BlockElement>()
                .FirstOrDefault(b => string.Equals(b.AssignedLocoId, loco.Code, StringComparison.OrdinalIgnoreCase))
                ?.Id;
        }

        if (string.IsNullOrWhiteSpace(sourceBlockId))
            return;

        var orientationForward = ResolveSelectedLocoPhysicalOrientation(layout.Elements, loco);
        ReserveInitialWindow(layout, route, loco.Code, sourceBlockId, targetBlockIdHint: null, travelForward: true, orientationForward);
    }

    /// <summary>
    /// RouteManager: pre aktiváciu vytvorí pracovné poradie cesty podľa aktuálnej polohy lokomotívy.
    /// Ak lokomotíva stojí na koncovom bloku pôvodnej cesty, všetky aktivačné operácie pracujú s Reverse(BlockIds).
    /// </summary>
    // ResolveActivationRouteOrder / CreateReversedActivationRoute:
    // presunuté do TrackFlow.Services.Operation.RouteActivationOrder (1:1, behavior-preserving).

    private void DiagnoseActivationRouteOrder(TrackLayout layout, RouteDefinition route)
    {
        var blockOrderStr = string.Join(" → ", route.BlockIds.Select(id => OperationDisplayHelpers.ResolveBlockDisplayName(layout, id)));
        Log.Debug("Activation route order: {BlockOrder}", blockOrderStr);
    }

    private void ApplyActivationLockWindow(TrackLayout layout, RouteDefinition activationRoute)
    {
        ApplyDynamicLockWindow(layout);
    }

    private void ReserveInitialWindow(
        TrackLayout layout,
        RouteDefinition route,
        string locoCode,
        string sourceBlockId,
        string? targetBlockIdHint,
        bool travelForward,
        bool orientationForward)
        => _reservationEngine.ReserveInitialWindow(new ReserveInitialWindowRequest(
            layout,
            route,
            locoCode,
            sourceBlockId,
            targetBlockIdHint,
            travelForward,
            orientationForward));

    /// <summary>RouteManager: vráti BlockIds pre cestu v SMERE JAZDY (ak je cesta aktívna), inak fallback na route.BlockIds.</summary>
    private List<string> GetActivationBlockOrder(string routeId, RouteDefinition route)
        => _traversalEngine.GetTraversalBlockOrder(routeId, route);

    // ResolveSelectedLocoPhysicalOrientation: implementácia presunutá do
    // TrackFlow.Services.Operation.LocoStateResolver (1:1, behavior-preserving).
    private static bool ResolveSelectedLocoPhysicalOrientation(IEnumerable<LayoutElement> elements, Locomotive loco)
        => LocoStateResolver.ResolveSelectedLocoPhysicalOrientation(elements, loco);


    private sealed record VisualTraversalWindowScope(
        int EffectiveLeadSegmentIndex,
        bool EffectiveKeepPreviousSegmentActive,
        bool KeepPreviousRejected,
        string? RejectedPreviousBlockId,
        string? FrontierBlockId,
        IReadOnlyList<string> ActiveBlockIds,
        IReadOnlyList<string> DroppedBlockIds);

    private void SetTraversalSegmentWindow(
        TrackLayout layout,
        RouteDefinition route,
        IReadOnlyList<string> traversalBlockIds,
        int leadSegmentIndex,
        bool keepPreviousSegmentActive)
    {
        var scope = BuildVisualTraversalWindowScope(route, traversalBlockIds, leadSegmentIndex, keepPreviousSegmentActive);
        var activeBlocksText = string.Join(",", scope.ActiveBlockIds);
        var droppedBlocksText = string.Join(",", scope.DroppedBlockIds);

        DiagnoseOrchestrationPass(
            layout,
            route.Id,
            "visual-window-frontier",
            $"frontier=[{scope.FrontierBlockId ?? string.Empty}], lead=[{scope.EffectiveLeadSegmentIndex}], keepPrevious=[{scope.EffectiveKeepPreviousSegmentActive}], blocks=[{activeBlocksText}]");

        if (scope.DroppedBlockIds.Count > 0)
        {
            DiagnoseOrchestrationPass(
                layout,
                route.Id,
                "frontier-window-trim",
                $"frontier=[{scope.FrontierBlockId ?? string.Empty}], removed=[{droppedBlocksText}]");

            var tailClearedSource = _runtimeRegistry.GetRuntime(route.Id)?.TailClearState.SourceBlockId;
            if (!string.IsNullOrWhiteSpace(tailClearedSource)
                && scope.DroppedBlockIds.Any(id => string.Equals(id, tailClearedSource, StringComparison.OrdinalIgnoreCase)))
            {
                DiagnoseOrchestrationPass(
                    layout,
                    route.Id,
                    "tail-cleared-window-drop",
                    $"frontier=[{scope.FrontierBlockId ?? string.Empty}], removed=[{tailClearedSource}]");
            }
        }

        if (scope.KeepPreviousRejected)
        {
            DiagnoseOrchestrationPass(
                layout,
                route.Id,
                "keepPrevious-window-reject",
                $"frontier=[{scope.FrontierBlockId ?? string.Empty}], rejected=[{scope.RejectedPreviousBlockId ?? string.Empty}], requestedKeepPrevious=[{keepPreviousSegmentActive}]");
        }

        if (_lastTraversalWindowLeadIndex.TryGetValue(route.Id, out var lastLead))
        {
            if (scope.EffectiveLeadSegmentIndex < lastLead)
            {
                DiagnoseOrchestrationPass(
                    layout,
                    route.Id,
                    "refresh-skip-tail-cleared",
                    $"flow=[traversal-window], requestedLead=[{scope.EffectiveLeadSegmentIndex}], lastLead=[{lastLead}], keepPrevious=[{scope.EffectiveKeepPreviousSegmentActive}]");
                return;
            }

            if (scope.EffectiveLeadSegmentIndex == lastLead
                && scope.EffectiveKeepPreviousSegmentActive
                && _lastTraversalWindowKeepPrevious.TryGetValue(route.Id, out var lastKeepPrevious)
                && !lastKeepPrevious)
            {
                DiagnoseOrchestrationPass(
                    layout,
                    route.Id,
                    "keepPrevious-window-reject",
                    $"frontier=[{scope.FrontierBlockId ?? string.Empty}], requestedLead=[{scope.EffectiveLeadSegmentIndex}], lastLead=[{lastLead}], reason=[backward-scope-widen]");
                return;
            }
        }

        var snapshot = BuildTraversalWindowSnapshot(route, scope);
        if (_lastTraversalWindowSnapshots.TryGetValue(route.Id, out var lastSnapshot)
            && string.Equals(lastSnapshot, snapshot, StringComparison.Ordinal))
        {
            DiagnoseOrchestrationPass(
                layout,
                route.Id,
                "refresh-skip-unchanged-window",
                $"flow=[traversal-window], frontier=[{scope.FrontierBlockId ?? string.Empty}], blocks=[{activeBlocksText}]");
            return;
        }

        var passKind = _lastTraversalWindowSnapshots.ContainsKey(route.Id)
            ? "traversal-refresh-delta"
            : "traversal-refresh-authoritative";
        DiagnoseOrchestrationPass(
            layout,
            route.Id,
            passKind,
            $"flow=[traversal-window], frontier=[{scope.FrontierBlockId ?? string.Empty}], lead=[{scope.EffectiveLeadSegmentIndex}], keepPrevious=[{scope.EffectiveKeepPreviousSegmentActive}], blocks=[{scope.ActiveBlockIds.Count}], scope=[{activeBlocksText}]");

        _traversalEngine.SetTraversalWindow(new TraversalWindowRequest(
            layout,
            route,
            traversalBlockIds,
            scope.EffectiveLeadSegmentIndex,
            scope.EffectiveKeepPreviousSegmentActive));

        _lastTraversalWindowSnapshots[route.Id] = snapshot;
        _lastTraversalWindowLeadIndex[route.Id] = scope.EffectiveLeadSegmentIndex;
        _lastTraversalWindowKeepPrevious[route.Id] = scope.EffectiveKeepPreviousSegmentActive;
    }

    private VisualTraversalWindowScope BuildVisualTraversalWindowScope(
        RouteDefinition route,
        IReadOnlyList<string> traversalBlockIds,
        int leadSegmentIndex,
        bool keepPreviousSegmentActive)
    {
        if (traversalBlockIds.Count == 0)
        {
            return new VisualTraversalWindowScope(
                EffectiveLeadSegmentIndex: 0,
                EffectiveKeepPreviousSegmentActive: false,
                KeepPreviousRejected: keepPreviousSegmentActive,
                RejectedPreviousBlockId: null,
                FrontierBlockId: null,
                ActiveBlockIds: Array.Empty<string>(),
                DroppedBlockIds: Array.Empty<string>());
        }

        var frontierIndex = Math.Clamp(leadSegmentIndex, 0, traversalBlockIds.Count - 1);
        var frontierBlockId = traversalBlockIds[frontierIndex];
        var runtime = _runtimeRegistry.GetRuntime(route.Id);
        var previousBlockId = frontierIndex > 0 ? traversalBlockIds[frontierIndex - 1] : null;
        var keepPreviousRejected = false;
        var effectiveKeepPreviousSegmentActive = keepPreviousSegmentActive && frontierIndex > 0;

        if (effectiveKeepPreviousSegmentActive
            && !string.IsNullOrWhiteSpace(previousBlockId)
            && runtime?.TailClearState.TailClearTriggered == true
            && string.Equals(runtime.TailClearState.SourceBlockId, previousBlockId, StringComparison.OrdinalIgnoreCase))
        {
            effectiveKeepPreviousSegmentActive = false;
            keepPreviousRejected = true;
        }

        var activeBlockIds = new List<string>();
        void AddActiveBlock(string? blockId)
        {
            if (string.IsNullOrWhiteSpace(blockId))
                return;
            if (!activeBlockIds.Any(id => string.Equals(id, blockId, StringComparison.OrdinalIgnoreCase)))
                activeBlockIds.Add(blockId);
        }

        if (effectiveKeepPreviousSegmentActive)
            AddActiveBlock(previousBlockId);

        AddActiveBlock(frontierBlockId);
        if (frontierIndex < traversalBlockIds.Count - 1)
            AddActiveBlock(traversalBlockIds[frontierIndex + 1]);

        var droppedCount = effectiveKeepPreviousSegmentActive
            ? Math.Max(frontierIndex - 1, 0)
            : frontierIndex;
        var droppedBlockIds = traversalBlockIds
            .Take(droppedCount)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new VisualTraversalWindowScope(
            EffectiveLeadSegmentIndex: frontierIndex,
            EffectiveKeepPreviousSegmentActive: effectiveKeepPreviousSegmentActive,
            KeepPreviousRejected: keepPreviousRejected,
            RejectedPreviousBlockId: keepPreviousRejected ? previousBlockId : null,
            FrontierBlockId: frontierBlockId,
            ActiveBlockIds: activeBlockIds,
            DroppedBlockIds: droppedBlockIds);
    }

    private static string BuildTraversalWindowSnapshot(
        RouteDefinition route,
        VisualTraversalWindowScope scope)
    {
        var builder = new StringBuilder()
            .Append(route.Id)
            .Append('|').Append(scope.EffectiveLeadSegmentIndex)
            .Append('|').Append(scope.EffectiveKeepPreviousSegmentActive)
            .Append('|').Append(scope.FrontierBlockId ?? string.Empty)
            .Append('|').Append(scope.ActiveBlockIds.Count);

        foreach (var blockId in scope.ActiveBlockIds)
            builder.Append("|block=").Append(blockId);

        return builder.ToString();
    }

    private IEnumerable<string> ResolveSegmentPathIds(TrackLayout layout, RouteDefinition route, string fromBlockId, string toBlockId)
    {
        if (string.IsNullOrWhiteSpace(fromBlockId) || string.IsNullOrWhiteSpace(toBlockId))
            return Array.Empty<string>();

        var routeTurnouts = route.TurnoutSettings
            .ToDictionary(t => t.TurnoutId, t => t.RequiredState, StringComparer.OrdinalIgnoreCase);

        // TraversalEngine extraction note: topology scan zachovaný iba pre segment-window path ids.
        // Nie je to autoritatívne continuation rozhodovanie; kandidát na budúci cleanup.
        var edge = new RoutePathfinder(layout, _settings.CurrentProject?.Settings)
            .FindAllRoutes()
            .FirstOrDefault(f =>
                string.Equals(f.FromBlockId, fromBlockId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.ToBlockId, toBlockId, StringComparison.OrdinalIgnoreCase)
                && f.TurnoutStates.All(kv => routeTurnouts.TryGetValue(kv.Key, out var state) && state == kv.Value));

        if (edge != null && edge.PathElementIds.Count > 0)
            return edge.PathElementIds;

        // Fallback pre jednoduchu cestu bez detailnych edge dat.
        if (route.BlockIds.Count == 2 && route.PathElementIds.Count > 0)
            return route.PathElementIds;

        return Array.Empty<string>();
    }

private async Task UpdateTraversalSignalWindowAsync(
        TrackLayout layout,
        RouteDefinition route,
        IReadOnlyList<string> traversalBlockIds,
        int leadSegmentIndex,
        bool keepPreviousSegmentActive,
        IDccCentralClient? dccClient,
        CancellationToken ct)
    {
        var snapshot = BuildTraversalSignalSnapshot(layout, route, traversalBlockIds, leadSegmentIndex, keepPreviousSegmentActive);
        if (_lastTraversalSignalSnapshots.TryGetValue(route.Id, out var lastSnapshot)
            && string.Equals(lastSnapshot, snapshot, StringComparison.Ordinal))
        {
            DiagnoseOrchestrationPass(
                layout,
                route.Id,
                "recompute-skip-unchanged",
                $"lead=[{leadSegmentIndex}], keepPrevious=[{keepPreviousSegmentActive}], flow=[traversal-signal-window]");
            return;
        }

        await _signalSafetyEngine.UpdateTraversalSignalWindowAsync(new UpdateTraversalSignalWindowRequest(
            layout,
            route,
            traversalBlockIds,
            leadSegmentIndex,
            keepPreviousSegmentActive,
            GetEffectiveDccClient(dccClient),
            ct));

        _lastTraversalSignalSnapshots[route.Id] = snapshot;
    }

    // ResolveSegmentTravelDirection / ResolveSegmentStartSignal: presunuté do
    // TrackFlow.Services.Signal.RouteSegmentSignalResolver (1:1, behavior-preserving).
    // Volania v tomto súbore používajú `using static` import – zostávajú nezmenené.

    // ResolveLocoCurrentBlockId forwarder odstránený – 0 call-sites.
    // Implementácia žije v TrackFlow.Services.Operation.LocoStateResolver.

    /// <summary>
    /// Faza 2.5.3: Lock window iba na aktuálne obsadený blok + bezprostredne nasledujúci blok
    /// pre každú aktívnu cestu. Ostatné bloky cesty zostávajú odomknuté pre iné vlaky.
    /// </summary>
    private void ApplyDynamicLockWindow(TrackLayout layout)
    {
        var lockSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blocksById = layout.Elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        foreach (var routeId in _runtimeRegistry.ActiveRouteIds)
        {
            var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
            if (route == null)
                continue;

            // RouteManager: pracuj s blokmi v SMERE JAZDY (rovnaké poradie ako pri aktivácii).
            var drivingOrder = GetActivationBlockOrder(routeId, route);
            if (drivingOrder.Count == 0)
                continue;

            // Nájdi lead/current blok danej cesty. Pri overlap-e source+target počas tail-clear
            // preferuj POSLEDNÝ obsadený blok v smere jazdy (vlak už fyzicky vedie v targete).
            int currentIndex = -1;
            for (int i = drivingOrder.Count - 1; i >= 0; i--)
            {
                var bid = drivingOrder[i];
                if (string.IsNullOrWhiteSpace(bid))
                    continue;
                if (blocksById.TryGetValue(bid, out var b)
                    && b.IsOccupied
                    && !string.IsNullOrWhiteSpace(b.AssignedLocoId))
                {
                    currentIndex = i;
                    break;
                }
            }

            // Ak loko ešte nie je v žiadnom bloku cesty, lockujeme prvý blok ako "očakávaný".
            if (currentIndex < 0)
            {
                for (int i = 0; i < drivingOrder.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(drivingOrder[i]))
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }
            if (currentIndex < 0)
                continue;

            // Lock window = current + next v smere jazdy.
            lockSet.Add(drivingOrder[currentIndex]);
            var nextIndex = currentIndex + 1;
            if (nextIndex < drivingOrder.Count && !string.IsNullOrWhiteSpace(drivingOrder[nextIndex]))
                lockSet.Add(drivingOrder[nextIndex]);
        }

        foreach (var block in layout.Elements.OfType<BlockElement>())
            block.IsLocked = lockSet.Contains(block.Id);
    }

    private void AdvanceReservationWindow(
        TrackLayout layout,
        RouteDefinition route,
        string locoCode,
        string currentBlockId,
        bool travelForward,
        bool orientationForward)
        => _reservationEngine.Advance(new AdvanceReservationWindowRequest(
            layout,
            route,
            locoCode,
            currentBlockId,
            orientationForward,
            "AdvanceReservationWindow"));

    private Task AdvanceReservationWindowInternalAsync(
        TrackLayout layout,
        RouteDefinition route,
        string locoCode,
        string currentBlockId,
        bool orientationForward,
        string source)
        => _reservationEngine.AdvanceAsync(new AdvanceReservationWindowRequest(
            layout,
            route,
            locoCode,
            currentBlockId,
            orientationForward,
            source));

    private void ApplyBoundaryEntryState(
        TrackLayout layout,
        BlockElement sourceBlock,
        BlockElement targetBlock,
        Locomotive? loco,
        string locoCode,
        params object?[] _unused)
    {
        var targetAssignedForward = sourceBlock.AssignedLocoIsForward;

        targetBlock.AssignedLocoId = locoCode;
        targetBlock.IsOccupied = true;
        targetBlock.AssignedLocoIsForward = targetAssignedForward;
        targetBlock.IsTailClearing = false;
        ClearShadowReservation(targetBlock);
        targetBlock.IsDragOverActive = false;

        sourceBlock.AssignedLocoId = locoCode;
        sourceBlock.IsOccupied = true;
        sourceBlock.IsDragOverActive = false;
        sourceBlock.IsTailClearing = true;

        if (loco != null)
        {
            loco.IsPlacedOnTrack = true;
            loco.AssignedBlockId = targetBlock.Id;
        }

        ApplyDynamicLockWindow(layout);
        MarkDirty();
        LayoutRefreshRequested?.Invoke();
    }

    private void ApplyBoundaryEntryStateWithoutImmediateRefresh(
        TrackLayout layout,
        RouteDefinition route,
        BlockElement sourceBlock,
        BlockElement targetBlock,
        Locomotive? loco,
        string locoCode)
        => _reservationEngine.ApplyBoundaryEntry(new BoundaryEntryReservationRequest(
            layout,
            route,
            sourceBlock,
            targetBlock,
            loco,
            locoCode,
            RequestLayoutRefresh: false));

    private async Task ApplyTailClearStateAsync(
        TrackLayout layout,
        RouteDefinition route,
        BlockElement sourceBlock,
        BlockElement targetBlock,
        IDccCentralClient? dccClient,
        CancellationToken ct)
        => await _reservationEngine.ReleaseAsync(new TailClearReleaseRequest(
            layout,
            route,
            sourceBlock,
            targetBlock,
            GetEffectiveDccClient(dccClient),
            ct));

    private RouteDefinition? ResolveBoundaryEntryRoute(
        TrackLayout layout,
        BlockElement sourceBlock,
        BlockElement targetBlock)
    {
        var activeRoute = _runtimeRegistry.ActiveRouteIds
            .Select(routeId => layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(route => route != null
                && route.BlockIds.Any(id => string.Equals(id, sourceBlock.Id, StringComparison.OrdinalIgnoreCase))
                && route.BlockIds.Any(id => string.Equals(id, targetBlock.Id, StringComparison.OrdinalIgnoreCase)));

        if (activeRoute != null)
            return activeRoute;

        return layout.Routes.FirstOrDefault(route => route.BlockIds.Any(id => string.Equals(id, sourceBlock.Id, StringComparison.OrdinalIgnoreCase))
            && route.BlockIds.Any(id => string.Equals(id, targetBlock.Id, StringComparison.OrdinalIgnoreCase)));
    }

    private List<string> BuildTraversalBlockSequence(RouteDefinition route, string sourceBlockId, string targetBlockId)
        => _traversalEngine.BuildTraversalBlockSequence(route, sourceBlockId, targetBlockId);

    /// <summary>
    /// RouteManager (Atomická operácia s BEZPEČNOSTNOU KONTROLOU):
    /// PRED rezerváciou MUSÍ byť:
    /// 1. Blok voľný (nie obsadený, nie zamknutý, nie rezervovaný inou loko)
    /// 2. Príslušné návestidlo chrániace blok musí byť na povoľujúcom aspekte (Voľno)
    /// 
    /// Iba ak sú obe podmienky splnené, vytvorí sa Shadow rezervácia.
    /// Toto je jediné správne miesto na vytvorenie shadow-rezervácie pre nasledujúci blok cesty.
    /// </summary>
    private bool ReserveNextBlock(
        BlockElement? next, 
        string locoCode, 
        bool orientationForward, 
        string? trainName,
        TrackLayout layout,
        string fromBlockId,
        NavigationDirection travelDirection,
        out bool isCriticalFailure)
        => ReserveNextBlockInternal(
            next,
            locoCode,
            orientationForward,
            trainName,
            layout,
            fromBlockId,
            travelDirection,
            out isCriticalFailure,
            suppressFailureDiagnostics: false);

    private bool ReserveNextBlockInternal(
        BlockElement? next,
        string locoCode,
        bool orientationForward,
        string? trainName,
        TrackLayout layout,
        string fromBlockId,
        NavigationDirection travelDirection,
        out bool isCriticalFailure,
        bool suppressFailureDiagnostics,
        bool suppressSuccessRefresh = false,
        bool ignoreProtectingSignalAspect = false)
        => _reservationEngine.TryReserveNextBlock(
            new ReserveNextBlockRequest(
                next,
                locoCode,
                orientationForward,
                trainName,
                layout,
                fromBlockId,
                travelDirection,
                suppressFailureDiagnostics,
                suppressSuccessRefresh,
                ignoreProtectingSignalAspect),
            out isCriticalFailure);

    /// <summary>
    /// RouteManager: Uvoľní shadow rezervácie pre danú lokomotívu vo všetkých blokoch s výnimkou nextBlockId
    /// (aby sa zachovala iba aktuálna look-ahead rezervácia jedného nasledujúceho bloku).
    /// </summary>
    private void ReleaseStaleShadows(TrackLayout layout, string locoCode, string? keepBlockId)
        => _reservationEngine.ReleaseStaleShadows(layout, locoCode, keepBlockId);

    private void ReleaseTraversedTurnouts(TrackLayout layout, RouteDefinition route, string fromBlockId, string toBlockId)
    {
        if (route.TurnoutSettings.Count == 0)
            return;

        var turnoutIdsToRelease = ResolveTraversedTurnoutIds(layout, route, fromBlockId, toBlockId);
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
            AssignStickyWaitWinnerForTurnout(layout, turnoutId);

            if (IsTurnoutStillRequiredByAnotherRoute(layout, route.Id, turnoutId))
            {
                DiagnoseTurnoutRuntime(layout, turnoutId, targetTurnout.State, route.Id, route.Id, "uvoľnená", DiagnosticLevel.Info);
                continue;
            }

            targetTurnout.State = TurnoutState.Straight;
            DiagnoseTurnoutRuntime(layout, turnoutId, targetTurnout.State, route.Id, route.Id, "uvoľnená", DiagnosticLevel.Info);
        }
    }

    private IReadOnlyCollection<string> ResolveTraversedTurnoutIds(
        TrackLayout layout,
        RouteDefinition route,
        string fromBlockId,
        string toBlockId)
    {
        var routeTurnoutStates = route.TurnoutSettings
            .Where(t => !string.IsNullOrWhiteSpace(t.TurnoutId))
            .ToDictionary(t => t.TurnoutId, t => t.RequiredState, StringComparer.OrdinalIgnoreCase);
        if (routeTurnoutStates.Count == 0)
            return Array.Empty<string>();

        var foundRoutes = new RoutePathfinder(layout, _settings.CurrentProject?.Settings).FindAllRoutes();
        var matched = foundRoutes
            .Where(r => string.Equals(r.FromBlockId, fromBlockId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.ToBlockId, toBlockId, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.TurnoutStates.All(kv => routeTurnoutStates.TryGetValue(kv.Key, out var state) && state == kv.Value))
            .OrderByDescending(r => r.TurnoutStates.Count)
            .FirstOrDefault();

        if (matched != null && matched.TurnoutStates.Count > 0)
            return matched.TurnoutStates.Keys.ToList();

        // Fallback pre jednoduché cesty (2 bloky), kde pathfinder nemusí mať úplné údaje.
        if (route.BlockIds.Count <= 2)
            return routeTurnoutStates.Keys.ToList();

        return Array.Empty<string>();
    }

    private void ClearLocoReservations(TrackLayout layout, string locoCode)
        => _reservationEngine.ClearLocoReservations(layout, locoCode);

    private static void ClearShadowReservation(BlockElement block)
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

    /// <summary>
    /// Pred aktiváciou novej cesty vyčistí všetky "zaseknuté" Shadow stavy v projekte.
    /// Zaseknutý = blok má IsShadowSet alebo ReservedLocoId, ale NEpatrí do žiadneho práve runtime-owned
    /// a NIE je fyzicky obsadený. Takéto stavy zostávajú po neukončených/prerušených jazdách
    /// a kazia ďalšie aktivácie (guard `next.IsShadowSet` ich odmietne ako duplicitu).
    /// </summary>
    private void ResetStuckShadowsBeforeActivation(TrackLayout layout)
        => _reservationEngine.ResetStuckShadowsBeforeActivation(layout);

    private void ClearRouteReservations(TrackLayout layout, RouteDefinition route)
        => _reservationEngine.ClearRouteReservations(layout, route);

    private static SignalAspect ResolveRouteStartSignalAspect(RouteDefinition route, IEnumerable<LayoutElement> layoutElements)
    {
        var elements = layoutElements.ToList();
        var blocksById = elements.OfType<BlockElement>().ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);
        var signalsById = elements.OfType<SignalElement>().ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(route.FromBlockId) || !blocksById.TryGetValue(route.FromBlockId, out var fromBlock))
            return SignalAspect.Stop;

        var startDirection = RouteDirection.NormalizeOrDefault(
            route.StartNavigationDirection,
            RouteDirection.Right,
            $"Route[{route.Id}].{nameof(RouteDefinition.StartNavigationDirection)}");

        var preferredSignalId = fromBlock.GetSignalForDirection(RouteSegmentSignalResolver.MapRouteDirectionToNavigationDirection(startDirection));
        if (!string.IsNullOrWhiteSpace(preferredSignalId) && signalsById.TryGetValue(preferredSignalId, out var signal))
            return signal.Aspect;

        foreach (var fallbackId in route.RouteSignalIds)
        {
            if (!string.IsNullOrWhiteSpace(fallbackId) && signalsById.TryGetValue(fallbackId, out var fallbackSignal))
                return fallbackSignal.Aspect;
        }

        return SignalAspect.Stop;
    }

    private static bool RouteMatches(RouteDefinition route, string sourceBlockId, string targetBlockId, bool allowReverse)
    {
        if (!route.IsEnabled)
            return false;

        var from = !string.IsNullOrWhiteSpace(route.FromBlockId)
            ? route.FromBlockId
            : route.BlockIds.FirstOrDefault() ?? string.Empty;
        var to = !string.IsNullOrWhiteSpace(route.ToBlockId)
            ? route.ToBlockId
            : route.BlockIds.LastOrDefault() ?? string.Empty;

        if (string.Equals(from, sourceBlockId, System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(to, targetBlockId, System.StringComparison.OrdinalIgnoreCase))
            return true;

        if (!allowReverse)
            return false;

        return string.Equals(from, targetBlockId, System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(to, sourceBlockId, System.StringComparison.OrdinalIgnoreCase);
    }

    private static List<SignalAspect> GetAspectCycle(string? signalSystemId, string? signalProfile)
    {
        var systemId = string.IsNullOrWhiteSpace(signalSystemId)
            ? SignalSystemDefinition.DefaultSystemId
            : signalSystemId;

        var profile = SignalSystemRegistry.GetProfile(systemId, signalProfile);
        var resolved = profile?.Aspects
            .Select(a => a.Aspect)
            .Distinct()
            .ToList();

        if (resolved != null && resolved.Count > 0)
            return resolved;

        return new List<SignalAspect>
        {
            SignalAspect.Stop,
            SignalAspect.Caution,
            SignalAspect.SlowProceed,
            SignalAspect.SlowExpect40,
            SignalAspect.Proceed
        };
    }

    private static string TranslateRouteActivationMessage(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        return key switch
        {
            "signal-protected-block-occupied" => "Navestidlo sa neda prepnut: chraneny blok je obsadeny.",
            "signal-change-requires-route" => "Navest je mozne postavit len cez potvrdenu jazdnu cestu.",
            "route-activated" => "Cesta bola aktivovana.",
            "route-released-block-occupied" => "Cesta bola uvolnena, lebo chraneny blok je obsadeny.",
            "ghost-alarm" => "GHOST alarm: nečakané obsadenie bez Shadow rezervácie. Dashboard zastavený.",
            "route-deactivated" => "Cesta bola deaktivovana.",
            "routes-deactivated-all" => "Vsetky cesty boli deaktivovane.",
            "loco-moved" => "Lokomotiva bola presunuta.",
            "no-project" => "Nie je otvoreny projekt.",
            "route-not-found" => "Cesta nebola najdena.",
            "route-not-selected" => "Najprv vyber cestu na markeri.",
            "route-not-configured" => "Cesta nie je spravne nakonfigurovana.",
            "route-conflict" => "Cestu nie je mozne aktivovat kvoli konfliktu s aktivnou cestou.",
            "route-track-disconnected" => "Cestu nie je mozne aktivovat: trat medzi blokmi je prerusena alebo neplatna.",
            "target-block-locked" => "Cielovy blok je zamknuty aktivnou cestou.",
            "target-block-occupied" => "Cielovy blok je uz obsadeny.",
            "target-block-reserved" => "Cielovy blok je rezervovany inou lokomotivou.",
            "assign-block-locked" => "Blok je uzamknutý aktívnou cestou — lokomotívu nie je možné priradiť.",
            "assign-block-other-loco" => "Blok je už priradený inej lokomotíve.",
            "neighbor-block-occupied" => "Susedny blok v bezpecnostnej vzdialenosti je obsadeny.",
            "source-block-not-found" => "Štartovací blok nebol najdeny.",
            "target-block-not-found" => "Cielovy blok nebol najdeny.",
            "loco-not-selected" => "Nie je vybrata ziadna lokomotiva.",
            "source-target-same" => "Štartovací a cielovy blok su rovnake.",
            "source-not-on-route" => "Vybrana lokomotiva sa nenachadza na zvolenej ceste.",
            "source-block-loco-mismatch" => "Lokomotiva sa nenachadza v štartovacom bloku.",
            _ => key
        };
    }

    private bool IsProtectedBlockOccupied(SignalElement signal)
    {
        var layout = _settings.CurrentProject?.Layout;
        if (layout == null || string.IsNullOrWhiteSpace(signal.ProtectsBlockId))
            return false;

        var block = layout.Elements
            .OfType<BlockElement>()
            .FirstOrDefault(b => string.Equals(b.Id, signal.ProtectsBlockId, StringComparison.Ordinal));

        return block?.IsOccupied == true;
    }

    private void SetAllSignalsRed(TrackLayout layout)
        => _signalSafetyEngine.SetAllSignalsRed(layout);

    private void SetSignalsRedRespectingActiveRoutes(TrackLayout layout, string ownerRouteId)
        => _signalSafetyEngine.SetSignalsRedRespectingActiveRoutes(layout, ownerRouteId);

    private void SetRouteSignalsPermissive(TrackLayout layout, string routeId)
    {
        var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
        if (route == null)
            return;

        if (route.RouteSignalIds.Count > 0)
        {
            ApplyExplicitRouteSignalRules(layout, route);
            return;
        }

        var blockIds = new HashSet<string>(route.BlockIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        var occupiedBlocks = layout.Elements
            .OfType<BlockElement>()
            .Where(b => b.IsOccupied)
            .Select(b => b.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var signal in layout.Elements.OfType<SignalElement>())
        {
            if (string.IsNullOrWhiteSpace(signal.ProtectsBlockId))
                continue;
            if (!blockIds.Contains(signal.ProtectsBlockId))
                continue;
            if (occupiedBlocks.Contains(signal.ProtectsBlockId))
                continue;
            if (!signal.HasValidDccAddress())
                continue;

            // Neprepíšeme signály, ktoré sú už na povoľujúcom aspekte
            // (napr. Yellow/LowerYellow nastavené cez ApplySignalAspectsForRouteAsync).
            if (signal.Aspect == SignalAspect.Stop)
                signal.Aspect = SignalAspectLogic.GetPermissiveAspect(signal);
        }
    }

    private void ApplyExplicitRouteSignalRules(TrackLayout layout, RouteDefinition route)
    {
        var signalSteps = BuildRouteSignalSteps(layout, route);
        if (signalSteps.Count == 0)
            return;

        var assignedAspects = new SignalAspect[signalSteps.Count];
        for (int i = signalSteps.Count - 1; i >= 0; i--)
        {
            var signalStep = signalSteps[i];
            if (i == 0)
            {
                // Startove navestidlo uz nastavuje SignalController (Yellow/LowerYellow).
                // Tu upravujeme iba navestidla dalej na trase.
                assignedAspects[i] = signalStep.Signal.Aspect;
                continue;
            }

            SignalAspect aspect;
            bool divergingAfterSignal = IsFirstTurnoutAfterSignalDiverging(signalStep)
                || (signalStep.Edge == null && i == 0 && IsFirstTurnoutAfterRouteSignalDiverging(route));

            if (i < signalSteps.Count - 1 && SignalAspectLogic.IsRestrictedAspect(assignedAspects[i + 1]))
            {
                aspect = SignalAspect.SlowExpect40;
            }
            else if (divergingAfterSignal)
            {
                aspect = SignalAspect.SlowProceed;
            }
            else if (i == signalSteps.Count - 1)
            {
                // POSLEDNÝ signál cesty: výstrahu (Caution) generujeme VÝLUČNE vtedy,
                // ak ďalšie HLAVNÉ návestidlo (za cieľom cesty) skutočne existuje
                // a jeho aktuálny aspekt je Stop. Koniec cesty / terminálny cieľ
                // sám o sebe NIE JE dôvod na výstrahu (železničiarsky správne:
                // "Výstraha = očakávaj STOJ na ďalšom hlavnom návestidle",
                // nie "o chvíľu končí cesta").
                var nextMainSignal = RouteSegmentSignalResolver.ResolveNextMainSignalAfterRouteTarget(layout, route);
                aspect = (nextMainSignal != null && SignalAspectLogic.IsStopAspect(nextMainSignal.Aspect))
                    ? SignalAspect.Caution
                    : SignalAspectLogic.GetPermissiveAspect(signalStep.Signal);
            }
            else
            {
                aspect = SignalAspectLogic.GetPermissiveAspect(signalStep.Signal);
            }

            assignedAspects[i] = aspect;
        }

        for (int i = 0; i < signalSteps.Count; i++)
        {
            if (signalSteps[i].Signal.HasValidDccAddress())
                signalSteps[i].Signal.Aspect = assignedAspects[i];
        }
    }

    private List<RouteSignalStep> BuildRouteSignalSteps(TrackLayout layout, RouteDefinition route)
    {
        var routeTurnouts = route.TurnoutSettings
            .ToDictionary(t => t.TurnoutId, t => t.RequiredState, StringComparer.OrdinalIgnoreCase);
        var signalsById = layout.Elements
            .OfType<SignalElement>()
            .ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
        var blocksById = layout.Elements
            .OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        var foundRoutes = new RoutePathfinder(layout, _settings.CurrentProject?.Settings).FindAllRoutes();
        var result = new List<RouteSignalStep>();

        for (int i = 0; i < route.BlockIds.Count - 1; i++)
        {
            var fromBlockId = route.BlockIds[i];
            var toBlockId = route.BlockIds[i + 1];

            var edge = foundRoutes.FirstOrDefault(f =>
                string.Equals(f.FromBlockId, fromBlockId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.ToBlockId, toBlockId, StringComparison.OrdinalIgnoreCase)
                && f.TurnoutStates.All(kv => routeTurnouts.TryGetValue(kv.Key, out var state) && state == kv.Value));

            if (edge == null)
                continue;
            if (!blocksById.TryGetValue(fromBlockId, out var fromBlock))
                continue;

            var direction = RouteSegmentSignalResolver.ResolveNavigationDirectionFromBlockPort(fromBlock, edge.FromBlockExitPort);
            var signalId = fromBlock.GetSignalForDirection(direction);
            if (!string.IsNullOrWhiteSpace(signalId) && signalsById.TryGetValue(signalId, out var signal))
                result.Add(new RouteSignalStep
                {
                    Signal = signal,
                    Edge = edge
                });
        }

        if (result.Count == 0)
        {
            foreach (var signalId in route.RouteSignalIds)
            {
                if (!string.IsNullOrWhiteSpace(signalId) && signalsById.TryGetValue(signalId, out var signal))
                    result.Add(new RouteSignalStep { Signal = signal, Edge = null });
            }
        }

        return result;
    }

    private static bool IsFirstTurnoutAfterSignalDiverging(RouteSignalStep signalStep)
    {
        if (signalStep.Edge == null || signalStep.Edge.PathElementIds.Count == 0 || signalStep.Edge.TurnoutStates.Count == 0)
            return false;

        foreach (var id in signalStep.Edge.PathElementIds)
        {
            if (!signalStep.Edge.TurnoutStates.TryGetValue(id, out var state))
                continue;

            return state != TurnoutState.Straight;
        }

        return false;
    }

    private static bool IsFirstTurnoutAfterRouteSignalDiverging(RouteDefinition route)
    {
        if (route.PathElementIds.Count == 0 || route.TurnoutSettings.Count == 0)
            return false;

        var turnoutById = route.TurnoutSettings
            .ToDictionary(t => t.TurnoutId, t => t.RequiredState, StringComparer.OrdinalIgnoreCase);

        foreach (var id in route.PathElementIds)
        {
            if (!turnoutById.TryGetValue(id, out var state))
                continue;

            return state != TurnoutState.Straight;
        }

        return false;
    }

    // IsRestrictedAspect / IsStopAspect: presunuté do TrackFlow.Services.Signal.SignalAspectLogic
    // (zachovaná verejná logika 1:1; using static TrackFlow.Services.Signal.SignalAspectLogic
    // zabezpečuje, že volania v tomto súbore zostávajú nezmenené).

    /// <summary>
    /// Vráti SKUTOČNÉ ďalšie hlavné návestidlo nachádzajúce sa za cieľovým blokom cesty
    /// v smere jazdy (t.j. návestidlo, ktoré by bolo štartom nadväzujúcej cesty).
    /// Vracia null ak také návestidlo neexistuje.
    /// Používa sa pre výpočet "Caution" iba pri reálnom riziku ďalšieho STOP – NIKDY
    /// na základe samotného faktu, že cesta končí (vrátane terminálnych blokov / bumperov).
    /// </summary>
    // ResolveNextMainSignalAfterRouteTarget: presunuté do
    // TrackFlow.Services.Signal.RouteSegmentSignalResolver (1:1, behavior-preserving).

    private static bool IsTerminalTargetBlock(RouteDefinition route, TrackLayout layout)
    {
        // Pomocná funkcia – už NIE JE povolené použiť ju v signal/aspect logike.
        // (Koniec cesty / terminálny cieľ NESMIE generovať Caution / SlowCaution.)
        // Ponechané len pre prípadné non-signal použitie (rezervácie, traversal window, atď.).
        if (string.IsNullOrWhiteSpace(route.ToBlockId))
            return false;

        var graph = new TrackGraphBuilder().Build(layout);
        if (!graph.PortsByElement.TryGetValue(route.ToBlockId, out var ports) || ports.Count == 0)
            return false;

        var toDirection = RouteDirection.NormalizeOrDefault(route.ToBlockDirection, RouteDirection.Right, "Route.ToBlockDirection");
        var wantedPort = toDirection is RouteDirection.Left or RouteDirection.Up ? "A" : "B";
        var port = ports.FirstOrDefault(p => string.Equals(p.PortName, wantedPort, StringComparison.OrdinalIgnoreCase));
        if (port == null)
            return false;

        return !graph.Adjacency.TryGetValue(port, out var neighbors) || neighbors.Count == 0;
    }

    // ResolveNavigationDirectionFromBlockPort: presunuté do
    // TrackFlow.Services.Signal.RouteSegmentSignalResolver (1:1, behavior-preserving).

    // GetPermissiveAspect / SynthesizeLookAheadAspect: presunuté do
    // TrackFlow.Services.Signal.SignalAspectLogic (1:1, behavior-preserving).
    // Volania v tomto súbore používajú `using static` import – zostávajú nezmenené.

    private void ApplyRouteSafetyFallback(TrackLayout layout, RouteDefinition route)
        => _signalSafetyEngine.ApplyRouteSafetyFallback(layout, route);

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

    private string? ResolvePrimaryRouteLocoId(TrackLayout layout, RouteDefinition route)
    {
        var runtime = _runtimeRegistry.GetRuntime(route.Id);
        if (runtime != null)
        {
            if (!string.IsNullOrWhiteSpace(runtime.CurrentBlockId))
            {
                var currentBlock = layout.Elements.OfType<BlockElement>()
                    .FirstOrDefault(b => string.Equals(b.Id, runtime.CurrentBlockId, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(currentBlock?.AssignedLocoId))
                    return currentBlock.AssignedLocoId;
                if (!string.IsNullOrWhiteSpace(currentBlock?.ReservedLocoId))
                    return currentBlock.ReservedLocoId;
            }

            if (!string.IsNullOrWhiteSpace(runtime.WaitingBlockId))
            {
                var waitingBlock = layout.Elements.OfType<BlockElement>()
                    .FirstOrDefault(b => string.Equals(b.Id, runtime.WaitingBlockId, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(waitingBlock?.ReservedLocoId))
                    return waitingBlock.ReservedLocoId;
            }
        }

        var routeBlockIds = ResolveRouteRuntimeOwnedBlockIds(layout, route.Id, route)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (routeBlockIds.Count == 0)
            return null;

        foreach (var block in layout.Elements.OfType<BlockElement>())
        {
            if (!routeBlockIds.Contains(block.Id))
                continue;

            if (IsBlockUsedByAnotherActiveRoute(layout, route.Id, block.Id))
                continue;

            if (!string.IsNullOrWhiteSpace(block.AssignedLocoId))
                return block.AssignedLocoId;
            if (!string.IsNullOrWhiteSpace(block.ReservedLocoId))
                return block.ReservedLocoId;
        }

        return null;
    }

    private IEnumerable<string> GetRouteActiveBlockIds(TrackLayout layout, string routeId)
    {
        var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
        return route == null
            ? Enumerable.Empty<string>()
            : ResolveRouteRuntimeOwnedBlockIds(layout, routeId, route);
    }

    private IEnumerable<string> ResolveRouteRuntimeOwnedBlockIds(TrackLayout layout, string routeId, RouteDefinition route)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_runtimeRegistry.TryGetReservationWindow(routeId, out var window) && window != null && window.BlockIds.Count > 0)
            owned.UnionWith(window.BlockIds.Where(id => !string.IsNullOrWhiteSpace(id)));

        var runtime = _runtimeRegistry.GetRuntime(routeId);
        if (runtime != null)
        {
            if (!string.IsNullOrWhiteSpace(runtime.CurrentBlockId))
                owned.Add(runtime.CurrentBlockId);

            if (!string.IsNullOrWhiteSpace(runtime.WaitingBlockId))
                owned.Add(runtime.WaitingBlockId);
        }

        if (owned.Count > 0)
            return owned;

        var drivingOrder = GetActivationBlockOrder(routeId, route);
        return drivingOrder
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Take(2)
            .ToList();
    }

    private IEnumerable<string> GetRouteActivePathElementIds(TrackLayout layout, string routeId)
    {
        if (_runtimeRegistry.TryGetReservationWindow(routeId, out var window) && window != null && window.PathElementIds.Count > 0)
            return window.PathElementIds;

        var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
        return route?.PathElementIds.Where(id => !string.IsNullOrWhiteSpace(id)) ?? Enumerable.Empty<string>();
    }

    private bool IsBlockUsedByAnotherActiveRoute(TrackLayout layout, string ownerRouteId, string blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId))
            return false;

        foreach (var routeId in _runtimeRegistry.ActiveRouteIds)
        {
            if (string.Equals(routeId, ownerRouteId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (GetRouteActiveBlockIds(layout, routeId).Any(id => string.Equals(id, blockId, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private bool IsPathElementUsedByAnotherActiveRoute(TrackLayout layout, string ownerRouteId, string pathElementId)
    {
        if (string.IsNullOrWhiteSpace(pathElementId))
            return false;

        foreach (var routeId in _runtimeRegistry.ActiveRouteIds)
        {
            if (string.Equals(routeId, ownerRouteId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (GetRouteActivePathElementIds(layout, routeId).Any(id => string.Equals(id, pathElementId, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private bool IsTurnoutUsedByAnotherActiveRoute(TrackLayout layout, string ownerRouteId, string turnoutId)
    {
        if (string.IsNullOrWhiteSpace(turnoutId))
            return false;

        if (IsPathElementUsedByAnotherActiveRoute(layout, ownerRouteId, turnoutId))
            return true;

        foreach (var routeId in _runtimeRegistry.ActiveRouteIds)
        {
            if (string.Equals(routeId, ownerRouteId, StringComparison.OrdinalIgnoreCase))
                continue;

            var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
            if (route == null)
                continue;

            if (route.TurnoutSettings.Any(t => string.Equals(t.TurnoutId, turnoutId, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private bool IsTurnoutStillRequiredByAnotherRoute(TrackLayout layout, string ownerRouteId, string turnoutId)
        => IsTurnoutUsedByAnotherActiveRoute(layout, ownerRouteId, turnoutId);

    // MapRouteDirectionToNavigationDirection: presunuté do
    // TrackFlow.Services.Signal.RouteSegmentSignalResolver (1:1, behavior-preserving).

    private static SignalAspect ResolveRouteSafetyFallbackAspect(RouteDefinition route)
        => SignalAspect.Stop;

    private sealed class RouteActiveWindowState
    {
        public RouteActiveWindowState(HashSet<string> pathElementIds, HashSet<string> blockIds)
        {
            PathElementIds = pathElementIds;
            BlockIds = blockIds;
        }

        public HashSet<string> PathElementIds { get; }
        public HashSet<string> BlockIds { get; }
    }

    private sealed class RouteSignalStep
    {
        public required SignalElement Signal { get; init; }
        public FoundRoute? Edge { get; init; }
    }

    private void SetRouteActivationMessage(string messageKey, bool? autoHide = null)
    {
        RouteActivationMessage = messageKey;

        ClearRouteMessageTtl();
        bool effectiveAutoHide = autoHide ?? ShouldAutoHideRouteMessage(messageKey);
        var ttlMs = ResolveRouteMessageTtlMs(messageKey);
        if (!effectiveAutoHide || ttlMs <= 0)
            return;

        _routeMessageCts = new CancellationTokenSource();
        var cts = _routeMessageCts;
        _ = ClearRouteMessageAfterDelayAsync(cts, ttlMs);
    }

    private static bool ShouldAutoHideRouteMessage(string messageKey)
        => ResolveRouteMessageTtlType(messageKey) != RouteMessageTtlType.None;

    private int ResolveRouteMessageTtlMs(string messageKey)
    {
        if (_transientRouteMessageTtlOverrideMs.HasValue)
            return Math.Clamp(_transientRouteMessageTtlOverrideMs.Value, 0, 15000);

        if (!_settings.App.EnableTransientRouteMessages)
            return 0;

        return ResolveRouteMessageTtlType(messageKey) switch
        {
            RouteMessageTtlType.Success => Math.Clamp(_settings.App.RouteMessageTtlSuccessMs, 0, 15000),
            RouteMessageTtlType.Info => Math.Clamp(_settings.App.RouteMessageTtlInfoMs, 0, 15000),
            RouteMessageTtlType.Warning => Math.Clamp(_settings.App.RouteMessageTtlWarningMs, 0, 15000),
            _ => 0
        };
    }

    private static RouteMessageTtlType ResolveRouteMessageTtlType(string messageKey)
    {
        if (string.IsNullOrWhiteSpace(messageKey))
            return RouteMessageTtlType.None;

        return messageKey switch
        {
            "route-activated" or "route-deactivated" or "routes-deactivated-all" or "loco-moved"
                => RouteMessageTtlType.Success,
            "signal-change-requires-route"
                => RouteMessageTtlType.Info,
            "signal-protected-block-occupied" or "route-released-block-occupied" or "ghost-alarm"
                => RouteMessageTtlType.Warning,
            _ => RouteMessageTtlType.None
        };
    }

    private async Task ClearRouteMessageAfterDelayAsync(CancellationTokenSource cts, int ttlMs)
    {
        try
        {
            await Task.Delay(ttlMs, cts.Token);
            if (!cts.Token.IsCancellationRequested && ReferenceEquals(_routeMessageCts, cts))
                RouteActivationMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
    }

    private void ClearRouteMessageTtl()
    {
        try
        {
            _routeMessageCts?.Cancel();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "OperationViewModel.Dispose: _routeMessageCts.Cancel failed");
        }
        finally
        {
            _routeMessageCts?.Dispose();
            _routeMessageCts = null;
        }
    }

    /// <summary>
    /// Získa množinu ID návestidiel, ktoré sú chránené aktívnymi cestami.
    /// Tieto návestidlá NESMÚ byť automaticky prepisované na Stop počas HandleOccupiedBlocks.
    /// </summary>
    private HashSet<string> GetActiveRouteProtectedSignalIds(TrackLayout layout)
    {
        var protectedSignalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var routeId in _runtimeRegistry.ActiveRouteIds)
        {
            var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
            if (route == null)
                continue;
            
            // Všetky návestidlá v RouteSignalIds sú chránené
            foreach (var signalId in route.RouteSignalIds)
            {
                if (!string.IsNullOrWhiteSpace(signalId))
                    protectedSignalIds.Add(signalId);
            }
        }
        
        return protectedSignalIds;
    }

    private async Task<int> HandleOccupiedBlocks(TrackLayout layout, IDccCentralClient? dccClient, CancellationToken ct, bool sendDcc)
    {
        int changed = 0;
        var occupiedBlockIds = layout.Elements
            .OfType<BlockElement>()
            .Where(b => b.IsOccupied)
            .Select(b => b.Id)
            .ToList();

        changed += await HandleReleasedBlocksAsync(layout, occupiedBlockIds, dccClient, ct, sendDcc);

        // Ghost detection: blok je obsadeny, ale nema ani priradenie, ani Shadow rezervaciu.
        var blocksById = layout.Elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);
        foreach (var blockId in occupiedBlockIds)
        {
            if (!blocksById.TryGetValue(blockId, out var blk))
                continue;

            if (!_lastOccupiedBlockIds.Contains(blockId))
            {
                var dispatcherTrainName = !string.IsNullOrWhiteSpace(blk.AssignedLocoId)
                    ? ResolveTrainDisplayName(blk.AssignedLocoId)
                    : !string.IsNullOrWhiteSpace(blk.ReservedLocoId)
                        ? ResolveTrainDisplayName(blk.ReservedLocoId)
                        : "Neznámy";
                TrackFlowDoctorService.Instance.Diagnose(
                    "Dispečer",
                    $"Obsadený blok [{OperationDisplayHelpers.BlockDisplayName(blk)}] vlakom [{dispatcherTrainName}]",
                    DiagnosticLevel.Info);
            }

            var isOnActiveRoute = _runtimeRegistry.ActiveRouteIds.Any(routeId =>
            {
                var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
                return route != null && route.BlockIds.Any(b => string.Equals(b, blockId, StringComparison.OrdinalIgnoreCase));
            });

            var mismatchAssignedVsShadow = isOnActiveRoute
                && !string.IsNullOrWhiteSpace(blk.AssignedLocoId)
                && !string.IsNullOrWhiteSpace(blk.ReservedLocoId)
                && !string.Equals(blk.AssignedLocoId, blk.ReservedLocoId, StringComparison.OrdinalIgnoreCase);
            var unassignedAndUnreserved = string.IsNullOrWhiteSpace(blk.AssignedLocoId) && string.IsNullOrWhiteSpace(blk.ReservedLocoId);

            // Ghost je vyhradne stav "biely blok + occupied + bez Shadow".
            if ((isOnActiveRoute && unassignedAndUnreserved) || mismatchAssignedVsShadow)
            {
                await TriggerGhostAlarmAsync(layout, blk, dccClient, ct, sendDcc);
                _lastOccupiedBlockIds.Clear();
                foreach (var occupied in occupiedBlockIds)
                    _lastOccupiedBlockIds.Add(occupied);
                return 1;
            }
        }

        foreach (var blockId in occupiedBlockIds)
            changed += await OnBlockOccupiedAsync(layout, blockId, dccClient, ct, sendDcc);

        _lastOccupiedBlockIds.Clear();
        foreach (var occupied in occupiedBlockIds)
            _lastOccupiedBlockIds.Add(occupied);

        return changed;
    }

    private async Task<int> HandleReleasedBlocksAsync(
        TrackLayout layout,
        IReadOnlyCollection<string> occupiedBlockIds,
        IDccCentralClient? dccClient,
        CancellationToken ct,
        bool sendDcc)
    {
        var currentOccupied = new HashSet<string>(occupiedBlockIds, StringComparer.OrdinalIgnoreCase);
        var releasedIds = _lastOccupiedBlockIds
            .Where(id => !currentOccupied.Contains(id))
            .ToList();

        if (releasedIds.Count == 0)
            return 0;

        var blocksById = layout.Elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);
        var actionableReleasedIds = releasedIds
            .Where(releasedId => blocksById.TryGetValue(releasedId, out var block)
                && (block.IsOccupied
                    || !string.IsNullOrWhiteSpace(block.AssignedLocoId)
                    || block.IsLocked
                    || block.IsDragOverActive
                    || block.IsTailClearing
                    || block.IsShadowSet
                    || !string.IsNullOrWhiteSpace(block.ReservedLocoId)
                    || layout.Elements.OfType<SignalElement>().Any(signal =>
                        string.Equals(signal.ProtectsBlockId, releasedId, StringComparison.OrdinalIgnoreCase)
                        && signal.Aspect != SignalAspect.Stop)))
            .ToList();

        if (actionableReleasedIds.Count == 0)
            return 0;

        var effectiveDccClient = GetEffectiveDccClient(dccClient);
        var changed = await _signalSafetyEngine.ApplyReleasedBlockSignalStopsAsync(
            layout,
            actionableReleasedIds,
            block =>
            {
                var hasLiveReservationOwner = (block.IsShadowSet || !string.IsNullOrWhiteSpace(block.ReservedLocoId))
                    && !string.IsNullOrWhiteSpace(ResolveOwningRouteForBlock(layout, block.Id, null));

                block.AssignedLocoId = null;
                block.AssignedLocoIsForward = true;
                block.IsOccupied = false;
                block.IsLocked = false;
                block.IsDragOverActive = false;

                // Released senzor znamená, že fyzické obsadenie skončilo; neznamená,
                // že môžeme zmazať čerstvú rezerváciu, ktorú medzitým získala ďalšia
                // aktívna/waiting trasa. Toto bol druhý zdroj kontaminácie po tail-clear.
                if (!hasLiveReservationOwner)
                    ClearShadowReservation(block);
            },
            effectiveDccClient,
            ct,
            sendDcc);

        // Okamžitý vizuálny refresh - blok musí zbelieť ihneď bez čakania na ďalšiu udalosť.
        if (actionableReleasedIds.Count > 0)
            RequestLayoutRefreshIfChanged(layout, "released-blocks");

        return changed;
    }

    // SignalDisplayName forwarder odstránený – 0 call-sites.
    // Implementácia žije v TrackFlow.Services.Operation.OperationDisplayHelpers.

    private async Task TriggerGhostAlarmAsync(
        TrackLayout layout,
        BlockElement ghostBlock,
        IDccCentralClient? dccClient,
        CancellationToken ct,
        bool sendDcc)
    {
        var effectiveDccClient = GetEffectiveDccClient(dccClient);
        var shouldSendDcc = sendDcc && ShouldSendDcc(effectiveDccClient);

        TrackFlowDoctorService.Instance.Diagnose(
            "Senzor",
            $"GHOST alarm: nečakané obsadenie bloku {OperationDisplayHelpers.BlockDisplayName(ghostBlock)} bez Shadow rezervácie.",
            DiagnosticLevel.Critical);

        SetRouteActivationMessage("ghost-alarm");
        _routeActivationService.DeactivateAll(layout, _runtimeRegistry.MutableActiveRouteIds);
        _runtimeRegistry.Clear();
        _activeRouteVisualScopeResolver.Clear();
        _lastTraversalSignalSnapshots.Clear();
        _lastTraversalWindowSnapshots.Clear();
        _lastTraversalWindowLeadIndex.Clear();
        _lastTraversalWindowKeepPrevious.Clear();
        OnPropertyChanged(nameof(ActiveRouteIds));

        SetAllSignalsRed(layout);

        foreach (var loco in Locomotives)
        {
            loco.TargetSpeed = 0;
            loco.CurrentDisplaySpeed = 0;
        }

        if (shouldSendDcc && effectiveDccClient != null)
        {
            if (effectiveDccClient.IsConnected)
            {
                try
                {
                    await effectiveDccClient.EmergencyStopAsync(ct);
                    TrackFlowDoctorService.Instance.Diagnose("DCC", "Núdzové zastavenie: príkaz núdzový STOP bol odoslaný.", DiagnosticLevel.Critical);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Ghost panic stop: EmergencyStopAsync failed");
                }

                foreach (var loco in Locomotives.Where(l => l.DccAddress > 0))
                {
                    try
                    {
                        var forward = !loco.IsReverse;
                        await effectiveDccClient.SetLocomotiveSpeedAsync(loco.DccAddress, 0, forward, ct);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Ghost panic stop: speed=0 send failed for loco address {Address}", loco.DccAddress);
                    }
                }
            }

            await _runtimeSafetyService.SendAllSignalStatesAsync(layout.Elements, effectiveDccClient, ct, reason: "ghost-alarm-stop");
        }

        MarkDirty();
        LayoutRefreshRequested?.Invoke();
    }

    private async Task<int> OnBlockOccupiedAsync(
        TrackLayout layout,
        string occupiedBlockId,
        IDccCentralClient? dccClient,
        CancellationToken ct,
        bool sendDcc)
    {
        var effectiveDccClient = GetEffectiveDccClient(dccClient);
        var shouldSendDcc = sendDcc && ShouldSendDcc(effectiveDccClient);

        // ÚLOHA 3: Ak je obsadený blok štartovací blok vybranej lokomotívy ALEBO má IsShadowSet pre túto lokomotívu,
        // ignoruj túto udalosť, aby sa návestidlo okamžite neprehodilo na STOJ.
        if (SelectedLoco != null)
        {
            var sourceBlockId = SelectedLoco.AssignedBlockId;
            if (string.IsNullOrWhiteSpace(sourceBlockId))
            {
                sourceBlockId = layout.Elements.OfType<BlockElement>()
                    .FirstOrDefault(b => string.Equals(b.AssignedLocoId, SelectedLoco.Code, StringComparison.OrdinalIgnoreCase))
                    ?.Id;
            }
            
            var occupiedBlock = layout.Elements.OfType<BlockElement>()
                .FirstOrDefault(b => string.Equals(b.Id, occupiedBlockId, StringComparison.OrdinalIgnoreCase));
            var occupiedBlockReservedForSelected = occupiedBlock != null
                && occupiedBlock.IsShadowSet
                && string.Equals(occupiedBlock.ReservedLocoId, SelectedLoco.Code, StringComparison.OrdinalIgnoreCase);

            // Ignoruj ak je to aktuálny blok lokomotívy, ale nie vtedy, keď práve vchádza do Shadow rezervácie.
            // hasAnotherOccupiedForSelectedLoco = lokomotíva je aktuálne v inom (predchádzajúcom) bloku
            // a tento occupiedBlockId predstavuje nový obsadený blok – treba vykonať postup okna rezervácií.
            var hasAnotherOccupiedForSelectedLoco = layout.Elements.OfType<BlockElement>()
                .Any(b => b.IsOccupied
                    && string.Equals(b.AssignedLocoId, SelectedLoco.Code, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(b.Id, occupiedBlockId, StringComparison.OrdinalIgnoreCase));
            if (!hasAnotherOccupiedForSelectedLoco && !occupiedBlockReservedForSelected)
                return 0;
            
            // ÚLOHA 3: Ignoruj aj ak má blok IsShadowSet pre túto lokomotívu (vlak aktívne vchádza)
            if (occupiedBlockReservedForSelected && occupiedBlock != null
                && string.IsNullOrWhiteSpace(occupiedBlock.AssignedLocoId))
            {
                // === FÁZA 3 - ÚLOHA 1: REAL-TIME KOREKCIA (Teleport/Sync) ===
                // Realita má prednosť. Ak vlak fyzicky preťal senzor, ale simulácia ešte nedorazila,
                // okamžite teleportuj simuláciu na začiatok bloku.
                if (_activeSimulations.TryGetValue(SelectedLoco.Code, out var simContext)
                    && simContext.Engine != null 
                    && string.Equals(simContext.TargetBlockId, occupiedBlockId, StringComparison.OrdinalIgnoreCase))
                {
                    var engine = simContext.Engine;
                    var preEntryMm = simContext.PreEntryDistanceMm;
                    
                    // Ak simulácia ešte neprešla boundary entry trigger (engine.CurrentDistanceMm < preEntryDistanceMm)
                    if (engine.CurrentDistanceMm < preEntryMm)
                    {
                        // TELEPORT: Nastav engine na začiatok bloku
                        engine.CurrentDistanceMm = preEntryMm + 1.0;
                        
                        TrackFlowDoctorService.Instance.Diagnose(
                            "Real-time Sync",
                            $"⚡ TELEPORT: Vlak {SelectedLoco.DisplayName} fyzicky preťal senzor {OperationDisplayHelpers.ResolveBlockDisplayName(layout, occupiedBlockId)} " +
                            $"pred simuláciou. Synchronizácia polohy: {engine.CurrentDistanceMm:F1}mm",
                            DiagnosticLevel.Info);
                        
                        // Okamžite vyvolaj boundary entry akciu
                        if (!simContext.BoundaryEntryTriggered)
                        {
                            simContext.BoundaryEntryTriggered = true;
                            // Poznámka: ApplyBoundaryEntryState sa zavolá v onTargetBoundaryEntry callbacku,
                            // ale ten už beží v inom threade. Tu len označíme, že boundary entry sa stalo.
                        }
                        
                        // Vyvolaj UI refresh pre okamžitú vizuálnu synchronizáciu (Úloha 4)
                        await Dispatcher.UIThread.InvokeAsync(() => LayoutRefreshRequested?.Invoke(), DispatcherPriority.Render);
                    }
                }
                
                return 0;
            }
        }
        
        var blocksById = layout.Elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        var releasedRoutes = new List<string>();
        var reservationAdvanced = false;
        foreach (var routeId in _runtimeRegistry.ActiveRouteIds.ToList())
        {
            var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
            if (route == null)
                continue;
            if (!route.BlockIds.Any(b => string.Equals(b, occupiedBlockId, StringComparison.OrdinalIgnoreCase)))
                continue;

            // OWNER-GUARD na úrovni runtime okna: route.BlockIds je iba statická definícia
            // celej cesty. Senzory zo starých blokov (napr. A/X po posune frontieru) nesmú
            // rekonsolidovať aktívnu/nadväznú trasu len preto, že blok historicky patrí do
            // jej definície. Platí iba aktuálne runtime okno: obsadený blok alebo najbližšia
            // shadow rezervácia pred vlakom.
            if (!GetRouteActiveBlockIds(layout, routeId).Any(id => string.Equals(id, occupiedBlockId, StringComparison.OrdinalIgnoreCase)))
            {
                DiagnoseOrchestrationPass(
                    layout,
                    routeId,
                    "inactive-occupancy-skip",
                    $"flow=[occupancy-callback], block=[{OperationDisplayHelpers.ResolveBlockDisplayName(layout, occupiedBlockId)}], reason=[outside-runtime-window]");
                continue;
            }

            if (blocksById.TryGetValue(occupiedBlockId, out var occupiedBlock)
                && !string.IsNullOrWhiteSpace(occupiedBlock.AssignedLocoId))
            {
                var locoCode = occupiedBlock.AssignedLocoId!;

                // ┌─────────────────────────────────────────────────────────────────────┐
                // │ OWNER-GUARD (anti ghost-reservation)                                │
                // │                                                                     │
                // │ Audit AUDIT_ROUTE_CONTAMINATION_2026-05-16: pri ≥2 aktívnych       │
                // │ trasách so zdieľaným blokom (X/Y) cudzí senzor druhého vlaku       │
                // │ aktivuje tento foreach aj pre TÚTO trasu (filter vyššie len        │
                // │ kontroluje route.BlockIds). Bez tohto guardu by sa locoCode        │
                // │ druhého vlaku použil ako autoritatívna informácia → posun lead,    │
                // │ tail-clear vlastných blokov, otrávenie runtime → po ukončení       │
                // │ trasy sa re-rezervujú „staré" bloky za vlakom.                     │
                // │                                                                     │
                // │ Riešenie: ak loko obsadzujúce blok NIE JE vlastníkom tejto trasy,  │
                // │ udalosť IGNORUJEME (patrí inej trase). Vlastná trasa udalosť       │
                // │ dostane keď náš vlak skutočne vstúpi do bloku.                     │
                // └─────────────────────────────────────────────────────────────────────┘
                var runtimeOwnerLoco = _runtimeRegistry.GetRuntime(routeId)?.OwnerLocomotiveId;
                if (!string.IsNullOrWhiteSpace(runtimeOwnerLoco)
                    && !string.Equals(runtimeOwnerLoco, locoCode, StringComparison.OrdinalIgnoreCase))
                {
                    DiagnoseOrchestrationPass(
                        layout,
                        routeId,
                        "foreign-occupancy-skip",
                        $"flow=[occupancy-callback], block=[{OperationDisplayHelpers.ResolveBlockDisplayName(layout, occupiedBlockId)}], occupiedBy=[{locoCode}], routeOwner=[{runtimeOwnerLoco}]");
                    continue;
                }

                var visualOrientationForward = string.Equals(occupiedBlock.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase)
                    ? occupiedBlock.ReservedLocoIsForward
                    : occupiedBlock.AssignedLocoIsForward;

                // Fáza 2.5.3: zachovaj fixnu fyzickú orientaciu (nemenná tvár).
                occupiedBlock.AssignedLocoIsForward = visualOrientationForward;

                // Riadený vjazd do rezervovaného bloku: current blok už nemá byť Shadow.
                ClearShadowReservation(occupiedBlock);

                var drivingOrder = GetActivationBlockOrder(routeId, route);
                var currentSegmentIndex = drivingOrder.FindIndex(id => string.Equals(id, occupiedBlockId, StringComparison.OrdinalIgnoreCase));
                if (currentSegmentIndex >= 0)
                {
                    DiagnoseOrchestrationPass(
                        layout,
                        routeId,
                        "reconcile-pass",
                        $"flow=[occupancy-callback], block=[{OperationDisplayHelpers.ResolveBlockDisplayName(layout, occupiedBlockId)}], lead=[{currentSegmentIndex}]");

                    // Ak je lokomotíva už v ďalšom bloku, ale predchádzajúci blok stále hlási obsadenie,
                    // uvoľni predchádzajúci blok v pamäti (zabráni ghost/double-occupied stavu).
                    if (currentSegmentIndex > 0)
                    {
                        var previousBlockId = drivingOrder[currentSegmentIndex - 1];
                        if (!string.IsNullOrWhiteSpace(previousBlockId)
                          && blocksById.TryGetValue(previousBlockId, out var previousBlock)
                          && previousBlock.IsOccupied
                          && string.Equals(previousBlock.AssignedLocoId, locoCode, StringComparison.OrdinalIgnoreCase))
                        {
                          previousBlock.AssignedLocoId = null;
                          previousBlock.AssignedLocoIsForward = true;
                          previousBlock.IsOccupied = false;
                          previousBlock.IsLocked = false;
                          previousBlock.IsDragOverActive = false;
                          ClearShadowReservation(previousBlock);
                        }
                      }

                    await UpdateTraversalSignalWindowAsync(
                        layout,
                        route,
                        drivingOrder,
                        currentSegmentIndex,
                        keepPreviousSegmentActive: false,
                        effectiveDccClient,
                        ct);

                    await AdvanceReservationWindowInternalAsync(
                        layout,
                        route,
                        locoCode,
                        occupiedBlock.Id,
                        visualOrientationForward,
                        source: "OnBlockOccupiedAsync");
                }
                DiagnoseReservationEngine(layout, routeId, "OnBlockOccupiedAsync", occupiedBlockId, _traversalEngine.ResolveNextBlock(routeId, route, occupiedBlockId), "skip", DiagnosticLevel.Info);

                ApplyDynamicLockWindow(layout);

                reservationAdvanced = true;
                continue;
            }

            _routeActivationService.Deactivate(layout, routeId, _runtimeRegistry.MutableActiveRouteIds);
            _runtimeRegistry.RemoveRuntime(routeId);
            _activeRouteVisualScopeResolver.ResetRoute(routeId);
            _lastTraversalSignalSnapshots.Remove(routeId);
            _lastTraversalWindowSnapshots.Remove(routeId);
            _lastTraversalWindowLeadIndex.Remove(routeId);
            _lastTraversalWindowKeepPrevious.Remove(routeId);
            DiagnoseRouteEnded(
                layout,
                route,
                $"uvoľnenie po obsadení {OperationDisplayHelpers.ResolveBlockDisplayName(layout, occupiedBlockId)}",
                DiagnosticLevel.Warning);
            releasedRoutes.Add(routeId);
        }

        if (releasedRoutes.Count > 0)
        {
            // ODSTRÁNENÉ: SetAllSignalsRed(layout) — predčasne zhodilo návestidlá pri OBSADENÍ bloku.
            // PRAVIDLO: Návestidlo chrániace blok N sa zhodí LEN A LEN pri Released/TailClear, NIE pri Occupied.
            SetRouteActivationMessage("route-released-block-occupied");
            OnPropertyChanged(nameof(ActiveRouteIds));
        }

        if (reservationAdvanced)
            MarkDirty();

        if (reservationAdvanced)
            RequestLayoutRefreshIfChanged(layout, "occupancy-callback");

        return releasedRoutes.Count + (reservationAdvanced ? 1 : 0);
    }

    /// <summary>
    /// Faza 2.5.3: Smer postupu v poradí BlockIds podľa toho, kde má loko ešte rezervácie.
    /// Ak existujú rezervácie na vyšších indexoch -> forward. Ak na nižších -> backward.
    /// </summary>
    /// <remarks>
    /// Kandidát na budúci cleanup: reservation-driven inference je zachovaná iba behavior-preserving.
    /// TraversalEngine continuation ju autoritatívne nepoužíva.
    /// </remarks>
    private static bool ResolveBlockIdsForwardFromReservations(
        RouteDefinition route,
        string occupiedBlockId,
        string locoCode,
        IReadOnlyDictionary<string, BlockElement> blocksById)
    {
        var currentIndex = route.BlockIds.FindIndex(id => string.Equals(id, occupiedBlockId, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            return true;

        for (int i = currentIndex + 1; i < route.BlockIds.Count; i++)
        {
            if (blocksById.TryGetValue(route.BlockIds[i], out var b)
                && string.Equals(b.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        for (int i = currentIndex - 1; i >= 0; i--)
        {
            if (blocksById.TryGetValue(route.BlockIds[i], out var b)
                && string.Equals(b.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (string.Equals(route.ToBlockId, occupiedBlockId, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    // Kandidát na budúci cleanup: reservation-driven inference zachovaná bez refaktoru,
    // nie ako súčasť route-path-driven continuation authority.
    private static bool ResolveTravelDirectionForOccupiedBlock(
        RouteDefinition route,
        string occupiedBlockId,
        string locoCode,
        IReadOnlyDictionary<string, BlockElement> blocksById)
    {
        if (blocksById.TryGetValue(occupiedBlockId, out var occupiedBlock))
        {
            var effectiveOrientationForward = string.Equals(occupiedBlock.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase)
                ? occupiedBlock.ReservedLocoIsForward
                : occupiedBlock.AssignedLocoIsForward;

            var startDirection = RouteDirection.NormalizeOrDefault(
                route.StartNavigationDirection,
                RouteDirection.Right,
                $"Route[{route.Id}].{nameof(RouteDefinition.StartNavigationDirection)}");

            var routeWantsForwardInBlock = IsForwardRouteDirection(startDirection);
            var isReversedByOrientation = routeWantsForwardInBlock != effectiveOrientationForward;
            var desiredDccForward = !isReversedByOrientation;

            return !isReversedByOrientation;
         }

        var currentIndex = route.BlockIds.FindIndex(id => string.Equals(id, occupiedBlockId, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            return true;

        var forwardIndex = currentIndex + 1;
        if (forwardIndex < route.BlockIds.Count
            && blocksById.TryGetValue(route.BlockIds[forwardIndex], out var forwardBlock)
            && string.Equals(forwardBlock.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var backwardIndex = currentIndex - 1;
        if (backwardIndex >= 0
            && blocksById.TryGetValue(route.BlockIds[backwardIndex], out var backwardBlock)
            && string.Equals(backwardBlock.ReservedLocoId, locoCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(route.FromBlockId, occupiedBlockId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(route.ToBlockId, occupiedBlockId, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private void DiagnoseRouteStarted(TrackLayout layout, RouteDefinition route)
    {
        var trainName = ResolveTrainDisplayName(ResolvePrimaryRouteLocoId(layout, route) ?? string.Empty);
        TrackFlowDoctorService.Instance.Diagnose(
            "Cesta",
            $"▶ Začiatok cesty: {FormatRouteDiagnosticLabel(layout, route)} (vlak {trainName})",
            DiagnosticLevel.Info);
    }

    private void ValidateMovementPreCommitReservationOrThrow(TrackLayout layout, string? routeId, BlockElement targetBlock, string locoCode)
        => _reservationEngine.ValidateMovementPreCommitReservationOrThrow(layout, routeId, targetBlock, locoCode);

    private void DiagnoseRouteEnded(TrackLayout layout, RouteDefinition route, string reason, DiagnosticLevel level)
    {
        var state = level == DiagnosticLevel.Success
            ? "dokončená"
            : reason.Contains("zrušená", StringComparison.OrdinalIgnoreCase)
                ? "zrušená"
                : reason.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                  || reason.Contains("časový-limit", StringComparison.OrdinalIgnoreCase)
                  || reason.Contains("núdzové", StringComparison.OrdinalIgnoreCase)
                  || level == DiagnosticLevel.Critical
                    ? "zlyhala"
                    : "deaktivovaná";
        var trainName = ResolveTrainDisplayName(ResolvePrimaryRouteLocoId(layout, route) ?? string.Empty);
        var suffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" — {reason}";
        TrackFlowDoctorService.Instance.Diagnose(
            "Cesta",
            $"■ Koniec cesty: {FormatActiveRouteDiagnosticLabel(layout, route)} ({state}, vlak {trainName}){suffix}",
            level);
    }

    private static string FormatRouteDiagnosticLabel(TrackLayout layout, RouteDefinition route)
    {
        var fromBlockName = OperationDisplayHelpers.ResolveBlockDisplayName(layout, OperationDisplayHelpers.ResolveRouteStartBlockId(route));
        var toBlockName = OperationDisplayHelpers.ResolveBlockDisplayName(layout, OperationDisplayHelpers.ResolveRouteEndBlockId(route));
        return $"{fromBlockName} → {toBlockName}";
    }

    private static string FormatRouteNameForDiagnostic(TrackLayout layout, RouteDefinition route)
        => FormatRouteDiagnosticLabel(layout, route);

    private static string ResolveRouteDisplayName(TrackLayout layout, string? routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return "Neznáma cesta";

        var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
        return route != null ? FormatRouteNameForDiagnostic(layout, route) : "Neznáma cesta";
    }

    private static string ResolveRouteUiCheckDisplayName(TrackLayout? layout, RouteElement? routeElement, RouteDefinition? route)
    {
        if (layout != null && route != null)
            return FormatRouteNameForDiagnostic(layout, route);

        if (!string.IsNullOrWhiteSpace(routeElement?.SelectedRouteDefinitionId))
            return routeElement!.SelectedRouteDefinitionId!;

        if (!string.IsNullOrWhiteSpace(routeElement?.Id))
            return routeElement!.Id;

        return "neznáma";
    }

    private static string ResolveLayoutResourceDisplayName(TrackLayout layout, string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            return "Neznámy prvok";

        var element = layout.Elements.FirstOrDefault(e => string.Equals(e.Id, resourceId, StringComparison.OrdinalIgnoreCase));
        if (element is BlockElement block)
            return OperationDisplayHelpers.BlockDisplayName(block);
        if (element != null)
            return LayoutElementDisplayHelper.Describe(element, includeId: false);

        return $"prvok [{LayoutElementDisplayHelper.ShortId(resourceId)}]";
    }

    private string FormatActiveRouteDiagnosticLabel(TrackLayout layout, RouteDefinition route)
    {
        var drivingOrder = _traversalEngine.GetTraversalBlockOrder(route.Id, route);
        if (drivingOrder.Count > 0)
        {
            var first = drivingOrder.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
            var last = drivingOrder.LastOrDefault(id => !string.IsNullOrWhiteSpace(id));
            if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last))
                return $"{OperationDisplayHelpers.ResolveBlockDisplayName(layout, first)} → {OperationDisplayHelpers.ResolveBlockDisplayName(layout, last)}";
        }

        return FormatRouteDiagnosticLabel(layout, route);
    }

    private string ResolveTrainDisplayName(string locoCode)
    {
        if (string.IsNullOrWhiteSpace(locoCode))
            return "Neznámy";

        var runtimeLoco = Locomotives.FirstOrDefault(l =>
            string.Equals(l.Code, locoCode, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(runtimeLoco?.DisplayName))
            return runtimeLoco.DisplayName;

        var catalogLoco = _settings.ProjectLocomotives.FirstOrDefault(l =>
            string.Equals(l.Id, locoCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.Address.ToString(), locoCode, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(catalogLoco?.Name))
            return catalogLoco.Name;

        return "Neznámy";
    }

    private void DiagnoseReservationEngine(
        TrackLayout layout,
        string routeId,
        string source,
        string? currentBlockId,
        string? nextBlockId,
        string action,
        DiagnosticLevel level)
    {
    }


    private void DiagnoseTagged(string source, string tags, string message, DiagnosticLevel level = DiagnosticLevel.Info)
        => TrackFlowDoctorService.Instance.Diagnose(source, $"{tags} {message}", level);

    private void DiagnoseOrchestrationPass(TrackLayout layout, string? routeId, string state, string detail, DiagnosticLevel level = DiagnosticLevel.Info)
    {
    }

    private string BuildTraversalSignalSnapshot(
        TrackLayout layout,
        RouteDefinition route,
        IReadOnlyList<string> traversalBlockIds,
        int leadSegmentIndex,
        bool keepPreviousSegmentActive)
    {
        var blocksById = layout.Elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);
        var runtime = _runtimeRegistry.GetRuntime(route.Id);
        var builder = new StringBuilder()
            .Append(route.Id)
            .Append('|').Append(runtime?.Diagnostics.CreatedAtUtc.Ticks ?? 0)
            .Append('|').Append(leadSegmentIndex)
            .Append('|').Append(keepPreviousSegmentActive)
            .Append('|').Append(runtime?.LifecycleState)
            .Append('|').Append(runtime?.CurrentTraversalIndex ?? -1)
            .Append('|').Append(runtime?.CurrentBlockId ?? string.Empty)
            .Append('|').Append(runtime?.TailClearState.SourceBlockId ?? string.Empty)
            .Append('|').Append(runtime?.TailClearState.TargetBlockId ?? string.Empty)
            .Append('|').Append(runtime?.TailClearState.BoundaryEntryTriggered ?? false)
            .Append('|').Append(runtime?.TailClearState.TailClearTriggered ?? false)
            .Append('|').Append(runtime?.ReservationWindow.LeadSegmentIndex ?? -1)
            .Append('|').Append(runtime?.ReservationWindow.KeepPreviousSegmentActive ?? false);

        foreach (var blockId in traversalBlockIds)
        {
            builder.Append("|block=").Append(blockId);
            if (!blocksById.TryGetValue(blockId, out var block))
            {
                builder.Append("<missing>");
                continue;
            }

            builder
                .Append(':').Append(block.IsOccupied)
                .Append(':').Append(block.AssignedLocoId ?? string.Empty)
                .Append(':').Append(block.AssignedLocoIsForward)
                .Append(':').Append(block.IsShadowSet)
                .Append(':').Append(block.ReservedLocoId ?? string.Empty)
                .Append(':').Append(block.ReservedLocoIsForward)
                .Append(':').Append(block.IsTailClearing);
        }

        return builder.ToString();
    }

    private string BuildLayoutRefreshSnapshot(TrackLayout layout)
    {
        var builder = new StringBuilder();

        foreach (var routeId in _runtimeRegistry.ActiveRouteIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var runtime = _runtimeRegistry.GetRuntime(routeId);
            builder
                .Append("|route=").Append(routeId)
                .Append(':').Append(runtime?.LifecycleState)
                .Append(':').Append(runtime?.CurrentTraversalIndex ?? -1)
                .Append(':').Append(runtime?.CurrentBlockId ?? string.Empty)
                .Append(':').Append(runtime?.ReservationWindow.LeadSegmentIndex ?? -1)
                .Append(':').Append(runtime?.ReservationWindow.KeepPreviousSegmentActive ?? false);

            if (runtime?.ReservationWindow.BlockIds != null)
            {
                foreach (var blockId in runtime.ReservationWindow.BlockIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
                    builder.Append("#wb=").Append(blockId);
            }

            if (runtime?.ReservationWindow.PathElementIds != null)
            {
                foreach (var pathId in runtime.ReservationWindow.PathElementIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
                    builder.Append("#wp=").Append(pathId);
            }
        }

        foreach (var block in layout.Elements.OfType<BlockElement>().OrderBy(b => b.Id, StringComparer.OrdinalIgnoreCase))
        {
            builder
                .Append("|b=").Append(block.Id)
                .Append(':').Append(block.IsOccupied)
                .Append(':').Append(block.AssignedLocoId ?? string.Empty)
                .Append(':').Append(block.AssignedLocoIsForward)
                .Append(':').Append(block.IsShadowSet)
                .Append(':').Append(block.ReservedLocoId ?? string.Empty)
                .Append(':').Append(block.ReservedLocoIsForward)
                .Append(':').Append(block.IsTailClearing)
                .Append(':').Append(block.IsLocked)
                .Append(':').Append(block.IsDragOverActive);
        }

        foreach (var signal in layout.Elements.OfType<SignalElement>().OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
            builder.Append("|s=").Append(signal.Id).Append(':').Append(signal.Aspect);

        return builder.ToString();
    }

    private bool RequestLayoutRefreshIfChanged(TrackLayout? layout, string source)
    {
        if (layout == null)
        {
            LayoutRefreshRequested?.Invoke();
            return true;
        }

        var snapshot = BuildLayoutRefreshSnapshot(layout);
        if (string.Equals(_lastLayoutRefreshSnapshot, snapshot, StringComparison.Ordinal))
        {
            DiagnoseOrchestrationPass(layout, routeId: null, "refresh-skip-unchanged", $"source=[{source}]");
            return false;
        }

        _lastLayoutRefreshSnapshot = snapshot;
        LayoutRefreshRequested?.Invoke();
        return true;
    }

    private void DiagnoseRouteLifecycle(TrackLayout layout, RouteDefinition route, string state, string? locoCode, DiagnosticLevel level)
    {
    }

    private void DiagnoseBlockRuntime(TrackLayout layout, string blockId, string state, string? locoCode, string? routeId, string? ownerRouteId, DiagnosticLevel level)
        => DiagnoseTagged(
            "Prevádzka",
            "[MULTI][BLOK]",
            $"cesta=[{FormatRouteTagValue(layout, routeId)}], blok=[{ResolveLayoutResourceDisplayName(layout, blockId)}], stav=[{state}], vlak=[{ResolveTrainDisplayName(locoCode ?? string.Empty)}], vlastník=[{FormatRouteTagValue(layout, ownerRouteId)}]",
            level);

    private void DiagnoseTurnoutRuntime(TrackLayout layout, string? turnoutId, TurnoutState requiredState, string? requestingRouteId, string? ownerRouteId, string state, DiagnosticLevel level)
    {
        if (string.IsNullOrWhiteSpace(turnoutId))
            return;

        DiagnoseTagged(
            "Prevádzka",
            "[MULTI][VYHYBKA]",
            $"výhybka=[{ResolveLayoutResourceDisplayName(layout, turnoutId)}], stav=[{state}], požadovaný=[{OperationDisplayHelpers.TurnoutStateDisplayName(requiredState)}], žiada=[{FormatRouteTagValue(layout, requestingRouteId)}], vlastník=[{FormatRouteTagValue(layout, ownerRouteId)}]",
            level);
    }

    private void DiagnoseSignalRuntime(TrackLayout layout, string? routeId, string? locoCode, SignalElement? signal, string state, DiagnosticLevel level)
    {
        if (signal == null)
            return;

        DiagnoseTagged(
            "Prevádzka",
            "[MULTI][NAVESTIDLO]",
            $"cesta=[{FormatRouteTagValue(layout, routeId)}], návestidlo=[{OperationDisplayHelpers.SignalDisplayName(signal)}], stav=[{state}], aspekt=[{signal.Aspect}], vlak=[{ResolveTrainDisplayName(locoCode ?? string.Empty)}]",
            level);
    }

    private void DiagnoseCleanupRuntime(TrackLayout layout, string routeId, string? locoCode, string state, int releasedReservations, int releasedTurnouts, DiagnosticLevel level)
    {
    }

    private void DiagnoseWaitState(string routeId, string blockId, string state, string? reason, int? retryCount, DateTime? waitingSinceUtc, DiagnosticLevel level)
    {
        var layout = _settings.CurrentProject?.Layout;
        if (layout == null)
            return;

        var route = layout.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.OrdinalIgnoreCase));
        var locoCode = route != null ? ResolvePrimaryRouteLocoId(layout, route) : null;
        var waitSeconds = waitingSinceUtc.HasValue ? (DateTime.UtcNow - waitingSinceUtc.Value).TotalSeconds : 0;

        DiagnoseTagged(
            "Prevádzka",
            "[MULTI][CAKANIE]",
            $"cesta=[{FormatRouteTagValue(layout, routeId)}], stav=[{state}], vlak=[{ResolveTrainDisplayName(locoCode ?? string.Empty)}], blok=[{ResolveLayoutResourceDisplayName(layout, blockId)}], dôvod=[{reason ?? "-"}], pokus=[{(retryCount.HasValue ? retryCount.Value.ToString() : "-")}], čakanie=[{waitSeconds:0.#}s]",
            level);
    }

    private void DiagnoseWaitRetry(TrackLayout layout, RouteDefinition route, string locoCode, string resourceId, string? reason, string? ownerRouteId, int retryCount, DateTime? waitingSinceUtc, string state, DiagnosticLevel level)
    {
        var waitSeconds = waitingSinceUtc.HasValue ? (DateTime.UtcNow - waitingSinceUtc.Value).TotalSeconds : 0;

        DiagnoseTagged(
            "Prevádzka",
            "[MULTI][CAKANIE]",
            $"cesta=[{FormatRouteNameForDiagnostic(layout, route)}], stav=[{state}], vlak=[{ResolveTrainDisplayName(locoCode)}], prvok=[{ResolveLayoutResourceDisplayName(layout, resourceId)}], dôvod=[{reason ?? "-"}], vlastník=[{FormatRouteTagValue(layout, ownerRouteId)}], pokus=[{retryCount}], čakanie=[{waitSeconds:0.#}s]",
            level);
    }

    private void DiagnoseArbiter(TrackLayout layout, string resourceKind, string? resourceId, string? winnerRouteId, string waitOrder, string stickyWindow, string handover, DiagnosticLevel level)
        => DiagnoseTagged(
            "Prevádzka",
            "[MULTI][ARBITRAZ]",
            $"typ=[{resourceKind}], prvok=[{ResolveLayoutResourceDisplayName(layout, resourceId)}], víťaz=[{FormatRouteTagValue(layout, winnerRouteId)}], poradie-čakania=[{waitOrder}], prioritné-okno=[{stickyWindow}], odovzdanie=[{handover}]",
            level);

    private void DiagnoseDeadlock(TrackLayout layout, string routeId, string? blockedByRouteId, string? waitingResourceKey, string state, DiagnosticLevel level, string? extra = "")
        => DiagnoseTagged(
            "Prevádzka",
            "[MULTI][PAT]",
            $"cesta=[{FormatRouteTagValue(layout, routeId)}], drží=[{FormatRouteHeldResources(layout, routeId)}], čaká-na=[{FormatWaitingResource(layout, waitingResourceKey)}], blokuje=[{FormatBlockingRoute(layout, blockedByRouteId)}], pat=[{state}]{extra}",
            level);

    private void DiagnoseDuplicateWaitOrchestration(TrackLayout layout, string routeId, string resourceId, string state, string detail, DiagnosticLevel level)
    {
    }

    private static bool ShouldEmitWaitRetryDiagnostic(int retryCount)
        => retryCount == 1 || retryCount % 5 == 0;

    private int CountRouteOwnedReservations(TrackLayout layout, RouteDefinition route)
        => _reservationEngine.CountRouteOwnedReservations(layout, route);

    private string? ResolveOwningRouteForBlock(TrackLayout layout, string? blockId, string? excludeRouteId = null)
    {
        if (string.IsNullOrWhiteSpace(blockId))
            return null;

        foreach (var routeId in _runtimeRegistry.ActiveRouteIds)
        {
            if (!string.IsNullOrWhiteSpace(excludeRouteId)
                && string.Equals(routeId, excludeRouteId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (GetRouteActiveBlockIds(layout, routeId).Any(id => string.Equals(id, blockId, StringComparison.OrdinalIgnoreCase)))
                return routeId;
        }

        return ResolveStickyWaitWinnerRouteIdForBlock(blockId);
    }

    private string? ResolveTurnoutOwnerRouteId(string? turnoutId)
    {
        if (string.IsNullOrWhiteSpace(turnoutId))
            return null;

        if (_turnoutRuntimeReservations.TryGetValue(turnoutId, out var ownerRouteId))
            return ownerRouteId;

        return ResolveStickyWaitWinnerRouteIdForTurnout(turnoutId);
    }

    private string? ResolveActiveRouteForSegment(TrackLayout layout, string fromBlockId, string toBlockId)
        => _traversalEngine.ResolveActiveRouteForSegment(layout, fromBlockId, toBlockId);

    private string? ResolveConflictingTurnoutId(TrackLayout layout, RouteDefinition route, string fromBlockId, string toBlockId)
    {
        var turnoutRequirements = ResolveSegmentTurnoutRequirements(layout, route, fromBlockId, toBlockId);
        foreach (var turnoutId in turnoutRequirements.Keys)
        {
            if (!IsTurnoutAvailableForRoute(route.Id, turnoutId))
                return turnoutId;
        }

        return turnoutRequirements.Keys.FirstOrDefault();
    }

    private static string FormatRouteTagValue(TrackLayout layout, string? routeId)
        => string.IsNullOrWhiteSpace(routeId) ? "žiadna" : ResolveRouteDisplayName(layout, routeId);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _globalCts.Cancel();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "OperationViewModel.Dispose: _globalCts.Cancel failed");
        }

        try
        {
            _routeMessageCts?.Cancel();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "OperationViewModel.Dispose: _routeMessageCts.Cancel failed");
        }

        try
        {
            DeactivateAllRoutes();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "OperationViewModel.Dispose: DeactivateAllRoutes failed");
        }

        _globalCts.Dispose();
        _panicStopCts.Dispose();
        _routeMessageCts?.Dispose();
        _routeMessageCts = null;
    }
}


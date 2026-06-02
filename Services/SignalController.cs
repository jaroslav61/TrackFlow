using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TrackFlow.Extensions;
using TrackFlow.Models.Layout;
using TrackFlow.Services.Dcc;
using TrackFlow.Services.Operation;
using TrackFlow.Services.Signal;

namespace TrackFlow.Services;

/// <summary>
/// Globálne nastavenia DCC vrstvy pre návestidlá (PeLi / Extended Accessory).
/// Riešia bežný "+4 / -4" offset problém medzi rôznymi centrálami.
/// </summary>
public static class SignalDccOptions
{
    /// <summary>
    /// Posun, ktorý sa pripočíta k DCC adrese pred odoslaním do centrály.
    /// Predvolene 0 (priamy režim). Typické hodnoty: 0, +4, -4.
    /// </summary>
    public static int AddressOffset { get; set; } = 0;

    /// <summary>
    /// Ak true, posiela sa adresa presne tak ako je v modeli (offset sa ignoruje).
    /// Ak false, k adrese sa pripočíta <see cref="AddressOffset"/>.
    /// </summary>
    public static bool UseRawAddresses { get; set; } = true;

    /// <summary>Počet pokusov o opätovné odoslanie pri zlyhaní pripojenia.</summary>
    public static int RetryCount { get; set; } = 1;

    /// <summary>Pauza pred retry pokusom (ms) – krátka, na zachytenie race-condition pri reconnecte.</summary>
    public static int RetryDelayMs { get; set; } = 250;

    public static int ResolveAddress(int raw) => UseRawAddresses ? raw : raw + AddressOffset;
}

/// <summary>
/// Jednoduchý controller signálov v1.
/// - obsadený chránený blok => Stop
/// - voľný blok + RequestYellow => Caution
/// - voľný blok => Proceed
/// - bez väzby / neznámy blok => Stop
/// </summary>
public sealed class SignalController
{
    private readonly IEnumerable<LayoutElement>? _runtimeLayoutElements;
    private readonly IDccCentralClient? _runtimeDccClient;

    /// <summary>
    /// Cache posledne odoslaného aspektu na adresu – zabráni duplicitným paketom pri refresh slučke,
    /// ale zároveň umožňuje vynútiť opätovné odoslanie cez <c>force=true</c> pri aktivácii cesty.
    /// </summary>
    private static readonly ConcurrentDictionary<int, SignalAspect> _lastSentByAddress = new();

    public SignalController()
    {
    }

    public SignalController(IEnumerable<LayoutElement>? runtimeLayoutElements, IDccCentralClient? runtimeDccClient = null)
    {
        _runtimeLayoutElements = runtimeLayoutElements;
        _runtimeDccClient = runtimeDccClient;
    }

    /// <summary>Vyčistí cache posledne odoslaných aspektov (po reconnecte centrály).</summary>
    public static void InvalidateSendCache() => _lastSentByAddress.Clear();

    public IReadOnlyList<SignalElement> RefreshAspects(
        IEnumerable<LayoutElement> layoutElements,
        IReadOnlyCollection<string>? activeRouteIds = null,
        IEnumerable<RouteDefinition>? allRoutes = null)
    {
        if (layoutElements == null) throw new ArgumentNullException(nameof(layoutElements));

        var elements = layoutElements.ToList();
        var blocks = elements.OfType<BlockElement>().ToDictionary(b => b.Id, b => b, StringComparer.Ordinal);
        var changed = new List<SignalElement>();

        // v1.4: Vybuduj mapu signálov, ktoré patria aktívnym cestám (nesmú byť prepisované).
        var protectedSignals = BuildActiveRouteSignalsMap(elements, activeRouteIds, allRoutes);

        foreach (var signal in elements.OfType<SignalElement>())
        {
            // v1.4: STOP REGRESII - ak je signál súčasťou aktívnej cesty, nesmie byť prepísaný.
            if (protectedSignals.Contains(signal.Id))
                continue;

            // v1.5: Skip návestidlá s neplatnou DCC adresou - nebudú ovládateľné, nemá zmysel meniť aspect.
            if (!signal.HasValidDccAddress())
                continue;

            var resolved = ResolveAspect(signal, blocks);
            if (signal.Aspect == resolved)
                continue;

            signal.Aspect = resolved;
            changed.Add(signal);
        }

        return changed;
    }

    public async Task<int> RefreshAllAsync(
        IEnumerable<LayoutElement> layoutElements,
        IDccCentralClient? dccClient,
        IReadOnlyCollection<string>? activeRouteIds = null,
        IEnumerable<RouteDefinition>? allRoutes = null,
        CancellationToken ct = default,
        string reason = "refresh",
        string? syncId = null)
    {
        var changedSignals = RefreshAspects(layoutElements, activeRouteIds, allRoutes);
        await ApplyDccAsync(changedSignals, dccClient, ct, reason, syncId);

        return changedSignals.Count;
    }

    /// <summary>
    /// Vypočíta aspekt návestidla pre danú cestu.
    /// </summary>
    /// <param name="route">Cesta, pre ktorú sa vypočíta aspekt.</param>
    /// <param name="nextSignalIsRestricted">true ak nasledujúci blok/aspekt vyžaduje obmedzenie.</param>
    /// <param name="routeSignal">Voliteľné štartové návestidlo pre profilové rozhodovanie (entry/exit).</param>
    public SignalAspect CalculateRouteAspect(
        RouteDefinition route,
        bool nextSignalIsRestricted = false,
        SignalElement? routeSignal = null)
    {
        if (route == null) throw new ArgumentNullException(nameof(route));

        var diverging = route.TurnoutSettings.Any(setting => setting.RequiredState != TurnoutState.Straight);
        if (diverging)
        {
            // Odbočka má prednosť: LowerYellow (SlowProceed) sa look-ahead pravidlom neupgraduje.
            return SignalAspect.SlowProceed;
        }

        if (nextSignalIsRestricted)
            return SignalAspect.SlowExpect40;

        return SignalAspect.Caution;
    }

    /// <summary>
    /// Fáza 2.2 – Look-ahead: Pre každú aktívnu cestu R2 (X→A) skontroluje,
    /// či existuje aktívna cesta R1 (A→B) začínajúca v cielovom bloku R2.
    /// Ak navestidlo R1 ukazuje obmedzený aspekt, upgradne
    /// navestidlo R2 z Caution na SlowExpect40.
    /// </summary>
    /// <returns>Počet návestidiel, ktoré boli upgradnuté.</returns>
    public async Task<int> ApplyLookAheadAspectsAsync(
        IReadOnlyCollection<string> activeRouteIds,
        IEnumerable<RouteDefinition> allRoutes,
        IEnumerable<LayoutElement> layoutElements,
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string reason = "look-ahead",
        string? syncId = null)
    {
        if (activeRouteIds == null) throw new ArgumentNullException(nameof(activeRouteIds));
        if (allRoutes == null) throw new ArgumentNullException(nameof(allRoutes));
        if (layoutElements == null) throw new ArgumentNullException(nameof(layoutElements));

        if (activeRouteIds.Count < 2)
            return 0; // Look-ahead je relevantný len pre ≥2 aktívne cesty.

        var elements = layoutElements.ToList();
        var signalsById = elements.OfType<SignalElement>()
            .ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
        var blocksById = elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        var activeSet = new HashSet<string>(activeRouteIds, StringComparer.OrdinalIgnoreCase);
        var activeRouteList = allRoutes
            .Where(r => activeSet.Contains(r.Id))
            .ToList();

        // Vybuduj mapu: FromBlockId → (route, startSignal?) pre každú aktívnu cestu.
        var routesByFromBlock = new Dictionary<string, List<(RouteDefinition Route, SignalElement? StartSignal)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var route in activeRouteList)
        {
            TryResolveRouteStartSignal(route, blocksById, signalsById, out var startSignal);

            if (!routesByFromBlock.TryGetValue(route.FromBlockId, out var list))
            {
                list = new List<(RouteDefinition, SignalElement?)>();
                routesByFromBlock[route.FromBlockId] = list;
            }
            list.Add((route, startSignal));
        }

        // Pre každú aktívnu cestu R2 (X→A), skontroluj či existuje aktívna cesta R1 (A→*)
        // a či R1's navestidlo ukazuje obmedzený aspekt → upgrade R2's navestidla.
        int upgraded = 0;
        var toSend = new List<SignalElement>();

        foreach (var r2 in activeRouteList)
        {
            if (string.IsNullOrWhiteSpace(r2.ToBlockId))
                continue;

            // Hľadaj aktívne cesty začínajúce v cielovom bloku R2.
            if (!routesByFromBlock.TryGetValue(r2.ToBlockId, out var onwardRoutes))
                continue;

            // Skontroluj, či niektoré onward navestidlo je obmedzené.
            bool hasRestrictedOnward = onwardRoutes
                .Any(o => o.StartSignal != null && IsRestrictedAspect(o.StartSignal.Aspect));

            if (!hasRestrictedOnward)
                continue;

            // Nájdi navestidlo R2 a upgradni ho z Caution → SlowExpect40.
            if (!TryResolveRouteStartSignal(r2, blocksById, signalsById, out var r2Signal))
                continue;

            // Skip návestidlá s neplatnou DCC adresou.
            if (!r2Signal.HasValidDccAddress())
                continue;

            // Upgrade iba z Caution (odbočka/slow aspekty zostávajú obmedzené).
            if (r2Signal.Aspect != SignalAspect.Caution)
                continue;

            r2Signal.Aspect = SignalAspect.SlowExpect40;
            toSend.Add(r2Signal);
            upgraded++;
        }

        foreach (var signal in toSend)
            await UpdatePhysicalSignal(signal, signal.Aspect, dccClient, ct, reason, syncId, force: true);

        return upgraded;
    }

    /// <summary>Vráti true ak aspekt indikuje obmedzenie rýchlosti alebo zastavenie.</summary>
    public static bool IsRestrictedAspect(SignalAspect aspect)
        => aspect is SignalAspect.Caution
            or SignalAspect.SlowProceed
            or SignalAspect.SlowCaution
            or SignalAspect.SlowExpect40;

    /// <summary>
    /// Preloží návestný aspekt na maximálnu cieľovú rýchlosť pre dashboard/automatiku (0..100).
    /// </summary>
    public static int ResolveSpeedLimitForAspect(SignalAspect aspect)
        => aspect switch
        {
            SignalAspect.Green => 100,
            SignalAspect.Proceed => 100,
            SignalAspect.SlowProceed => 40,
            SignalAspect.SlowCaution => 35,
            SignalAspect.SlowExpect40 => 35,
            SignalAspect.Yellow => 50,
            SignalAspect.Caution => 50,
            SignalAspect.White => 25,
            SignalAspect.ShuntingPermitted => 25,
            SignalAspect.Stop => 0,
            _ => 0
        };

    public Task<bool> ApplySignalAspectsForRouteAsync(
        RouteDefinition route,
        CancellationToken ct = default,
        string reason = "route-activation",
        string? syncId = null,
        bool sendDcc = true)
    {
        if (_runtimeLayoutElements == null)
        {
            Log.Warning("Route signal apply skipped: runtime layout context missing (route={RouteId}, reason={Reason}, syncId={SyncId})",
                route.Id, reason, syncId ?? "-");
            return Task.FromResult(false);
        }

        return ApplySignalAspectsForRouteAsync(route, _runtimeLayoutElements, _runtimeDccClient, ct, reason, syncId, sendDcc);
    }

    public async Task<bool> ApplySignalAspectsForRouteAsync(
        RouteDefinition route,
        IEnumerable<LayoutElement> layoutElements,
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string reason = "route-activation",
        string? syncId = null,
        bool sendDcc = true)
    {
        if (route == null) throw new ArgumentNullException(nameof(route));
        if (layoutElements == null) throw new ArgumentNullException(nameof(layoutElements));

        var elements = layoutElements.ToList();
        var signalsById = elements.OfType<SignalElement>()
            .ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
        var blocksById = elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        var fallbackAspect = ResolveSafetyFallbackAspect(route);
        if (!TryResolveRouteStartSignal(route, blocksById, signalsById, out var signal))
        {
            Log.Warning("Route signal apply failed: no start signal resolved for route {RouteId}. Safety fallback remains {FallbackAspect}. reason={Reason} syncId={SyncId}",
                route.Id,
                fallbackAspect,
                reason,
                syncId ?? "-");
            return false;
        }

        var routeSignal = signal;

        var nextBlockOccupied = !string.IsNullOrWhiteSpace(route.ToBlockId)
            && blocksById.TryGetValue(route.ToBlockId, out var nextBlock)
            && nextBlock.IsOccupied;
        var aspect = CalculateRouteAspect(route, nextSignalIsRestricted: nextBlockOccupied, routeSignal: routeSignal);

        if (!IsPhysicalAspectSupportedBySignal(routeSignal, aspect))
        {
            var fallbackForUnsupportedAspect = NormalizeAspectForSignal(routeSignal, aspect, reason);
            Log.Warning(
                "Route signal apply blocked/normalized: route={RouteId} signal={SignalId} profile={ProfileId} requestedAspect={RequestedAspect} fallbackAspect={FallbackAspect} sendDcc={SendDcc} reason={Reason} syncId={SyncId}",
                route.Id,
                routeSignal.Id,
                routeSignal.SignalProfile ?? "<default>",
                aspect,
                fallbackForUnsupportedAspect,
                sendDcc,
                reason,
                syncId ?? "-");

            if (!sendDcc)
                return false;

            aspect = fallbackForUnsupportedAspect;
        }

        if (!sendDcc)
            return true;

        routeSignal.Aspect = NormalizeAspectForSignal(routeSignal, aspect, reason);
        TrackFlowDoctorService.Instance.Diagnose(
            "Návestidlo",
            $"Nahadzujem návestidlo {SignalDisplayName(routeSignal)} na {routeSignal.Aspect.ToSlovakName()}",
            DiagnosticLevel.Info);

        if (routeSignal.HasValidDccAddress())
        {
            // Route activation MUSÍ vždy fyzicky odoslať príkaz (force=true) – nesmie ho zablokovať
            // cache, ani prípadný neaktuálny "naposledy odoslaný" stav.
            await UpdatePhysicalSignal(routeSignal, routeSignal.Aspect, dccClient, ct, reason, syncId, force: true);
        }

        return true;
    }

    public async Task<int> SendAllCurrentStatesToCentralAsync(
        IEnumerable<LayoutElement> layoutElements,
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string reason = "force",
        string? syncId = null)
    {
        if (layoutElements == null) throw new ArgumentNullException(nameof(layoutElements));

        var signals = layoutElements.OfType<SignalElement>().ToList();
        int sent = 0;
        foreach (var signal in signals)
        {
            if (await SendCurrentStateToCentral(signal, dccClient, ct, reason, syncId))
                sent++;
        }

        return sent;
    }

    public SignalAspect ResolveAspect(SignalElement signal, IReadOnlyDictionary<string, BlockElement> blocks)
    {
        if (signal == null) throw new ArgumentNullException(nameof(signal));
        if (blocks == null) throw new ArgumentNullException(nameof(blocks));

        if (string.IsNullOrWhiteSpace(signal.ProtectsBlockId))
            return SignalAspect.Stop;

        if (!blocks.TryGetValue(signal.ProtectsBlockId, out var protectedBlock))
            return SignalAspect.Stop;

        return ResolveAspectForProfile(signal.SignalProfile, protectedBlock);
    }

    /// <summary>
    /// Vyrieši aspekt podľa profilu návestidla a stavu bloku.
    /// Delegované do SignalSystemRegistry (centrálny zdroj pravidiel).
    /// </summary>
    public static SignalAspect ResolveAspectForProfile(string? profile, BlockElement block)
    {
        return SignalSystemRegistry.ResolveRuntimeAspect(profile, block.IsOccupied, block.RequestYellow);
    }

    public bool TryValidateRouteStartSignalSupport(
        RouteDefinition route,
        IEnumerable<LayoutElement> layoutElements,
        [NotNullWhen(false)] out string? failureReason)
    {
        if (route == null) throw new ArgumentNullException(nameof(route));
        if (layoutElements == null) throw new ArgumentNullException(nameof(layoutElements));

        var elements = layoutElements.ToList();
        var signalsById = elements.OfType<SignalElement>()
            .ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
        var blocksById = elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        if (!TryResolveRouteStartSignal(route, blocksById, signalsById, out var routeSignal))
        {
            failureReason = null;
            return true;
        }

        var nextBlockOccupied = !string.IsNullOrWhiteSpace(route.ToBlockId)
            && blocksById.TryGetValue(route.ToBlockId, out var nextBlock)
            && nextBlock.IsOccupied;
        var requestedAspect = CalculateRouteAspect(route, nextSignalIsRestricted: nextBlockOccupied, routeSignal: routeSignal);
        if (IsPhysicalAspectSupportedBySignal(routeSignal, requestedAspect))
        {
            failureReason = null;
            return true;
        }

        failureReason = $"Návestidlo {SignalDisplayName(routeSignal)} s profilom {routeSignal.SignalProfile ?? "<default>"} nevie zobraziť návesť {requestedAspect.ToSlovakName()} pre cestu {ResolveRouteDisplayName(route)}. Nastav správny typ návestidla.";
        return false;
    }

    public bool TryValidateRouteSignalSupport(
        RouteDefinition route,
        IEnumerable<LayoutElement> layoutElements,
        [NotNullWhen(false)] out string? failureReason)
    {
        if (route == null) throw new ArgumentNullException(nameof(route));
        if (layoutElements == null) throw new ArgumentNullException(nameof(layoutElements));

        var layout = new TrackLayout();
        layout.Elements.AddRange(layoutElements);

        var traversalBlockIds = route.BlockIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        if (traversalBlockIds.Count < 2)
        {
            failureReason = null;
            return true;
        }

        var validatedSignalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int segmentIndex = 0; segmentIndex < traversalBlockIds.Count - 1; segmentIndex++)
        {
            var fromBlockId = traversalBlockIds[segmentIndex];
            var toBlockId = traversalBlockIds[segmentIndex + 1];
            var direction = RouteSegmentSignalResolver.ResolveSegmentTravelDirection(layout, route, fromBlockId, toBlockId);
            var signal = RouteSegmentSignalResolver.ResolveSegmentStartSignal(layout, fromBlockId, direction, toBlockId);
            if (signal == null || !validatedSignalIds.Add(signal.Id))
                continue;

            if (!SignalSystemRegistry.SupportsTrainRouteRole(signal.SignalSystemId, signal.SignalProfile))
            {
                failureReason =
                    $"Návestidlo {SignalDisplayName(signal)} na segmente " +
                    $"{OperationDisplayHelpers.ResolveBlockDisplayName(layout, fromBlockId)} → {OperationDisplayHelpers.ResolveBlockDisplayName(layout, toBlockId)} " +
                    $"má profil {signal.SignalProfile ?? "<default>"}, ktorý nie je vhodný pre vlakovú cestu. Nastav správny typ návestidla.";
                return false;
            }

            foreach (var aspect in ResolvePotentialRouteSignalAspects(layout, route, traversalBlockIds, segmentIndex, signal))
            {
                if (IsPhysicalAspectSupportedBySignal(signal, aspect))
                    continue;

                failureReason =
                    $"Návestidlo {SignalDisplayName(signal)} na segmente " +
                    $"{OperationDisplayHelpers.ResolveBlockDisplayName(layout, fromBlockId)} → {OperationDisplayHelpers.ResolveBlockDisplayName(layout, toBlockId)} " +
                    $"s profilom {signal.SignalProfile ?? "<default>"} nevie zobraziť návesť {aspect.ToSlovakName()} pre cestu {ResolveRouteDisplayName(route)}. Nastav správny typ návestidla.";
                return false;
            }
        }

        failureReason = null;
        return true;
    }

    private IReadOnlyCollection<SignalAspect> ResolvePotentialRouteSignalAspects(
        TrackLayout layout,
        RouteDefinition route,
        IReadOnlyList<string> traversalBlockIds,
        int segmentIndex,
        SignalElement signal)
    {
        var possibleAspects = new HashSet<SignalAspect>();
        var baseAspect = CalculateRouteAspect(route);
        bool isStartSegment = segmentIndex == 0;
        bool isDiverged = isStartSegment
                          && (baseAspect == SignalAspect.SlowProceed
                              || baseAspect == SignalAspect.SlowCaution);

        if (isDiverged)
            possibleAspects.Add(SignalAspect.SlowProceed);
        else
            possibleAspects.Add(SignalAspectLogic.GetPermissiveAspect(signal));

        if (HasRouteLookAheadSignal(layout, route, traversalBlockIds, segmentIndex))
            possibleAspects.Add(isDiverged ? SignalAspect.SlowCaution : SignalAspect.Caution);

        return possibleAspects;
    }

    private static bool HasRouteLookAheadSignal(
        TrackLayout layout,
        RouteDefinition route,
        IReadOnlyList<string> traversalBlockIds,
        int segmentIndex)
    {
        if (segmentIndex < 0 || segmentIndex + 2 >= traversalBlockIds.Count)
            return false;

        var nextBlockId = traversalBlockIds[segmentIndex + 1];
        var nextDirection = RouteSegmentSignalResolver.ResolveSegmentTravelDirection(
            layout,
            route,
            nextBlockId,
            traversalBlockIds[segmentIndex + 2]);

        return RouteSegmentSignalResolver.ResolveSegmentStartSignal(layout, nextBlockId, nextDirection, traversalBlockIds[segmentIndex + 2]) != null;
    }

    public async Task ApplyDccAsync(
        IEnumerable<SignalElement> changedSignals,
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string reason = "refresh",
        string? syncId = null)
    {
        if (dccClient == null) return;

        foreach (var signal in changedSignals)
            await UpdatePhysicalSignal(signal, signal.Aspect, dccClient, ct, reason, syncId, force: false);
    }

    /// <summary>
    /// Odošle aktuálny stav konkrétneho návestidla do centrály (route activation / manual override).
    /// Vždy force=true – ignoruje cache.
    /// </summary>
    public Task<bool> SendCurrentStateToCentral(
        SignalElement signal,
        IDccCentralClient? dccClient,
        CancellationToken ct = default,
        string reason = "runtime",
        string? syncId = null)
    {
        if (signal == null) throw new ArgumentNullException(nameof(signal));
        return UpdatePhysicalSignal(signal, signal.Aspect, dccClient, ct, reason, syncId, force: true);
    }

    // Jediny vstup pre fyzicke odoslanie navestidla do DCC.
    public Task<bool> UpdatePhysicalSignal(SignalElement signal, SignalAspect aspect)
        => UpdatePhysicalSignal(signal, aspect, _runtimeDccClient, CancellationToken.None, "runtime", null, force: false);

    /// <summary>
    /// JEDINÝ bod pre fyzické odoslanie aspektu návestidla na DCC centrálu.
    /// PeLi dekodéry používajú BASIC TURNOUT príkazy (nie Extended Accessory).
    /// Board address N ovláda 4 TURNOUT adresy: N×4+1, N×4+2, N×4+3, N×4+4.
    /// Žiadna iná metóda v aplikácii NESMIE volať DCC príkazy priamo.
    /// </summary>
    /// <param name="signal">Návestidlo, ktoré sa má fyzicky aktualizovať.</param>
    /// <param name="aspect">Požadovaný aspekt pred profilovou normalizáciou.</param>
    /// <param name="dccClient">DCC klient použitý na odoslanie príkazu.</param>
    /// <param name="ct">Token zrušenia.</param>
    /// <param name="reason">Textový dôvod odoslania pre diagnostiku.</param>
    /// <param name="syncId">Voliteľný identifikátor synchronizačnej operácie.</param>
    /// <param name="force">true = ignoruj cache (route activation, manual override).</param>
    private async Task<bool> UpdatePhysicalSignal(
        SignalElement signal,
        SignalAspect aspect,
        IDccCentralClient? dccClient,
        CancellationToken ct,
        string reason,
        string? syncId,
        bool force = false)
    {
        if (signal == null) throw new ArgumentNullException(nameof(signal));

        var effectiveAspect = NormalizeAspectForSignal(signal, aspect, reason);
        if (signal.Aspect != effectiveAspect)
            signal.Aspect = effectiveAspect;

        // Stop aspekt musí byť vždy odoslaný - ignorujeme cache (bezpečnostné pravidlo).
        if (effectiveAspect == SignalAspect.Stop)
            force = true;

        if (!signal.HasValidDccAddress())
            return false;

        if (dccClient == null)
        {
            Log.Warning("Signal DCC skipped: dccClient is NULL (signal={SignalId}, reason={Reason}, syncId={SyncId}). " +
                        "DCC pipeline is not wired – check DccConnectionService injection.",
                signal.Id, reason, syncId ?? "-");
            return false;
        }

        // PeLi board address → base TURNOUT address calculation
        int board = SignalDccOptions.ResolveAddress(signal.DccAddress);

        // Fail-safe reset: Stop sa musí vždy fyzicky odoslať (aj pri cache hit),
        // aby po de/aktivácii cesty nezostal na trati starý povoľujúci aspekt.
        bool forceStopReset = effectiveAspect == SignalAspect.Stop
            && (!_lastSentByAddress.TryGetValue(board, out var lastStopCheck) || lastStopCheck != effectiveAspect);

        // Cache: pre route-activation / force / Stop reset vždy posielame, inak len pri reálnej zmene.
        if (!force && !forceStopReset && _lastSentByAddress.TryGetValue(board, out var lastAspect) && lastAspect == effectiveAspect)
            return false;

        // Reconnect-wait: krátka pauza ak centrála nie je momentálne pripojená.
        if (!dccClient.IsConnected)
        {
            Log.Warning("Signal DCC: client reports DISCONNECTED before send (signal={SignalId}, reason={Reason}, syncId={SyncId}). " +
                        "Waiting {DelayMs} ms for transient reconnect…",
                signal.Id, reason, syncId ?? "-", SignalDccOptions.RetryDelayMs);

            try { await Task.Delay(SignalDccOptions.RetryDelayMs, ct); }
            catch (OperationCanceledException) { return false; }

            if (!dccClient.IsConnected)
            {
                Log.Error("Signal DCC ABORTED: client still disconnected after wait (signal={SignalId}, board={Board}, " +
                          "aspect={Aspect}, reason={Reason}, syncId={SyncId}). Aspect will NOT be visible on track. " +
                          "Diagnostic: skontroluj DccConnectionService stav, sieť, IP/port v EffectiveSettings.",
                    signal.Id, board, effectiveAspect, reason, syncId ?? "-");
                return false;
            }

        }

        // Diagnostika DCC odoslania – až po cache/disconnect guardoch, teda len pri reálnom pokuse o fyzický send.
        TrackFlowDoctorService.Instance.Diagnose(
            "DCC",
            $"Odosielam aspekt '{effectiveAspect.ToSlovakName()}' na adresu {board}",
            DiagnosticLevel.Info);

        // Vlastný send + retry pri sieťovej chybe (Z21 setne IsConnected=false ak SendAsync zhodí).
        int attempts = Math.Max(1, 1 + SignalDccOptions.RetryCount);
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                if (signal.IsBasicMode)
                {
                    // PeLi aspect → TURNOUT address + state mapping
                    var (turnoutAddr, branch) = MapPeliAspectToTurnout(board, effectiveAspect);
                    if (turnoutAddr == 0)
                    {
                        Log.Warning("Signal DCC skipped: unmapped aspect {Aspect} for signal {SignalId} board {Board}",
                            effectiveAspect, signal.Id, board);
                        return false;
                    }

                    await dccClient.SetTurnoutAsync(turnoutAddr, branch, activate: true, ct);
                }
                else
                {
                    var aspectNumber = MapAspectToExtendedNumber(effectiveAspect);
                    await dccClient.SetExtendedAccessoryAspectAsync(board, aspectNumber, ct);
                }

                _lastSentByAddress[board] = effectiveAspect;
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Signal DCC send failed (signal={SignalId}, attempt={Attempt}/{Max}, reason={Reason})",
                    signal.Id, attempt, attempts, reason);

                if (attempt >= attempts) break;

                try { await Task.Delay(SignalDccOptions.RetryDelayMs, ct); }
                catch (OperationCanceledException) { return false; }
            }
        }

        Log.Error("Signal DCC FAILED after {Attempts} attempts (signal={SignalId}, board={Board}, aspect={Aspect})",
            attempts, signal.Id, board, effectiveAspect);
        return false;
    }

    public static bool IsPhysicalAspectSupportedBySignal(SignalElement signal, SignalAspect aspect)
    {
        if (signal == null) throw new ArgumentNullException(nameof(signal));

        var systemId = string.IsNullOrWhiteSpace(signal.SignalSystemId)
            ? SignalSystemDefinition.DefaultSystemId
            : signal.SignalSystemId;

        return SignalSystemRegistry.SupportsPhysicalAspect(systemId, signal.SignalProfile, aspect);
    }

    public bool TryValidateRouteStartSignalAspectCompatibility(
        RouteDefinition route,
        IEnumerable<LayoutElement> layoutElements,
        [NotNullWhen(false)] out string? failureReason)
        => TryValidateRouteStartSignalSupport(route, layoutElements, out failureReason);

    public bool TryValidateRouteSignalAspectCompatibility(
        RouteDefinition route,
        IEnumerable<LayoutElement> layoutElements,
        [NotNullWhen(false)] out string? failureReason)
        => TryValidateRouteSignalSupport(route, layoutElements, out failureReason);

    public static bool IsAspectSupportedBySignal(SignalElement signal, SignalAspect aspect)
        => IsPhysicalAspectSupportedBySignal(signal, aspect);

    public static SignalAspect NormalizeAspectForSignal(SignalElement signal, SignalAspect requestedAspect, string? reason = null)
    {
        if (signal == null) throw new ArgumentNullException(nameof(signal));

        var systemId = string.IsNullOrWhiteSpace(signal.SignalSystemId)
            ? SignalSystemDefinition.DefaultSystemId
            : signal.SignalSystemId;

        var effectiveAspect = SignalSystemRegistry.ResolveFailSafeAspect(systemId, signal.SignalProfile, requestedAspect);
        if (effectiveAspect != requestedAspect)
        {
            Log.Warning(
                "Signal profile compatibility fallback: signal={SignalId} profile={ProfileId} requestedAspect={RequestedAspect} fallbackAspect={FallbackAspect} reason={Reason}",
                signal.Id,
                signal.SignalProfile ?? "<default>",
                requestedAspect,
                effectiveAspect,
                reason ?? "-");
        }

        return effectiveAspect;
    }


    private static string SignalDisplayName(SignalElement signal) =>
        !string.IsNullOrWhiteSpace(signal.Label) ? signal.Label : signal.Id;

    private static string ResolveRouteDisplayName(RouteDefinition route) =>
        !string.IsNullOrWhiteSpace(route.Name) ? route.Name : route.Id;

    /// <summary>
    /// Mapuje PeLi aspect na TURNOUT adresu a výber výstupu.
    /// Board address N → adresy N×4+1, N×4+2, N×4+3, N×4+4 (každá má 2 stavy).
    /// branch: false = straight (rovno), true = thrown (do odbočky).
    /// </summary>
    private static (int turnoutAddress, bool branch) MapPeliAspectToTurnout(int board, SignalAspect aspect)
    {
        int baseAddr = board * 4;
        
        return aspect switch
        {
            SignalAspect.Stop
                => (baseAddr + 1, false),  // 97 straight → Aspect 1 (Stoj/Červená)
            
            SignalAspect.Proceed or SignalAspect.Green 
                => (baseAddr + 1, true),   // 97 thrown → Aspect 2 (Voľno/Zelená)
            
            SignalAspect.Caution or SignalAspect.Yellow 
                => (baseAddr + 2, false),  // 98 straight → Aspect 3 (Výstraha/Horná žltá)
            
            SignalAspect.SlowProceed 
                => (baseAddr + 2, true),   // 98 thrown → Aspect 4 (40 a Voľno - Zelená+dolná žltá)
            
            SignalAspect.SlowCaution 
                => (baseAddr + 3, false),  // 99 straight → Aspect 5 (40 a Výstraha - Horná+dolná žltá)
            
            SignalAspect.SlowExpect40 
                => (baseAddr + 3, true),   // 99 thrown → Aspect 10 (Očakávaj 40 - Blikajúca+dolná)
            
            SignalAspect.ShuntingPermitted or SignalAspect.White 
                => (baseAddr + 4, false),  // 100 straight → Aspect 14 (Posun/Biela)
            
            // Aspect 16 (Privolávacia) zatiaľ nemáme v SignalAspect enum
            // => (baseAddr + 4, true),   // 100 thrown → Aspect 16 (Privolávacia)
            
            SignalAspect.Off 
                => (baseAddr + 1, false),  // Off → defaultne Stop
            
            _ => (0, false)  // Neznámy aspect
        };
    }

    public static bool MapAspectToAccessory(SignalAspect aspect)
    {
        return aspect switch
        {
            SignalAspect.Stop => false,
            SignalAspect.Off => false,
            SignalAspect.Proceed => true,
            SignalAspect.Caution => true,
            SignalAspect.SlowProceed => true,
            SignalAspect.SlowCaution => true,
            SignalAspect.SlowExpect40 => true,
            SignalAspect.ShuntingPermitted => true,
            _                                => false
        };
    }

    public static int MapAspectToExtendedNumber(SignalAspect aspect)
    {
        return aspect switch
        {
            SignalAspect.Off => 0,
            SignalAspect.Stop => 1,                 // Stoj (Červená)
            SignalAspect.Proceed => 2,              // Voľno (Zelená)
            SignalAspect.Caution => 3,              // Výstraha (Horná žltá)
            SignalAspect.SlowProceed => 4,          // 40 a Voľno (Zelená + Dolná žltá)
            SignalAspect.SlowCaution => 5,          // 40 a Výstraha (Horná žltá + Dolná žltá)
            SignalAspect.SlowExpect40 => 6,         // 40 a očakávaj 40 (Blikajúca žltá + Dolná žltá)
            SignalAspect.ShuntingPermitted => 14,   // Posun dovolený (Biela)

            // Legacy aliasy (spätná kompatibilita so staršími projektmi pred TAB2 prechodom).
            SignalAspect.Green => 2,
            SignalAspect.Yellow => 3,
            SignalAspect.White => 14,

            _ => 1  // Fallback: Stoj (bezpečný stav)
        };
    }


    public bool ApplySignalAspectsForRoute(RouteDefinition route, string? reason = null)
    {
        if (_runtimeLayoutElements == null)
        {
            Log.Warning("Route signal apply skipped: runtime layout context missing (route={RouteId}, reason={Reason})",
                route.Id, reason);
            return false;
        }

        return ApplySignalAspectsForRouteAsync(route, _runtimeLayoutElements, _runtimeDccClient, CancellationToken.None, reason ?? "route-activation", null)
            .GetAwaiter()
            .GetResult();
    }

    public static bool TryResolveRouteStartSignal(
        RouteDefinition route,
        IReadOnlyDictionary<string, BlockElement> blocksById,
        IReadOnlyDictionary<string, SignalElement> signalsById,
        [NotNullWhen(true)] out SignalElement? signal)
    {
        signal = null;
        if (string.IsNullOrWhiteSpace(route.FromBlockId) || !blocksById.TryGetValue(route.FromBlockId, out var fromBlock))
            return false;

        var startDirection = RouteDirection.NormalizeOrDefault(
            route.StartNavigationDirection,
            RouteDirection.Right,
            $"Route[{route.Id}].{nameof(RouteDefinition.StartNavigationDirection)}");

        var navigationDirection = startDirection switch
        {
            var d when string.Equals(d, RouteDirection.Left, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Left,
            var d when string.Equals(d, RouteDirection.Up, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Up,
            var d when string.Equals(d, RouteDirection.Down, StringComparison.OrdinalIgnoreCase) => NavigationDirection.Down,
            _ => NavigationDirection.Right
        };

        var signalId = fromBlock.GetSignalForDirection(navigationDirection);
        if (!string.IsNullOrWhiteSpace(signalId)
            && signalsById.TryGetValue(signalId, out signal))
        {
            return true;
        }

        signal = null;

        if (string.IsNullOrWhiteSpace(signalId) || signal == null)
        {
            // Fallback: skús nájsť prvé návestidlo z RouteSignalIds, ktoré patrí FromBlock.
            foreach (var fallbackSignalId in route.RouteSignalIds)
            {
                if (string.IsNullOrWhiteSpace(fallbackSignalId) || !signalsById.TryGetValue(fallbackSignalId, out var fallbackSignal))
                    continue;

                // Skontroluj, či blok vlastní toto návestidlo PRESNE v smere jazdy.
                // (Smerová integrita: návestidlo musí byť priradené pre daný smer.)
                if (!string.Equals(fromBlock.GetSignalForDirection(navigationDirection), fallbackSignal.Id, StringComparison.OrdinalIgnoreCase))
                    continue;

                signal = fallbackSignal;
                return true;
            }
            return false;
        }

        return false;
    }

    internal static bool IsSignalFacingDirection(SignalElement signal, NavigationDirection requiredDirection)
    {
        var normalized = NormalizeRightAngle(signal.Rotation);
        var facing = normalized switch
        {
            90 => NavigationDirection.Right,
            180 => NavigationDirection.Down,
            270 => NavigationDirection.Left,
            _ => NavigationDirection.Up
        };

        return facing == requiredDirection;
    }

    private static int NormalizeRightAngle(double rotation)
    {
        var rounded = (int)Math.Round(rotation);
        var norm = ((rounded % 360) + 360) % 360;
        return ((norm + 45) / 90) * 90 % 360;
    }


    private static SignalAspect ResolveSafetyFallbackAspect(RouteDefinition route)
    {
        if (!string.Equals(route.SafetyFallbackAspect, "Stop", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Unsupported route SafetyFallbackAspect '{SafetyFallbackAspect}' on route {RouteId}. Using Stop.",
                route.SafetyFallbackAspect,
                route.Id);
        }

        return SignalAspect.Stop;
    }

    /// <summary>
    /// v1.4: Vybuduje množinu ID signálov, ktoré sú súčasťou aktívnych ciest.
    /// Tieto signály majú aspekt nastavený explicitne cestou a nesmú byť prepísané automatickým refresh.
    /// </summary>
    private static HashSet<string> BuildActiveRouteSignalsMap(
        IEnumerable<LayoutElement> layoutElements,
        IReadOnlyCollection<string>? activeRouteIds,
        IEnumerable<RouteDefinition>? allRoutes)
    {
        var protectedSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (activeRouteIds == null || activeRouteIds.Count == 0 || allRoutes == null)
            return protectedSignals;

        var elements = layoutElements.ToList();
        var signalsById = elements.OfType<SignalElement>()
            .ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
        var blocksById = elements.OfType<BlockElement>()
            .ToDictionary(b => b.Id, b => b, StringComparer.OrdinalIgnoreCase);

        var activeSet = new HashSet<string>(activeRouteIds, StringComparer.OrdinalIgnoreCase);
        var activeRoutes = allRoutes.Where(r => activeSet.Contains(r.Id)).ToList();

        foreach (var route in activeRoutes)
        {
            // Nájdi štartové návestidlo cesty.
            if (TryResolveRouteStartSignal(route, blocksById, signalsById, out var startSignal) && startSignal != null)
                protectedSignals.Add(startSignal.Id);

            // OPRAVA: Chráň aj všetky ďalšie návestidlá priradené k ceste (RouteSignalIds).
            // Inak by ich periodický RefreshAspects prepísal na Voľno len preto, že chránený blok
            // bol uvoľnený – návestidlá v aktívnej ceste musia zostať pod kontrolou RouteManager
            // až do TailClear / deaktivácie cesty.
            foreach (var sigId in route.RouteSignalIds)
            {
                if (string.IsNullOrWhiteSpace(sigId))
                    continue;

                protectedSignals.Add(sigId);
            }
        }

        return protectedSignals;
    }
}


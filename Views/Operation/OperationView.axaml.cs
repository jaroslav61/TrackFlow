using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Serilog;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.Helpers;
using TrackFlow.ViewModels.Operation;
using TrackFlow.Views.Editor.Markers;

namespace TrackFlow.Views.Operation;

public partial class OperationView : UserControl
{
    private const double Cell = 24.0; // Rovnaká veľkosť bunky ako v editore
    
    private Canvas? _operationCanvas;
    private Canvas? _elementsLayer;
    private Border? _routeActivationMessageOverlay;
    
    // Cache pre drag arrow controls pre lepšiu performance
    private Image? _dragArrowRight;
    private Image? _dragArrowLeft;
    private Image? _dragArrowDown;
    private Image? _dragArrowUp;

    // === Cursor ownership arbitration (route hover) =========================
    // Aktívny route hover má najvyššiu prioritu. Window-level pin udržiava HAND
    // aj počas RefreshLayout (kedy sa hosti rekonštruujú a Avalonia stratí
    // pointer-tracked cursor až do najbližšieho mouse move).
    private bool _isRefreshingLayout;
    private RouteElement? _hoveredRouteElement;
    private bool _routeHoverWindowCursorPinned;
    private Window? _pinnedWindow;
    private OperationViewModel? _vmCurrent;

    // === Route host cache ===================================================
    // Route hostingy MUSIA prežiť RefreshLayout tick. Predtým sa pri každom
    // ticku _elementsLayer.Children.Clear() zničili všetky hosty, čo spôsobilo
    // synthetic PointerExited/Entered a Avalonia fallback na Arrow → HAND
    // ↔ Arrow flicker. Cache zaisťuje, že route host sa vytvorí raz a potom
    // sa už len reposition/update visual state. Full recreate iba ak sa zmení
    // marker rotácia, alebo ak route element zmizne.
    private readonly Dictionary<string, Control> _routeHostCache =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class RouteHostMeta
    {
        public int Rotation;
    }

    public OperationView()
    {
        AvaloniaXamlLoader.Load(this);
        
        _operationCanvas = this.FindControl<Canvas>("OperationCanvas");
        _elementsLayer = this.FindControl<Canvas>("ElementsLayer");
        _routeActivationMessageOverlay = this.FindControl<Border>("RouteActivationMessageOverlay");
        
        // Cache arrow controls pre rýchlejší prístup
        _dragArrowRight = this.FindControl<Image>("DragArrowRight");
        _dragArrowLeft = this.FindControl<Image>("DragArrowLeft");
        _dragArrowDown = this.FindControl<Image>("DragArrowDown");
        _dragArrowUp = this.FindControl<Image>("DragArrowUp");
        
        // Povoliť drag&drop pre priradenie lokomotív
        if (_operationCanvas != null)
        {
            DragDrop.SetAllowDrop(_operationCanvas, true);
            _operationCanvas.AddHandler(DragDrop.DragOverEvent, OnCanvasLocoDragOver);
            _operationCanvas.AddHandler(DragDrop.DragLeaveEvent, OnCanvasLocoDragLeave);
            _operationCanvas.AddHandler(DragDrop.DropEvent, OnCanvasLocoDrop);
        }
        
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }
    
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachFromVm();

        if (DataContext is OperationViewModel vm)
        {
            _vmCurrent = vm;

            // Pripojiť sa na event pre refresh schémy
            vm.LayoutRefreshRequested += RefreshLayout;
            
            // Subscribovať na zmeny vagónov každej lokomotívy (aby sa blok prekresli keď sa zmení súprava)
            foreach (var loco in vm.Locomotives)
            {
                loco.AttachedWagons.CollectionChanged += OnLocoWagonsChanged;
                loco.PropertyChanged += OnLocoPropertyChanged;
            }

            // Keď sa pridá nová lokomotíva, subscribovať aj na ňu
            vm.Locomotives.CollectionChanged += OnLocomotivesCollectionChanged;
            
            // Prvotné vykreslenie
            RefreshLayout();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachFromVm();
    }

    private void DetachFromVm()
    {
        if (_vmCurrent == null)
            return;

        _vmCurrent.LayoutRefreshRequested -= RefreshLayout;
        _vmCurrent.Locomotives.CollectionChanged -= OnLocomotivesCollectionChanged;

        foreach (var loco in _vmCurrent.Locomotives)
        {
            loco.AttachedWagons.CollectionChanged -= OnLocoWagonsChanged;
            loco.PropertyChanged -= OnLocoPropertyChanged;
        }

        _vmCurrent = null;
    }

    private void OnLocomotivesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems != null)
            foreach (Locomotive l in args.NewItems)
            {
                l.AttachedWagons.CollectionChanged += OnLocoWagonsChanged;
                l.PropertyChanged += OnLocoPropertyChanged;
            }

        if (args.OldItems != null)
            foreach (Locomotive l in args.OldItems)
            {
                l.AttachedWagons.CollectionChanged -= OnLocoWagonsChanged;
                l.PropertyChanged -= OnLocoPropertyChanged;
            }
    }

    private void OnLocoWagonsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Ak sa zmenili vagóny lokomotívy, prekreslíme layout (blok zobrazuje ikony aj vagónov)
        RefreshLayout();
    }

    private void OnLocoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Ak sa zmenila orientácia alebo aktívny stav lokomotívy, prekreslíme layout
        if (e.PropertyName is nameof(Locomotive.IsFlipped) or nameof(Locomotive.IsPlacedOnTrack))
            RefreshLayout();
    }
    
    private void RefreshLayout()
    {
        if (_elementsLayer == null) return;
        if (DataContext is not OperationViewModel vm) return;
        
        // Cursor ownership: počas refreshu sa NON-route hostingy rekonštruujú;
        // route hostingy reuseujeme z cache. Suppressujeme PointerExited cleanup
        // pre route hover, aby HAND nezhasol medzi tickami.
        _isRefreshingLayout = true;
        try
        {
            // 1) Vyčistiť canvas - ALE PONECHAŤ existing route hostingy.
            //    Predtým: _elementsLayer.Children.Clear() zničil všetko a vyvolal
            //    synthetic PointerExited/Entered → HAND ↔ Arrow flicker.
            var preservedRouteHosts = new HashSet<Control>(_routeHostCache.Values);
            for (int i = _elementsLayer.Children.Count - 1; i >= 0; i--)
            {
                var child = _elementsLayer.Children[i];
                if (child is Control c && preservedRouteHosts.Contains(c))
                    continue;
                _elementsLayer.Children.RemoveAt(i);
            }

            var seenRouteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 2) prechod: všetky ne-bloky (routes idú cez cache pipeline)
            foreach (var element in vm.LayoutElements)
            {
                if (IsBlockRenderElement(element)) continue;
                if (element is RouteElement routeEl)
                {
                    RenderOrReuseRouteHost(routeEl, vm, seenRouteIds);
                    continue;
                }
                RenderElement(element);
            }
            
            // 3) prechod: bloky nakoniec (na vrchu)
            foreach (var element in vm.LayoutElements)
            {
                if (!IsBlockRenderElement(element)) continue;
                RenderElement(element);
            }

            // 4) Odstrániť orphaned route hostingy (route element zmizol z layoutu).
            var orphanIds = _routeHostCache.Keys
                .Where(id => !seenRouteIds.Contains(id))
                .ToList();
            foreach (var id in orphanIds)
            {
                var orphan = _routeHostCache[id];
                _elementsLayer.Children.Remove(orphan);
                _routeHostCache.Remove(id);
            }
        }
        finally
        {
            _isRefreshingLayout = false;
            ReconcileRouteHoverCursorAfterRefresh(vm);
        }
    }

    /// <summary>
    /// Po RefreshLayout overí, či logicky-hoverovaná route stále existuje
    /// a je povolená. Podľa toho udrží alebo uvoľní window-level HAND pin.
    /// </summary>
    private void ReconcileRouteHoverCursorAfterRefresh(OperationViewModel vm)
    {
        if (_hoveredRouteElement == null)
            return;

        bool stillExists = vm.LayoutElements.OfType<RouteElement>()
            .Any(r => string.Equals(r.Id, _hoveredRouteElement.Id, StringComparison.OrdinalIgnoreCase));

        if (!stillExists)
        {
            ReleaseRouteHoverWindowCursor("route-element-removed-during-refresh");
            _hoveredRouteElement = null;
            return;
        }

        bool enabled = vm.IsRouteUiActivationEnabled(_hoveredRouteElement, out _, emitDiagnostic: false);
        if (enabled)
            PinRouteHoverWindowCursor(_hoveredRouteElement, "post-refresh-reapply");
        else
            ReleaseRouteHoverWindowCursor("post-refresh-route-not-enabled");
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // ROUTE HOST CACHE PIPELINE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pre route element vráti existujúci host z cache (a iba update visual
    /// state) alebo vytvorí nový. Týmto route hostingy preživajú RefreshLayout
    /// ticky → žiadny synthetic PointerExited/Entered → žiadny HAND flicker.
    /// Full recreate iba ak sa zmenila marker rotácia (alebo cache miss).
    /// </summary>
    private void RenderOrReuseRouteHost(RouteElement routeElement, OperationViewModel vm, HashSet<string> seenIds)
    {
        if (_elementsLayer == null) return;
        seenIds.Add(routeElement.Id);

        int rotation = LayoutElementFootprintHelper.NormalizeMarkerAngle(routeElement.Rotation);

        if (_routeHostCache.TryGetValue(routeElement.Id, out var existing)
            && existing.Tag is RouteHostMeta meta
            && meta.Rotation == rotation)
        {
            ApplyRouteHostMutableState(existing, routeElement, vm);
            return;
        }

        // Marker rotation zmena alebo cache miss → full recreate.
        if (existing != null)
        {
            _elementsLayer.Children.Remove(existing);
            _routeHostCache.Remove(routeElement.Id);
        }

        var host = BuildRouteHost(routeElement);
        if (host == null) return;

        _routeHostCache[routeElement.Id] = host;
        _elementsLayer.Children.Add(host);
        ApplyRouteHostMutableState(host, routeElement, vm);
    }

    /// <summary>
    /// Vybuduje stable route host (Grid + MarkerRoute + event wiring). Volá
    /// sa LEN raz pre daný route element (alebo pri zmene rotácie). Všetko
    /// čo sa môže meniť každý tick (pozícia, glow, route assigned color)
    /// rieši ApplyRouteHostMutableState.
    /// </summary>
    private Control? BuildRouteHost(RouteElement routeElement)
    {
        var inner = CreateMarkerInstance("Route");
        if (inner is not MarkerRoute routeMarker)
            return null;

        int markerAngle = LayoutElementFootprintHelper.NormalizeMarkerAngle(routeElement.Rotation);

        inner.UseLayoutRounding = false;
        if (inner is UserControl uc)
        {
            uc.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            uc.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        }

        // Pointer eventy MUSÍ vlastniť host Grid (vnútorný marker subtree by
        // vracal default Arrow → flicker). Route marker už obsahuje vlastný
        // outline, druhú "outline" vrstvu netvoríme.
        inner.IsHitTestVisible = false;
        if (inner is IMarkerAngle m)
            m.SetAngle(markerAngle);

        var (hostWidth, hostHeight) = GetElementFootprint(routeElement);
        var host = new Grid
        {
            Width = hostWidth,
            Height = hostHeight,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            UseLayoutRounding = false,
            ClipToBounds = false,
            Background = Brushes.Transparent,
            Tag = new RouteHostMeta { Rotation = markerAngle }
        };
        host.Children.Add(inner);

        void ApplyRouteUiState(bool emitDiagnostic, bool hovered, bool pressed)
        {
            var enabled = DataContext is OperationViewModel routeVm
                && routeVm.IsRouteUiActivationEnabled(routeElement, out _, emitDiagnostic);

            SetCursor(
                host,
                enabled ? Avalonia.Input.StandardCursorType.Hand : Avalonia.Input.StandardCursorType.Arrow);

            routeMarker.SetInteractionState(isHovered: enabled && hovered, isPressed: enabled && pressed);

            if (hovered)
            {
                if (enabled)
                    PinRouteHoverWindowCursor(routeElement, "route-hover-pin");
                else
                    ReleaseRouteHoverWindowCursor("route-hover-not-enabled");
            }
        }

        ApplyRouteUiState(emitDiagnostic: false, hovered: false, pressed: false);

        host.PointerEntered += (_, _) =>
        {
            _hoveredRouteElement = routeElement;
            ApplyRouteUiState(emitDiagnostic: true, hovered: true, pressed: false);
        };
        host.PointerExited += (_, _) =>
        {
            // Pri RefreshLayout sa NIČ nedeje – host je preserved → tento branch
            // je defensive guard pre prípad cache miss / rotation change.
            if (_isRefreshingLayout)
                return;

            if (ReferenceEquals(_hoveredRouteElement, routeElement))
                _hoveredRouteElement = null;

            ReleaseRouteHoverWindowCursor("route-pointer-exited");
            ApplyRouteUiState(emitDiagnostic: false, hovered: false, pressed: false);
        };
        host.PointerPressed += async (_, e) =>
        {
            if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
                return;

            // POZN: `_` v out-discard by tu konfliktoval s lambda sender param,
            // preto explicitný typed discard.
            var enabled = DataContext is OperationViewModel routeVm
                && routeVm.IsRouteUiActivationEnabled(routeElement, out string _, emitDiagnostic: true);

            SetCursor(
                host,
                enabled ? Avalonia.Input.StandardCursorType.Hand : Avalonia.Input.StandardCursorType.Arrow);

            if (!enabled)
            {
                routeMarker.SetInteractionState(isHovered: false, isPressed: false);
                ReleaseRouteHoverWindowCursor("route-press-not-enabled");
                e.Handled = true;
                return;
            }

            PinRouteHoverWindowCursor(routeElement, "route-press-pin");
            routeMarker.SetInteractionState(isHovered: true, isPressed: true);
            await OnRoutePointerPressedAsync(e, routeElement);
        };
        host.PointerReleased += (_, _) => ApplyRouteUiState(emitDiagnostic: false, hovered: true, pressed: false);

        return host;
    }

    /// <summary>
    /// Aplikuje na cached route host všetko, čo sa môže meniť každý tick
    /// (pozícia, ZIndex, glow effect podľa active path, MarkerRoute farba
    /// podľa SelectedRouteDefinitionId). Žiadny rebuild — host zostáva
    /// referenčne stabilný.
    /// </summary>
    private void ApplyRouteHostMutableState(Control host, RouteElement routeElement, OperationViewModel vm)
    {
        Canvas.SetLeft(host, routeElement.X);
        Canvas.SetTop(host, routeElement.Y);
        host.ZIndex = 300;

        bool isOnActivePath = IsElementOnActiveRoutePath(vm, routeElement.Id);
        if (isOnActivePath)
        {
            if (host.Effect is not DropShadowEffect)
            {
                host.Effect = new DropShadowEffect
                {
                    Color = Color.Parse("#00D4AA"),
                    BlurRadius = 10,
                    OffsetX = 0,
                    OffsetY = 0,
                    Opacity = 1.0
                };
            }
        }
        else
        {
            host.Effect = null;
        }

        bool hasRoute = RouteMarkerAssignmentHelper.HasAssignedRoute(vm.Settings.CurrentProject?.Layout, routeElement);
        if (host is Grid g)
        {
            var marker = g.Children.OfType<MarkerRoute>().FirstOrDefault();
            marker?.SetRouteAssigned(hasRoute);
        }
    }
    
    private void RenderElement(LayoutElement element)
    {
        if (_elementsLayer == null) return;

        // Routes idú EXKLUZÍVNE cez RenderOrReuseRouteHost (cache pipeline).
        // Tento guard chráni pred neúmyselným pridaním druhého (transient)
        // route hostu, ktorý by spôsoboval HAND ↔ Arrow flicker.
        if (element is RouteElement)
        {
            return;
        }

        Control? markerControl = CreateMarkerControl(element);
        
        if (markerControl != null)
        {
            if (element is BlockElement block)
            {
                markerControl.PointerPressed += async (_, e) => await OnBlockPointerPressedAsync(e, block);
            }

            Canvas.SetLeft(markerControl, element.X);
            Canvas.SetTop(markerControl, element.Y);
            markerControl.ZIndex = element switch
            {
                RouteElement => 300,
                BlockElement => 200,
                TextElement => 50,
                _ => 100
            };
            
            _elementsLayer.Children.Add(markerControl);
        }
    }
    
    private Control? CreateMarkerControl(LayoutElement element)
    {
        var markerKey = ResolveMarkerKey(element);
        bool isRouteMarker = string.Equals(markerKey, "Route", StringComparison.OrdinalIgnoreCase);

        // Block má špeciálne zaobchádzanie
        if (string.Equals(markerKey, "Block", StringComparison.OrdinalIgnoreCase))
            return CreateBlockControl(element);
        
        // Text má špeciálne zaobchádzanie - dynamická veľkosť
        if (string.Equals(markerKey, "Text", StringComparison.OrdinalIgnoreCase) && element is TextElement textEl)
            return CreateTextControl(textEl);

        if (string.IsNullOrWhiteSpace(markerKey))
        {
            Log.Debug("Operation render skipped: no marker key for element {ElementId} ({ElementType}).", element.Id, element.ElementType);
            return null;
        }
        
        // Route marker už obsahuje vlastný outline, preto druhú "outline" vrstvu nevytvárame.
        Control? innerOutline = isRouteMarker ? null : CreateMarkerInstance(markerKey);
        
        // Vytvoríme hornú vrstvu (normálny marker)
        Control? inner = CreateMarkerInstance(markerKey);
        
        if (inner == null)
        {
            Log.Warning("Operation render skipped: unsupported marker '{MarkerKey}' for element {ElementId} ({ElementType}).",
                markerKey, element.Id, element.ElementType);
            return null;
        }
        
        int markerAngle = LayoutElementFootprintHelper.NormalizeMarkerAngle(element.Rotation);

        // Nastavenie outline vrstvy
        if (innerOutline != null)
        {
            innerOutline.UseLayoutRounding = false;
            if (innerOutline is UserControl ucOutline)
            {
                ucOutline.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                ucOutline.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            }
            
            // Rotácia pre outline
            if (innerOutline is IMarkerAngle mOutline)
                mOutline.SetAngle(markerAngle);
            else if (markerAngle != 0)
                innerOutline.RenderTransform = new RotateTransform(markerAngle, Cell / 2, Cell / 2);
            
            // Aplikovať outline štýl
            ApplyOutlineStyle(innerOutline);
        }
        
        // Nastavenie hornej vrstvy
        inner.UseLayoutRounding = false;
        if (inner is UserControl uc)
        {
            uc.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            uc.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        }

        bool hostOwnsInteraction = element is RouteElement or SignalElement or TurnoutElement;
        if (hostOwnsInteraction)
        {
            // Kurzory a pointer eventy musia vlastniť host Grid; inak vnútorný marker subtree
            // vracia default Arrow a pri route hoveri vzniká flicker Hand ↔ Arrow.
            inner.IsHitTestVisible = false;
            if (innerOutline != null)
                innerOutline.IsHitTestVisible = false;
        }
        
        // Rotácia
        if (inner is IMarkerAngle m)
            m.SetAngle(markerAngle);
        else if (markerAngle != 0)
            inner.RenderTransform = new RotateTransform(markerAngle, Cell / 2, Cell / 2);

        if (element is SignalElement signalElement)
        {
            int signCount = SignalFootprintHelper.ParseSignCount(signalElement.SignalProfile);
            if (innerOutline is IMarkerSignalProfile outlineProf)
                outlineProf.SetProfile(signCount);
            if (inner is IMarkerSignalProfile innerProf)
                innerProf.SetProfile(signCount);
            if (innerOutline is IMarkerSignalProfileId outlineProfId)
                outlineProfId.SetProfileId(signalElement.SignalProfile);
            if (inner is IMarkerSignalProfileId innerProfId)
                innerProfId.SetProfileId(signalElement.SignalProfile);
            if (innerOutline is IMarkerSignalCompact outlineCompact)
                outlineCompact.SetCompactTwoAspect(true);
            if (inner is IMarkerSignalCompact innerCompact)
                innerCompact.SetCompactTwoAspect(true);
            if (innerOutline is IMarkerSignalAspect outlineAspect)
                outlineAspect.SetAspect(signalElement.Aspect);
            if (inner is IMarkerSignalAspect markerAspect)
                markerAspect.SetAspect(signalElement.Aspect);
        }

        if (element is RouteElement route)
        {
            // Route handling moved to BuildRouteHost / ApplyRouteHostMutableState
            // (cache pipeline). Tento branch by sa nemal vykonať vďaka guardu
            // na vrchu RenderElement; necháme ho ako no-op kvôli explicit clarity.
            _ = route;
        }

        bool isOnActivePath = DataContext is OperationViewModel vmPath
            && IsElementOnActiveRoutePath(vmPath, element.Id);
        
        var (hostWidth, hostHeight) = GetElementFootprint(element);
        // Host Grid s obidvomi vrstvami
        var host = new Grid
        {
            Width = hostWidth,
            Height = hostHeight,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            UseLayoutRounding = false,
            ClipToBounds = false,
            Background = Brushes.Transparent,
        };

        if (isOnActivePath)
        {
            host.Effect = new DropShadowEffect
            {
                Color = Color.Parse("#00D4AA"),
                BlurRadius = 10,
                OffsetX = 0,
                OffsetY = 0,
                Opacity = 1.0
            };
        }
        
        if (innerOutline != null)
            host.Children.Add(innerOutline);
        host.Children.Add(inner);
        
        // Pre výhybky: aplikovať stav a presunúť interakciu na host Grid
        if (element is TurnoutElement turnout)
        {
            // Aplikovať farby vetiev podľa stavu
            ApplyTurnoutState(inner, turnout);
            
            // Kliknutie prepína stav - event na celom Grid (celá bunka)
            host.PointerPressed += (s, e) =>
            {
                turnout.State = GetNextTurnoutState(turnout);
                RefreshLayout();
            };
            SetCursor(host, Avalonia.Input.StandardCursorType.Hand);
        }

        if (element is SignalElement signal)
        {
            host.PointerPressed += async (_, e) => await OnSignalPointerPressedAsync(e, signal);
            SetCursor(host, Avalonia.Input.StandardCursorType.Hand);
        }

        if (element is RouteElement routeElement && inner is MarkerRoute)
        {
            // Route event wiring & cursor policy je teraz v BuildRouteHost.
            // Sem sa nikdy nedostaneme vďaka guardu na vrchu RenderElement.
            _ = routeElement;
        }
        
        return host;
    }

    private static bool IsBlockRenderElement(LayoutElement element)
    {
        if (element is BlockElement)
            return true;

        return string.Equals(ResolveMarkerKey(element), "Block", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveMarkerKey(LayoutElement element)
    {
        if (!string.IsNullOrWhiteSpace(element.MarkerKey))
            return element.MarkerKey;

        // Fallback pre legacy/problémové dáta bez MarkerKey.
        return element.ElementType switch
        {
            LayoutElementType.Block => "Block",
            LayoutElementType.Text => "Text",
            LayoutElementType.Signal => "Signal",
            LayoutElementType.Sensor => "Sensor",
            LayoutElementType.Route => "Route",
            LayoutElementType.Bumper => "Bumper",
            LayoutElementType.TrackSegment => "TrackSegment",
            _ => string.Empty
        };
    }

    /// <summary>Vráti ďalší stav výhybky pri kliknutí podľa typu výhybky.</summary>
    private static TurnoutState GetNextTurnoutState(TurnoutElement turnout)
    {
        // Pre trojcestnú výhybku: Straight → DivergeLeft → DivergeRight → Straight
        if (turnout.MarkerKey == "Turnout_3W")
        {
            return turnout.State switch
            {
                TurnoutState.Straight => TurnoutState.DivergeLeft,
                TurnoutState.DivergeLeft => TurnoutState.DivergeRight,
                TurnoutState.DivergeRight => TurnoutState.Straight,
                _ => TurnoutState.Straight
            };
        }
        
        // Pre Doubleslip: Straight → Cross → DivergeRight → DivergeLeft → Straight
        if (turnout.MarkerKey == "DoubleSlip")
        {
            return turnout.State switch
            {
                TurnoutState.Straight => TurnoutState.Cross,
                TurnoutState.Cross => TurnoutState.DivergeRight,
                TurnoutState.DivergeRight => TurnoutState.DivergeLeft,
                TurnoutState.DivergeLeft => TurnoutState.Straight,
                _ => TurnoutState.Straight
            };
        }
        
        // Pre ostatné výhybky: Straight ↔ Diverge
        return turnout.State == TurnoutState.Straight 
            ? TurnoutState.Diverge 
            : TurnoutState.Straight;
    }
    
    private Control? CreateMarkerInstance(string markerKey)
    {
        return markerKey switch
        {
            "TrackSegment" => new TrackFlow.Views.Editor.Markers.MarkerTrackSegment(),
            "Curve_45" => new TrackFlow.Views.Editor.Markers.MarkerCurve45(),
            "Curve_90" => new TrackFlow.Views.Editor.Markers.MarkerCurve90(),
            "Bumper" => new TrackFlow.Views.Editor.Markers.MarkerBumper(),
            "Turnout_L" => new TrackFlow.Views.Editor.Markers.MarkerTurnoutL(),
            "Turnout_R" => new TrackFlow.Views.Editor.Markers.MarkerTurnoutR(),
            "TurnoutL90" => new TrackFlow.Views.Editor.Markers.MarkerTurnoutL90(),
            "TurnoutR90" => new TrackFlow.Views.Editor.Markers.MarkerTurnoutR90(),
            "TurnoutCurve_L" => new TrackFlow.Views.Editor.Markers.MarkerTurnoutCurveL(),
            "TurnoutCurve_R" => new TrackFlow.Views.Editor.Markers.MarkerTurnoutCurveR(),
            "Turnout_Y" => new TrackFlow.Views.Editor.Markers.MarkerTurnoutY(),
            "Turnout_3W" => new TrackFlow.Views.Editor.Markers.MarkerTurnout3W(),
            "Cross90" => new TrackFlow.Views.Editor.Markers.MarkerCross90(),
            "Cross45" => new TrackFlow.Views.Editor.Markers.MarkerCross45(),
            "DoubleSlip" => new TrackFlow.Views.Editor.Markers.MarkerDoubleSlip(),
            "Bridge90" => new TrackFlow.Views.Editor.Markers.MarkerBridge90(),
            "Bridge45L" => new TrackFlow.Views.Editor.Markers.MarkerBridge45L(),
            "Bridge45R" => new TrackFlow.Views.Editor.Markers.MarkerBridge45R(),
            "Signal" => new TrackFlow.Views.Editor.Markers.MarkerSignal(),
            "Signal5" => new TrackFlow.Views.Editor.Markers.MarkerSignal(),
            "Signal4" => new TrackFlow.Views.Editor.Markers.MarkerSignal(),
            "Signal2Main" => new TrackFlow.Views.Editor.Markers.MarkerSignal(),
            "Signal2Shunt" => new TrackFlow.Views.Editor.Markers.MarkerSignal(),
            "Signal2Route" => new TrackFlow.Views.Editor.Markers.MarkerSignal(),
            "Signal3Entry" => new TrackFlow.Views.Editor.Markers.MarkerSignal(),
            "Sensor" => new TrackFlow.Views.Editor.Markers.MarkerSensor(),
            "Route" => new TrackFlow.Views.Editor.Markers.MarkerRoute(),
            _ => null
        };
    }
    
    private Control CreateTextControl(TextElement textEl)
    {
        // Marker zaberá WidthInCells × HeightInCells buniek (každá bunka 24px).
        double cellW = textEl.WidthInCells  * Cell;
        double cellH = textEl.HeightInCells * Cell;

        // Vonkajší host (zaberá celú plochu buniek)
        var host = new Grid
        {
            Width  = cellW,
            Height = cellH,
            Background = Brushes.Transparent,
            UseLayoutRounding = false,
            ClipToBounds = false
        };

        // Voliteľný Border (rám + pozadie) - iba ak je niečo nastavené
        bool hasFrame = textEl.FrameThickness > 0;
        bool hasBg = !string.IsNullOrEmpty(textEl.BackgroundColor)
                     && textEl.BackgroundColor != "Transparent";

        if (hasFrame || hasBg)
        {
            var frameBorder = new Border
            {
                Width  = cellW,
                Height = cellH,
                CornerRadius = new CornerRadius(3),
                ClipToBounds = false,
                UseLayoutRounding = false,
                IsHitTestVisible = false,
                Background = hasBg ? Brush.Parse(textEl.BackgroundColor) : Brushes.Transparent,
                BorderThickness = hasFrame ? new Thickness(textEl.FrameThickness) : new Thickness(0),
                BorderBrush = hasFrame
                    ? (textEl.FrameColor != "Automatic" && !string.IsNullOrEmpty(textEl.FrameColor)
                        ? Brush.Parse(textEl.FrameColor)
                        : new SolidColorBrush(Color.Parse("#333")))
                    : Brushes.Transparent
            };
            host.Children.Add(frameBorder);
        }

        // TextBlock - zobrazenie textu
        var textBlock = new TextBlock
        {
            Text = textEl.Text,
            FontSize = textEl.FontSize,
            FontFamily = new FontFamily(textEl.FontName),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2),
            IsHitTestVisible = false
        };

        textBlock.TextAlignment = textEl.HorizontalAlignment switch
        {
            "Left"  => TextAlignment.Left,
            "Right" => TextAlignment.Right,
            _       => TextAlignment.Center
        };
        textBlock.VerticalAlignment = textEl.VerticalAlignment switch
        {
            "Top"    => Avalonia.Layout.VerticalAlignment.Top,
            "Bottom" => Avalonia.Layout.VerticalAlignment.Bottom,
            _        => Avalonia.Layout.VerticalAlignment.Center
        };
        textBlock.HorizontalAlignment = textEl.HorizontalAlignment switch
        {
            "Left"  => Avalonia.Layout.HorizontalAlignment.Left,
            "Right" => Avalonia.Layout.HorizontalAlignment.Right,
            _       => Avalonia.Layout.HorizontalAlignment.Center
        };

        textBlock.Foreground = (textEl.FillColor != "Automatic" && !string.IsNullOrEmpty(textEl.FillColor))
            ? Brush.Parse(textEl.FillColor)
            : new SolidColorBrush(Color.Parse("#333"));

        // Ak je VisibleInEditModeOnly = true, text sa vôbec nezobrazí v Operation mode
        if (textEl.VisibleInEditModeOnly)
            host.Opacity = 0;
        else
            host.Children.Add(textBlock);

        return host;
    }
    
    private static (double Width, double Height) GetElementFootprint(LayoutElement element)
        // V operation režime musí footprint 2-znakových návestidiel zodpovedať editoru (1 bunka).
        => LayoutElementFootprintHelper.GetFootprint(element, Cell, compactTwoAspectSignals: true);

    private static void ApplyOutlineStyle(Control marker)
    {
        if (marker is not UserControl uc) return;

        // Variant 1: Canvas s children (väčšina markerov)
        if (uc.Content is Canvas canvas)
        {
            foreach (var child in canvas.Children)
            {
                // Preskočíme prvky s Tag="NoOutline"
                if (child is Shape shape && shape.Tag?.ToString() == "NoOutline")
                    continue;
                
                // Preskočíme prvky, ktoré už sú outline (majú "Outline" v názve)
                // Tieto sú definované priamo v AXAML markerov výhybiek
                if (child is Control ctrl && ctrl.Name?.Contains("Outline") == true)
                    continue;

                if (child is Line line)
                {
                    line.StrokeThickness += 2;
                    line.Stroke = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0));
                }
                else if (child is Path path)
                {
                    path.StrokeThickness += 2;
                    path.Stroke = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0));
                }
            }
        }
        // Variant 2: Path priamo ako obsah (Curve45, Curve90)
        else if (uc.Content is Path pathDirect)
        {
            pathDirect.StrokeThickness += 2;
            pathDirect.Stroke = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0));
        }
    }
    
    private static void ApplyTurnoutState(Control marker, TurnoutElement turnout)
    {
        if (marker is not UserControl uc) return;
        if (uc.Content is not Canvas canvas) return;
        
        var activeColor = new SolidColorBrush(Color.Parse("#71c5ff"));   // Modrá - aktívna cesta
        var inactiveColor = new SolidColorBrush(Color.Parse("#FFFFFF")); // Biela - neaktívna cesta
        
        // Pre Doubleslip výhybku
        if (turnout.MarkerKey == "DoubleSlip")
        {
            Line? line1 = null;      // vertikálna/horizontálna čiara (Straight)
            Line? line2 = null;      // šikmá čiara (Cross)
            Path? arcR = null;       // pravý oblúk (DivergeRight)
            Path? arcL = null;       // ľavý oblúk (DivergeLeft)
            
            foreach (var child in canvas.Children)
            {
                if (child is Line line)
                {
                    if (line.Name == "Line1") line1 = line;
                    else if (line.Name == "Line2") line2 = line;
                }
                else if (child is Path path)
                {
                    if (path.Name == "ArcRPath") arcR = path;
                    else if (path.Name == "ArcLPath") arcL = path;
                }
            }
            
            if (line1 != null) 
            {
                line1.Stroke = turnout.State == TurnoutState.Straight ? activeColor : inactiveColor;
                line1.ZIndex = turnout.State == TurnoutState.Straight ? 10 : 1;
            }
            if (line2 != null) 
            {
                line2.Stroke = turnout.State == TurnoutState.Cross ? activeColor : inactiveColor;
                line2.ZIndex = turnout.State == TurnoutState.Cross ? 10 : 1;
            }
            if (arcR != null) 
            {
                arcR.Stroke = turnout.State == TurnoutState.DivergeRight ? activeColor : inactiveColor;
                arcR.ZIndex = turnout.State == TurnoutState.DivergeRight ? 10 : 1;
            }
            if (arcL != null) 
            {
                arcL.Stroke = turnout.State == TurnoutState.DivergeLeft ? activeColor : inactiveColor;
                arcL.ZIndex = turnout.State == TurnoutState.DivergeLeft ? 10 : 1;
            }
            return;
        }
        
        // Pre trojcestnú výhybku
        if (turnout.MarkerKey == "Turnout_3W")
        {
            Line? centerLine = null;  // priama čiara (Straight)
            Path? arcR = null;        // pravý oblúk (DivergeRight)
            Path? arcL = null;        // ľavý oblúk (DivergeLeft)
            
            foreach (var child in canvas.Children)
            {
                if (child is Line && ((Line)child).Name == "CenterLine")
                    centerLine = (Line)child;
                else if (child is Path path)
                {
                    if (path.Name == "ArcRPath") arcR = path;
                    else if (path.Name == "ArcLPath") arcL = path;
                }
            }
            
            if (centerLine != null)
            {
                centerLine.Stroke = turnout.State == TurnoutState.Straight ? activeColor : inactiveColor;
                centerLine.ZIndex = turnout.State == TurnoutState.Straight ? 10 : 1;
            }
            if (arcR != null)
            {
                arcR.Stroke = turnout.State == TurnoutState.DivergeRight ? activeColor : inactiveColor;
                arcR.ZIndex = turnout.State == TurnoutState.DivergeRight ? 10 : 1;
            }
            if (arcL != null)
            {
                arcL.Stroke = turnout.State == TurnoutState.DivergeLeft ? activeColor : inactiveColor;
                arcL.ZIndex = turnout.State == TurnoutState.DivergeLeft ? 10 : 1;
            }
            return;
        }
        
        // Pre ostatné výhybky (2-cestné) - pôvodná logika
        Line? straightLine = null;
        Line? straightLineOutline = null;
        Path? curvePath = null;
        Path? curvePathOutline = null;
        
        foreach (var child in canvas.Children)
        {
            if (child is Line line)
            {
                if (line.Name == "StraightLine")
                    straightLine = line;
                else if (line.Name == "StraightLineOutline")
                    straightLineOutline = line;
            }
            else if (child is Path path)
            {
                if (path.Name == "CurvePath")
                    curvePath = path;
                else if (path.Name == "CurvePathOutline")
                    curvePathOutline = path;
            }
        }
        
        if (straightLine == null || curvePath == null) return;
        
        if (turnout.State == TurnoutState.Straight)
        {
            // Rovná koľaj je aktívna (modrá) - musí byť na vrchu
            straightLine.Stroke = activeColor;
            straightLine.ZIndex = 10;
            if (straightLineOutline != null)
                straightLineOutline.ZIndex = 9;
            
            curvePath.Stroke = inactiveColor;
            curvePath.ZIndex = 1;
            if (curvePathOutline != null)
                curvePathOutline.ZIndex = 0;
        }
        else
        {
            // Oblúk je aktívny (modrý) - musí byť na vrchu
            straightLine.Stroke = inactiveColor;
            straightLine.ZIndex = 1;
            if (straightLineOutline != null)
                straightLineOutline.ZIndex = 0;
            
            curvePath.Stroke = activeColor;
            curvePath.ZIndex = 10;
            if (curvePathOutline != null)
                curvePathOutline.ZIndex = 9;
        }
    }
    
    private Control CreateBlockControl(LayoutElement el)
    {
        int blockLengthCells = LayoutElementFootprintHelper.GetBlockLength(el);
        bool isVertical = LayoutElementFootprintHelper.IsVertical(el.Rotation);
        var (W, H) = LayoutElementFootprintHelper.GetFootprint(el, Cell, compactTwoAspectSignals: false);

        var canvas = new Canvas
        {
            Width = W,
            Height = H,
            ClipToBounds = true,
            UseLayoutRounding = false,
        };

        var fillBrush = el is BlockElement blockState && blockState.IsOccupied && !blockState.IsTailClearing
            ? new SolidColorBrush(Color.Parse("#FFD6D6"))      // Červené - obsadený
            : el is BlockElement shadowState && shadowState.IsShadowSet
                ? new SolidColorBrush(Color.Parse("#CFE8FF"))  // Svetlo modré - rezervovaný (shadow)
                : el is BlockElement lockState && lockState.IsLocked
                    ? new SolidColorBrush(Color.Parse("#CFE8FF"))  // Svetlo modré - uzamknutý (zjednotené s rezerváciou)
                    : new SolidColorBrush(Color.Parse("#FFFFDC"));  // Žlté - voľný (aj IsTailClearing)

        // Pozadie bloku
        var rect = new Rectangle
        {
            Width = W,
            Height = H,
            Fill = fillBrush,
            Stroke = new SolidColorBrush(Color.Parse("#003366")),
            StrokeThickness = 1,
            RadiusX = 2,
            RadiusY = 2,
            UseLayoutRounding = false,
        };
        Canvas.SetLeft(rect, 0);
        Canvas.SetTop(rect, 0);
        canvas.Children.Add(rect);

        // REŽIM PREVÁDZKA: Nezobraziť názov bloku, len priradené lokomotívy s vagónmi.
        // Vykresľovanie vlaku je delegované do zdieľaného helpera BlockTrainRenderer
        // (jednotná logika s Layout editorom, žiadne duplicitné transform-hacky).
        if (DataContext is OperationViewModel vm && el is BlockElement blockElAssign)
        {
            // Ak je blok v stave "tail clearing", nezobraziť ikonu - vizuálne prázdny
            if (blockElAssign.IsTailClearing)
            {
                // Blok vyzerá prázdny, aj keď logicky je ešte obsadený pre návestidlá
                return canvas;
            }

            var renderLocoId = ResolveRenderableBlockLocoId(blockElAssign);

            if (!string.IsNullOrWhiteSpace(renderLocoId))
            {
                var loco = vm.Locomotives.FirstOrDefault(l => l.Code == renderLocoId);
                if (loco != null)
                {
                    var isReservedShadow = IsReservedShadowVisual(blockElAssign);
                    var isTransitionShadow = blockElAssign.IsOccupied
                                            && !string.IsNullOrWhiteSpace(blockElAssign.AssignedLocoId)
                                            && !string.Equals(loco.AssignedBlockId, blockElAssign.Id, StringComparison.OrdinalIgnoreCase);
                    var isShadowVisual = isReservedShadow || isTransitionShadow;

                    var orientation = TrackFlow.Views.Shared.TrainOrientationExtensions.From(
                        isVertical,
                        isReservedShadow ? blockElAssign.ReservedLocoIsForward : blockElAssign.AssignedLocoIsForward);
                    var train = TrackFlow.Views.Shared.BlockTrainRenderer.CreateTrainVisual(
                        loco,
                        orientation,
                        W,
                        H,
                        showName: !isShadowVisual,
                        visualOpacity: isShadowVisual ? 0.35 : 1.0);
                    Canvas.SetLeft(train, 0);
                    Canvas.SetTop(train, 0);
                    canvas.Children.Add(train);
                }
            }
        }

        return canvas;
    }

    private static string? ResolveRenderableBlockLocoId(BlockElement block)
    {
        // Lokomotívu vykreslíme vždy keď je k bloku priradená (AssignedLocoId).
        // Predtým sme vyžadovali aj IsOccupied=true, ale to spôsobovalo dve chyby:
        //   1) Po priradení v editore (IsOccupied=true) sa po prepnutí do prevádzky
        //      a následnom safety/simulation resete IsOccupied zhodilo a loko zmizla,
        //      hoci AssignedLocoId zostalo platné.
        //   2) Drag&drop priradenie v prevádzkovom režime zámerne nenastavuje
        //      IsOccupied (potvrdiť ho má až senzor / centrála), takže loko vôbec
        //      nebola vidieť. Vykresľujeme rovnako ako editor – podľa AssignedLocoId.
        // Stav "transition shadow" (loko sa už presunula inam) je naďalej riešený
        // nižšie cez kontrolu loco.AssignedBlockId vs. block.Id.
        if (!string.IsNullOrWhiteSpace(block.AssignedLocoId))
            return block.AssignedLocoId;

        return IsReservedShadowVisual(block)
            ? block.ReservedLocoId
            : null;
    }

    private static bool IsReservedShadowVisual(BlockElement block)
        => !block.IsOccupied
           && block.IsShadowSet
           && !string.IsNullOrWhiteSpace(block.ReservedLocoId);

    private static bool IsElementOnActiveRoutePath(OperationViewModel vm, string elementId)
    {
        return vm.IsElementOnActiveRoutePath(elementId);
    }
    
    /// <summary>Načíta ikonu vozidla z Assets/LocoIcons alebo Assets/WagonIcons.</summary>
    private static Image? LoadIconImage(string iconName)
    {
        var bitmap = VehicleIconLoader.TryLoadBitmap(iconName);
        return bitmap == null ? null : new Image { Source = bitmap };
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // Drag&Drop lokomotív na bloky (rovnaký ako v editore)
    // ═══════════════════════════════════════════════════════════════════════════
    
    private const string LocoFormat = "trackflow/locomotive";
    
    private void OnCanvasLocoDragOver(object? sender, DragEventArgs e)
    {
        if (!DragDropCompat.Contains(e, LocoFormat))
        {
            e.DragEffects = DragDropEffects.None;
            HideDragArrow();
            return;
        }

        var canvas = _operationCanvas;
        if (canvas == null)
            return;
        
        var pos = e.GetPosition(canvas);
        var block = FindBlockElementAt(pos.X, pos.Y);
        
        if (block == null)
        {
            e.DragEffects = DragDropEffects.None;
            HideDragArrow();
            return;
        }
        
        bool isForward = ComputeDropDirection(block, pos.X, pos.Y);
        
        // Zobraz floating šípku nad kurzorom
        ShowDragArrow(pos.X, pos.Y, block, isForward);
        
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }
    
    private void OnCanvasLocoDragLeave(object? sender, DragEventArgs e)
    {
        HideDragArrow();
    }
    
    private async void OnCanvasLocoDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (DataContext is not OperationViewModel vm) return;
            if (!DragDropCompat.Contains(e, LocoFormat)) return;

            var loco = DragDropCompat.Get(e, LocoFormat) as Locomotive;
            if (loco == null) return;

            var canvas = _operationCanvas;
            if (canvas == null)
                return;

            var pos = e.GetPosition(canvas);
            var block = FindBlockElementAt(pos.X, pos.Y);

            // Skryj šípku
            HideDragArrow();

            if (block == null) return;

            bool isForward = ComputeDropDirection(block, pos.X, pos.Y);
            var dccClient = (this.GetVisualRoot() as Window)?.DataContext is ViewModels.MainWindowViewModel mainVmDrop
                ? mainVmDrop.Dcc?.Client
                : null;
            var assign = await vm.AssignLocomotiveToBlockAsync(loco.Code, block.Id, isForward, dccClient);
            if (!assign.IsSafe)
            {
                Log.Warning("Locomotive assign blocked in operation view. Loco={LocoCode}, Block={BlockId}, Reason={Reason}, BlockingBlock={BlockingBlockId}",
                    loco.Code, block.Id, assign.Reason, assign.BlockingBlockId);
                e.Handled = true;
                return;
            }

            // Drop sa môže udiať iba v Operation režime → automaticky aktivovať lokomotívu,
            // aby sa okamžite zobrazil Dashboard a v smart páse mala plnú opacity.
            var mainWindow = this.GetVisualRoot() as Window;
            if (mainWindow?.DataContext is ViewModels.MainWindowViewModel mainVm && mainVm.SmartStrips != null)
            {
                loco.IsActive = true;
                if (!mainVm.SmartStrips.ActiveLocomotives.Contains(loco))
                    mainVm.SmartStrips.ActiveLocomotives.Add(loco);
            }

            e.Handled = true;
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("OperationView.OnCanvasLocoDrop", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Prevádzka",
                $"⚠️ Drag&drop lokomotívy zlyhal: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);
            e.Handled = true;
        }
    }
    
    /// <summary>Zobrazí floating šípku nad kurzorom pri drag&drop.</summary>
    private void ShowDragArrow(double cursorX, double cursorY, BlockElement block, bool isForward)
    {
        if (_dragArrowRight == null || _dragArrowLeft == null || 
            _dragArrowDown == null || _dragArrowUp == null) return;

        // Skryj všetky šípky
        _dragArrowRight.IsVisible = false;
        _dragArrowLeft.IsVisible = false;
        _dragArrowDown.IsVisible = false;
        _dragArrowUp.IsVisible = false;
        
        // Zisti, či je blok vertikálny
        bool isVertical = LayoutElementFootprintHelper.IsVertical(block.Rotation);
        
        if (isVertical)
        {
            // Vertikálny blok: použij up/down šípky
            if (isForward)
            {
                // Forward = dole (bottom)
                Canvas.SetLeft(_dragArrowDown, cursorX);
                Canvas.SetTop(_dragArrowDown, cursorY);
                _dragArrowDown.IsVisible = true;
            }
            else
            {
                // Backward = hore (top)
                Canvas.SetLeft(_dragArrowUp, cursorX);
                Canvas.SetTop(_dragArrowUp, cursorY);
                _dragArrowUp.IsVisible = true;
            }
            
            // Pre vertikálne bloky neskrývame kurzor
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window != null)
                SetWindowCursor(window, Cursor.Default);
        }
        else
        {
            // Horizontálny blok: použij left/right šípky
            if (isForward)
            {
                // Forward = vpravo (right)
                Canvas.SetLeft(_dragArrowRight, cursorX);
                Canvas.SetTop(_dragArrowRight, cursorY);
                _dragArrowRight.IsVisible = true;
            }
            else
            {
                // Backward = vľavo (left)
                Canvas.SetLeft(_dragArrowLeft, cursorX);
                Canvas.SetTop(_dragArrowLeft, cursorY);
                _dragArrowLeft.IsVisible = true;
            }
            
            // Pre horizontálne bloky (left/right šípky) skryť kurzor na úrovni Window
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window != null)
                SetWindowCursor(window, new Cursor(StandardCursorType.None));
        }
    }
    
    /// <summary>Skryje floating šípku.</summary>
    private void HideDragArrow()
    {
        if (_dragArrowRight != null) _dragArrowRight.IsVisible = false;
        if (_dragArrowLeft != null) _dragArrowLeft.IsVisible = false;
        if (_dragArrowDown != null) _dragArrowDown.IsVisible = false;
        if (_dragArrowUp != null) _dragArrowUp.IsVisible = false;
        
        // Obnov pôvodný kurzor na úrovni Window
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window != null)
        {
            // Cursor priority: ak je route hover stále aktívny, NEPREPISUJ HAND
            // späť na default Arrow – drag-arrow layer nesmie prebíjať route hover.
            if (_routeHoverWindowCursorPinned && _hoveredRouteElement != null)
            {
                SetWindowCursor(window, new Cursor(StandardCursorType.Hand));
                _pinnedWindow = window;
            }
            else
            {
                SetWindowCursor(window, Cursor.Default);
            }
        }
    }
    
    private BlockElement? FindBlockElementAt(double x, double y)
    {
        if (DataContext is not OperationViewModel vm) return null;
        
        foreach (var el in vm.LayoutElements.Reverse())
        {
            if (el is not BlockElement block) continue;
            var (w, h) = LayoutElementFootprintHelper.GetFootprint(block, Cell, compactTwoAspectSignals: false);
            
            if (x >= block.X && x < block.X + w &&
                y >= block.Y && y < block.Y + h)
                return block;
        }
        
        return null;
    }
    
    private static bool ComputeDropDirection(BlockElement block, double x, double y)
    {
        bool isVertical = LayoutElementFootprintHelper.IsVertical(block.Rotation);
        var (blockWidth, blockHeight) = LayoutElementFootprintHelper.GetFootprint(block, Cell, compactTwoAspectSignals: false);
        
        if (!isVertical)
        {
            // Horizontálny blok: porovnaj X
            double relX = x - block.X;
            return relX > blockWidth / 2; // Pravá polovica = forward
        }
        else
        {
            // Vertikálny blok: porovnaj Y
            double relY = y - block.Y;
            return relY > blockHeight / 2; // Dolná polovica = forward
        }
    }

    private ContextMenu? _openContextMenu;

    private System.Threading.Tasks.Task OnBlockPointerPressedAsync(PointerPressedEventArgs e, BlockElement targetBlock)
    {
        if (DataContext is not OperationViewModel vm)
            return System.Threading.Tasks.Task.CompletedTask;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            SelectLocomotiveFromBlock(vm, targetBlock);
            e.Handled = true;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return System.Threading.Tasks.Task.CompletedTask;

        _openContextMenu?.Close();
        var menu = BuildBlockContextMenu(vm, targetBlock);
        _openContextMenu = menu;
        menu.Closed += (_, _) => _openContextMenu = null;
        menu.Open(this);
        e.Handled = true;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private void SelectLocomotiveFromBlock(OperationViewModel vm, BlockElement block)
    {
        if (string.IsNullOrWhiteSpace(block.AssignedLocoId))
            return;

        var loco = vm.Locomotives.FirstOrDefault(l => string.Equals(l.Code, block.AssignedLocoId, StringComparison.OrdinalIgnoreCase));
        if (loco == null)
            return;

        vm.SelectedLoco = loco;

        if ((this.GetVisualRoot() as Window)?.DataContext is ViewModels.MainWindowViewModel mainVm)
            mainVm.SmartStrips.SelectedLocomotive = loco;
    }

    private ContextMenu BuildBlockContextMenu(OperationViewModel vm, BlockElement targetBlock)
    {
        var selectedLoco = vm.SelectedLoco;
        var canMove = selectedLoco != null
            && !string.IsNullOrWhiteSpace(selectedLoco.Code)
            && vm.LayoutElements.OfType<BlockElement>().Any(b => string.Equals(b.AssignedLocoId, selectedLoco.Code, StringComparison.OrdinalIgnoreCase))
            && !string.Equals(selectedLoco.AssignedBlockId, targetBlock.Id, StringComparison.OrdinalIgnoreCase);

        var moveItem = new MenuItem
        {
            Header = "Presunúť sem",
            IsEnabled = canMove
        };
        moveItem.Click += async (_, _) => await ExecuteMoveToBlockAsync(vm, targetBlock);

        var hasLoco = !string.IsNullOrWhiteSpace(targetBlock.AssignedLocoId);
        var removeLocoItem = new MenuItem
        {
            Header = "Odstraniť lokomotívu",
            IsEnabled = hasLoco
        };
        removeLocoItem.Click += (_, _) =>
        {
            vm.RemoveLocomotiveFromBlock(targetBlock.Id);
        };

        return new ContextMenu
        {
            ItemsSource = new object[] { moveItem, new Separator(), removeLocoItem }
        };
    }

    private async System.Threading.Tasks.Task ExecuteMoveToBlockAsync(OperationViewModel vm, BlockElement targetBlock)
    {
        var selectedLoco = vm.SelectedLoco;
        if (selectedLoco == null || string.IsNullOrWhiteSpace(selectedLoco.Code))
        {
            Log.Information("Block move skipped: no selected locomotive.");
            return;
        }

        var sourceBlock = vm.LayoutElements
            .OfType<BlockElement>()
            .FirstOrDefault(b => string.Equals(b.AssignedLocoId, selectedLoco.Code, StringComparison.OrdinalIgnoreCase));
        if (sourceBlock == null)
        {
            Log.Information("Block move skipped: selected locomotive {LocoCode} is not assigned to any block.", selectedLoco.Code);
            return;
        }

        if (string.Equals(sourceBlock.Id, targetBlock.Id, StringComparison.OrdinalIgnoreCase))
            return;

        var dccClient = (this.GetVisualRoot() as Window)?.DataContext is ViewModels.MainWindowViewModel mainVm
            ? mainVm.Dcc?.Client
            : null;

        var move = await vm.MoveLocomotiveBetweenBlocksAsync(selectedLoco.Code, sourceBlock.Id, targetBlock.Id, dccClient);
        if (!move.IsSuccess)
        {
            Log.Warning("Block move failed. Loco={LocoCode}, Source={SourceBlock}, Target={TargetBlock}, Reason={Reason}",
                selectedLoco.Code, sourceBlock.Id, targetBlock.Id, move.Reason);
        }
    }

    private async System.Threading.Tasks.Task OnRoutePointerPressedAsync(PointerPressedEventArgs e, RouteElement routeElement)
    {
        if (DataContext is not OperationViewModel vm)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var dccClient = (this.GetVisualRoot() as Window)?.DataContext is ViewModels.MainWindowViewModel mainVm
            ? mainVm.Dcc?.Client
            : null;

        var result = await vm.MoveLocomotiveByRouteElementAsync(routeElement, dccClient);
        if (!result.IsSuccess)
        {
            Log.Warning("Route button action failed. RouteMarker={RouteMarkerId}, Reason={Reason}", routeElement.Id, result.Reason);
        }

        e.Handled = true;
    }

    private async System.Threading.Tasks.Task OnSignalPointerPressedAsync(PointerPressedEventArgs e, SignalElement signal)
    {
        if (DataContext is not OperationViewModel vm)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var dccClient = (this.GetVisualRoot() as Window)?.DataContext is ViewModels.MainWindowViewModel mainVm
            ? mainVm.Dcc?.Client
            : null;

        await vm.HandleSignalClickAsync(signal, dccClient);
        e.Handled = true;
    }

    private static void SetCursor(Control owner, StandardCursorType cursorType)
    {
        owner.Cursor = new Cursor(cursorType);
    }

    private static void SetWindowCursor(Window window, Cursor cursor)
    {
        window.Cursor = cursor;
    }

    /// <summary>
    /// Pinne window-level HAND cursor pre aktívny route hover. Toto má vyššiu
    /// prioritu než passive canvas hover, runtime overlay refresh, drag fallback
    /// a selection refresh. Drží HAND aj počas RefreshLayout, kedy Avalonia
    /// stratí pointer-tracked cursor na rekonštruovanom hostovi.
    /// </summary>
    private void PinRouteHoverWindowCursor(RouteElement routeElement, string reason)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return;

        if (_routeHoverWindowCursorPinned && ReferenceEquals(_pinnedWindow, window))
        {
            // Už pinnuté – netreba znovu nastavovať (zníži šum v logoch).
            return;
        }

        SetWindowCursor(window, new Cursor(StandardCursorType.Hand));
        _routeHoverWindowCursorPinned = true;
        _pinnedWindow = window;
    }

    private void ReleaseRouteHoverWindowCursor(string reason)
    {
        if (!_routeHoverWindowCursorPinned)
            return;

        var window = _pinnedWindow ?? TopLevel.GetTopLevel(this) as Window;
        _routeHoverWindowCursorPinned = false;
        _pinnedWindow = null;

        if (window == null) return;
        SetWindowCursor(window, Cursor.Default);
    }
    
}

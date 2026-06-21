using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow;
using TrackFlow.Helpers;
using TrackFlow.ViewModels.Editor;
using TrackFlow.Views.Editor.Markers;

namespace TrackFlow.Views.Editor;

public partial class LayoutEditorView : UserControl
{
    private const double Cell  = LayoutEditorViewModel.CellSize;
    private const double Ruler = LayoutEditorViewModel.RulerSize;

    /// <summary>Cache pre načítané ikony vozidiel – prevencia opakovaného načítavania.</summary>
    private static readonly Dictionary<string, Bitmap> _iconCache = new();

    private LayoutEditorViewModel? _vm;

    /// <summary>Aktuálne otvorené kontextové menu – ochrana pred duplikátmi.</summary>
    private ContextMenu? _openContextMenu;

    /// <summary>Marquee selection behavior pre multi-select.</summary>
    private MarqueeSelectionBehavior? _marqueeBehavior;

    // 🟢 Drag-kreslenie rovnej koľaje ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private bool _isDraggingTrack = false;
    private int _dragStartCellX = -1;
    private int _dragStartCellY = -1;
    
    // 🟢 Drag-posúvanie multi-select výberu ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private bool _isDraggingSelection = false;
    private Point _dragSelectionStart;
    private Dictionary<LayoutElement, (double X, double Y)> _originalPositions = new();
    
    // 🟢 Resize bloku ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private bool _isResizingBlock = false;
    private BlockElement? _resizingBlock;
    private string _resizeDirection = ""; // "left", "right", "top", "bottom"
    private int _originalBlockLength = 4;
    private double _originalBlockX = 0;
    private double _originalBlockY = 0;

    // 🟢 Resize textu (oddelené od Bloku) ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private bool _isResizingText = false;
    private TextElement? _resizingText;
    private string _textResizeDir = ""; // "left", "right", "top", "bottom"
    private int _originalTextWCells = 1;
    private int _originalTextHCells = 1;
    private double _originalTextX = 0;
    private double _originalTextY = 0;

    // 🟢 Handlery pre automatickú detekciu ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private TrackCrossingHandler? _crossingHandler;
    private TrackCrossing45Handler? _crossing45Handler;
    private TrackTurnoutAutoHandler? _turnoutAutoHandler;
    private TrackTurnout90Handler? _turnout90Handler;
    private TrackCurve45Handler? _curve45Handler;
    private TrackCurve90Handler? _curve90Handler;
    private bool _isBatchOperation = false;
    private bool _rebuildPending = false;

    // Mapa blok.Id → aktuálny Control vrátený CreateBlockControl(...), aby sme pri R-BUS
    // zmene obsadenosti mohli prekresliť LEN ten jeden blok namiesto celého layoutu.
    private readonly Dictionary<string, Control> _blockControlsById = new(StringComparer.OrdinalIgnoreCase);

    public LayoutEditorView()
    {
        AvaloniaXamlLoader.Load(this);
        AttachEventHandlers();
    }

    private void AttachEventHandlers()
    {
        this.AttachedToVisualTree += (_, _) =>
        {
            DrawGrid();
            DrawRulers();
            // Ensure view can receive key events
            this.Focusable = true;
            this.AddHandler(KeyDownEvent, OnKeyDown);
        };

        this.DetachedFromVisualTree += (_, _) =>
        {
            // Vyčisti icon cache pri zatvorení editora
            ClearIconCache();
        };

        this.DataContextChanged += OnDataContextChanged;

        var canvas = this.FindControl<Canvas>("LayoutCanvas")!;
        canvas.PointerMoved   += OnCanvasPointerMoved;
        canvas.PointerPressed += OnCanvasPointerPressed;
        canvas.PointerReleased += OnCanvasPointerReleased;
        canvas.PointerExited  += OnCanvasPointerExited;

        // 🟢 Drag-and-Drop lokomotívy na blok ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        DragDrop.SetAllowDrop(canvas, true);
        canvas.AddHandler(DragDrop.DragOverEvent, OnCanvasLocoDragOver);
        canvas.AddHandler(DragDrop.DragLeaveEvent, OnCanvasLocoDragLeave);
        canvas.AddHandler(DragDrop.DropEvent, OnCanvasLocoDrop);

        var marqueeLayer = this.FindControl<Canvas>("MarqueeLayer")!;
        _marqueeBehavior = new MarqueeSelectionBehavior(this, canvas, marqueeLayer);

        var scroller = this.FindControl<ScrollViewer>("CanvasScroller")!;
        scroller.ScrollChanged += (_, _) => DrawRulers();
    }

    // 🟢 DataContext / Elements sync ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.Elements.CollectionChanged -= OnElementsChanged;
            _vm.PropertyChanged            -= OnVmPropertyChanged;
            _vm.VisualRefreshRequested     -= OnVisualRefreshRequested;
            _vm.BlockPropertiesRequested   -= OnBlockPropertiesRequested;
            _vm.TurnoutPropertiesRequested -= OnTurnoutPropertiesRequested;
            _vm.SignalPropertiesRequested  -= OnSignalPropertiesRequested;
            _vm.SensorPropertiesRequested  -= OnSensorPropertiesRequested;
            _vm.RoutePropertiesRequested   -= OnRoutePropertiesRequested;
            _vm.TextPropertiesRequested    -= OnTextPropertiesRequested;
            _vm.BatchOperationStarting     -= OnBatchOperationStarting;
            _vm.BatchOperationCompleted    -= OnBatchOperationCompleted;
            if (_vm.SmartStripsLocomotives != null)
            {
                _vm.SmartStripsLocomotives.CollectionChanged -= OnSmartStripsLocomotivesChanged;
                foreach (var loco in _vm.SmartStripsLocomotives)
                {
                    loco.PropertyChanged        -= OnLocoPropertyChanged;
                    loco.AttachedWagons.CollectionChanged -= OnLocoWagonsChanged;
                }
            }
            _vm = null;
        }

        if (DataContext is LayoutEditorViewModel vm)
        {
            _vm = vm;
            _crossingHandler = new TrackCrossingHandler(_vm);
            _crossing45Handler = new TrackCrossing45Handler(_vm);
            _turnoutAutoHandler = new TrackTurnoutAutoHandler(_vm, _crossing45Handler);
            _turnout90Handler = new TrackTurnout90Handler(_vm);
            _curve45Handler = new TrackCurve45Handler();
            _curve90Handler = new TrackCurve90Handler();
            _vm.Elements.CollectionChanged += OnElementsChanged;
            _vm.PropertyChanged            += OnVmPropertyChanged;
            _vm.VisualRefreshRequested     += OnVisualRefreshRequested;
            _vm.BlockPropertiesRequested   += OnBlockPropertiesRequested;
            _vm.TurnoutPropertiesRequested += OnTurnoutPropertiesRequested;
            _vm.SignalPropertiesRequested  += OnSignalPropertiesRequested;
            _vm.SensorPropertiesRequested  += OnSensorPropertiesRequested;
            _vm.RoutePropertiesRequested   += OnRoutePropertiesRequested;
            _vm.TextPropertiesRequested    += OnTextPropertiesRequested;
            _vm.BatchOperationStarting     += OnBatchOperationStarting;
            _vm.BatchOperationCompleted    += OnBatchOperationCompleted;

            // Keď ViewModel požiada o prekreslenie jedného bloku, prekreslíme LEN ten blok.
            // Full rebuild robíme len ako fallback, ak blok ešte nemáme v mape (napr. nový element).
            _vm.RequestBlockRepaint = block =>
            {
                if (block == null) { ScheduleRebuild(); return; }
                if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(
                        () => RepaintSingleBlock(block),
                        Avalonia.Threading.DispatcherPriority.Render);
                    return;
                }
                RepaintSingleBlock(block);
            };

            // Subscribovať na zmeny vlastností lokomotív (IsFlipped, AttachedWagons) pre okamžitý refresh bloku
            if (_vm.SmartStripsLocomotives != null)
            {
                foreach (var loco in _vm.SmartStripsLocomotives)
                {
                    loco.PropertyChanged        += OnLocoPropertyChanged;
                    loco.AttachedWagons.CollectionChanged += OnLocoWagonsChanged;
                }
                _vm.SmartStripsLocomotives.CollectionChanged += OnSmartStripsLocomotivesChanged;
            }

            // CheckAndCreateCrossings(); // VYPNUTÉ - Cross90 sa vytvára len pri auto-insert track line
            RebuildElementsLayer();
        }
    }

    private void OnBatchOperationStarting()
    {
        _isBatchOperation = true;
    }
    
    private void OnBatchOperationCompleted()
    {
        _isBatchOperation = false;
        RebuildElementsLayer(); // Prekreslíme len raz po skončení batch operácie
    }
    
    private void OnElementsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Pri batch operáciách (napr. mazanie viacerých prvkov) sa prekreslí len raz
        if (_isBatchOperation) return;
        
        ScheduleRebuild();
    }

    private void OnLocoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Ak sa zmenila orientácia lokomotívy, prekreslíme layout
        if (e.PropertyName is nameof(Locomotive.IsFlipped) or nameof(Locomotive.IsPlacedOnTrack))
            ScheduleRebuild();
    }

    private void OnLocoWagonsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        ScheduleRebuild();
    }

    private void OnVisualRefreshRequested()
    {
        ScheduleRebuild();
    }

    private void OnSmartStripsLocomotivesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (Locomotive l in e.NewItems)
            {
                l.PropertyChanged        += OnLocoPropertyChanged;
                l.AttachedWagons.CollectionChanged += OnLocoWagonsChanged;
            }
        if (e.OldItems != null)
            foreach (Locomotive l in e.OldItems)
            {
                l.PropertyChanged        -= OnLocoPropertyChanged;
                l.AttachedWagons.CollectionChanged -= OnLocoWagonsChanged;
            }
        ScheduleRebuild();
    }

    // Prekreslenie pri zmene SelectedElement (výber/rotácia)
    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LayoutEditorViewModel.SelectedElement)
                           or nameof(LayoutEditorViewModel.InspectorAngle)
                           or nameof(LayoutEditorViewModel.InspectorSignalHighlightVersion))
            ScheduleRebuild();
    }
    
    /// <summary>Naplánuje prekreslenie - debounce pre viacnásobné volania.</summary>
    private void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _rebuildPending = false;
            RebuildElementsLayer();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>Detekuje a vytvára kríženia koľají - NEPOUŽÍVA SA, len pri auto-insert.</summary>
    private void CheckAndCreateCrossings()
    {
        // VYPNUTÉ - Cross90 sa vytvára len pri auto-insert track line
        // _crossingHandler?.CheckAndCreateCrossings();
    }

    // 🟢 Vykreslenie všetkých prvkov ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void RebuildElementsLayer()
    {
        var layer = this.FindControl<Canvas>("ElementsLayer");
        if (layer == null || _vm == null) return;

        layer.UseLayoutRounding = false;
        this.UseLayoutRounding = false;
        layer.Children.Clear();
        _blockControlsById.Clear();

        // 1. prechod: všetky prvky OKREM blokov (koľaje, výhybky, signály...)
        foreach (var el in _vm.Elements)
        {
            if (el.MarkerKey == "Block") continue;
            bool isSelected = _vm.Selection.IsSelected(el)
                              || _vm.SelectedElement == el
                              || _vm.IsInspectorDirectionalSignalHighlighted(el);
            var ctrl = CreateMarkerControl(el, isSelected);
            if (ctrl == null) continue;
            Canvas.SetLeft(ctrl, el.X);
            Canvas.SetTop(ctrl,  el.Y);
            layer.Children.Add(ctrl);
        }

        // 2. prechod: bloky VŽDY navrchu – prekrývajú koľaje pod nimi
        foreach (var el in _vm.Elements)
        {
            if (el.MarkerKey != "Block") continue;
            bool isSelected = _vm.Selection.IsSelected(el) || _vm.SelectedElement == el;
            var ctrl = CreateMarkerControl(el, isSelected);
            if (ctrl == null) continue;
            Canvas.SetLeft(ctrl, el.X);
            Canvas.SetTop(ctrl,  el.Y);
            layer.Children.Add(ctrl);
            if (el is BlockElement blockEl && !string.IsNullOrEmpty(blockEl.Id))
                _blockControlsById[blockEl.Id] = ctrl;
        }

        if (_vm.Selection.SelectionCount > 0)
            DrawSelectionRectangle(layer);
    }

    /// <summary>
    /// Rýchle prekreslenie JEDNÉHO bloku po R-BUS zmene obsadenia.
    /// Nahradí existujúci Control bloku na rovnakej pozícii bez rebuildu celého layoutu.
    /// </summary>
    private void RepaintSingleBlock(BlockElement block)
    {
        if (_vm == null || block == null || string.IsNullOrEmpty(block.Id))
            return;

        var layer = this.FindControl<Canvas>("ElementsLayer");
        if (layer == null)
        {
            ScheduleRebuild();
            return;
        }

        if (!_blockControlsById.TryGetValue(block.Id, out var existing))
        {
            // Blok zatiaľ nemáme v mape (napr. čerstvo pridaný cez editor) – fallback full rebuild.
            ScheduleRebuild();
            return;
        }

        bool isSelected = _vm.Selection.IsSelected(block) || _vm.SelectedElement == block;
        var fresh = CreateMarkerControl(block, isSelected);
        if (fresh == null)
        {
            ScheduleRebuild();
            return;
        }

        var index = layer.Children.IndexOf(existing);
        if (index < 0)
        {
            ScheduleRebuild();
            return;
        }

        Canvas.SetLeft(fresh, block.X);
        Canvas.SetTop(fresh, block.Y);
        layer.Children[index] = fresh;
        _blockControlsById[block.Id] = fresh;
    }
    
    /// <summary>Vykreslí jeden veľký modrý obdĺžnik okolo všetkých vybratých prvkov.</summary>
    private void DrawSelectionRectangle(Canvas layer)
    {
        if (_vm == null || _vm.Selection.SelectionCount == 0) return;
        
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        
        // Nájdeme bounding box všetkých vybratých prvkov
        foreach (var el in _vm.Selection.SelectedElements)
        {
            var (elemWidth, elemHeight) = GetElementFootprint(el);
            
            minX = Math.Min(minX, el.X);
            minY = Math.Min(minY, el.Y);
            maxX = Math.Max(maxX, el.X + elemWidth);
            maxY = Math.Max(maxY, el.Y + elemHeight);
        }
        
        // Vytvoríme selection rectangle s malým padding
        const double padding = 2;
        var selectionRect = new Border
        {
            Width = maxX - minX + padding * 2,
            Height = maxY - minY + padding * 2,
            BorderBrush = new SolidColorBrush(Color.Parse("#1565C0")),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(30, 21, 101, 192)), // Jemne modrá priehľadná
            CornerRadius = new CornerRadius(4),
            IsHitTestVisible = false,
        };
        
        Canvas.SetLeft(selectionRect, minX - padding);
        Canvas.SetTop(selectionRect, minY - padding);
        layer.Children.Add(selectionRect);
    }

    // 🟢 Factory: vytvorí správny marker podľa MarkerKey / ElementType ━━━━━━━━
    private Control? CreateMarkerControl(LayoutElement el, bool isSelected)
    {
        // Block má špeciálne zaobchádzanie – Canvas host, bez outline vrstvy
        if (el.MarkerKey == "Block")
            return CreateBlockControl(el, isSelected);

        // Text má špeciálne zaobchádzanie – dynamická veľkosť
        if (el.MarkerKey == "Text" && el is TextElement textEl)
            return CreateTextControl(textEl, isSelected);

        // Vytvoríme spodnú vrstvu (oramovanie) - hrubšia, čierna/priesvitná
        Control? innerOutline = el.MarkerKey switch
        {
            "TrackSegment"   => new MarkerTrackSegment(),
            "Curve_45"       => new MarkerCurve45(),
            "Curve_90"       => new MarkerCurve90(),
            "Bumper"         => new MarkerBumper(),
            "Turnout_L"      => new MarkerTurnoutL(),
            "Turnout_R"      => new MarkerTurnoutR(),
            "TurnoutL90"     => new MarkerTurnoutL90(),
            "TurnoutR90"     => new MarkerTurnoutR90(),
            "TurnoutCurve_L" => new MarkerTurnoutCurveL(),
            "TurnoutCurve_R" => new MarkerTurnoutCurveR(),
            "Turnout_Y"      => new MarkerTurnoutY(),
            "Turnout_3W"     => new MarkerTurnout3W(),
            "Cross90"        => new MarkerCross90(),
            "Cross45"        => new MarkerCross45(),
            "DoubleSlip"     => new MarkerDoubleSlip(),
            "Bridge90"       => new MarkerBridge90(),
            "Bridge45L"      => new MarkerBridge45L(),
            "Bridge45R"      => new MarkerBridge45R(),
            "Signal"         => new MarkerSignal(),
            "Signal5"       => new MarkerSignal(),
            "Signal4"       => new MarkerSignal(),
            "Signal2Main"   => new MarkerSignal(),
            "Signal2Shunt"  => new MarkerSignal(),
            "Signal2Route"  => new MarkerSignal(),
            "Signal3Entry"  => new MarkerSignal(),
            "Sensor"         => new MarkerSensor(),
            "Route"          => new MarkerRoute(),
            _ => null
        };

        // Vytvoríme hornú vrstvu (normálny marker)
        Control? inner = el.MarkerKey switch
        {
            "TrackSegment"   => new MarkerTrackSegment(),
            "Curve_45"       => new MarkerCurve45(),
            "Curve_90"       => new MarkerCurve90(),
            "Bumper"         => new MarkerBumper(),
            "Turnout_L"      => new MarkerTurnoutL(),
            "Turnout_R"      => new MarkerTurnoutR(),
            "TurnoutL90"     => new MarkerTurnoutL90(),
            "TurnoutR90"     => new MarkerTurnoutR90(),
            "TurnoutCurve_L" => new MarkerTurnoutCurveL(),
            "TurnoutCurve_R" => new MarkerTurnoutCurveR(),
            "Turnout_Y"      => new MarkerTurnoutY(),
            "Turnout_3W"     => new MarkerTurnout3W(),
            "Cross90"        => new MarkerCross90(),
            "Cross45"        => new MarkerCross45(),
            "DoubleSlip"     => new MarkerDoubleSlip(),
            "Bridge90"       => new MarkerBridge90(),
            "Bridge45L"      => new MarkerBridge45L(),
            "Bridge45R"      => new MarkerBridge45R(),
            "Signal"         => new MarkerSignal(),
            "Signal5"       => new MarkerSignal(),
            "Signal4"       => new MarkerSignal(),
            "Signal2Main"   => new MarkerSignal(),
            "Signal2Shunt"  => new MarkerSignal(),
            "Signal2Route"  => new MarkerSignal(),
            "Signal3Entry"  => new MarkerSignal(),
            "Sensor"         => new MarkerSensor(),
            "Route"          => new MarkerRoute(),
            _ => null
        };

        if (inner == null) return null;
        
        int markerAngle = NormalizeMarkerAngle(el.Rotation);

        // Nastavenie spodnej vrstvy (oramovanie)
        if (innerOutline != null)
        {
            innerOutline.UseLayoutRounding = false;
            if (innerOutline is UserControl ucOutline)
            {
                ucOutline.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                ucOutline.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            }
            
            // Nastavenie rotácie pre oramovanie
            if (innerOutline is IMarkerAngle mOutline)
                mOutline.SetAngle(markerAngle);
            else if (markerAngle != 0)
                innerOutline.RenderTransform = new RotateTransform(markerAngle, Cell / 2, Cell / 2);
            
            // Zmeníme farbu a hrúbku čiar pre oramovanie
            ApplyOutlineStyle(innerOutline);
        }
        
        // Nastavenie hornej vrstvy (normálny marker)
        inner.UseLayoutRounding = false;
        if (inner is UserControl uc)
        {
            uc.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            uc.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        }

        // Všetky markery implementujú IMarkerAngle
         if (inner is IMarkerAngle m)
             m.SetAngle(markerAngle);
         else if (markerAngle != 0)
             inner.RenderTransform = new RotateTransform(markerAngle, Cell / 2, Cell / 2);

         // Ak ide o RouteElement, nastav farbu šípky
         if (el is RouteElement route)
         {
             bool hasRoute = RouteMarkerAssignmentHelper.HasAssignedRoute(_vm?.CurrentLayout, route);
             if (inner is MarkerRoute markerRoute)
                 markerRoute.SetRouteAssigned(hasRoute);
         }

         // Ak ide o SignalElement, nastav profil (počet znakov) aj výchozí aspekt
         if (el is SignalElement signalEl)
         {
             int signCount = SignalFootprintHelper.ParseSignCount(signalEl.SignalProfile);
             if (innerOutline is IMarkerSignalProfile outlineProf)
                 outlineProf.SetProfile(signCount);
             if (inner is IMarkerSignalProfile innerProf)
                 innerProf.SetProfile(signCount);
             // Nastav profil ID pre správne farby (napr. 2-aspect-main = červená/zelená)
             if (innerOutline is IMarkerSignalProfileId outlineProfId)
                 outlineProfId.SetProfileId(signalEl.SignalProfile);
             if (inner is IMarkerSignalProfileId innerProfId)
                 innerProfId.SetProfileId(signalEl.SignalProfile);
             // V editore používame PREVIEW mód: všetky svetlá sa zobrazia v prirodzených
             // farbách (rovnako ako náhľad v ribbon páse), nie len jediný "aktívny" aspekt.
             if (innerOutline is IMarkerSignalPreview outlinePreview)
                 outlinePreview.SetPreviewAllLit(true);
             if (inner is IMarkerSignalPreview innerPreview)
                 innerPreview.SetPreviewAllLit(true);
         }

         var (hostWidth, hostHeight) = GetElementFootprint(el);
         var host = new Grid
        {
            Width = hostWidth,
            Height = hostHeight,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            UseLayoutRounding = false,
            ClipToBounds = false,
        };

        if (innerOutline != null)
            host.Children.Add(innerOutline);
        host.Children.Add(inner);

        if (isSelected)
        {
            host.Children.Add(new Border
            {
                Margin = new Thickness(-1),
                BorderBrush = new SolidColorBrush(Color.Parse("#1565C0")),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(40, 21, 101, 192)),
                CornerRadius = new CornerRadius(2),
                IsHitTestVisible = false,
            });
        }
        return host;
    }

    /// <summary>Načíta ikonu vozidla z Assets/LocoIcons alebo Assets/WagonIcons s cachovaním.</summary>
    private static Image? LoadIconImage(string iconName)
    {
        if (string.IsNullOrEmpty(iconName)) return null;

        // Skontroluj cache
        if (_iconCache.TryGetValue(iconName, out var cachedBitmap))
        {
            return new Image { Source = cachedBitmap };
        }

        var bitmap = VehicleIconLoader.TryLoadBitmap(iconName);
        if (bitmap == null)
            return null;

        _iconCache[iconName] = bitmap;
        return new Image { Source = bitmap };
    }

    /// <summary>
    /// Vytvorí vizuál pre marker Block priamo z primitív (Rectangle + TextBlock).
    /// ŽIADNY RenderTransform na UserControl – canvas má presne vizuálnu veľkosť bloku.
    /// </summary>
    private Control CreateBlockControl(LayoutElement el, bool isSelected)
    {
        // Získame BlockElement pre prístup k BlockLengthCells
        int blockLengthCells = 4; // default
        bool isOccupied = false;
        if (el is BlockElement blockEl)
        {
            blockLengthCells = blockEl.BlockLengthCells;
            isOccupied = blockEl.IsOccupied;
            // Validácia: min 1, max 20
            if (blockLengthCells < 1) blockLengthCells = 1;
            if (blockLengthCells > 20) blockLengthCells = 20;
        }
        
        bool isVertical = el.Rotation == 90 || el.Rotation == 270;
        double W = isVertical ? Cell : Cell * blockLengthCells;   // vizuálna šírka
        double H = isVertical ? Cell * blockLengthCells : Cell;    // vizuálna výška

        // Názov bloku z Label (použi priamo názov, nie parsovanie čísla)
        string blockName = string.IsNullOrWhiteSpace(el.Label) ? "Blok" : el.Label;

        // Hlavný Canvas – má presne vizuálnu veľkosť bloku
        var canvas = new Canvas
        {
            Width             = W,
            Height            = H,
            ClipToBounds      = true,  // Obmedzíme ikony vlaku len na oblasť bloku
            UseLayoutRounding = false,
        };

        // 🟦 Pozadie bloku ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        var rect = new Avalonia.Controls.Shapes.Rectangle
        {
            Width           = W,
            Height          = H,
            Fill            = isOccupied
                ? new SolidColorBrush(Color.Parse("#FFD6D6"))
                : new SolidColorBrush(Color.Parse("#FFFFDC")),
            Stroke          = new SolidColorBrush(Color.Parse("#003366")),
            StrokeThickness = 1,
            RadiusX         = 2,
            RadiusY         = 2,
            UseLayoutRounding = false,
        };
        Canvas.SetLeft(rect, 0);
        Canvas.SetTop(rect, 0);
        canvas.Children.Add(rect);

        // 📝 Text ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        const double TextH = 14.0;  // aproximovaná výška textu (FontSize=11)

        var text = new TextBlock
        {
            Text        = blockName,
            FontSize    = 12,
            FontWeight  = FontWeight.Normal,
            Foreground  = new SolidColorBrush(Color.Parse("#333333")),
            TextAlignment = Avalonia.Media.TextAlignment.Center,
        };

        if (!isVertical)
        {
            // Horizontálne: TextBlock šírky ako blok, vertikálne centrované
            text.Width = W;
            Canvas.SetLeft(text, 0);
            Canvas.SetTop(text, (H - TextH) / 2);   // ← (24-14)/2 = 5
        }
        else
        {
            // Vertikálne: TextBlock má šírku = H (96px), rotujeme okolo vlastného stredu
            // Stred bloku je na (W/2, H/2) = (12, 48)
            // Canvas.Left = W/2 - H/2 = 12 - 48 = -36
            // Canvas.Top  = H/2 - TextH/2 ← 48 - 7 = 41
            text.Width  = H;          // 96px – po rotácii bude "výška" vizuálu
            text.Height = TextH;
            text.RenderTransform       = new RotateTransform(el.Rotation == 90 ? 90 : -90);
            text.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            Canvas.SetLeft(text, W / 2 - H / 2);           // -36
            Canvas.SetTop(text,  H / 2 - TextH / 2);       //  41
        }
        canvas.Children.Add(text);

        // 🔵 Selection overlay ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        if (isSelected)
        {
            var sel = new Border
            {
                Width            = W + 2,
                Height           = H + 2,
                BorderBrush      = new SolidColorBrush(Color.Parse("#1565C0")),
                BorderThickness  = new Thickness(1),
                Background       = new SolidColorBrush(Color.FromArgb(40, 21, 101, 192)),
                CornerRadius     = new CornerRadius(2),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(sel, -1);
            Canvas.SetTop(sel,  -1);
            canvas.Children.Add(sel);
        }

        // 🚂 Ghost lokomotívy/vlaku v bloku (opacity 0.5) ━━━━━━━━━━━━━━━━━━━━━━━
        if (el is BlockElement blockElAssign && !string.IsNullOrEmpty(blockElAssign.AssignedLocoId))
        {
            // Nájdeme runtime Locomotive objekt pomocou Code (AssignedLocoId je Code)
            var loco = _vm?.SmartStripsLocomotives?.FirstOrDefault(l => l.Code == blockElAssign.AssignedLocoId);
            if (loco != null)
            {
                // Vykresľovanie vlaku je delegované do zdieľaného helpera BlockTrainRenderer.
                // Jednotná logika s OperationView – žiadne duplicitné transform-hacky.
                // V editore názov nezobrazujeme – ten už renderuje "text" element vyššie ako názov bloku.
                var orientation = TrackFlow.Views.Shared.TrainOrientationExtensions.From(
                    isVertical, blockElAssign.AssignedLocoIsForward);
                var train = TrackFlow.Views.Shared.BlockTrainRenderer.CreateTrainVisual(
                    loco, orientation, W, H, showName: false);
                Canvas.SetLeft(train, 0);
                Canvas.SetTop(train, 0);
                canvas.Children.Add(train);
            }
        }


        return canvas;
    }

    private Control CreateTextControl(TextElement textEl, bool isSelected)
    {
        // Marker zaberá WidthInCells × HeightInCells buniek (každá bunka 24px).
        // Default: žiadny rám, žiadne pozadie - iba text. Rám/pozadie pridáva užívateľ cez dialóg.
        double cellW = textEl.WidthInCells  * Cell;
        double cellH = textEl.HeightInCells * Cell;

        // Vonkajší host (zaberá celú plochu buniek - hit-test pre drag/select/click)
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
                        : new SolidColorBrush(Color.Parse("#FFFFFF")))
                    : Brushes.Transparent
            };
            host.Children.Add(frameBorder);
        }

        // TextBlock - vždy zobrazený, vyplní celú plochu
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

        host.Children.Add(textBlock);

        if (isSelected)
        {
            var selectionBorder = new Border
            {
                Width  = cellW,
                Height = cellH,
                BorderBrush = new SolidColorBrush(Color.Parse("#1565C0")),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(2),
                IsHitTestVisible = false,
                Child = host
            };
            return selectionBorder;
        }

        return host;
    }

    // ── Pomocné metódy pre signálové markery ─────────────────────────────────

    private static int NormalizeMarkerAngle(double angle)
        => LayoutElementFootprintHelper.NormalizeMarkerAngle(angle);

    private static (double Width, double Height) GetElementFootprint(LayoutElement el)
        => LayoutElementFootprintHelper.GetFootprint(el, Cell, compactTwoAspectSignals: true);

    // Pomocná metóda na aplikovanie štýlu oramovania na marker
    private static void ApplyOutlineStyle(Control marker)
    {
        if (marker is not UserControl uc) return;

        // Variant 1: Canvas s children (väčšina markerov)
        if (uc.Content is Canvas canvas)
        {
            foreach (var child in canvas.Children)
            {
                // Preskočíme prvky s Tag="NoOutline" - použité pre skryté segmenty pod mostami
                if (child is Shape shape && shape.Tag?.ToString() == "NoOutline")
                    continue;
                
                // Preskočíme prvky, ktoré už sú outline (majú "Outline" v názve)
                // Tieto sú definované priamo v AXAML markerov výhybiek
                if (child is Control ctrl && ctrl.Name?.Contains("Outline") == true)
                    continue;

                if (child is Line line)
                {
                    // Zvýšime hrúbku o 2px (1px z každej strany)
                    line.StrokeThickness += 2;
                    // Nastavíme čiernu priesvitnú farbu (alpha 80%)
                    line.Stroke = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0));
                }
                else if (child is Path path)
                {
                    // Zvýšime hrúbku o 2px
                    path.StrokeThickness += 2;
                    // Nastavíme čiernu priesvitnú farbu (alpha 80%)
                    path.Stroke = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0));
                }
            }
        }
        // Variant 2: Path priamo ako obsah (Curve45, Curve90)
        else if (uc.Content is Path pathDirect)
        {
            // Zvýšime hrúbku o 2px
            pathDirect.StrokeThickness += 2;
            // Nastavíme čiernu priesvitnú farbu (alpha 80%)
            pathDirect.Stroke = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0));
        }
    }


    // 🟦 Mriežka ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawGrid()
    {
        var layer = this.FindControl<Canvas>("GridLayer");
        if (layer == null) return;
        layer.Children.Clear();

        var vm = DataContext as LayoutEditorViewModel;
        var w = vm?.CanvasWidth  ?? 2400;
        var h = vm?.CanvasHeight ?? 1444;

        // Mriežka: čierna farba s alpha 0.15 (15% opacity = 38/255)
        var pen = new SolidColorBrush(Color.FromArgb(60, 30, 136, 229)); // modrá mriežka

        // Vertikálne čiary - pixel-perfect rendering
        for (var x = 0.0; x <= w; x += Cell)
        {
            var line = new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, h),
                Stroke = pen,
                StrokeThickness = 0.8,
                IsHitTestVisible = false,
                UseLayoutRounding = true,
                StrokeLineCap = PenLineCap.Flat
            };
            layer.Children.Add(line);
        }

        // Horizontálne čiary - pixel-perfect rendering
        for (var y = 0.0; y <= h; y += Cell)
        {
            var line = new Line
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(w, y),
                Stroke = pen,
                StrokeThickness = 1,
                IsHitTestVisible = false,
                UseLayoutRounding = true,
                StrokeLineCap = PenLineCap.Flat
            };
            layer.Children.Add(line);
        }
    }

    // 📏 Pravítka ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Pravidlá:
    // - Číslo bunky sa zobrazuje nad 1., 10., 20., 30.... bunkou
    // - Po každej 5. bunke dlhšia čiarka
    // - Po každej 10. bunke čiarka na celé pravítko
    private void DrawRulers()
    {
        var scroller  = this.FindControl<ScrollViewer>("CanvasScroller");
        var topRuler  = this.FindControl<Canvas>("TopRulerCanvas");
        var leftRuler = this.FindControl<Canvas>("LeftRulerCanvas");
        if (scroller == null || topRuler == null || leftRuler == null) return;

        var offsetX = scroller.Offset.X;
        var offsetY = scroller.Offset.Y;
        var viewW   = scroller.Viewport.Width;
        var viewH   = scroller.Viewport.Height;

        var tickBrush  = new SolidColorBrush(Color.Parse("#666666"));
        var labelBrush = new SolidColorBrush(Color.Parse("#333333"));

        // Výšky/šírky značiek:
        // - Bežná bunka: krátka čiarka (5px)
        // - Každá 5. bunka: stredná čiarka (10px)
        // - Každá 10. bunka: čiarka na celé pravítko (Ruler = 20px)
        double minorTick  = 5;
        double majorTick  = 10;
        double fullTick   = Ruler;  // Celé pravítko

        topRuler.Children.Clear();
        topRuler.Width = viewW;

        var startCellX = (int)Math.Floor(offsetX / Cell);
        var endCellX   = (int)Math.Ceiling((offsetX + viewW) / Cell);

        for (var ci = startCellX; ci <= endCellX; ci++)
        {
            var screenX  = ci * Cell - offsetX;
            int cellNumber = ci + 1;  // Číslo bunky (1-based)
            
            // Čiarka na pozícii ci je PO ci bunkách (= na pravom okraji bunky ci)
            // Preto používame ci pre kontrolu, nie cellNumber
            // ci=5 → PO 5. bunke, ci=10 → PO 10. bunke
            bool isTenth = ci > 0 && ci % 10 == 0;
            bool isFifth = ci > 0 && ci % 5 == 0;
            double tickH = isTenth ? fullTick : (isFifth ? majorTick : minorTick);

            topRuler.Children.Add(new Line
            {
                StartPoint      = new Point(screenX, Ruler - tickH),
                EndPoint        = new Point(screenX, Ruler),
                Stroke          = tickBrush,
                StrokeThickness = 1,
            });

            // Číslo bunky sa zobrazuje nad 1., 10., 20., 30.... bunkou (v strede bunky)
            bool showLabel = cellNumber == 1 || cellNumber % 10 == 0;
            if (showLabel)
            {
                // Odhad šírky textu pre centrovanie
                double textWidth = cellNumber.ToString().Length * 5;
                topRuler.Children.Add(new TextBlock
                {
                    Text       = cellNumber.ToString(),
                    FontSize   = 8,
                    Foreground = labelBrush,
                    [Canvas.LeftProperty] = screenX + Cell / 2 - textWidth / 2,
                    [Canvas.TopProperty]  = 1,
                });
            }
        }

        leftRuler.Children.Clear();
        leftRuler.Height = viewH;

        var startCellY = (int)Math.Floor(offsetY / Cell);
        var endCellY   = (int)Math.Ceiling((offsetY + viewH) / Cell);

        for (var ci = startCellY; ci <= endCellY; ci++)
        {
            var screenY  = ci * Cell - offsetY;
            int cellNumber = ci + 1;  // Číslo bunky (1-based)
            
            // Čiarka na pozícii ci je PO ci bunkách (= na dolnom okraji bunky ci)
            // Preto používame ci pre kontrolu, nie cellNumber
            // ci=5 → PO 5. bunke, ci=10 → PO 10. bunke
            bool isTenth = ci > 0 && ci % 10 == 0;
            bool isFifth = ci > 0 && ci % 5 == 0;
            double tickW = isTenth ? fullTick : (isFifth ? majorTick : minorTick);

            leftRuler.Children.Add(new Line
            {
                StartPoint      = new Point(Ruler - tickW, screenY),
                EndPoint        = new Point(Ruler, screenY),
                Stroke          = tickBrush,
                StrokeThickness = 1,
            });

            // Číslo bunky sa zobrazuje nad 1., 10., 20., 30.... bunkou (v strede bunky)
            bool showLabel = cellNumber == 1 || cellNumber % 10 == 0;
            if (showLabel)
            {
                var tb = new TextBlock
                {
                    Text       = cellNumber.ToString(),
                    FontSize   = 8,
                    Foreground = labelBrush,
                };
                tb.RenderTransform       = new RotateTransform(-90);
                tb.RenderTransformOrigin = RelativePoint.TopLeft;
                tb[Canvas.LeftProperty]  = 2.0;
                tb[Canvas.TopProperty]   = screenY + Cell / 2 + 4;
                leftRuler.Children.Add(tb);
            }
        }
    }

    // 🟢 Drag-and-Drop lokomotívy na blok ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private const string LocoFormat = "trackflow/locomotive";

    /// <summary>
    /// Počas ťahania lokomotívy nad canvasom: nájde blok pod kurzorom,
    /// vypočíta smer (ľavá/pravá resp. horná/dolná polovica) a zobrazí floating šípku nad kurzorom.
    /// </summary>
    private void OnCanvasLocoDragOver(object? sender, DragEventArgs e)
    {
        if (_vm == null || !DragDropCompat.Contains(e, LocoFormat))
        {
            e.DragEffects = DragDropEffects.None;
            HideDragArrow();
            return;
        }

        var canvas = this.FindControl<Canvas>("LayoutCanvas");
        if (canvas == null) { e.DragEffects = DragDropEffects.None; HideDragArrow(); return; }

        var pos = e.GetPosition(canvas);
        var block = FindBlockElementAt(pos.X, pos.Y);

        if (block == null)
        {
            _vm.SetBlockDragOver(null, false);
            e.DragEffects = DragDropEffects.None;
            HideDragArrow();
            return;
        }

        bool isForward = ComputeDropDirection(block, pos.X, pos.Y);
        _vm.SetBlockDragOver(block, isForward);
        
        // Zobraz floating šípku nad kurzorom
        ShowDragArrow(pos.X, pos.Y, block, isForward);
        
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>Kurzor opustil canvas – vyčisti drag-over overlay.</summary>
    private void OnCanvasLocoDragLeave(object? sender, DragEventArgs e)
    {
        _vm?.SetBlockDragOver(null, false);
        HideDragArrow();
    }

    /// <summary>
    /// Drop lokomotívy na blok: priradí lokomotive ku bloku, nastaví smer,
    /// vyčistí drag-over overlay.
    /// </summary>
    private void OnCanvasLocoDrop(object? sender, DragEventArgs e)
    {
        if (_vm == null || !DragDropCompat.Contains(e, LocoFormat)) return;

        var loco = DragDropCompat.Get(e, LocoFormat) as Locomotive;
        if (loco == null) return;

        var canvas = this.FindControl<Canvas>("LayoutCanvas");
        if (canvas == null) return;

        var pos = e.GetPosition(canvas);
        var block = FindBlockElementAt(pos.X, pos.Y);

        // Vyčisti drag-over stav
        _vm.SetBlockDragOver(null, false);
        HideDragArrow();

        if (block == null) return;

        bool isForward = ComputeDropDirection(block, pos.X, pos.Y);
        _vm.AssignLocomotiveToBlock(block, loco.Code, isForward);
        e.Handled = true;
    }

    /// <summary>
    /// Nájde BlockElement na pixelových súradniciach (rešpektuje BlockLengthCells).
    /// </summary>
    private BlockElement? FindBlockElementAt(double x, double y)
    {
        if (_vm == null) return null;

        for (int i = _vm.Elements.Count - 1; i >= 0; i--)
        {
            var el = _vm.Elements[i];
            if (el is not BlockElement block) continue;

            int len = block.BlockLengthCells;
            if (len < 1) len = 1;
            if (len > 20) len = 20;

            bool isVertical = el.Rotation == 90 || el.Rotation == 270;
            double w = isVertical ? Cell : Cell * len;
            double h = isVertical ? Cell * len : Cell;

            if (x >= el.X && x < el.X + w && y >= el.Y && y < el.Y + h)
                return block;
        }
        return null;
    }

    /// <summary>
    /// Vypočíta smer dropu: pre horizontálny blok porovnáva X s polovicou šírky,
    /// pre vertikálny blok porovnáva Y s polovicou výšky.
    /// Vracia true = dopredu (pravá/dolná polovica), false = dozadu (ľavá/horná polovica).
    /// </summary>
    private static bool ComputeDropDirection(BlockElement block, double x, double y)
    {
        int len = block.BlockLengthCells;
        if (len < 1) len = 1;
        if (len > 20) len = 20;

        bool isVertical = block.Rotation == 90 || block.Rotation == 270;

        if (!isVertical)
        {
            double centerX = block.X + (Cell * len) / 2.0;
            return x >= centerX; // pravá polovica = dopredu
        }
        else
        {
            double centerY = block.Y + (Cell * len) / 2.0;
            return y >= centerY; // dolná polovica = dopredu
        }
    }

    /// <summary>Zobrazí floating šípku nad kurzorom pri drag&drop.</summary>
    private void ShowDragArrow(double cursorX, double cursorY, BlockElement block, bool isForward)
    {
        var arrowRight = this.FindControl<Image>("DragArrowRight");
        var arrowLeft = this.FindControl<Image>("DragArrowLeft");
        var arrowDown = this.FindControl<Image>("DragArrowDown");
        var arrowUp = this.FindControl<Image>("DragArrowUp");
        
        if (arrowRight == null || arrowLeft == null || arrowDown == null || arrowUp == null) return;

        // Skryj všetky šípky
        arrowRight.IsVisible = false;
        arrowLeft.IsVisible = false;
        arrowDown.IsVisible = false;
        arrowUp.IsVisible = false;
        
        // Zisti, či je blok vertikálny
        bool isVertical = block.Rotation == 90 || block.Rotation == 270;
        
        if (isVertical)
        {
            // Vertikálny blok: použij up/down šípky
            if (isForward)
            {
                // Forward = dole (bottom)
                Canvas.SetLeft(arrowDown, cursorX);
                Canvas.SetTop(arrowDown, cursorY);
                arrowDown.IsVisible = true;
            }
            else
            {
                // Backward = hore (top)
                Canvas.SetLeft(arrowUp, cursorX);
                Canvas.SetTop(arrowUp, cursorY);
                arrowUp.IsVisible = true;
            }
        }
        else
        {
            // Horizontálny blok: použij left/right šípky
            if (isForward)
            {
                // Forward = vpravo (right)
                Canvas.SetLeft(arrowRight, cursorX);
                Canvas.SetTop(arrowRight, cursorY);
                arrowRight.IsVisible = true;
            }
            else
            {
                // Backward = vľavo (left)
                Canvas.SetLeft(arrowLeft, cursorX);
                Canvas.SetTop(arrowLeft, cursorY);
                arrowLeft.IsVisible = true;
            }
        }
    }

    /// <summary>Skryje floating šípku.</summary>
    private void HideDragArrow()
    {
        var arrowRight = this.FindControl<Image>("DragArrowRight");
        var arrowLeft = this.FindControl<Image>("DragArrowLeft");
        var arrowDown = this.FindControl<Image>("DragArrowDown");
        var arrowUp = this.FindControl<Image>("DragArrowUp");
        
        if (arrowRight != null) arrowRight.IsVisible = false;
        if (arrowLeft != null) arrowLeft.IsVisible = false;
        if (arrowDown != null) arrowDown.IsVisible = false;
        if (arrowUp != null) arrowUp.IsVisible = false;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)    {
        var pos   = e.GetPosition(this.FindControl<Canvas>("LayoutCanvas"));
        var cellX = (int)Math.Floor(pos.X / Cell);
        var cellY = (int)Math.Floor(pos.Y / Cell);
        var hover = this.FindControl<Rectangle>("HoverCell")!;

        // Zrušenie hover efektu - vždy skryjeme
        hover.IsVisible = false;

        if (DataContext is LayoutEditorViewModel vm)
        {
            vm.HoverCellX = cellX;
            vm.HoverCellY = cellY;
        }

        // Drag-kreslenie rovnej koľaje
        if (_isDraggingTrack && _vm != null)
        {
            DrawGhostTrack(_dragStartCellX, _dragStartCellY, cellX, cellY);
        }
        // Drag-posúvanie multi-select výberu
        else if (_isDraggingSelection && _vm != null)
        {
            double deltaX = pos.X - _dragSelectionStart.X;
            double deltaY = pos.Y - _dragSelectionStart.Y;
            
            // Veľmi citlivý snap - posun pri ~4px (15% bunky) pre maximálnu responzivitu
            // Marker drží krok s kurzorom, kurzor takmer neopúšťa marker
            double snappedDeltaX = Math.Floor(deltaX / Cell + 0.15) * Cell;
            double snappedDeltaY = Math.Floor(deltaY / Cell + 0.15) * Cell;
            
            // Posunieme každý prvok od jeho ORIGINÁLNEJ pozície
            foreach (var el in _vm.Selection.SelectedElements)
            {
                if (_originalPositions.TryGetValue(el, out var originalPos))
                {
                    el.X = originalPos.X + snappedDeltaX;
                    el.Y = originalPos.Y + snappedDeltaY;
                }
            }
            
            RebuildElementsLayer();
        }
        // Drag-resize bloku
        else if (_isResizingBlock && _vm != null && _resizingBlock != null)
        {
            HandleBlockResize(pos.X, pos.Y);
        }
        // Drag-resize textu (oddelené od bloku)
        else if (_isResizingText && _vm != null && _resizingText != null)
        {
            HandleTextResize(pos.X, pos.Y);
        }
        // Aktualizácia marquee výberu
        else if (_marqueeBehavior?.IsMarqueeDragging == true && _vm != null)
        {
            _marqueeBehavior.UpdateMarquee(pos, _vm);
        }
        // Zmena kurzora
        else if (_openContextMenu?.IsOpen != true && _vm != null)
        {
            // Resize kurzor pri okraji textu (vyššia priorita ako block - text môže byť nad ním)
            var (textResizeDir, _) = GetTextResizeDirection(pos.X, pos.Y);
            if (!string.IsNullOrEmpty(textResizeDir))
            {
                this.Cursor = (textResizeDir == "left" || textResizeDir == "right")
                    ? new Cursor(StandardCursorType.SizeWestEast)
                    : new Cursor(StandardCursorType.SizeNorthSouth);
            }
            else
            {
                // Resize kurzor pri okraji bloku
                var (resizeDir, _) = GetResizeDirection(pos.X, pos.Y);
                if (!string.IsNullOrEmpty(resizeDir))
                {
                    this.Cursor = (resizeDir == "left" || resizeDir == "right")
                        ? new Cursor(StandardCursorType.SizeWestEast)
                        : new Cursor(StandardCursorType.SizeNorthSouth);
                }
                // Move kurzor nad výberom (v Select režime)
                else if (_vm.SelectedTool == LayoutTool.Select &&
                         _vm.Selection.SelectionCount > 0 && IsPointerOverSelection(pos.X, pos.Y))
                {
                    this.Cursor = new Cursor(StandardCursorType.SizeAll);
                }
                // Hand kurzor nad klikateľnými prvkami
                else
                {
                    var elementAtPointer = FindElementAt(pos.X, pos.Y);
                    if (elementAtPointer != null && IsClickableElement(elementAtPointer))
                        this.Cursor = new Cursor(StandardCursorType.Hand);
                    else
                        this.Cursor = Cursor.Default;
                }
            }
        }
        else if (_openContextMenu?.IsOpen != true)
        {
            this.Cursor = Cursor.Default;
        }
    }

    /// <summary>Vráti true ak je bunka (cellX, cellY) pokrytá niektorým Blokom.</summary>
    /// <summary>Vráti true ak je bunka (cellX, cellY) pokrytá niektorým Blokom.</summary>
    private bool IsCellCoveredByBlock(int cellX, int cellY)
    {
        if (_vm == null) return false;
        foreach (var el in _vm.Elements)
        {
            if (el.MarkerKey != "Block") continue;
            
            // Ziskame skutocnu dlzku bloku
            int blockLen = 4; // default
            if (el is BlockElement blockEl)
            {
                blockLen = blockEl.BlockLengthCells;
                if (blockLen < 1) blockLen = 1;
                if (blockLen > 20) blockLen = 20;
            }
            
            // Použijeme Math.Floor (nie Round) pre konzistentnú výpočet bunkovej pozície
            int ex = (int)Math.Floor(el.X / Cell);
            int ey = (int)Math.Floor(el.Y / Cell);
            
            bool isVertical = el.Rotation == 90 || el.Rotation == 270;
            
            if (!isVertical)
            {
                // Horizontálne: blockLen buniek doprava
                if (cellY == ey && cellX >= ex && cellX < ex + blockLen) return true;
            }
            else
            {
                // Vertikálne (90° alebo 270°): blockLen buniek nadol
                if (cellX == ex && cellY >= ey && cellY < ey + blockLen) return true;
            }
        }
        return false;
    }

    private void OnCanvasPointerExited(object? sender, PointerEventArgs e)
    {
        var hover = this.FindControl<Rectangle>("HoverCell");
        if (hover != null) hover.IsVisible = false;
        
        // Vyčistíme ghost pri opustení canvasu
        if (_isDraggingTrack)
        {
            ClearGhostLayer();
        }
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not LayoutEditorViewModel vm) return;

        // Ak je kontextové menu práve otvorené, ignorujeme ďalší PointerPressed
        if (_openContextMenu?.IsOpen == true)
        {
            e.Handled = true;
            return;
        }

        this.Focus();

        var props = e.GetCurrentPoint(null).Properties;
        var keyModifiers = e.KeyModifiers;

        if (props.IsRightButtonPressed)
        {
            var pos    = e.GetPosition(this.FindControl<Canvas>("LayoutCanvas"));
            var hitEl  = FindElementAt(pos.X, pos.Y);

            if (hitEl != null)
            {
                // Ak klikneme pravým na prvok ktorý NIE JE vybratý, vyberieme ho
                if (!vm.Selection.IsSelected(hitEl) && vm.SelectedElement != hitEl)
                {
                    vm.Selection.ClearSelection();
                    vm.SelectedElement = hitEl;
                    vm.Selection.AddToSelection(hitEl);
                }
                
                ShowElementContextMenu(hitEl, vm, e);
                e.Handled = true;
                return;
            }
            
            // Ak je myš nad výberom (ale nie priamo na markeri), zobraz multi-select menu
            if (vm.Selection.SelectionCount > 0 && IsPointerOverSelection(pos.X, pos.Y))
            {
                // Použijeme prvý vybratý prvok pre menu (ale zobrazí multi-select verziu)
                var firstSelected = vm.Selection.SelectedElements.FirstOrDefault();
                if (firstSelected != null)
                {
                    ShowElementContextMenu(firstSelected, vm, e);
                    e.Handled = true;
                    return;
                }
            }

            // Prázdna bunka:
            // - V režime Place (Insert): Iba prepneme na Select, BEZ menu
            // - V režime Select: Prepneme na Select a zobrazíme menu
            if (vm.SelectedTool == LayoutTool.Place)
            {
                // Režim Insert/Place - iba prepneme na Select, ŽIADNE MENU!
                vm.ActivateSelectTool();
                e.Handled = true;
                return;
            }
            else
            {
                // Režim Select - prepneme na Select a zobrazíme menu
                vm.ActivateSelectTool();
                ShowEmptyCellContextMenu(pos.X, pos.Y, vm);
                e.Handled = true;
                return;
            }
        }

        if (props.IsLeftButtonPressed)
        {
            var pos = e.GetPosition(this.FindControl<Canvas>("LayoutCanvas"));
            var cellX = (int)Math.Floor(pos.X / Cell);
            var cellY = (int)Math.Floor(pos.Y / Cell);

            // 🔧 PRIORITA 0a: Resize textu – funguje v oboch režimoch ━━━━━━━━━━━━
            // POZN.: Pri dvojkliku resize PRESKAKUJEME, aby double-click otvoril dialóg.
            if (e.ClickCount < 2)
            {
                var (textResizeDir, textForResize) = GetTextResizeDirection(pos.X, pos.Y);
                if (!string.IsNullOrEmpty(textResizeDir) && textForResize != null)
                {
                    StartTextResize(textForResize, textResizeDir);
                    e.Handled = true;
                    return;
                }
            }

            // 🔧 PRIORITA 0: Resize bloku – funguje v oboch režimoch ━━━━━━━━━━━━━━
            if (e.ClickCount < 2)
            {
                var (resizeDir, resizeBlock) = GetResizeDirection(pos.X, pos.Y);
                if (!string.IsNullOrEmpty(resizeDir) && resizeBlock != null)
                {
                    StartBlockResize(resizeBlock, resizeDir);
                    e.Handled = true;
                    return;
                }
            }
            
            // 🔧 PRIORITA 0b: Dvojklik na klikateľný prvok – funguje v OBOCH režimoch
            // (Place aj Select). Inak by užívateľ musel najprv prepnúť na Výber.
            if (e.ClickCount == 2)
            {
                var hitDbl = FindElementAt(pos.X, pos.Y);
                if (hitDbl != null && IsClickableElement(hitDbl) && e.ClickCount == 2)
                {
                    vm.Selection.ClearSelection();
                    vm.SelectedElement = hitDbl;
                    vm.Selection.AddToSelection(hitDbl);
                    vm.ShowElementProperties();
                    e.Handled = true;
                    return;
                }
            }

            if (vm.SelectedTool == LayoutTool.Place)
            {
                // Ak vkladáme rovnú koľaj, začneme drag-kreslenie
                if (vm.SelectedMarkerKey == "TrackSegment")
                {
                    _isDraggingTrack = true;
                    _dragStartCellX = cellX;
                    _dragStartCellY = cellY;
                }
                else
                {
                    // Pre ostatné prvky použijeme štandardné vkladanie
                    vm.PlaceElementAt(pos.X, pos.Y);
                }
            }
            else // Select režim
            {
                // Najprv zistíme, či klikáme na existujúci prvok
                var hitEl = FindElementAt(pos.X, pos.Y);
                
                // DVOJKLIK na klikateľný prvok - otvorí vlastnosti
                if (hitEl != null && IsClickableElement(hitEl) && e.ClickCount == 2)
                {
                    vm.Selection.ClearSelection();
                    vm.SelectedElement = hitEl;
                    vm.Selection.AddToSelection(hitEl);
                    vm.ShowElementProperties();
                    e.Handled = true;
                    return;
                }
                
                // Kontrola či klikáme na existujúci výber - začneme drag
                if (vm.Selection.SelectionCount > 0 && IsPointerOverSelection(pos.X, pos.Y))
                {
                    vm.CaptureUndoCheckpoint("drag-move", force: true);
                    _isDraggingSelection = true;
                    _dragSelectionStart = pos;
                    
                    // Uložíme si originálne pozície VŠETKÝCH vybratých prvkov
                    _originalPositions.Clear();
                    foreach (var el in vm.Selection.SelectedElements)
                    {
                        _originalPositions[el] = (el.X, el.Y);
                    }
                }
                else if (hitEl != null)
                {
                    // Ctrl+Click = toggle výber prvku
                    if (keyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        vm.Selection.ToggleSelection(hitEl);
                        if (vm.Selection.IsSelected(hitEl))
                            vm.SelectedElement = hitEl;
                        else if (vm.SelectedElement == hitEl)
                            vm.SelectedElement = null;
                        RebuildElementsLayer();
                    }
                    else
                    {
                        // Normálny klik - vyber len tento prvok
                        vm.Selection.ClearSelection();
                        vm.SelectedElement = hitEl;
                        vm.Selection.AddToSelection(hitEl);
                    }
                }
                else
                {
                    // Klik na prázdne miesto - začni marquee select
                    if (!keyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        vm.Selection.ClearSelection();
                        vm.SelectedElement = null;
                    }
                    
                    _marqueeBehavior?.StartMarquee(pos, vm);
                }
            }
        }
    }

    /// <summary>Nájde prvok na pozícii (x,y), ak existuje. Bloky majú prioritu.</summary>
    private LayoutElement? FindElementAt(double x, double y)
    {
        if (_vm == null) return null;
        
        // Najprv hľadáme bloky (majú prioritu pred koľajami)
        // Hľadáme OD KONCA - lebo bloky sa vykreslujú v 2. prechode, posledný je navrchu
        for (int i = _vm.Elements.Count - 1; i >= 0; i--)
        {
            var el = _vm.Elements[i];
            if (el.MarkerKey != "Block") continue;
            
            int blockLengthCells = 4;
            if (el is BlockElement blockEl)
            {
                blockLengthCells = blockEl.BlockLengthCells;
                if (blockLengthCells < 1) blockLengthCells = 1;
                if (blockLengthCells > 20) blockLengthCells = 20;
            }
            
            double w = el.Rotation == 0 || el.Rotation == 180 ? Cell * blockLengthCells : Cell;
            double h = el.Rotation == 90 || el.Rotation == 270 ? Cell * blockLengthCells : Cell;
            if (x >= el.X && x < el.X + w && y >= el.Y && y < el.Y + h)
                return el;
        }
        
        // Potom hľadáme ostatné prvky (od konca - najvrchnejší ako prvý)
        for (int i = _vm.Elements.Count - 1; i >= 0; i--)
        {
            var el = _vm.Elements[i];
            if (el.MarkerKey == "Block") continue;

            var (w, h) = GetElementFootprint(el);

            if (x >= el.X && x < el.X + w && y >= el.Y && y < el.Y + h)
                return el;
        }
        
        return null;
    }
    
    /// <summary>Skontroluje či je myš nad selection bounding boxom.</summary>
    private bool IsPointerOverSelection(double x, double y)
    {
        if (_vm == null || _vm.Selection.SelectionCount == 0) return false;
        
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        
        foreach (var el in _vm.Selection.SelectedElements)
        {
            var (elemWidth, elemHeight) = GetElementFootprint(el);
            
            minX = Math.Min(minX, el.X);
            minY = Math.Min(minY, el.Y);
            maxX = Math.Max(maxX, el.X + elemWidth);
            maxY = Math.Max(maxY, el.Y + elemHeight);
        }
        
        const double padding = 2;
        return x >= minX - padding && x <= maxX + padding &&
               y >= minY - padding && y <= maxY + padding;
    }
    
    /// <summary>Určí, či je prvok klikateľný (má vlastnosti - výhybky, bloky, signály, senzory).</summary>
    // 🔧 Resize bloku ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━#

    /// <summary>
    /// Zistí, či je kurzor pri okraji ľubovoľného bloku.
    /// Resize zóna = posledných 6px vnútri bloku pri ľavom/pravom (alebo hornom/dolnom) okraji.
    /// </summary>
    private (string direction, BlockElement? block) GetResizeDirection(double x, double y)
    {
        if (_vm == null) return (string.Empty, null);
        const double edgeZone = 6;

        foreach (var el in _vm.Elements)
        {
            if (el.MarkerKey != "Block" || el is not BlockElement blockEl) continue;

            int len = blockEl.BlockLengthCells;
            if (len < 1) len = 1;
            if (len > 20) len = 20;

            bool isVertical = el.Rotation == 90 || el.Rotation == 270;
            double bw = isVertical ? Cell : Cell * len;
            double bh = isVertical ? Cell * len : Cell;

            // Kurzor musí byť vnútri bloku
            if (x < el.X || x > el.X + bw || y < el.Y || y > el.Y + bh) continue;

            if (!isVertical)
            {
                if (x <= el.X + edgeZone)           return ("left",   blockEl);
                if (x >= el.X + bw - edgeZone)      return ("right",  blockEl);
            }
            else
            {
                if (y <= el.Y + edgeZone)            return ("top",    blockEl);
                if (y >= el.Y + bh - edgeZone)      return ("bottom", blockEl);
            }
        }
        return (string.Empty, null);
    }

    /// <summary>Spustí resize operáciu - uloží počiatočný stav.</summary>
    private void StartBlockResize(BlockElement block, string direction)
    {
        _vm?.CaptureUndoCheckpoint("resize-block", force: true);
        _isResizingBlock     = true;
        _resizingBlock       = block;
        _resizeDirection     = direction;
        _originalBlockLength = block.BlockLengthCells;
        _originalBlockX      = block.X;
        _originalBlockY      = block.Y;
    }

    /// <summary>Aktualizuje dĺžku bloku počas ťahania myšou.</summary>
    private void HandleBlockResize(double mouseX, double mouseY)
    {
        if (_resizingBlock == null) return;

        bool isVertical = _resizingBlock.Rotation == 90 || _resizingBlock.Rotation == 270;
        int newLen = _originalBlockLength;
        double newX = _originalBlockX;
        double newY = _originalBlockY;

        if (!isVertical)
        {
            if (_resizeDirection == "right")
            {
                double delta = mouseX - _originalBlockX;
                newLen = Math.Max(1, Math.Min(20, (int)Math.Round(delta / Cell)));
            }
            else if (_resizeDirection == "left")
            {
                double delta = _originalBlockX - mouseX;
                int extra = (int)Math.Round(delta / Cell);
                newLen = Math.Max(1, Math.Min(20, _originalBlockLength + extra));
                newX = _originalBlockX - (newLen - _originalBlockLength) * Cell;
            }
        }
        else
        {
            if (_resizeDirection == "bottom")
            {
                double delta = mouseY - _originalBlockY;
                newLen = Math.Max(1, Math.Min(20, (int)Math.Round(delta / Cell)));
            }
            else if (_resizeDirection == "top")
            {
                double delta = _originalBlockY - mouseY;
                int extra = (int)Math.Round(delta / Cell);
                newLen = Math.Max(1, Math.Min(20, _originalBlockLength + extra));
                newY = _originalBlockY - (newLen - _originalBlockLength) * Cell;
            }
        }

        if (newLen != _resizingBlock.BlockLengthCells || newX != _resizingBlock.X || newY != _resizingBlock.Y)
        {
            _resizingBlock.BlockLengthCells = newLen;
            _resizingBlock.X = newX;
            _resizingBlock.Y = newY;
            RebuildElementsLayer();
        }
    }

    /// <summary>Ukončí resize operáciu, snapne na grid, uloží.</summary>
    private void EndBlockResize()
    {
        if (_resizingBlock != null && _vm != null)
        {
            _resizingBlock.X = Math.Floor(_resizingBlock.X / Cell) * Cell;
            _resizingBlock.Y = Math.Floor(_resizingBlock.Y / Cell) * Cell;
            _vm.SyncToProject();
            _vm.NotifyInspector(); // Aktualizácia Inspektora po zmene veľkosti/pozície
            RebuildElementsLayer();
        }
        _isResizingBlock = false;
        _resizingBlock   = null;
        _resizeDirection = string.Empty;
        this.Cursor      = Cursor.Default;
    }

    // 🔧 Resize textu (nezávislé od Block resize) ━━━━━━━━━━━━━━━━━━━━━━━━━━
    private const int MaxTextCells = 40;

    /// <summary>Detekuje resize zónu (6px) na okraji niektorého TextElementu.</summary>
    private (string direction, TextElement? text) GetTextResizeDirection(double x, double y)
    {
        if (_vm == null) return (string.Empty, null);
        const double edgeZone = 6;

        for (int i = _vm.Elements.Count - 1; i >= 0; i--)
        {
            if (_vm.Elements[i] is not TextElement t) continue;
            double w = t.WidthInCells  * Cell;
            double h = t.HeightInCells * Cell;
            if (x < t.X || x > t.X + w || y < t.Y || y > t.Y + h) continue;

            // Priorita: rohové by sa hodili, ale zatiaľ stačia 4 hrany
            if (x <= t.X + edgeZone)        return ("left",   t);
            if (x >= t.X + w - edgeZone)    return ("right",  t);
            if (y <= t.Y + edgeZone)        return ("top",    t);
            if (y >= t.Y + h - edgeZone)    return ("bottom", t);
        }
        return (string.Empty, null);
    }

    private void StartTextResize(TextElement t, string direction)
    {
        _vm?.CaptureUndoCheckpoint("resize-text", force: true);
        _isResizingText      = true;
        _resizingText        = t;
        _textResizeDir       = direction;
        _originalTextWCells  = t.WidthInCells;
        _originalTextHCells  = t.HeightInCells;
        _originalTextX       = t.X;
        _originalTextY       = t.Y;
    }

    private void HandleTextResize(double mouseX, double mouseY)
    {
        if (_resizingText == null) return;

        int newW = _originalTextWCells;
        int newH = _originalTextHCells;
        double newX = _originalTextX;
        double newY = _originalTextY;

        if (_textResizeDir == "right")
        {
            double delta = mouseX - _originalTextX;
            newW = Math.Max(1, Math.Min(MaxTextCells, (int)Math.Round(delta / Cell)));
        }
        else if (_textResizeDir == "left")
        {
            double delta = _originalTextX - mouseX;
            int extra = (int)Math.Round(delta / Cell);
            newW = Math.Max(1, Math.Min(MaxTextCells, _originalTextWCells + extra));
            newX = _originalTextX - (newW - _originalTextWCells) * Cell;
        }
        else if (_textResizeDir == "bottom")
        {
            double delta = mouseY - _originalTextY;
            newH = Math.Max(1, Math.Min(MaxTextCells, (int)Math.Round(delta / Cell)));
        }
        else if (_textResizeDir == "top")
        {
            double delta = _originalTextY - mouseY;
            int extra = (int)Math.Round(delta / Cell);
            newH = Math.Max(1, Math.Min(MaxTextCells, _originalTextHCells + extra));
            newY = _originalTextY - (newH - _originalTextHCells) * Cell;
        }

        if (newW != _resizingText.WidthInCells
            || newH != _resizingText.HeightInCells
            || newX != _resizingText.X
            || newY != _resizingText.Y)
        {
            _resizingText.WidthInCells  = newW;
            _resizingText.HeightInCells = newH;
            _resizingText.X = newX;
            _resizingText.Y = newY;
            RebuildElementsLayer();
        }
    }

    private void EndTextResize()
    {
        if (_resizingText != null && _vm != null)
        {
            _resizingText.X = Math.Floor(_resizingText.X / Cell) * Cell;
            _resizingText.Y = Math.Floor(_resizingText.Y / Cell) * Cell;
            _vm.SyncToProject();
            _vm.NotifyInspector();
            RebuildElementsLayer();
        }
        _isResizingText  = false;
        _resizingText    = null;
        _textResizeDir   = string.Empty;
        this.Cursor      = Cursor.Default;
    }

    private static bool IsClickableElement(LayoutElement element)
    {
        return element.MarkerKey switch
        {
            "Block" => true,
            "Turnout_L" or "Turnout_R" or "TurnoutL90" or "TurnoutR90"
                or "TurnoutCurve_L" or "TurnoutCurve_R" 
                or "Turnout_Y" or "Turnout_3W" or "DoubleSlip" => true,
            "Signal" or "Signal5" or "Signal4" or "Signal2Main" or "Signal2Shunt" or "Signal2Route" or "Signal3Entry" => true,
            "Sensor" => true,
            "Route" => true,
            "Text" => true,
            _ => false
        };
    }

    /// <summary>Nájde Block na pozícii (x,y), ak existuje. (Legacy metóda)</summary>
    private LayoutElement? FindBlockAt(double x, double y)
    {
        if (_vm == null) return null;
        foreach (var el in _vm.Elements)
        {
            if (el.MarkerKey != "Block") continue;
            double w = el.Rotation == 0 ? Cell * 4 : Cell;
            double h = el.Rotation == 0 ? Cell      : Cell * 4;
            if (x >= el.X && x < el.X + w && y >= el.Y && y < el.Y + h)
                return el;
        }
        return null;
    }

    /// <summary>Zobrazí kontextové menu pre ľubovoľný prvok.</summary>
    private void ShowElementContextMenu(LayoutElement element, LayoutEditorViewModel vm, PointerPressedEventArgs e)
    {
        // Ochrana pred opätovným otvorením počas toho, ako je menu ešte zobrazené
        if (_openContextMenu?.IsOpen == true) return;

        static MenuItem MakeItem(string header, string iconPath, System.Windows.Input.ICommand? cmd = null)
        {
            var item = new MenuItem { Header = header };
            if (cmd != null) item.Command = cmd;

            // Ikona z Assets/Appicons/16
            try
            {
                var uri = new Uri($"avares://TrackFlow/Assets/Appicons/16/{iconPath}");
                var bmp = new Bitmap(Avalonia.Platform.AssetLoader.Open(uri));
                item.Icon = new Image { Source = bmp, Width = 16, Height = 16 };
            }
            catch { /* ikona nenájdená - nezobrazíme ju */ }

            return item;
        }

        // Určíme, či má prvok vlastnosti
        bool hasProperties = element.MarkerKey switch
        {
            "Block" => true,
            "Turnout_L" or "Turnout_R" or "TurnoutL90" or "TurnoutR90"
                or "TurnoutCurve_L" or "TurnoutCurve_R" 
                or "Turnout_Y" or "Turnout_3W" or "DoubleSlip" => true,
            "Signal" or "Signal5" or "Signal4" or "Signal2Main" or "Signal2Shunt" or "Signal2Route" or "Signal3Entry" => true,
            "Sensor" => true,
            "Route" => true,
            "Text" => true,
            _ => false
        };
        
        // DEBUG: Vypíšeme typ elementu a MarkerKey
        System.Diagnostics.Debug.WriteLine($"[ShowElementContextMenu] Element: Type={element.GetType().Name}, MarkerKey={element.MarkerKey}, hasProperties={hasProperties}");


        var menu = new ContextMenu();
        
        // Použijeme multi-select príkazy ak je vybratých viac prvkov
        if (vm.Selection.SelectionCount > 1)
        {
            menu.Items.Add(MakeItem($"Kopírovať ({vm.SelectedCount})", "copy.png", vm.CopySelectedElementsCommand));
            menu.Items.Add(MakeItem($"Vystrihnúť ({vm.SelectedCount})", "cut.png", vm.CutSelectedElementsCommand));
            menu.Items.Add(MakeItem("Vložiť", "paste.png", vm.PasteSelectedElementsCommand));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeItem($"Vymazať ({vm.SelectedCount})", "delete.png", vm.DeleteSelectedElementsCommand));
            
            // Vlastnosti - disabled pre multi-select (nemá zmysel editovať viacero naraz)
            var propsItem = MakeItem("Vlastnosti", "prop.png");
            propsItem.IsEnabled = false;
            menu.Items.Add(propsItem);
        }
        else
        {
            menu.Items.Add(MakeItem("Kopírovať", "copy.png", vm.CopyElementCommand));
            menu.Items.Add(MakeItem("Vystrihnúť", "cut.png", vm.CutElementCommand));
            menu.Items.Add(MakeItem("Vložiť", "paste.png", vm.PasteElementCommand));
            menu.Items.Add(MakeItem("Vymazať", "delete.png", vm.DeleteSelectedCommand));
            menu.Items.Add(new Separator());
            
            // Vlastnosti - enabled len ak je to Block alebo výhybka
            var propsItem = MakeItem(vm.PropertiesMenuText, "prop.png", vm.ShowElementPropertiesCommand);
            propsItem.IsEnabled = hasProperties;
            menu.Items.Add(propsItem);

            // Odstraniť lokomotívu — len pre blok s priradenou lokomotívou
            if (element is TrackFlow.Models.Layout.BlockElement blockEl
                && !string.IsNullOrWhiteSpace(blockEl.AssignedLocoId))
            {
                menu.Items.Add(new Separator());
                var removeLocoItem = MakeItem("Odstraniť lokomotívu", "delete.png");
                removeLocoItem.Click += (_, _) =>
                {
                    blockEl.AssignedLocoId = null;
                    blockEl.AssignedLocoIsForward = true;
                    blockEl.IsOccupied = false;
                    vm.RequestBlockRepaint?.Invoke(blockEl);
                    vm.CaptureUndoCheckpoint("Odstraniť lokomotívu");
                };
                menu.Items.Add(removeLocoItem);
            }
            menu.Items.Add(new Separator());
        }

        _openContextMenu = menu;
        
        // Explicitne nastavíme default kurzor na menu aj na tento control
        menu.Cursor = Cursor.Default;
        var originalCursor = this.Cursor;
        this.Cursor = Cursor.Default;
        
        // Pri zatvorení menu obnovíme kurzor
        menu.Closed += (_, _) =>
        {
            _openContextMenu = null;
            this.Cursor = originalCursor;
        };

        // Otvoríme menu na pozícii kurzora v rámci LayoutEditorView
        menu.Open(this);
    }

    /// <summary>Zobrazí kontextové menu pre Block marker. (Legacy metóda)</summary>
    private void ShowBlockContextMenu(LayoutElement block, LayoutEditorViewModel vm, PointerPressedEventArgs e)
    {
        ShowElementContextMenu(block, vm, e);
    }

    /// <summary>Zobrazí kontextové menu pre prázdnu bunku (iba Vložiť).</summary>
    private void ShowEmptyCellContextMenu(double x, double y, LayoutEditorViewModel vm)
    {
        // Ak nie je nič v clipboarde, nezobrazuj menu vôbec
        if (!vm.CanPaste)
            return;

        // Ochrana pred opätovným otvorením počas toho, ako je menu ešte zobrazené
        if (_openContextMenu?.IsOpen == true) return;

        MenuItem MakeItem(string header, string iconPath, System.Action? onClick = null)
        {
            var item = new MenuItem { Header = header };
            if (onClick != null)
                item.Click += (_, _) => onClick();

            try
            {
                var uri = new Uri($"avares://TrackFlow/Assets/Appicons/16/{iconPath}");
                var bmp = new Bitmap(Avalonia.Platform.AssetLoader.Open(uri));
                item.Icon = new Image { Source = bmp, Width = 16, Height = 16 };
            }
            catch { /* ikona nenájdená */ }

            return item;
        }

        var pasteItem = MakeItem("Vložiť", "paste.png", () => vm.PasteElementAt(x, y));
        
        var menu = new ContextMenu
        {
            Items = { pasteItem }
        };

        _openContextMenu = menu;
        
        // Explicitne nastavíme default kurzor
        menu.Cursor = Cursor.Default;
        var originalCursor = this.Cursor;
        this.Cursor = Cursor.Default;
        
        // Pri zatvorení menu obnovíme kurzor
        menu.Closed += (_, _) =>
        {
            _openContextMenu = null;
            this.Cursor = originalCursor;
        };

        menu.Open(this);
    }

    private async void OnBlockPropertiesRequested(BlockElement block)
    {
        try
        {
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow == null) return;

            var availableSignals = _vm?.Elements.OfType<SignalElement>().ToList() ?? new List<SignalElement>();
            var dialogVm = new BlockPropertiesViewModel(block, availableSignals)
            {
                SettingsManager = _vm?.SettingsManager
            };
            var dialog   = new BlockPropertiesWindow { DataContext = dialogVm };

            var saved = await dialog.ShowDialog<bool>(parentWindow);
            if (saved)
                _vm?.OnBlockPropertiesEdited();

            RebuildElementsLayer();
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LayoutEditorView.OnBlockPropertiesRequested", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Editor",
                $"⚠️ Otvorenie vlastností bloku zlyhalo: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);
        }
    }

    private async void OnTurnoutPropertiesRequested(TurnoutElement turnout)
    {
        
        // Zskame parent window - pouijeme TopLevel alebo hlavn okno aplikcie ako fallback
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        
        // Ak TopLevel nefunguje alebo nem PlatformImpl, sksime hlavn okno aplikcie
        if (parentWindow == null || parentWindow.PlatformImpl == null)
        {
            
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                parentWindow = desktop.MainWindow;
            }
        }
        
        if (parentWindow == null)
        {
            return;
        }


        // Získame SettingsManager z ViewModel
        var settingsManager = _vm?.SettingsManager;
        
        var dialogVm = new TurnoutPropertiesViewModel(turnout, _vm, settingsManager);
        var dialog   = new TurnoutPropertiesWindow { DataContext = dialogVm };

        bool saved = false;
        try
        {
            saved = await dialog.ShowDialog<bool>(parentWindow);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LayoutEditorView.OnTurnoutPropertiesRequested", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Editor",
                $"⚠️ Otvorenie vlastností výhybky zlyhalo: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);
        }

        if (saved)
        {
            // Refresh layout
            RebuildElementsLayer();
        }
    }

    private async void OnSignalPropertiesRequested(SignalElement signal)
    {
        var parentWindow = TopLevel.GetTopLevel(this) as Window;

        if (parentWindow == null || parentWindow.PlatformImpl == null)
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                parentWindow = desktop.MainWindow;
            }
        }

        if (parentWindow == null)
            return;

        var blocks = _vm?.Elements.OfType<BlockElement>().ToList() ?? new List<BlockElement>();
        var signalSystems = _vm?.SettingsManager?.CurrentProject?.Layout?.SignalSystems
                            ?? new System.Collections.Generic.List<TrackFlow.Models.Layout.SignalSystemDefinition>();
        var dialogVm = new SignalPropertiesViewModel(signal, blocks, signalSystems);
        var dialog = new SignalPropertiesWindow { DataContext = dialogVm };

        bool saved = false;
        try
        {
            saved = await dialog.ShowDialog<bool>(parentWindow);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LayoutEditorView.OnSignalPropertiesRequested", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Editor",
                $"⚠️ Otvorenie vlastností návestidla zlyhalo: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);
            return;
        }

        if (saved)
        {
            _vm?.SyncToProject();
            RebuildElementsLayer();
        }
    }

    private void OnSensorPropertiesRequested(SensorElement sensor)
    {
        // TODO: Implementovať dialóg vlastností senzora
    }

    private async void OnRoutePropertiesRequested(RouteElement route)
    {
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        
        if (parentWindow == null || parentWindow.PlatformImpl == null)
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                parentWindow = desktop.MainWindow;
            }
        }
        
        if (parentWindow == null)
        {
            return;
        }

        var settingsManager = _vm?.SettingsManager;
        var dialogVm = new RouteEditorViewModel(_vm, settingsManager);
        dialogVm.LoadFromElement(route);
        
        var dialog = new RouteEditorWindow { DataContext = dialogVm };

        bool saved = false;
        try
        {
            saved = await dialog.ShowDialog<bool>(parentWindow);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LayoutEditorView.OnRoutePropertiesRequested", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Editor",
                $"⚠️ Otvorenie vlastností cesty zlyhalo: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);
        }

        if (saved)
        {
            // RouteEditorViewModel.OnSave už zapísal model cez LoadFromElement/SaveToElement flow.
            _vm?.SyncToProject();
            RebuildElementsLayer();
        }
    }

    private async void OnTextPropertiesRequested(TextElement textElement)
    {
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow == null) return;

        var dialogVm = new TrackFlow.ViewModels.Editor.TextPropertiesViewModel();
        dialogVm.LoadFromElement(textElement);
        
        var dialog = new TrackFlow.Views.Editor.TextPropertiesWindow { DataContext = dialogVm };

        bool saved = false;
        try
        {
            saved = await dialog.ShowDialog<bool>(parentWindow);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LayoutEditorView.OnTextPropertiesRequested", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose(
                "Editor",
                $"⚠️ Otvorenie vlastností textu zlyhalo: {ex.GetType().Name}: {ex.Message}",
                DiagnosticLevel.Warning);
        }

        if (saved && dialogVm.DialogResult)
        {
            // Uložíme hodnoty späť do TextElement
            dialogVm.SaveToElement(textElement);
            
            _vm?.SyncToProject();
            RebuildElementsLayer();
        }
    }
    
  // 🔧 Drag-kreslenie rovnej koľaje ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    
    /// <summary>Zarovná koncový bod na najbližší povolený uhol (0°, 45°, 90°, 135°).</summary>
    private (int cellX, int cellY) SnapToAllowedAngle(int startX, int startY, int endX, int endY)
    {
        int dx = endX - startX;
        int dy = endY - startY;
        
        // Ak je to rovnaký bod, vrátime bez zmeny
        if (dx == 0 && dy == 0) return (endX, endY);
        
        // Vypočítame vzdialenosť
        double distance = Math.Sqrt(dx * dx + dy * dy);
        
        // Vypočítame uhol v radiánoch
        double angle = Math.Atan2(dy, dx);
        
        // zaokrúhlime na najbližší povolený uhol (0°, 45°, 90°, 135°, 180°, -45°, -90°, -135°)
        // V radiánoch: 0, π/4, π/2, 3π/4, π, -π/4, -π/2, -3π/4
        double[] allowedAngles = { 0, Math.PI / 4, Math.PI / 2, 3 * Math.PI / 4, Math.PI, -Math.PI / 4, -Math.PI / 2, -3 * Math.PI / 4 };
        
        // Nájdeme najbližší povolený uhol
        double closestAngle = allowedAngles[0];
        double minDiff = Math.Abs(angle - closestAngle);
        
        foreach (var allowedAngle in allowedAngles)
        {
            double diff = Math.Abs(angle - allowedAngle);
            if (diff < minDiff)
            {
                minDiff = diff;
                closestAngle = allowedAngle;
            }
        }
        
        // Vypočítame nový koncový bod podľa zarovnaného uhla
        int newEndX = startX + (int)Math.Round(distance * Math.Cos(closestAngle));
        int newEndY = startY + (int)Math.Round(distance * Math.Sin(closestAngle));
        
        return (newEndX, newEndY);
    }
    
    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // 🔧 Ukončenie resize textu ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        if (_isResizingText && _vm != null)
        {
            EndTextResize();
            return;
        }

        // 🔧 Ukončenie resize bloku ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        if (_isResizingBlock && _vm != null)
        {
            EndBlockResize();
            return;
        }

        // Ukončenie drag výberu (multi-select move)
        if (_isDraggingSelection && _vm != null)
        {
            // Snap všetky prvky na mriežku
            foreach (var el in _vm.Selection.SelectedElements)
            {
                el.X = Math.Floor(el.X / Cell) * Cell;
                el.Y = Math.Floor(el.Y / Cell) * Cell;
            }

            // Bezpečnostná kontrola: presun nesmie vytvoriť nelegálne prekrytia.
            try
            {
                var tmp = new TrackLayout { Elements = _vm.Elements.ToList() };
                var overlaps = LayoutOverlapIntegrityService.FindIllegalOverlaps(tmp, Cell);
                if (overlaps.Any(o => o.Elements.Any(e => _vm.Selection.IsSelected(e))))
                {
                    // Revert na originálne pozície.
                    foreach (var el in _vm.Selection.SelectedElements)
                    {
                        if (_originalPositions.TryGetValue(el, out var p))
                        {
                            el.X = p.X;
                            el.Y = p.Y;
                        }
                    }

                    RebuildElementsLayer();
                    _isDraggingSelection = false;
                    return;
                }
            }
            catch
            {
                // Best-effort: ak by kontrola zlyhala, presun necháme prebehnúť.
            }
            
            _isDraggingSelection = false;
            _vm.SyncToProject();
            _vm.NotifyInspector(); // Aktualizácia Inspektora po zmene pozície
            RebuildElementsLayer();
            return;
        }
        
        // Ukončenie marquee výberu
        if (_marqueeBehavior?.IsMarqueeDragging == true && _vm != null)
        {
            var keyModifiers = e.KeyModifiers;
            _marqueeBehavior.EndMarquee(_vm, keyModifiers.HasFlag(KeyModifiers.Control));
            RebuildElementsLayer();
            return;
        }
        
        // Ukončenie drag-kreslenia koľají
        if (!_isDraggingTrack) return;
        
        var pos   = e.GetPosition(this.FindControl<Canvas>("LayoutCanvas"));
        var cellX = (int)Math.Floor(pos.X / Cell);
        var cellY = (int)Math.Floor(pos.Y / Cell);
        
        // Zarovnáme koncový bod na povolený uhol
        var (snappedX, snappedY) = SnapToAllowedAngle(_dragStartCellX, _dragStartCellY, cellX, cellY);
        
        // Vyčistíme ghost layer
        ClearGhostLayer();
        
        // Vytvoríme všetky TrackSegment prvky medzi začiatkom a koncom
        if (_vm != null)
        {
            _vm.CaptureUndoCheckpoint("draw-track", force: true);
            CreateTrackLine(_dragStartCellX, _dragStartCellY, snappedX, snappedY);
        }
        
        // Resetujeme stav
        _isDraggingTrack = false;
        _dragStartCellX = -1;
        _dragStartCellY = -1;
    }
    
    /// <summary>Vykreslí ghost náhľad koľajnice počas ťahania.</summary>
    private void DrawGhostTrack(int startX, int startY, int endX, int endY)
    {
        var ghostLayer = this.FindControl<Canvas>("GhostLayer");
        if (ghostLayer == null) return;
        
        ghostLayer.Children.Clear();
        
        // Zarovnáme koncový bod na povolený uhol
        var (snappedEndX, snappedEndY) = SnapToAllowedAngle(startX, startY, endX, endY);
        
        // Nakreslíme tenkú čiaru od začiatku po koniec (so zarovnaným koncovým bodom)
        double x1 = startX * Cell + Cell / 2;
        double y1 = startY * Cell + Cell / 2;
        double x2 = snappedEndX * Cell + Cell / 2;
        double y2 = snappedEndY * Cell + Cell / 2;
        
        var line = new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = new SolidColorBrush(Color.FromArgb(200, 33, 150, 243)), // Modrá s alpha
            StrokeThickness = 2,
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 2 }, // Prerušovaná čiara
            IsHitTestVisible = false
        };
        
        ghostLayer.Children.Add(line);
    }
    
    /// <summary>Vyčistí ghost layer.</summary>
    private void ClearGhostLayer()
    {
        var ghostLayer = this.FindControl<Canvas>("GhostLayer");
        if (ghostLayer != null)
        {
            ghostLayer.Children.Clear();
        }
    }
    
/// <summary>Skontroluje, či na danej bunke (cellX, cellY) existuje nejaký marker.</summary>
    private bool HasMarkerAtCell(int cellX, int cellY)
    {
        if (_vm == null) return false;

        double x = cellX * Cell;
        double y = cellY * Cell;
        const double tolerance = 0.01;
        
        foreach (var el in _vm.Elements)
        {
            // Tolerancia je dôležitá: historické uložené hodnoty môžu byť „takmer na mriežke"
            // (napr. 48.00000000002) a bez tolerancie by sa dalo vytvoriť prekrytie.
            if (LayoutElementFootprintHelper.IsPointInside(el, x, y, Cell, compactTwoAspectSignals: true, tolerance: tolerance))
                return true;
        }
        
        return false;
    }
    
    /// <summary>Zistí, či na danej bunke je Bumper a aký je uhol vedľajšej koľaje.</summary>
    private (bool hasBumper, LayoutElement? bumper, int adjacentTrackAngle, int directionToAdjacent) GetBumperAndAdjacentTrackAngle(int cellX, int cellY)
    {
        if (_vm == null) return (false, null, 0, 0);
        
        double x = cellX * Cell;
        double y = cellY * Cell;
        const double tolerance = 0.01;
        
        // Najprv nájdeme bumper na tejto pozícii
        LayoutElement? bumper = null;
        foreach (var el in _vm.Elements)
        {
            if (el.MarkerKey == "Bumper" && 
                Math.Abs(el.X - x) < tolerance && 
                Math.Abs(el.Y - y) < tolerance)
            {
                bumper = el;
                break;
            }
        }
        
        if (bumper == null) return (false, null, 0, 0);
        
        // Teraz hľadáme vedľajšiu koľaj (TrackSegment) v susedných bunkách
        int[] dx = { -1, 1, 0, 0, -1, 1, -1, 1 }; // 8 smerov
        int[] dy = { 0, 0, -1, 1, -1, -1, 1, 1 };
        
        for (int i = 0; i < 8; i++)
        {
            int adjX = cellX + dx[i];
            int adjY = cellY + dy[i];
            double adjPosX = adjX * Cell;
            double adjPosY = adjY * Cell;
            
            foreach (var el in _vm.Elements)
            {
                if (el.MarkerKey == "TrackSegment" &&
                    Math.Abs(el.X - adjPosX) < tolerance && 
                    Math.Abs(el.Y - adjPosY) < tolerance)
                {
                    // Smer od bumpera k susednej koľaji
                    double angleRad = Math.Atan2(dy[i], dx[i]);
                    double angleDeg = angleRad * 180.0 / Math.PI;
                    if (angleDeg < 0) angleDeg += 360;
                    int dirToAdj = ((int)Math.Round(angleDeg / 45.0)) * 45;
                    
                    return (true, bumper, (int)Math.Round(el.Rotation), dirToAdj);
                }
            }
        }
        
        return (true, bumper, 0, 0);
    }

    /// <summary>
    /// Určí pozíciu bumpera relatívne k susednej koľaji.
    /// Vracia "HB" (horný bumper), "DB" (dolný bumper), "PB" (pravý bumper), "LB" (ľavý bumper).
    /// PB = pravá strana Z POHĽADU smeru koľaje, LB = ľavá strana Z POHĽADU smeru koľaje
    /// </summary>
    private string GetBumperPosition(int bumperCellX, int bumperCellY, int adjacentCellX, int adjacentCellY, int adjacentAngle)
    {
        // Určíme smer od susednej koľaje k bumperu
        int dx = bumperCellX - adjacentCellX;
        int dy = bumperCellY - adjacentCellY;
        
        // Pre vodorovnú koľaj smerujúcu DOPRAVA (0°): → 
        // PB (pravý bumper) = HORE (dy < 0), LB (ľavý bumper) = DOLE (dy > 0) - INVERTOVANÉ
        if (adjacentAngle == 0)
        {
            if (dy < 0) return "PB";  // Bumper je nad koľajou = pravá strana
            if (dy > 0) return "LB";  // Bumper je pod koľajou = ľavá strana
            if (dx > 0) return "LB";  // Bumper je vpravo (za koľajou v smere) = ľavá strana
            if (dx < 0) return "PB";  // Bumper je vľavo (pred koľajou) = pravá strana
        }
        // Pre vodorovnú koľaj smerujúcu DOĽAVA (180°): ←
        // PB (pravý bumper) = DOLE (dy > 0), LB (ľavý bumper) = HORE (dy < 0) - INVERTOVANÉ
        else if (adjacentAngle == 180)
        {
            if (dy < 0) return "LB";  // Bumper je nad koľajou = ľavá strana
            if (dy > 0) return "PB";  // Bumper je pod koľajou = pravá strana
            if (dx > 0) return "PB";  // Bumper je vpravo (pred koľajou) = pravá strana
            if (dx < 0) return "LB";  // Bumper je vľavo (za koľajou) = ľavá strana
        }
        // Pre zvislú koľaj smerujúcu NAHOR (90°): ↑
        // PB (pravý bumper) = VĽAVO (dx < 0), LB (ľavý bumper) = VPRAVO (dx > 0) - INVERTOVANÉ
        // HB a DB INVERTOVANÉ - horný má byť dole, spodný má byť hore
        else if (adjacentAngle == 90)
        {
            if (dy < 0) return "DB";  // Bumper je nad koľajou (za koľajou v smere) = dolný bumper
            if (dy > 0) return "HB";  // Bumper je pod koľajou (pred koľajou) = horný bumper
            if (dx > 0) return "LB";  // Bumper je vpravo = ľavá strana
            if (dx < 0) return "PB";  // Bumper je vľavo = pravá strana
        }
        // Pre zvislú koľaj smerujúcu NADOL (270°): ↓
        // PB (pravý bumper) = VPRAVO (dx > 0), LB (ľavý bumper) = VĽAVO (dx < 0) - INVERTOVANÉ
        // HB a DB INVERTOVANÉ - horný má byť dole, spodný má byť hore
        else if (adjacentAngle == 270)
        {
            if (dy < 0) return "HB";  // Bumper je nad koľajou (pred koľajou) = horný bumper
            if (dy > 0) return "DB";  // Bumper je pod koľajou (za koľajou v smere) = dolný bumper
            if (dx > 0) return "PB";  // Bumper je vpravo = pravá strana
            if (dx < 0) return "LB";  // Bumper je vľavo = ľavá strana
        }
        // Pre šikmú koľaj smerujúcu VPRAVO HORE (45°): ↗
        // Pri šikmých koľajach používame len HB/DB (horný/dolný bumper)
        else if (adjacentAngle == 45)
        {
            if (dy < 0 || dx < 0) return "HB";  // Bumper je hore alebo vľavo = horný bumper
            if (dy > 0 || dx > 0) return "DB";  // Bumper je dole alebo vpravo = dolný bumper
        }
        // Pre šikmú koľaj smerujúcu VĽAVO HORE (135°): ↖
        else if (adjacentAngle == 135)
        {
            if (dy < 0 || dx > 0) return "HB";  // Bumper je hore alebo vpravo = horný bumper
            if (dy > 0 || dx < 0) return "DB";  // Bumper je dole alebo vľavo = dolný bumper
        }
        // Pre šikmú koľaj smerujúcu VĽAVO DOLE (225°): ↙
        else if (adjacentAngle == 225)
        {
            if (dy > 0 || dx > 0) return "HB";  // Bumper je dole alebo vpravo = horný bumper
            if (dy < 0 || dx < 0) return "DB";  // Bumper je hore alebo vľavo = dolný bumper
        }
        // Pre šikmú koľaj smerujúcu VPRAVO DOLE (315°): ↘
        else if (adjacentAngle == 315)
        {
            if (dy > 0 || dx < 0) return "HB";  // Bumper je dole alebo vľavo = horný bumper
            if (dy < 0 || dx > 0) return "DB";  // Bumper je hore alebo vpravo = dolný bumper
        }
        
        return "";  // Nepodarilo sa určiť
    }

    /// <summary>Získa súradnice bunky susednej koľaje podľa uhla.</summary>
    private (int cellX, int cellY) GetAdjacentCellForAngle(int cellX, int cellY, int angle)
    {
        return angle switch
        {
            0   => (cellX - 1, cellY),      // Vľavo
            45  => (cellX - 1, cellY - 1),  // Vľavo hore
            90  => (cellX, cellY - 1),      // Hore
            135 => (cellX + 1, cellY - 1),  // Vpravo hore
            180 => (cellX + 1, cellY),      // Vpravo
            225 => (cellX + 1, cellY + 1),  // Vpravo dole
            270 => (cellX, cellY + 1),      // Dole
            315 => (cellX - 1, cellY + 1),  // Vľavo dole
            _   => (cellX, cellY)
        };
    }


    /// <summary>Vytvorí TrackSegment prvky medzi začiatkom a koncom.</summary>
    private void CreateTrackLine(int startX, int startY, int endX, int endY)
    {
        if (_vm == null) return;
        
        var cells = GetLineCells(startX, startY, endX, endY);
        
        // Ak je len jedna bunka (jednoduchý klik), vložíme jednu koľaj
        if (cells.Count == 1)
        {
            // Skontrolujeme či na bunke nie je marker
            if (!HasMarkerAtCell(startX, startY))
            {
                var newEl = new TrackSegmentElement
                {
                    MarkerKey = "TrackSegment",
                    X = startX * Cell,
                    Y = startY * Cell,
                    Rotation = 0
                };
                _vm.Elements.Add(newEl);  // Toto automaticky spustí RebuildElementsLayer cez OnElementsChanged
                _vm.SyncToProject();
            }
            return;
        }
        
        // ===================================================================
        // VÝPOČET UHLA LÍNIE
        // ===================================================================
        int dx = endX - startX;
        int dy = endY - startY;
        
        // Vypočítame uhol v radiánoch a prevedieme na stupne
        double angleRad = Math.Atan2(dy, dx);
        double angleDeg = angleRad * 180.0 / Math.PI;
        
        // Normalizujeme na rozsah 0-360°
        if (angleDeg < 0) angleDeg += 360;
        
        // zaokrúhlime na najbližší násobok 45°
        int trackRotation = (int)Math.Round(angleDeg / 45) * 45;
        if (trackRotation >= 360) trackRotation = 0;
        
        // Určíme rotáciu pre bumpre
        int firstBumperRotation = (trackRotation + 270) % 360;  // -90° od smeru koľaje
        int lastBumperRotation = (trackRotation + 90) % 360;    // +90° od smeru koľaje
    
        // ===================================================================
        // KONTROLA ČI MÁ BYŤ BUMPER NA ZAČIATKU/KONCI
        // ===================================================================
        
        // Kontrola či na začiatku je bumper
        var (hasStartBumper, startBumper, startAdjacentAngle, startDirToAdj) = GetBumperAndAdjacentTrackAngle(cells[0].Item1, cells[0].Item2);
        
        // Ak NA začiatku NIE JE bumper → shouldPlaceStartBumper = true (vložíme nový)
        // Ak NA začiatku JE bumper → shouldPlaceStartBumper = false (vymažeme ho, vložíme koľaj/oblúk/výhybku)
        bool shouldPlaceStartBumper = !hasStartBumper;
        
        // Kontrola či na konci je bumper
        bool shouldPlaceEndBumper = !HasMarkerAtCell(cells[cells.Count - 1].Item1, cells[cells.Count - 1].Item2);
        
        bool replaceStartWithCurve = false;
        bool replaceStartWithTurnout = false;
        string startTurnoutKey = "";
        int startCurveRotation = 0;
        
        if (hasStartBumper && startBumper != null)
        {
            // Koľaj je obojsmerná - kontrolujeme aj opačný smer (adjAngle + 180°)
            int angleDiff = Math.Abs(trackRotation - startAdjacentAngle);
            if (angleDiff > 180) angleDiff = 360 - angleDiff;
            
            int reverseAdj = (startAdjacentAngle + 180) % 360;
            int angleDiff2 = Math.Abs(trackRotation - reverseAdj);
            if (angleDiff2 > 180) angleDiff2 = 360 - angleDiff2;
            
            // Smer OD susednej koľaje (opačný smer k susednej)
            int dirFromAdj = (startDirToAdj + 180) % 360;
            // Diff medzi novou koľajou a smerom OD susednej
            int diffFromAdj = Math.Abs(trackRotation - dirFromAdj);
            if (diffFromAdj > 180) diffFromAdj = 360 - diffFromAdj;
            
            
            // =======================================================================
            // KOLMÉ NAPOJENIE (90°) - Oblúk Curve_90
            // =======================================================================
            if (angleDiff == 90 || angleDiff2 == 90)
            {
                // Určíme pozíciu bumpera - použijeme startDirToAdj (smer K susednej), nie startAdjacentAngle (rotácia susednej)
                var (adjCellX, adjCellY) = GetAdjacentCellForAngle(cells[0].Item1, cells[0].Item2, startDirToAdj);
                string bumperPos = GetBumperPosition(cells[0].Item1, cells[0].Item2, adjCellX, adjCellY, startAdjacentAngle);
                
                if (!string.IsNullOrEmpty(bumperPos))
                {
                    int curve90Rotation = _curve90Handler?.CalculateCurve90Rotation(startAdjacentAngle, trackRotation, bumperPos) ?? -1;
                    
                    if (curve90Rotation != -1)
                    {
                        // Našli sme platný oblúk 90° v lookup tabuľke
                        replaceStartWithCurve = true;
                        startCurveRotation = curve90Rotation;
                        shouldPlaceStartBumper = false;
                        // Poznačíme si že ide o Curve_90 (nie Curve_45)
                        startTurnoutKey = "Curve_90";  // Použijeme toto pole na rozlíšenie typu oblúka
                    }
                }
            }
            // =======================================================================
            // ŠIKMÉ NAPOJENIE (45°) - Oblúk Curve_45 alebo Výhybka
            // =======================================================================
            else if (angleDiff == 45 || angleDiff2 == 45)
            {
                // Rozlíšenie oblúk vs výhybka:
                // Ak nová koľaj smeruje PREČ od susednej (diffToAdj == 45°) – oblúk (plynulý)
                // Ak nová koľaj smeruje K susednej (diff od dirToAdj == 45°) – výhybka (rozvetvenie)
                int diffToAdj = Math.Abs(trackRotation - startDirToAdj);
                if (diffToAdj > 180) diffToAdj = 360 - diffToAdj;
                
                if (diffFromAdj == 45)
                {
                    // Plynulé napojenie - oblúk
                    int effectiveAdj = angleDiff == 45 ? startAdjacentAngle : reverseAdj;
                    replaceStartWithCurve = true;
                    startCurveRotation = _curve45Handler?.CalculateCurve45Rotation(startAdjacentAngle, trackRotation) ?? -1;
                    
                    // Ak oblúk nie je definovaný (-1), nebudeme ho vkladať
                    if (startCurveRotation == -1)
                    {
                        replaceStartWithCurve = false;
                    }
                    else
                    {
                        shouldPlaceStartBumper = false;
                    }
                }
                else if (diffToAdj == 45)
                {
                    // Rozvetvenie - výhybka
                    // Použijeme lookup tabuľku pre spoľahlivé určenie typu a rotácie
                    var turnoutInfo = CalculateTurnoutTypeAndRotation(startAdjacentAngle, trackRotation);
                    
                    if (turnoutInfo.HasValue)
                    {
                        replaceStartWithTurnout = true;
                        shouldPlaceStartBumper = false;
                        startTurnoutKey = turnoutInfo.Value.TurnoutKey;
                        startCurveRotation = turnoutInfo.Value.Rotation;
                    }
                    else
                    {
                        // NEPOUŽÍVAME fallback - chceme len overené prípady
                    }
                }
            }
        }
        
        // Kontrola či na konci je bumper a či potrebujeme oblúk alebo výhybku
        var (hasEndBumper, endBumper, endAdjacentAngle, endDirToAdj) = GetBumperAndAdjacentTrackAngle(cells[cells.Count - 1].Item1, cells[cells.Count - 1].Item2);
        bool replaceEndWithCurve = false;
        bool replaceEndWithTurnout = false;
        string endTurnoutKey = "";
        int endCurveRotation = 0;
        
        
        if (hasEndBumper && endBumper != null)
        {
            int angleDiff = Math.Abs(trackRotation - endAdjacentAngle);
            if (angleDiff > 180) angleDiff = 360 - angleDiff;
            
            int reverseAdj = (endAdjacentAngle + 180) % 360;
            int angleDiff2 = Math.Abs(trackRotation - reverseAdj);
            if (angleDiff2 > 180) angleDiff2 = 360 - angleDiff2;
            
            int dirFromAdj = (endDirToAdj + 180) % 360;
            int diffFromAdj = Math.Abs(trackRotation - dirFromAdj);
            if (diffFromAdj > 180) diffFromAdj = 360 - diffFromAdj;
            
            
            // =======================================================================
            // KOLMÉ NAPOJENIE (90°) - Oblúk Curve_90
            // =======================================================================
            if (angleDiff == 90 || angleDiff2 == 90)
            {
                // Určíme pozíciu bumpera - použijeme endDirToAdj (smer K susednej), nie endAdjacentAngle (rotácia susednej)
                var (adjCellX, adjCellY) = GetAdjacentCellForAngle(cells[cells.Count - 1].Item1, cells[cells.Count - 1].Item2, endDirToAdj);
                string bumperPos = GetBumperPosition(cells[cells.Count - 1].Item1, cells[cells.Count - 1].Item2, adjCellX, adjCellY, endAdjacentAngle);
                
                if (!string.IsNullOrEmpty(bumperPos))
                {
                    int curve90Rotation = _curve90Handler?.CalculateCurve90Rotation(endAdjacentAngle, trackRotation, bumperPos) ?? -1;
                    
                    if (curve90Rotation != -1)
                    {
                        // Našli sme platný oblúk 90° v lookup tabuľke
                        replaceEndWithCurve = true;
                        endCurveRotation = curve90Rotation;
                        shouldPlaceEndBumper = false;
                        // Poznačíme si že ide o Curve_90 (nie Curve_45)
                        endTurnoutKey = "Curve_90";  // Použijeme toto pole na rozlíšenie typu oblúka
                    }
                }
            }
            // =======================================================================
            // ŠIKMÉ NAPOJENIE (45°) - Oblúk Curve_45 alebo Výhybka
            // =======================================================================
            else if (angleDiff == 45 || angleDiff2 == 45)
            {
                int diffToAdj = Math.Abs(trackRotation - endDirToAdj);
                if (diffToAdj > 180) diffToAdj = 360 - diffToAdj;
                
                if (diffFromAdj == 45)
                {
                    int effectiveAdj = angleDiff == 45 ? endAdjacentAngle : reverseAdj;
                    replaceEndWithCurve = true;
                    endCurveRotation = _curve45Handler?.CalculateCurve45Rotation(effectiveAdj, trackRotation) ?? -1;
                    
                    if (endCurveRotation == -1)
                    {
                        replaceEndWithCurve = false;
                    }
                        shouldPlaceEndBumper = false;
                }
                else if (diffToAdj == 45)
                {
                    var turnoutInfo = CalculateTurnoutTypeAndRotation(endAdjacentAngle, trackRotation);
                    
                    if (turnoutInfo.HasValue)
                    {
                        replaceEndWithTurnout = true;
                        shouldPlaceEndBumper = false;
                        endTurnoutKey = turnoutInfo.Value.TurnoutKey;
                        endCurveRotation = turnoutInfo.Value.Rotation;
                    }
                    else
                    {
                    }
                }
            }
        }
    
        // ===================================================================
        // OPTIMALIZÁCIA: Dočasne odpojíme event handlery aby sa layout
        // neprekresľoval a inšpektor sa neaktualizoval po každej vloženej bunke
        // ===================================================================
        _vm.Elements.CollectionChanged -= OnElementsChanged;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        
        try
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var (cx, cy) = cells[i];
                double x = cx * Cell;
                double y = cy * Cell;
                
                bool isFirst = (i == 0);
                bool isLast = (i == cells.Count - 1);
                
                // Pre prvú bunku - nahradiť bumper oblúkom ak je potrebné
                if (isFirst && replaceStartWithCurve && startBumper != null)
                {
                    // Vymažeme starý bumper
                    _vm.Elements.Remove(startBumper);
                    
                    // Rozlíšime medzi Curve_45 a Curve_90
                    bool isCurve90 = startTurnoutKey == "Curve_90";
                    string curveKey = isCurve90 ? "Curve_90" : "Curve_45";
                    var curveType = isCurve90 ? LayoutElementType.Curve : LayoutElementType.CurveNarrow;
                    
                    var originalType = _vm.PendingElementType;
                    var originalMarkerKey = _vm.SelectedMarkerKey;
                    
                    _vm.PendingElementType = curveType;
                    _vm.SelectedMarkerKey = curveKey;
                    
                    var elementBeforePlacement = _vm.SelectedElement;
                    _vm.PlaceElementAt(x + Cell / 2, y + Cell / 2);
                    
                    if (_vm.SelectedElement != null && 
                        _vm.SelectedElement != elementBeforePlacement && 
                        _vm.SelectedElement.MarkerKey == curveKey)
                    {
                        _vm.SelectedElement.Rotation = startCurveRotation;
                        RebuildElementsLayer();
                    }
                    
                    _vm.PendingElementType = originalType;
                    _vm.SelectedMarkerKey = originalMarkerKey;
                }
                // Pre poslednú bunku - nahradiť bumper oblúkom ak je potrebné
                else if (isLast && replaceEndWithCurve && endBumper != null)
                {
                    _vm.Elements.Remove(endBumper);
                    
                    // Rozlíšime medzi Curve_45 a Curve_90
                    bool isCurve90 = endTurnoutKey == "Curve_90";
                    string curveKey = isCurve90 ? "Curve_90" : "Curve_45";
                    var curveType = isCurve90 ? LayoutElementType.Curve : LayoutElementType.CurveNarrow;
                    
                    var originalType = _vm.PendingElementType;
                    var originalMarkerKey = _vm.SelectedMarkerKey;
                    
                    _vm.PendingElementType = curveType;
                    _vm.SelectedMarkerKey = curveKey;
                    
                    var elementBeforePlacement = _vm.SelectedElement;
                    _vm.PlaceElementAt(x + Cell / 2, y + Cell / 2);
                    
                    if (_vm.SelectedElement != null && 
                        _vm.SelectedElement != elementBeforePlacement && 
                        _vm.SelectedElement.MarkerKey == curveKey)
                    {
                        _vm.SelectedElement.Rotation = endCurveRotation;
                        RebuildElementsLayer();
                    }
                    
                    _vm.PendingElementType = originalType;
                    _vm.SelectedMarkerKey = originalMarkerKey;
                }
                // Pre prvú bunku - nahradiť bumper výhybkou ak je potrebné
                else if (isFirst && replaceStartWithTurnout && startBumper != null)
                {
                    _vm.Elements.Remove(startBumper);
                    
                    var originalType = _vm.PendingElementType;
                    var originalMarkerKey = _vm.SelectedMarkerKey;
                    
                    _vm.PendingElementType = LayoutElementType.Turnout;
                    _vm.SelectedMarkerKey = startTurnoutKey;
                    
                    var elementBeforePlacement = _vm.SelectedElement;
                    _vm.PlaceElementAt(x + Cell / 2, y + Cell / 2);
                    
                    if (_vm.SelectedElement != null && 
                        _vm.SelectedElement != elementBeforePlacement && 
                        (_vm.SelectedElement.MarkerKey == "Turnout_L" || _vm.SelectedElement.MarkerKey == "Turnout_R"))
                    {
                        _vm.SelectedElement.Rotation = startCurveRotation;
                        RebuildElementsLayer();
                    }
                    
                    _vm.PendingElementType = originalType;
                    _vm.SelectedMarkerKey = originalMarkerKey;
                }
                // Pre poslednú bunku - nahradiť bumper výhybkou ak je potrebné
                else if (isLast && replaceEndWithTurnout && endBumper != null)
                {
                    _vm.Elements.Remove(endBumper);
                    
                    var originalType = _vm.PendingElementType;
                    var originalMarkerKey = _vm.SelectedMarkerKey;
                    
                    _vm.PendingElementType = LayoutElementType.Turnout;
                    _vm.SelectedMarkerKey = endTurnoutKey;
                    
                    var elementBeforePlacement = _vm.SelectedElement;
                    _vm.PlaceElementAt(x + Cell / 2, y + Cell / 2);
                    
                    if (_vm.SelectedElement != null && 
                        _vm.SelectedElement != elementBeforePlacement && 
                        (_vm.SelectedElement.MarkerKey == "Turnout_L" || _vm.SelectedElement.MarkerKey == "Turnout_R"))
                    {
                        _vm.SelectedElement.Rotation = endCurveRotation;
                        RebuildElementsLayer();
                    }
                    
                    _vm.PendingElementType = originalType;
                    _vm.SelectedMarkerKey = originalMarkerKey;
                }
                // Pre prvú a poslednú bunku vytvoríme bumper len ak tam ešte nie je žiadny marker
                else if ((isFirst && shouldPlaceStartBumper) || (isLast && shouldPlaceEndBumper))
                {
                    var originalType = _vm.PendingElementType;
                    var originalMarkerKey = _vm.SelectedMarkerKey;
                    
                    _vm.PendingElementType = LayoutElementType.Bumper;
                    _vm.SelectedMarkerKey = "Bumper";
                    
                    // FIX: Uložíme referenciou na predchádzajúci element PRED volaním PlaceElementAt
                    var elementBeforePlacement = _vm.SelectedElement;
                    _vm.PlaceElementAt(x + Cell / 2, y + Cell / 2);
                    
                    // FIX: Nastavíme rotáciu LEN ak sa vytvoril NOVÝ element (PlaceElementAt uspelo)
                    if (_vm.SelectedElement != null && 
                        _vm.SelectedElement != elementBeforePlacement && 
                        _vm.SelectedElement.MarkerKey == "Bumper")
                    {
                        _vm.SelectedElement.Rotation = isFirst ? firstBumperRotation : lastBumperRotation;
                    }
                    
                    _vm.PendingElementType = originalType;
                    _vm.SelectedMarkerKey = originalMarkerKey;
                }
                // Pre prvú bunku - ak začíname z bumpera ale nejde o oblúk/výhybku, vymaž bumper a vlož rovnú koľaj
                else if (isFirst && hasStartBumper && startBumper != null)
                {
                    // Vymažeme starý bumper
                    _vm.Elements.Remove(startBumper);
                    
                    // FIX: Uložíme referenciu pred placement
                    var elementBeforePlacement = _vm.SelectedElement;
                    _vm.PlaceElementAt(x + Cell / 2, y + Cell / 2);
                    
                    // FIX: Nastavíme rotáciu LEN ak sa vytvoril NOVÝ element
                    if (_vm.SelectedElement != null && 
                        _vm.SelectedElement != elementBeforePlacement && 
                        _vm.SelectedElement.MarkerKey == "TrackSegment")
                    {
                        _vm.SelectedElement.Rotation = trackRotation;
                    }
                }
                else if (!isFirst && !isLast)
                {
                    // Stredné bunky - vždy vytvoríme koľaj
                    var elementBeforePlacement = _vm.SelectedElement;
                    _vm.PlaceElementAt(x + Cell / 2, y + Cell / 2);
                    
                    // FIX: Nastavíme rotáciu LEN ak sa vytvoril NOVÝ element
                    if (_vm.SelectedElement != null && 
                        _vm.SelectedElement != elementBeforePlacement && 
                        _vm.SelectedElement.MarkerKey == "TrackSegment")
                    {
                        _vm.SelectedElement.Rotation = trackRotation;
                    }
                }
            }
        }
        finally
        {
            // Event handlery pripojíme AŽ PO všetkých auto-detekciách,
            // aby sa layout neprekresľoval pri každej zmene v handleroch.
        }
        
        // Detekcia krížení koľají - vykoná sa po vložení všetkých koľají
        // Kontrolujeme LEN stredné bunky (nie prvú a poslednú, kde sú bumpery/oblúky/výhybky)
        if (cells.Count > 2)
        {
            var middleCells = cells.GetRange(1, cells.Count - 2);
            
            // Detekcia Cross90 (kolmé kríženia 90°)
            _crossingHandler?.CheckAndCreateCrossings(middleCells, trackRotation);
            
            // Detekcia Cross45 (šikmé kríženia 45°)
            _crossing45Handler?.CheckAndCreateCrossings45(middleCells, trackRotation);
        }
        
        // Detekcia dotykových križovatiek pre diagonálne koľaje (PRVÁ a POSLEDNÁ bunka)
        // Volá sa PRED výhybkami, aby sme mohli detekovať dotyk diagonálnych koľají
        if (cells.Count > 0)
        {
            // Kontrola prvej bunky (začiatok koľaje)
            _crossingHandler?.CheckTouchCrossing(cells[0].Item1, cells[0].Item2, trackRotation);
            
            // Kontrola poslednej bunky (koniec koľaje)
            if (cells.Count > 1)
            {
                _crossingHandler?.CheckTouchCrossing(cells[cells.Count - 1].Item1, cells[cells.Count - 1].Item2, trackRotation);
            }
        }
        
        // Detekcia výhybiek pri dotykoch koľají (PRVÁ a POSLEDNÁ bunka)
        _turnoutAutoHandler?.CheckAndCreateTurnouts(cells, trackRotation);
        
        // Detekcia kolmých výhybiek (TurnoutL90/TurnoutR90) pri kolmom napájaní koľají
        _turnout90Handler?.CheckAndCreateTurnout90(cells, trackRotation);
        
        // AŽ TERAZ pripojíme event handlery (po všetkých auto-detekciách)
        _vm.Elements.CollectionChanged += OnElementsChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;
        
        // Prekreslíme layout JEN RAZ na konci
        RebuildElementsLayer();
    }
    
    /// <summary>Vráti zoznam buniek (cellX, cellY) medzi začiatkom a koncom línie.</summary>
    private System.Collections.Generic.List<(int, int)> GetLineCells(int startX, int startY, int endX, int endY)
    {
        var cells = new System.Collections.Generic.List<(int, int)>();
        
        int dx = Math.Abs(endX - startX);
        int dy = Math.Abs(endY - startY);
        
        int sx = startX < endX ? 1 : -1;
        int sy = startY < endY ? 1 : -1;
        
        int err = dx - dy;
        int x = startX;
        int y = startY;
        
        // Bresenhamov algoritmus pre kreslenie čiary
        while (true)
        {
            cells.Add((x, y));
            
            if (x == endX && y == endY) break;
            
            int e2 = 2 * err;
            
            if (e2 > -dy)
            {
                err -= dy;
                x += sx;
            }
            
            if (e2 < dx)
            {
                err += dx;
                y += sy;
            }
        }
        
        return cells;
    }
    
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm == null) return;

        if (e.Source is TextBox || e.Source is NumericUpDown) return;

        // Klávesy pre rotáciu
        if (e.Key == Key.R)
        {
            _vm.RotateSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.T)
        {
            _vm.RotateSelectedLeft();
            e.Handled = true;
        }
        // Delete - vymaže vybrané prvky (single alebo multi)
        else if (e.Key == Key.Delete)
        {
            if (_vm.Selection.SelectionCount > 0)
                _vm.DeleteSelectedElements();
            else
                _vm.DeleteSelected();
            e.Handled = true;
        }
        // Ctrl+A - vybrať všetko
        else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _vm.SelectAll();
            RebuildElementsLayer();
            e.Handled = true;
        }
        // Ctrl+C - kopírovať
        else if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (_vm.Selection.SelectionCount > 0)
                _vm.CopySelectedElements();
            else if (_vm.SelectedElement != null)
                _vm.CopyElement();
            e.Handled = true;
        }
        // Ctrl+X - vystrihnúť
        else if (e.Key == Key.X && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (_vm.Selection.SelectionCount > 0)
                _vm.CutSelectedElements();
            else if (_vm.SelectedElement != null)
                _vm.CutElement();
            e.Handled = true;
        }
        // Ctrl+V - vložiť
        else if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _vm.PasteSelectedElements(); // Automaticky použije multi alebo single clipboard
            e.Handled = true;
        }
        // Escape - zruš výber
        else if (e.Key == Key.Escape)
        {
            _vm.Selection.ClearSelection();
            _vm.SelectedElement = null;
            RebuildElementsLayer();
            e.Handled = true;
        }
    }

    // =======================================================================
    // TURNOUT LOOKUP TABLE - Hard-coded kombinácie
    // =======================================================================


    /// <summary>
    /// LOOKUP TABUĽKA pre výhybky (Turnout_L / Turnout_R).
    /// Kombinácie kde nová koľaj odbočuje (diffToAdj == 45°).
    /// Formát: (adjacentAngle, trackAngle) → (typ výhybky, rotácia)
    /// VP = Turnout_R (pravá výhybka), VL = Turnout_L (ľavá výhybka)
    /// </summary>
    private static readonly Dictionary<(int adjAngle, int trackAngle), (string type, int rotation)> TurnoutLookup = new()
    {
        // Hard-coded kombinácie pre výhybky (overené používateľom 2026-04-09)
        [(45, 180)]   = ("Turnout_R", 270),  // 45° → 180° = VP @ 270°
        [(180, 45)]   = ("Turnout_R", 90),   // 180° → 45° = VP @ 90°
        [(45, 0)]     = ("Turnout_R", 90),   // 45° → 0° = VP @ 90°
        [(0, 45)]     = ("Turnout_R", 90),   // 0° → 45° = VP @ 90°
        [(0, 225)]    = ("Turnout_R", 270),  // 0° → 225° = VP @ 270°
        [(225, 0)]    = ("Turnout_R", 90),   // 225° → 0° = VP @ 90°
        [(180, 225)]  = ("Turnout_R", 270),  // 180° → 225° = VP @ 270°
        [(135, 180)]  = ("Turnout_L", 270),  // 135° → 180° = VL @ 270° 
        [(135, 0)]    = ("Turnout_L", 90),   // 135° → 0° = VL @ 90°
        [(0, 135)]    = ("Turnout_L", 90),   // 0° → 135° = VL @ 90°
        [(315, 0)]    = ("Turnout_L", 90),   // 315° → 0° = VL @ 90°
        [(315, 180)]  = ("Turnout_L", 270),  // 315° → 180° = VL @ 270°
        [(0, 315)]    = ("Turnout_L", 90),   // 0° → 315° = VL @ 90°
        [(225, 180)]  = ("Turnout_R", 270),  // 225° → 180° = VP @ 270°
    };

    /// <summary>
    /// Vypočíta typ výhybky (L/R) a rotáciu pre automatické vkladanie pri tvorení koľaje.
    /// Používa LOOKUP TABUĽKU s overenými prípadmi. Pre neoverené prípady vracia null.
    /// </summary>
    /// <param name="adjacentAngle">Uhol susednej koľaje (kam už existuje bumper)</param>
    /// <param name="trackRotation">Uhol novej koľaje ktorú kreslíme</param>
    /// <returns>Tuple (TurnoutKey, Rotation) alebo null ak nie je v tabuľke</returns>
    private (string TurnoutKey, int Rotation)? CalculateTurnoutTypeAndRotation(int adjacentAngle, int trackRotation)
    {
        // Normalizujeme uhly na rozsah 0-359°
        adjacentAngle = adjacentAngle % 360;
        if (adjacentAngle < 0) adjacentAngle += 360;
        
        trackRotation = trackRotation % 360;
        if (trackRotation < 0) trackRotation += 360;
        
        
        // Vyhľadáme v tabuľke
        var key = (adjacentAngle, trackRotation);
        if (TurnoutLookup.TryGetValue(key, out var result))
        {
            return (result.type, result.rotation);
        }
        
        return null; // Použije sa fallback logika v volacom kóde
    }

    /// <summary>Vyčistí cache ikon - uvoľní všetky načítané bitmapy.</summary>
    private static void ClearIconCache()
    {
        foreach (var bitmap in _iconCache.Values)
        {
            bitmap?.Dispose();
        }
        _iconCache.Clear();
    }
}

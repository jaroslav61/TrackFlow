using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.ViewModels.Editor;

namespace TrackFlow.Views.Editor;

public partial class BlockPropertiesWindow : Window
{
    private BlockPropertiesViewModel? _vm;
    private bool _closeRequestedByViewModel;
    private bool _autoSaveInProgress;

    // Súradnice diagramu (zodpovedajú AXAML hodnotám)
    private const double DiagLeft  = 60.0;
    private const double DiagWidth = 420.0;
    private const double FwdTop    = 0.0;   // Forward blok začína na Canvas.Top=0
    private const double FwdHeight = 90.0;
    private const double BwdTop    = 90.0;  // Backward blok začína na Canvas.Top=90
    private const double BwdHeight = 90.0;
    private const double ScaleWidth = 420.0;

    // Drag stav
    private Canvas? _draggedMarkerCanvas;
    private string? _draggedMarkerKey;
    private bool    _isDraggingMarker;

    // Vizuálne canvasy markerov na diagrame
    private readonly System.Collections.Generic.Dictionary<string, Canvas> _markerVisuals = new();
    private readonly System.Collections.Generic.Dictionary<string, TextBlock> _markerTexts = new();
    
    // Vizuálne elementy indikátorov
    private readonly System.Collections.Generic.Dictionary<Guid, Border> _indicatorVisuals = new();
    private readonly System.Collections.Generic.List<Rectangle> _resizeHandles = new();
    
    // Drag stav pre indikátory
    private Guid? _draggedIndicatorId;
    private bool _isDraggingIndicator;
    private bool _isResizingIndicator;
    private bool _isResizingLeft; // true = left handle, false = right handle
    private double _dragStartX;
    private int _dragStartCm; // Pôvodná pozícia indikátora pred dragom

    private readonly DispatcherTimer _indicatorRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Dictionary<Guid, bool> _lastIndicatorStates = new();

    public BlockPropertiesWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => AttachToVm(DataContext as BlockPropertiesViewModel);
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
        _indicatorRefreshTimer.Tick += OnIndicatorRefreshTick;
    }

    private void AttachToVm(BlockPropertiesViewModel? vm)
    {
        if (_vm != null)
        {
            _vm.CloseRequested  -= OnCloseRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = vm;
        if (_vm != null)
        {
            _vm.CloseRequested  += OnCloseRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnCloseRequested(bool saved)
    {
        _closeRequestedByViewModel = true;
        Close(saved);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _indicatorRefreshTimer.Stop();

        // Explicitné zatvorenie cez Save/Cancel necháme prebehnúť.
        if (_closeRequestedByViewModel)
            return;

        // Pri zatvorení cez X spravíme fallback auto-save.
        if (_vm == null || _autoSaveInProgress)
            return;

        _autoSaveInProgress = true;
        try
        {
            // SaveCommand vyvolá CloseRequested(true), čím sa okno zavrie korektne so saved=true.
            _vm.SaveCommand.Execute(null);

            // Aktuálne zatváranie cez X zrušíme; okno zatvorí Save flow vyššie.
            e.Cancel = true;
        }
        finally
        {
            _autoSaveInProgress = false;
        }
    }

    private void OnWindowOpened(object? sender, System.EventArgs e)
    {
        DrawScales();
        DrawIndicators();
        DrawMarkers();
        SnapshotIndicatorStates();
        _indicatorRefreshTimer.Start();
    }

    private void OnIndicatorRefreshTick(object? sender, EventArgs e)
    {
        if (_vm == null)
            return;

        var currentStates = _vm.Indicators.ToDictionary(indicator => indicator.Id, indicator => indicator.IsActive);
        if (currentStates.Count == _lastIndicatorStates.Count
            && currentStates.All(entry => _lastIndicatorStates.TryGetValue(entry.Key, out var lastState) && lastState == entry.Value))
            return;

        DrawIndicators();
        _lastIndicatorStates = currentStates;
    }

    private void SnapshotIndicatorStates()
    {
        _lastIndicatorStates = _vm?.Indicators.ToDictionary(indicator => indicator.Id, indicator => indicator.IsActive)
            ?? new Dictionary<Guid, bool>();
    }

    // ── Event handler pre pridanie indikátora ────────────────────────────────
    
    private void DiagramCanvas_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // Kliknutie na prázdny Canvas (mimo indikátora/markeru) zruší výber
        if (_vm == null) return;
        
        // Zruš výber indikátora a markeru
        _vm.SelectedIndicator = null;
        _vm.SelectedMarker = null;
        
        // Označ všetky indikátory ako nevybrané
        foreach (var ind in _vm.Indicators)
        {
            ind.IsSelected = false;
        }
        
        // Prekreslenie
        DrawIndicators();
        
        e.Handled = true;
    }

    private void MarkerPositionSpinner_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Pri LostFocus aktualizuj marker
        if (_vm?.SelectedMarker != null)
        {
            UpdateMarkerText(_vm.SelectedMarker);
            UpdateMarkerPosition(_vm.SelectedMarker);
        }
    }

    private void MarkerEndSpinner_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Pri LostFocus aktualizuj marker
        if (_vm?.SelectedMarker != null)
        {
            UpdateMarkerText(_vm.SelectedMarker);
        }
    }
    
    private void IndicatorTypeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (cb.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not string typeStr) return;
        
        // Parse type
        if (!System.Enum.TryParse<BlockIndicatorType>(typeStr, out var type)) return;
        
        // Zavolaj command
        _vm?.AddIndicatorCommand.Execute(type);
        
        // Reset ComboBox späť na placeholder
        cb.SelectedIndex = -1;
        
        // Prekreslime indikátory a markery
        DrawIndicators();
        DrawMarkers();
    }

    // ── Stupnica ──────────────────────────────────────────────────────────────

    private void DrawScales()
    {
        if (_vm == null) return;
        var upper = this.FindControl<Canvas>("UpperScaleCanvas");
        var lower = this.FindControl<Canvas>("LowerScaleCanvas");
        if (upper == null || lower == null) return;

        upper.Children.Clear();
        lower.Children.Clear();

        int lengthMm = _vm.LengthMm;
        if (lengthMm <= 0) return;

        const double edgeOffset = 1.0;
        double pxPerCm = (ScaleWidth - edgeOffset * 2) / lengthMm;

        int tickStep, labelStep;
        if      (lengthMm <=   200) { tickStep =   5; labelStep =   20; }
        else if (lengthMm <=   500) { tickStep =  10; labelStep =   50; }
        else if (lengthMm <=  2000) { tickStep =  50; labelStep =  200; }
        else if (lengthMm <=  5000) { tickStep = 100; labelStep =  500; }
        else if (lengthMm <= 20000) { tickStep = 500; labelStep = 2000; }
        else                        { tickStep =1000; labelStep = 5000; }

        for (int c = 0; c <= lengthMm; c += tickStep)
        {
            double x       = edgeOffset + c * pxPerCm;
            bool hasLabel  = (c % labelStep == 0);
            double tickH   = hasLabel ? 15 : (c % (tickStep * 2) == 0 ? 10 : 6);
            double thick   = hasLabel ? 1.5 : 1.0;
            int revC       = lengthMm - c;

            // Horná stupnica – čiarky idú NAHOR od y=0 (bola 30, teraz -30px = 0)
            upper.Children.Add(new Line
            {
                StartPoint = new Avalonia.Point(x, 0), EndPoint = new Avalonia.Point(x, -tickH),
                Stroke = Brushes.Black, StrokeThickness = thick
            });
            if (hasLabel)
            {
                var tb = new TextBlock { Text = $"{c}cm", FontSize = 10, Foreground = Brushes.Black };
                Canvas.SetLeft(tb, x - 14); Canvas.SetTop(tb, -tickH - 14);
                upper.Children.Add(tb);
            }

            // Dolná stupnica – čiarky idú NADOL od y=30 (bola 0, teraz +30px = 30)
            lower.Children.Add(new Line
            {
                StartPoint = new Avalonia.Point(x, 30), EndPoint = new Avalonia.Point(x, 30 + tickH),
                Stroke = Brushes.Black, StrokeThickness = thick
            });
            if (hasLabel)
            {
                var tb = new TextBlock { Text = $"{revC}cm", FontSize = 10, Foreground = Brushes.Black };
                Canvas.SetLeft(tb, x - 14); Canvas.SetTop(tb, 30 + tickH + 2);
                lower.Children.Add(tb);
            }
        }
    }

    // ── Indikátory ────────────────────────────────────────────────────────────

    private void DrawIndicators()
    {
        if (_vm == null) return;
        var diagram = this.FindControl<Canvas>("DiagramCanvas");
        if (diagram == null) return;

        // Odstráň staré vizuály indikátorov
        foreach (var border in _indicatorVisuals.Values)
            diagram.Children.Remove(border);
        _indicatorVisuals.Clear();

        // Odstráň staré resize handles
        foreach (var handle in _resizeHandles)
            diagram.Children.Remove(handle);
        _resizeHandles.Clear();

        if (_vm.LengthMm <= 0 || !_vm.HasIndicators) return;

        // Vykresli každý indikátor (ŽLTÝ na ŠEDOM pozadí bloku)
        foreach (var indicator in _vm.Indicators)
        {
            DrawIndicator(diagram, indicator);
        }

        SnapshotIndicatorStates();
    }

    private void DrawIndicator(Canvas diagram, BlockIndicatorViewModel indicator)
    {
        if (_vm == null) return;

        // INDIKÁTOR POKRÝVA OBE POLOVICE BLOKU (Forward + Backward)
        // Jeden fyzický detekčný úsek pre oba smery jazdy
        double indicatorTop = FwdTop;
        double indicatorHeight = FwdHeight + BwdHeight; // Celá výška = 180px (90+90)
        
        // X pozícia a šírka
        double x = DiagLeft + indicator.StartX;
        double width = indicator.Width;
        
        // Border pre indikátor - ŽLTÁ farba (#EEBC08) na šedom pozadí bloku
        var border = new Border
        {
            Width = width,
            Height = indicatorHeight,
            Background = new SolidColorBrush(Color.Parse("#EEBC08")), // ŽLTÁ farba
            BorderBrush = indicator.IsSelected ? new SolidColorBrush(Color.Parse("#1976D2")) : new SolidColorBrush(Color.Parse("#64B5F6")),
            BorderThickness = new Avalonia.Thickness(3), // 3px oramovanie všade
            CornerRadius = new Avalonia.CornerRadius(0),
            ZIndex = 10,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        
        // Nastav ToolTip na celý indikátor (Border)
        ToolTip.SetTip(border, indicator.ToolTipText);
        ToolTip.SetShowDelay(border, 500); // Zobrazí sa po 0.5 sekundách
        // Grid pre rozdelenie na dve polovice s ikonou
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star))); // Horná polovica
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(2, GridUnitType.Pixel))); // Deliaca čiara
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star))); // Dolná polovica

        // Ikona v hornej polovici (vertikálne centrovaná)
        var icon = new Image
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        
        // Nastav ToolTip s názvom indikátora
        ToolTip.SetTip(icon, indicator.ToolTipText);
        ToolTip.SetShowDelay(icon, 500); // Zobrazí sa po 0.5 sekundách
        
        try
        {
            var iconPath = indicator.IconPath;
            var uri = iconPath.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(iconPath)
                : new Uri($"avares://TrackFlow{iconPath}");
            icon.Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(uri));
        }
        catch { }
        
        Grid.SetRow(icon, 0);
        grid.Children.Add(icon);

        // Horizontálna deliaca čiara medzi Forward a Backward (modrá)
        var separatorLine = new Rectangle
        {
            Height = 2,
            Fill = new SolidColorBrush(Color.Parse("#1976D2")), // Modrá
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        Grid.SetRow(separatorLine, 1);
        grid.Children.Add(separatorLine);

        border.Child = grid;
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, indicatorTop);

        // NEVIDITEĽNÉ RESIZE ZÓNY - len pre interakciu, nie vizuálne
        var leftHandle = new Rectangle
        {
            Width = 20, // Širšia zóna pre jednoduchšie chytenie
            Height = indicatorHeight,
            Fill = Brushes.Transparent, // NEVIDITEĽNÉ
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeWestEast),
            ZIndex = 20
        };
        Canvas.SetLeft(leftHandle, x - 10); // Polovica mimo, polovica v indikátore
        Canvas.SetTop(leftHandle, indicatorTop);

        var rightHandle = new Rectangle
        {
            Width = 20,
            Height = indicatorHeight,
            Fill = Brushes.Transparent, // NEVIDITEĽNÉ
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeWestEast),
            ZIndex = 20
        };
        Canvas.SetLeft(rightHandle, x + width - 10); // Polovica v indikátore, polovica mimo
        Canvas.SetTop(rightHandle, indicatorTop);

        // Interakcia - kliknutie na výber, drag na resize
        var capturedIndicatorId = indicator.Id;

        // Kliknutie na indikátor - výber alebo drag
        border.PointerPressed += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                OpenIndicatorPropertiesWindow(indicator);
                e.Handled = true;
                return;
            }
            
            if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
            
            // Začni drag indikátora (posúvanie)
            _isDraggingIndicator = true;
            _draggedIndicatorId = capturedIndicatorId;
            _dragStartX = e.GetPosition(diagram).X;
            _dragStartCm = indicator.StartCm;
            
            _vm?.SelectIndicator(capturedIndicatorId);
            DrawIndicators();
            
            e.Pointer.Capture(border);
            e.Handled = true;
        };

        // Drag indikátora (posúvanie)
        border.PointerMoved += (_, e) =>
        {
            if (!_isDraggingIndicator || _draggedIndicatorId != capturedIndicatorId) return;
            
            double currentX = e.GetPosition(diagram).X;
            double deltaX = currentX - _dragStartX;
            double deltaCm = (deltaX / DiagWidth) * _vm.LengthMm;
            
            int newStartCm = (int)(_dragStartCm + deltaCm);
            int indicatorWidth = indicator.EndCm - indicator.StartCm;
            
            // Obmedzenie - indikátor nesmie vyjsť mimo bloku
            newStartCm = System.Math.Clamp(newStartCm, 0, _vm.LengthMm - indicatorWidth);
            int newEndCm = newStartCm + indicatorWidth;
            
            indicator.StartCm = newStartCm;
            indicator.EndCm = newEndCm;
            
            // Vizuálna aktualizácia
            double newX = DiagLeft + indicator.StartX;
            border.Width = indicator.Width;
            Canvas.SetLeft(border, newX);
            Canvas.SetLeft(leftHandle, newX - 10);
            Canvas.SetLeft(rightHandle, newX + indicator.Width - 10);
            
            // Dynamické prekreslenie markerov (AbsolutePositionCm sa automaticky aktualizuje)
            DrawMarkers();
            
            e.Handled = true;
        };

        border.PointerReleased += (_, e) =>
        {
            if (_isDraggingIndicator && _draggedIndicatorId == capturedIndicatorId)
            {
                _isDraggingIndicator = false;
                _draggedIndicatorId = null;
                e.Pointer.Capture(null);
                
                // Prekreslenie po ukončení dragu
                DrawIndicators();
                DrawMarkers();
                
                e.Handled = true;
            }
        };

        // ZJEDNODUŠENÁ RESIZE INTERAKCIA
        // Ľavý handle
        leftHandle.PointerPressed += (s, e) =>
        {
            _isResizingIndicator = true;
            _isResizingLeft = true;
            _draggedIndicatorId = capturedIndicatorId;
            if (s is Control control)
                e.Pointer.Capture(control);
            e.Handled = true;
        };

        // Pravý handle  
        rightHandle.PointerPressed += (s, e) =>
        {
            _isResizingIndicator = true;
            _isResizingLeft = false;
            _draggedIndicatorId = capturedIndicatorId;
            if (s is Control control)
                e.Pointer.Capture(control);
            e.Handled = true;
        };

        // Spoločný PointerMoved pre celý diagram
        diagram.PointerMoved += OnDiagramPointerMoved;
        
        // Spoločný PointerReleased
        var releaseHandler = new EventHandler<Avalonia.Input.PointerReleasedEventArgs>((s, e) =>
        {
            if (_isResizingIndicator && _draggedIndicatorId == capturedIndicatorId)
            {
                _isResizingIndicator = false;
                _draggedIndicatorId = null;
                e.Pointer.Capture(null);
                
                // Prekreslenie AŽ PO uvoľnení myši
                DrawIndicators();
                DrawMarkers();
                
                e.Handled = true;
            }
        });
        
        leftHandle.PointerReleased += releaseHandler;
        rightHandle.PointerReleased += releaseHandler;

        diagram.Children.Add(border);
        diagram.Children.Add(leftHandle);
        diagram.Children.Add(rightHandle);
        _indicatorVisuals[indicator.Id] = border;
        _resizeHandles.Add(leftHandle);
        _resizeHandles.Add(rightHandle);
    }

    private void OnDiagramPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!_isResizingIndicator || _draggedIndicatorId == null) return;
        
        var diagram = sender as Canvas;
        if (diagram == null) return;
        
        var mouseX = e.GetPosition(diagram).X;
        HandleIndicatorResize(mouseX, _isResizingLeft);
        e.Handled = true;
    }

    private void HandleIndicatorResize(double mouseX, bool isLeftHandle)
    {
        if (_vm == null || _draggedIndicatorId == null) return;
        var indicator = _vm.Indicators.FirstOrDefault(i => i.Id == _draggedIndicatorId);
        if (indicator == null) return;

        // Prepočítaj pozíciu myši na cm
        double clampedX = System.Math.Clamp(mouseX, DiagLeft, DiagLeft + DiagWidth);
        double fraction = (clampedX - DiagLeft) / DiagWidth;
        int newCm = (int)(fraction * _vm.LengthMm);

        // Ulož staré hodnoty pre prepočet markerov
        int oldStartCm = indicator.StartCm;
        int oldEndCm = indicator.EndCm;

        if (isLeftHandle)
        {
            // Ľavý handle - mení StartCm
            int proposedStart = System.Math.Clamp(newCm, 0, indicator.EndCm - 10); // Min 10cm šírka
            indicator.StartCm = proposedStart;
        }
        else
        {
            // Pravý handle - mení EndCm
            int proposedEnd = System.Math.Clamp(newCm, indicator.StartCm + 10, _vm.LengthMm); // Min 10cm šírka
            indicator.EndCm = proposedEnd;
        }

        // VIZUÁLNA AKTUALIZÁCIA počas dragu - priama manipulácia s Border elementom
        if (_indicatorVisuals.TryGetValue(indicator.Id, out var border))
        {
            double newX = DiagLeft + indicator.StartX;
            double newWidth = indicator.Width;
            
            border.Width = newWidth;
            Canvas.SetLeft(border, newX);
            
            // Aktualizuj aj resize handles
            var handles = _resizeHandles.Where(h => 
            {
                var left = Canvas.GetLeft(h);
                return (isLeftHandle && left < DiagLeft + DiagWidth / 2) || 
                       (!isLeftHandle && left >= DiagLeft + DiagWidth / 2);
            }).ToList();
            
            if (handles.Count >= 2)
            {
                Canvas.SetLeft(handles[0], newX - 10); // Left handle
                Canvas.SetLeft(handles[1], newX + newWidth - 10); // Right handle
            }
        }

        // Prepočítaj markery a dynamicky ich prekresli
        if (oldStartCm != indicator.StartCm || oldEndCm != indicator.EndCm)
        {
            _vm.RecalculateMarkersForIndicator(indicator.Id, oldStartCm, oldEndCm);
            DrawMarkers(); // Dynamické prekreslenie
        }
    }

    private async void OpenIndicatorPropertiesWindow(BlockIndicatorViewModel indicator)
    {
        try
        {
            // Vytvor ViewModel pre indikátor
            var indicatorModel = indicator.GetModel();
            var dialogVm = new IndicatorPropertiesViewModel(indicatorModel, _vm?.SettingsManager);
            
            // Vytvor a otvor dialóg
            var dialog = new IndicatorPropertiesWindow
            {
                DataContext = dialogVm
            };
            
            var saved = await dialog.ShowDialog<bool>(this);
            
            if (saved)
            {
                // Aktualizuj indikátor v bloku
                // Nie je potrebné nič robiť - model sa aktualizoval priamo
                
                // Prekreslenie pre prípad zmeny názvu alebo iných vlastností
                DrawIndicators();
            }
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("BlockPropertiesWindow.OpenIndicatorPropertiesWindow", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose("Editor", $"Otvorenie vlastností indikátora zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
        }
    }

    // ── Markery ───────────────────────────────────────────────────────────────

    private void DrawMarkers()
    {
        if (_vm == null) return;
        var diagram = this.FindControl<Canvas>("DiagramCanvas");
        if (diagram == null) return;

        // Vyčisti staré vizuály markerov
        foreach (var mc in _markerVisuals.Values)
            diagram.Children.Remove(mc);
        _markerVisuals.Clear();
        _markerTexts.Clear();

        if (_vm.LengthMm <= 0 || !_vm.HasIndicators) return;

        // NOVÉ: Vykresli markery zo všetkých indikátorov
        foreach (var indicator in _vm.Indicators)
        {
            foreach (var marker in indicator.Markers)
            {
                DrawMarkerFromIndicator(diagram, marker);
            }
        }
    }

    /// <summary>
    /// Vykreslí marker z indikátora na canvas
    /// </summary>
    private void DrawMarkerFromIndicator(Canvas diagram, IndicatorMarkerViewModel marker)
    {
        if (_vm == null) return;
        
        const double markerHeight = 20.0;
        const double gap = 2.0;
        
        // Pozícia markera v bloku (absolútna)
        int absolutePosCm = marker.AbsolutePositionCm;
        int absoluteEndCm = marker.AbsoluteEndCm;
        
        // Typ a smer určujú pozíciu
        bool isForward = marker.Direction == MarkerDirection.Forward;
        
        // Index podľa typu (Distance=0, Braking=1, Stop=2, Action=3)
        int markerIndex = marker.Type switch
        {
            MarkerType.Distance => 0,
            MarkerType.Braking => 1,
            MarkerType.Stop => 2,
            MarkerType.Action => 3,
            _ => 0
        };
        
        double blockTop = isForward ? FwdTop : BwdTop;
        double topY = blockTop + gap + markerIndex * (markerHeight + gap);
        
        // X pozícia podľa absolútnej pozície v bloku
        double frac = _vm.LengthMm > 0 ? System.Math.Clamp((double)absolutePosCm / _vm.LengthMm, 0, 1) : 0;
        double x = isForward ? DiagLeft + frac * DiagWidth : DiagLeft + (1.0 - frac) * DiagWidth;
        
        bool isSel = marker.IsSelected;

        // Canvas pre marker
        var mc = new Canvas { Width = 35, Height = 20, ZIndex = 20 };

        // Farba a shape podľa typu
        var brush = new SolidColorBrush(Color.Parse(marker.ColorHex));
        
        // Path s geometriou
        mc.Children.Add(new Path
        {
            Fill = brush,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Data = Avalonia.Media.Geometry.Parse(marker.PathData),
        });

        // Text zobrazujúci "Vzdialenosť:Dojazd"
        string labelText = $"{marker.PositionCm:0}:{marker.EndPositionCm:0}";
        var textBlock = new TextBlock
        {
            Text = labelText,
            Foreground = Brushes.White,
            FontSize = 10,
            FontWeight = Avalonia.Media.FontWeight.Normal,
            Width = 35,
            Height = 20,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Canvas.SetLeft(textBlock, 0);
        Canvas.SetTop(textBlock, 3);
        mc.Children.Add(textBlock);

        // Uložíme pre prípadnú aktualizáciu textu
        string markerKey = marker.Id.ToString();
        _markerTexts[markerKey] = textBlock;

        // Selection highlight
        if (isSel)
        {
            mc.Children.Add(new Path
            {
                Fill = Brushes.Transparent,
                Stroke = Brushes.Yellow,
                StrokeThickness = 2.5,
                Data = Avalonia.Media.Geometry.Parse(marker.PathData),
            });
        }

        // Pozícia: anchor point na ľavej/pravej hrane
        double anchorX = isForward ? 0 : 35;
        Canvas.SetLeft(mc, x - anchorX);
        Canvas.SetTop(mc, topY);

        // Interakcia
        var capturedMarker = marker;
        var capturedText = textBlock;

        mc.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(mc).Properties.IsLeftButtonPressed) return;
            _vm?.SelectMarkerInIndicatorCommand.Execute(capturedMarker);
            _draggedMarkerCanvas = mc;
            _draggedMarkerKey = markerKey;
            e.Pointer.Capture(mc);
            e.Handled = true;
        };

        mc.PointerMoved += (_, e) =>
        {
            if (_draggedMarkerCanvas != mc) return;
            if (_vm == null || _vm.LengthMm <= 0) return;
            
            // Drag marker v rámci jeho indikátora
            var parentIndicator = _vm.Indicators.FirstOrDefault(i => i.Markers.Contains(capturedMarker));
            if (parentIndicator == null) return;
            
            double rawX = System.Math.Clamp(e.GetPosition(diagram).X, DiagLeft, DiagLeft + DiagWidth);
            double dragFrac = (rawX - DiagLeft) / DiagWidth;
            if (!isForward) dragFrac = 1.0 - dragFrac;
            
            int absoluteCm = (int)(dragFrac * _vm.LengthMm);
            
            // Prepočítaj na relatívnu pozíciu v rámci indikátora
            int relativeCm = absoluteCm - parentIndicator.StartCm;
            relativeCm = System.Math.Clamp(relativeCm, 0, parentIndicator.EndCm - parentIndicator.StartCm);
            
            _isDraggingMarker = true;
            capturedMarker.PositionCm = relativeCm;
            
            // Notifikuj ViewModel o zmene pozície
            _vm.NotifySelectedMarkerPositionChanged();
            
            _isDraggingMarker = false;
            
            // Aktualizuj text
            capturedText.Text = $"{capturedMarker.PositionCm:0}:{capturedMarker.EndPositionCm:0}";
            
            // Presuň marker vizuálne
            double newFrac = _vm.LengthMm > 0 ? System.Math.Clamp((double)capturedMarker.AbsolutePositionCm / _vm.LengthMm, 0, 1) : 0;
            double newX = isForward ? DiagLeft + newFrac * DiagWidth : DiagLeft + (1.0 - newFrac) * DiagWidth;
            Canvas.SetLeft(mc, newX - anchorX);
            
            e.Handled = true;
        };

        mc.PointerReleased += (_, e) =>
        {
            if (_draggedMarkerCanvas != mc) return;
            e.Pointer.Capture(null);
            _draggedMarkerCanvas = null;
            _draggedMarkerKey = null;
            e.Handled = true;
        };

        diagram.Children.Add(mc);
        _markerVisuals[markerKey] = mc;
    }

    // ── VM property changed ───────────────────────────────────────────────────

    /// <summary>
    /// Aktualizuje text v markeri
    /// </summary>
    private void UpdateMarkerText(IndicatorMarkerViewModel marker)
    {
        string markerKey = marker.Id.ToString();
        if (_markerTexts.TryGetValue(markerKey, out var textBlock))
        {
            textBlock.Text = $"{marker.PositionCm:0}:{marker.EndPositionCm:0}";
        }
    }

    /// <summary>
    /// Presunie marker na novú pozíciu podľa hodnoty v ViewModel
    /// </summary>
    private void UpdateMarkerPosition(IndicatorMarkerViewModel marker)
    {
        if (_vm == null) return;
        
        string markerKey = marker.Id.ToString();
        if (!_markerVisuals.TryGetValue(markerKey, out var markerCanvas)) return;
        
        bool isForward = marker.Direction == MarkerDirection.Forward;
        
        // Vypočítaj novú pozíciu
        double frac = _vm.LengthMm > 0 ? Math.Clamp((double)marker.AbsolutePositionCm / _vm.LengthMm, 0, 1) : 0;
        double newX = isForward ? DiagLeft + frac * DiagWidth : DiagLeft + (1.0 - frac) * DiagWidth;
        
        // Anchor point (kde je "hrot" šípky)
        double anchorX = isForward ? 0 : 35;
        
        // Presuň marker
        Canvas.SetLeft(markerCanvas, newX - anchorX);
    }

    private static readonly System.Collections.Generic.HashSet<string> _markerRedrawProps = new()
    {
        nameof(BlockPropertiesViewModel.FwdDistanceActive), nameof(BlockPropertiesViewModel.FwdBrakingActive),
        nameof(BlockPropertiesViewModel.FwdStopActive),     nameof(BlockPropertiesViewModel.FwdActionActive),
        nameof(BlockPropertiesViewModel.BwdDistanceActive), nameof(BlockPropertiesViewModel.BwdBrakingActive),
        nameof(BlockPropertiesViewModel.BwdStopActive),     nameof(BlockPropertiesViewModel.BwdActionActive),
        nameof(BlockPropertiesViewModel.FwdDistanceCm),     nameof(BlockPropertiesViewModel.FwdBrakingCm),
        nameof(BlockPropertiesViewModel.FwdStopCm),         nameof(BlockPropertiesViewModel.FwdActionCm),
        nameof(BlockPropertiesViewModel.BwdDistanceCm),     nameof(BlockPropertiesViewModel.BwdBrakingCm),
        nameof(BlockPropertiesViewModel.BwdStopCm),         nameof(BlockPropertiesViewModel.BwdActionCm),
        nameof(BlockPropertiesViewModel.FwdDistanceEndCm),  nameof(BlockPropertiesViewModel.FwdBrakingEndCm),
        nameof(BlockPropertiesViewModel.FwdStopEndCm),      nameof(BlockPropertiesViewModel.FwdActionEndCm),
        nameof(BlockPropertiesViewModel.BwdDistanceEndCm),  nameof(BlockPropertiesViewModel.BwdBrakingEndCm),
        nameof(BlockPropertiesViewModel.BwdStopEndCm),      nameof(BlockPropertiesViewModel.BwdActionEndCm),
        nameof(BlockPropertiesViewModel.SelectedMarkerKey),
        nameof(BlockPropertiesViewModel.MarkerStartCm),     nameof(BlockPropertiesViewModel.MarkerEndCm),
    };

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BlockPropertiesViewModel.LengthMm))
        {
            DrawScales();
            if (!_isDraggingMarker && !_isResizingIndicator) 
            {
                DrawIndicators();
                DrawMarkers();
            }
        }
        else if (e.PropertyName == nameof(BlockPropertiesViewModel.BlockName))
        {
            // Prekreslenie indikátorov kvôli aktualizácii ToolTipov s novými názvami
            DrawIndicators();
        }
        else if (e.PropertyName == nameof(BlockPropertiesViewModel.HasIndicators) || 
                 e.PropertyName == nameof(BlockPropertiesViewModel.BlockFillBrush))
        {
            // Refresh pri zmene indikátorov
            if (!_isResizingIndicator)
            {
                DrawIndicators();
            }
        }
        else if (e.PropertyName == nameof(BlockPropertiesViewModel.SelectedIndicatorMarkers) ||
                 e.PropertyName == nameof(BlockPropertiesViewModel.SelectedMarker))
        {
            // Refresh pri zmene markerov v indikátore
            if (!_isDraggingMarker)
            {
                DrawMarkers();
            }
        }
        else if (e.PropertyName == nameof(BlockPropertiesViewModel.SelectedMarkerPositionCm) ||
                 e.PropertyName == nameof(BlockPropertiesViewModel.SelectedMarkerEndCm))
        {
            // Aktualizuj marker pri zmene hodnoty v spineri
            if (!_isDraggingMarker && _vm?.SelectedMarker != null)
            {
                // Aktualizuj text v markeri
                UpdateMarkerText(_vm.SelectedMarker);
                
                // Presuň marker na novú pozíciu
                UpdateMarkerPosition(_vm.SelectedMarker);
            }
        }
        else if (_markerRedrawProps.Contains(e.PropertyName ?? "") && !_isDraggingMarker)
        {
            DrawMarkers();
        }
    }
}

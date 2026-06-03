using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Editor;
using TrackFlow.Views.Editor.Markers;

namespace TrackFlow.Views.Editor;

// Wrapper trieda pre položky ComboBoxu
public class TurnoutStateItem
{
    public TurnoutState State { get; set; }
    public string Label { get; set; } = "";
    
    // Factory funkcia pre vytvorenie vizuálu - volá sa zakaždým keď je potrebný
    public Func<Control>? CreateVisual { get; set; }
    
    // Property ktorá vytvorí nový vizuál zakaždým keď je prístupná
    public Control Visual => CreateVisual?.Invoke() ?? new Border();
}

public partial class TurnoutPropertiesWindow : Window
{
    private TurnoutPropertiesViewModel? _boundViewModel;
    private DispatcherTimer? _indicatorRefreshTimer;

    public TurnoutPropertiesWindow()
    {
        AvaloniaXamlLoader.Load(this);
        AttachEventHandlers();
    }

    private void AttachEventHandlers()
    {
        this.DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel != null)
            _boundViewModel.CloseRequested -= OnCloseRequested;

        _boundViewModel = null;
        _indicatorRefreshTimer?.Stop();

        if (DataContext is TurnoutPropertiesViewModel vm)
        {
            _boundViewModel = vm;
            vm.CloseRequested += OnCloseRequested;
            
            // Vytvoríme vizualizáciu default stavu
            BuildInitialStateComboBox(vm);
            
            // Nastavíme funkčnosť indikátorov v záložke "Indikátory"
            SetupIndicatorListBoxes(vm);

            // Dialóg má reflektovať živý stav indikátorov (aktívny/neaktívny) počas otvorenia.
            _indicatorRefreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _indicatorRefreshTimer.Tick -= OnIndicatorRefreshTimerTick;
            _indicatorRefreshTimer.Tick += OnIndicatorRefreshTimerTick;
            _indicatorRefreshTimer.Start();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _indicatorRefreshTimer?.Stop();
        if (_boundViewModel != null)
            _boundViewModel.CloseRequested -= OnCloseRequested;
        _boundViewModel = null;
        base.OnClosed(e);
    }

    private void OnIndicatorRefreshTimerTick(object? sender, EventArgs e)
    {
        _boundViewModel?.RefreshSensorStates();
    }

    private void OnCloseRequested(bool saved)
    {
        Close(saved);
    }

    private void BuildInitialStateComboBox(TurnoutPropertiesViewModel vm)
    {
        var combo = this.FindControl<ComboBox>("InitialStateCombo");
        if (combo == null) return;

        // Vytvoríme položky s vizualizáciou výhybky podľa typu
        var items = new System.Collections.Generic.List<TurnoutStateItem>();
        
        // Pre trojcestnú výhybku
        if (vm.TurnoutTypeName == "Výhybka trojcestná")
        {
            items.Add(new TurnoutStateItem 
            { 
                State = TurnoutState.Straight, 
                Label = "Rovno",
                CreateVisual = () => CreateTurnoutVisual(vm, TurnoutState.Straight)
            });
            items.Add(new TurnoutStateItem 
            { 
                State = TurnoutState.DivergeLeft, 
                Label = "Do ľava",
                CreateVisual = () => CreateTurnoutVisual(vm, TurnoutState.DivergeLeft)
            });
            items.Add(new TurnoutStateItem 
            { 
                State = TurnoutState.DivergeRight, 
                Label = "Do prava",
                CreateVisual = () => CreateTurnoutVisual(vm, TurnoutState.DivergeRight)
            });
        }
        // Pre výhybku Doubleslip
        else if (vm.TurnoutTypeName == "Výhybka dvojitá krížová")
        {
            items.Add(new TurnoutStateItem 
            { 
                State = TurnoutState.Straight, 
                Label = "Rovno",
                CreateVisual = () => CreateTurnoutVisual(vm, TurnoutState.Straight)
            });
            items.Add(new TurnoutStateItem 
            { 
                State = TurnoutState.Cross, 
                Label = "Krížom",
                CreateVisual = () => CreateTurnoutVisual(vm, TurnoutState.Cross)
            });
            items.Add(new TurnoutStateItem 
            { 
                State = TurnoutState.DivergeRight, 
                Label = "Doprava",
                CreateVisual = () => CreateTurnoutVisual(vm, TurnoutState.DivergeRight)
            });
            items.Add(new TurnoutStateItem 
            { 
                State = TurnoutState.DivergeLeft, 
                Label = "Doľava",
                CreateVisual = () => CreateTurnoutVisual(vm, TurnoutState.DivergeLeft)
            });
        }
        // Pre ostatné výhybky (2-cestné)
        else
        {
            items.Add(new TurnoutStateItem 
            { 
                State = TurnoutState.Straight, 
                Label = "Priamo",
                CreateVisual = () => CreateTurnoutVisual(vm, TurnoutState.Straight)
            });
            items.Add(new TurnoutStateItem 
            { 
                State = TurnoutState.Diverge, 
                Label = "Odbočka",
                CreateVisual = () => CreateTurnoutVisual(vm, TurnoutState.Diverge)
            });
        }

        combo.ItemsSource = items;
        
        // Nastavíme aktuálny výber
        if (vm.TurnoutTypeName == "Výhybka trojcestná")
        {
            combo.SelectedIndex = vm.InitialState switch
            {
                TurnoutState.Straight => 0,
                TurnoutState.DivergeLeft => 1,
                TurnoutState.DivergeRight => 2,
                _ => 0
            };
        }
        else if (vm.TurnoutTypeName == "Výhybka dvojitá krížová")
        {
            combo.SelectedIndex = vm.InitialState switch
            {
                TurnoutState.Straight => 0,
                TurnoutState.Cross => 1,
                TurnoutState.DivergeRight => 2,
                TurnoutState.DivergeLeft => 3,
                _ => 0
            };
        }
        else
        {
            combo.SelectedIndex = vm.InitialState == TurnoutState.Straight ? 0 : 1;
        }
        
        // Pri zmene výberu aktualizujeme ViewModel
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0 || combo.SelectedItem is not TurnoutStateItem selectedItem) 
                return;
            
            vm.InitialState = selectedItem.State;
        };
    }
    private static Control CreateTurnoutVisual(TurnoutPropertiesViewModel vm, TurnoutState highlightState)
    {
        // Určíme typ markera z TurnoutTypeName
        var markerKey = GetMarkerKeyFromViewModel(vm);
        
        // Vytvoríme zmenšenú verziu markera
        Control? marker = markerKey switch
        {
            "Turnout_L" => new MarkerTurnoutL(),
            "Turnout_R" => new MarkerTurnoutR(),
            "TurnoutL90" => new MarkerTurnoutL90(),
            "TurnoutR90" => new MarkerTurnoutR90(),
            "TurnoutCurve_L" => new MarkerTurnoutCurveL(),
            "TurnoutCurve_R" => new MarkerTurnoutCurveR(),
            "Turnout_Y" => new MarkerTurnoutY(),
            "Turnout_3W" => new MarkerTurnout3W(),
            "DoubleSlip" => new MarkerDoubleSlip(),
            _ => new MarkerTurnoutL()  // fallback
        };

        // Najprv pridáme čierne stroke obrysy
        AddBlackStrokes(marker);
        
        // Aplikujeme farebné zvýraznenie podľa stavu
        ApplyStateColoring(marker, highlightState);

        // Nastavíme veľkosť (zmenšená verzia - 1.5x)
        var container = new Border
        {
            Width = 36,
            Height = 22,
            ClipToBounds = true,
            Background = Brushes.White,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(2)
        };

        // Vytvoríme scaled down verziu
        var viewbox = new Viewbox
        {
            Width = 36,
            Height = 22,
            Stretch = Stretch.Uniform,
            Child = marker
        };

        container.Child = viewbox;
        return container;
    }

    private static string GetMarkerKeyFromViewModel(TurnoutPropertiesViewModel vm)
    {
        return vm.TurnoutTypeName switch
        {
            "Výhybka ľavá" => "Turnout_L",
            "Výhybka pravá" => "Turnout_R",
            "Výhybka oblúková ľavá" => "TurnoutCurve_L",
            "Výhybka oblúková pravá" => "TurnoutCurve_R",
            "Výhybka ľavá 90°" => "TurnoutL90",
            "Výhybka pravá 90°" => "TurnoutR90",
            "Výhybka Y" => "Turnout_Y",
            "Výhybka trojcestná" => "Turnout_3W",
            "Výhybka dvojitá krížová" => "DoubleSlip",
            _ => "Turnout_L"
        };
    }

    private static void AddBlackStrokes(Control marker)
    {
        if (marker is not UserControl uc) return;
        if (uc.Content is not Canvas canvas) return;

        var blackColor = Brushes.Black;
        var strokeThickness = 0.3; // Tenký čierny obrys

        // Prechádzame všetky existujúce elementy a pridáme im čierny obrys
        var elementsToOutline = new System.Collections.Generic.List<Shape>();
        
        foreach (var child in canvas.Children)
        {
            if (child is Line line)
            {
                elementsToOutline.Add(line);
            }
            else if (child is Path path)
            {
                elementsToOutline.Add(path);
            }
        }

        // Pre každý element vytvoríme čierny obrys
        foreach (var shape in elementsToOutline)
        {
            Shape? outlineShape = null;
            
            if (shape is Line line)
            {
                outlineShape = new Line
                {
                    StartPoint = line.StartPoint,
                    EndPoint = line.EndPoint,
                    Stroke = blackColor,
                    StrokeThickness = line.StrokeThickness + strokeThickness * 2,
                    StrokeLineCap = line.StrokeLineCap,
                    UseLayoutRounding = false,
                    ZIndex = -1 // Pod farebnou čiarou
                };
                RenderOptions.SetEdgeMode(outlineShape, EdgeMode.Antialias);
            }
            else if (shape is Path path)
            {
                outlineShape = new Path
                {
                    Data = path.Data,
                    Stroke = blackColor,
                    StrokeThickness = path.StrokeThickness + strokeThickness * 2,
                    Fill = Brushes.Transparent,
                    StrokeLineCap = path.StrokeLineCap,
                    UseLayoutRounding = false,
                    Stretch = path.Stretch,
                    ZIndex = -1 // Pod farebným Path
                };
                RenderOptions.SetEdgeMode(outlineShape, EdgeMode.Antialias);
            }
            
            if (outlineShape != null)
            {
                // Pridáme čierny obrys pred farebný element
                var index = canvas.Children.IndexOf(shape);
                canvas.Children.Insert(index, outlineShape);
            }
        }
    }

    private static void ApplyStateColoring(Control marker, TurnoutState highlightState)
    {
        if (marker is not UserControl uc) return;
        if (uc.Content is not Canvas canvas) return;

        var blueColor = new SolidColorBrush(Color.Parse("#71c5ff"));  // Modrá - aktívna cesta
        var grayColor = new SolidColorBrush(Color.Parse("#888888"));   // Šedá - neaktívne cesty

        foreach (var child in canvas.Children)
        {
            // Pre Line elementy
            if (child is Line line)
            {
                // Preskočíme čierne obrysy (majú ZIndex = -1)
                if (line.ZIndex == -1) continue;

                // Line1 - priamy smer (vertikálna/horizontálna čiara)
                if (line.Name == "Line1" || IsVerticalOrHorizontalLine(line))
                {
                    bool isActive = highlightState == TurnoutState.Straight;
                    line.Stroke = isActive ? blueColor : grayColor;
                    line.StrokeThickness = 2;
                    line.ZIndex = isActive ? 10 : 1;
                }
                // Line2 - krížový smer (šikmá čiara pre Doubleslip)
                else if (line.Name == "Line2")
                {
                    bool isActive = highlightState == TurnoutState.Cross;
                    line.Stroke = isActive ? blueColor : grayColor;
                    line.StrokeThickness = 2;
                    line.ZIndex = isActive ? 10 : 1;
                }
                // Odbočka (šikmá čiara pre ostatné výhybky)
                else
                {
                    bool isActive = highlightState == TurnoutState.Diverge;
                    line.Stroke = isActive ? blueColor : grayColor;
                    line.StrokeThickness = 2;
                    line.ZIndex = isActive ? 10 : 1;
                }
            }
            // Pre Path elementy (oblúky)
            else if (child is Path path)
            {
                // Preskočíme čierne obrysy (majú ZIndex = -1)
                if (path.ZIndex == -1) continue;

                // Pre trojcestnú výhybku a Doubleslip
                if (path.Name == "ArcLPath")
                {
                    // Ľavý oblúk - modrý ak DivergeLeft, šedý inak
                    bool isActive = highlightState == TurnoutState.DivergeLeft;
                    path.Stroke = isActive ? blueColor : grayColor;
                    path.StrokeThickness = 2;
                    path.ZIndex = isActive ? 10 : 1;
                }
                else if (path.Name == "ArcRPath")
                {
                    // Pravý oblúk - modrý ak DivergeRight, šedý inak
                    bool isActive = highlightState == TurnoutState.DivergeRight;
                    path.Stroke = isActive ? blueColor : grayColor;
                    path.StrokeThickness = 2;
                    path.ZIndex = isActive ? 10 : 1;
                }
                // Pre ostatné oblúkové výhybky
                else
                {
                    bool isActive = highlightState == TurnoutState.Diverge;
                    path.Stroke = isActive ? blueColor : grayColor;
                    path.StrokeThickness = 2;
                    path.ZIndex = isActive ? 10 : 1;
                }
            }
        }
        
        // Pre trojcestnú výhybku - špeciálne spracovanie strednej čiary (CenterLine)
        var centerLine = canvas.Children.OfType<Line>().FirstOrDefault(l => l.Name == "CenterLine");
        if (centerLine != null && centerLine.ZIndex != -1)
        {
            bool isActive = highlightState == TurnoutState.Straight;
            centerLine.Stroke = isActive ? blueColor : grayColor;
            centerLine.StrokeThickness = 2;
            centerLine.ZIndex = isActive ? 10 : 1;
        }
    }

    private static bool IsVerticalOrHorizontalLine(Line line)
    {
        // Kontrola či je čiara približne horizontálna alebo vertikálna
        var dy = Math.Abs(line.EndPoint.Y - line.StartPoint.Y);
        var dx = Math.Abs(line.EndPoint.X - line.StartPoint.X);
        
        // Vertikálna čiara
        if (dx < 2 && dy > 5)
            return true;
            
        // Horizontálna čiara
        if (dy < 2 && dx > 5)
            return true;
            
        return false;
    }

    private void SetupIndicatorListBoxes(TurnoutPropertiesViewModel vm)
    {
        var availableListBox = this.FindControl<ListBox>("AvailableIndicatorsListBox");
        var assignedListBox = this.FindControl<ListBox>("AssignedIndicatorsListBox");
        var addButton = this.FindControl<Button>("AddIndicatorButton");
        var removeButton = this.FindControl<Button>("RemoveIndicatorButton");

        if (availableListBox == null || assignedListBox == null || addButton == null || removeButton == null)
            return;

        // Rozdelíme indikátory na dostupné a priradené
        var available = new System.Collections.ObjectModel.ObservableCollection<SensorItem>();
        var assigned = new System.Collections.ObjectModel.ObservableCollection<SensorItem>();

        foreach (var sensor in vm.AvailableSensors)
        {
            System.Diagnostics.Debug.WriteLine($"[TurnoutPropertiesWindow] Sensor: {sensor.Name}, IconPath: {sensor.IconPath}, IsSelected: {sensor.IsSelected}");
            if (sensor.IsSelected)
                assigned.Add(sensor);
            else
                available.Add(sensor);
        }
        
        System.Diagnostics.Debug.WriteLine($"[TurnoutPropertiesWindow] Available: {available.Count}, Assigned: {assigned.Count}");

        availableListBox.ItemsSource = available;
        assignedListBox.ItemsSource = assigned;

        // Handler pre pridanie indikátora
        addButton.Click += (_, _) =>
        {
            var selected = availableListBox.SelectedItems?.Cast<SensorItem>().ToList();
            if (selected == null || selected.Count == 0) return;

            foreach (var item in selected)
            {
                available.Remove(item);
                assigned.Add(item);
                item.IsSelected = true;
            }
        };

        // Handler pre odstránenie indikátora
        removeButton.Click += (_, _) =>
        {
            var selected = assignedListBox.SelectedItems?.Cast<SensorItem>().ToList();
            if (selected == null || selected.Count == 0) return;

            foreach (var item in selected)
            {
                assigned.Remove(item);
                available.Add(item);
                item.IsSelected = false;
            }
        };
    }
}














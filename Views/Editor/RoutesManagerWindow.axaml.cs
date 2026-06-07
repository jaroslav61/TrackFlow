using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Editor;
using TrackFlow.Views.Editor.Markers;

namespace TrackFlow.Views.Editor;

public partial class RoutesManagerWindow : Window
{
    // Referencia na VM – umožňuje korektné odhlásenie pri zmene DataContextu
    private RoutesManagerViewModel? _subscribedVm;

    public RoutesManagerWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var preview = this.FindControl<RoutePreviewControl>("RoutePreview");
        if (preview != null)
            preview.BlockClicked += OnPreviewBlockClicked;

        UpdateRoutePreview();
        UpdateManualSelectionUi();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Odhlásenie zo starého ViewModelu pred prihlásením na nový.
        if (_subscribedVm != null)
            _subscribedVm.RoutePreviewChanged -= OnRoutePreviewChanged;

        _subscribedVm = null;

        if (DataContext is RoutesManagerViewModel vm)
        {
            _subscribedVm = vm;
            vm.RoutePreviewChanged += OnRoutePreviewChanged;
            UpdateRoutePreview(vm);
            UpdateManualSelectionUi(vm);
        }
        else
        {
            UpdateManualSelectionUi();
        }
    }

    /// <summary>Odhlásenie z udalostí pri zatváraní okna – predchádza memory leaku.</summary>
    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        var preview = this.FindControl<RoutePreviewControl>("RoutePreview");
        if (preview != null)
            preview.BlockClicked -= OnPreviewBlockClicked;

        if (_subscribedVm != null)
            _subscribedVm.RoutePreviewChanged -= OnRoutePreviewChanged;

        _subscribedVm = null;
        UpdateManualSelectionUi();
    }

    private void UpdateRoutePreview()
    {
        if (DataContext is not RoutesManagerViewModel vm) return;
        UpdateRoutePreview(vm);
    }

    private void UpdateRoutePreview(RoutesManagerViewModel vm)
    {
        var state = vm.GetRoutePreviewState();

        var preview = this.FindControl<RoutePreviewControl>("RoutePreview");
        if (preview == null) return;

        preview.SetLayoutAndRoute(state.Layout, state.SelectedRoute, state.ManualStartBlockId, state.ManualEndBlockId);
    }

    private void OnRoutePreviewChanged(object? sender, EventArgs e)
    {
        if (sender is RoutesManagerViewModel vm)
        {
            UpdateRoutePreview(vm);
            UpdateManualSelectionUi(vm);
        }
        else
        {
            UpdateRoutePreview();
            UpdateManualSelectionUi();
        }
    }

    private void UpdateManualSelectionUi()
    {
        if (DataContext is RoutesManagerViewModel vm)
        {
            UpdateManualSelectionUi(vm);
            return;
        }

        var preview = this.FindControl<RoutePreviewControl>("RoutePreview");
        if (preview != null)
            preview.Cursor = new Cursor(StandardCursorType.Arrow);

        var host = this.FindControl<Border>("RoutePreviewHost");
        if (host != null)
        {
            host.BorderBrush = new SolidColorBrush(Color.Parse("#D0D0D0"));
            host.BorderThickness = new Thickness(1);
        }
    }

    private void UpdateManualSelectionUi(RoutesManagerViewModel vm)
    {
        var isManualSelection = vm.IsManualRouteSelectionActive;

        var preview = this.FindControl<RoutePreviewControl>("RoutePreview");
        if (preview != null)
            preview.Cursor = new Cursor(isManualSelection ? StandardCursorType.Hand : StandardCursorType.Arrow);

        var host = this.FindControl<Border>("RoutePreviewHost");
        if (host != null)
        {
            host.BorderBrush = new SolidColorBrush(Color.Parse(isManualSelection ? "#FB8C00" : "#D0D0D0"));
            host.BorderThickness = new Thickness(isManualSelection ? 2 : 1);
        }
    }

    private void OnPreviewBlockClicked(string blockId)
    {
        if (DataContext is not RoutesManagerViewModel vm)
            return;

        vm.HandlePreviewBlockClicked(blockId);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TurnoutVisualHost_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContentControl host) return;
        if (host.DataContext is not TurnoutSettingVm turnoutVm) return;
        host.Content = CreateTurnoutVisual(turnoutVm.TurnoutType, turnoutVm.RequiredState);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Vykresľovanie vizuálov výhybiek
    // ════════════════════════════════════════════════════════════════════════

    private static Control CreateTurnoutVisual(string markerKey, TurnoutState highlightState)
    {
        Control marker;
        switch (markerKey)
        {
            case "Turnout_L": marker = new MarkerTurnoutL(); break;
            case "Turnout_R": marker = new MarkerTurnoutR(); break;
            case "TurnoutL90": marker = new MarkerTurnoutL90(); break;
            case "TurnoutR90": marker = new MarkerTurnoutR90(); break;
            case "TurnoutCurve_L": marker = new MarkerTurnoutCurveL(); break;
            case "TurnoutCurve_R": marker = new MarkerTurnoutCurveR(); break;
            case "Turnout_Y": marker = new MarkerTurnoutY(); break;
            case "Turnout_3W": marker = new MarkerTurnout3W(); break;
            case "DoubleSlip": marker = new MarkerDoubleSlip(); break;
            default: marker = new MarkerTurnoutL(); break;
        }

        AddBlackStrokes(marker);
        ApplyStateColoring(marker, highlightState);

        var container = new Border
        {
            Width = 36, Height = 22,
            ClipToBounds = true,
            Background = Brushes.White,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(2)
        };
        container.Child = new Viewbox
        {
            Width = 36, Height = 22,
            Stretch = Stretch.Uniform,
            Child = marker
        };
        return container;
    }

    /// <summary>
    ///     Pridá čierne obrysové čiary pod každý tvar v Canvase markera.
    ///     Bezpečne skončí ak marker nemá očakávanú štruktúru (UserControl → Canvas).
    /// </summary>
    private static void AddBlackStrokes(Control marker)
    {
        if (marker is not UserControl uc) return;
        if (uc.Content is not Canvas canvas) return;

        const double strokeExtra = 0.6; // rozšírenie hrúbky obrysu
        var blackBrush = Brushes.Black;

        // Najprv zozbierame tvary, aby sme sa vyhli modifikácii kolekcie počas iterácie
        var shapes = canvas.Children.OfType<Shape>()
            .Where(s => s is Line or Path)
            .ToList();

        foreach (var shape in shapes)
            try
            {
                Shape? outline = shape switch
                {
                    Line line => new Line
                    {
                        StartPoint = line.StartPoint,
                        EndPoint = line.EndPoint,
                        Stroke = blackBrush,
                        StrokeThickness = line.StrokeThickness + strokeExtra,
                        StrokeLineCap = line.StrokeLineCap,
                        UseLayoutRounding = false,
                        ZIndex = -1
                    },
                    Path path => new Path
                    {
                        Data = path.Data,
                        Stroke = blackBrush,
                        StrokeThickness = path.StrokeThickness + strokeExtra,
                        Fill = Brushes.Transparent,
                        StrokeLineCap = path.StrokeLineCap,
                        UseLayoutRounding = false,
                        Stretch = path.Stretch,
                        ZIndex = -1
                    },
                    _ => null
                };

                if (outline == null)
                    continue;

                RenderOptions.SetEdgeMode(outline, EdgeMode.Antialias);

                var idx = canvas.Children.IndexOf(shape);
                if (idx >= 0)
                    canvas.Children.Insert(idx, outline);
            }
            catch
            {
                // Neočakávaná štruktúra elementu – ticho preskočíme
            }
    }

    /// <summary>
    ///     Aplikuje farebné zvýraznenie aktívnej/neaktívnej vetvy podľa stavu výhybky.
    ///     Bezpečne skončí ak marker nemá očakávanú štruktúru.
    /// </summary>
    private static void ApplyStateColoring(Control marker, TurnoutState highlightState)
    {
        if (marker is not UserControl uc) return;
        if (uc.Content is not Canvas canvas) return;

        var blue = new SolidColorBrush(Color.Parse("#71c5ff"));
        var gray = new SolidColorBrush(Color.Parse("#888888"));

        foreach (var child in canvas.Children)
            try
            {
                if (child is Line line && line.ZIndex != -1)
                {
                    var active = IsVerticalOrHorizontalLine(line)
                        ? highlightState == TurnoutState.Straight
                        : highlightState == TurnoutState.Diverge;
                    line.Stroke = active ? blue : gray;
                    line.StrokeThickness = 2;
                    line.ZIndex = active ? 10 : 1;
                }
                else if (child is Path path && path.ZIndex != -1)
                {
                    var active = path.Name switch
                    {
                        "ArcLPath" => highlightState == TurnoutState.DivergeLeft,
                        "ArcRPath" => highlightState == TurnoutState.DivergeRight,
                        _ => highlightState == TurnoutState.Diverge
                    };
                    path.Stroke = active ? blue : gray;
                    path.StrokeThickness = 2;
                    path.ZIndex = active ? 10 : 1;
                }
            }
            catch
            {
                // Neočakávaná štruktúra elementu – preskočíme
            }

        // Osobitné ošetrenie CenterLine (používa sa u Y-výhybiek)
        var centerLine = canvas.Children.OfType<Line>()
            .FirstOrDefault(l => l.Name == "CenterLine");
        if (centerLine is { ZIndex: not -1 })
        {
            var active = highlightState == TurnoutState.Straight;
            centerLine.Stroke = active ? blue : gray;
            centerLine.StrokeThickness = 2;
            centerLine.ZIndex = active ? 10 : 1;
        }
    }

    private static bool IsVerticalOrHorizontalLine(Line line)
    {
        var dy = Math.Abs(line.EndPoint.Y - line.StartPoint.Y);
        var dx = Math.Abs(line.EndPoint.X - line.StartPoint.X);
        return (dx < 2 && dy > 5) || (dy < 2 && dx > 5);
    }
}
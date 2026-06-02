using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using TrackFlow.ViewModels.Editor;

namespace TrackFlow.Views.Editor;

/// <summary>
/// Behavior pre marquee (obdĺžnikový) výber v LayoutEditore.
/// </summary>
public class MarqueeSelectionBehavior
{
    private readonly LayoutEditorView _view;
    private readonly Canvas _canvas;
    private readonly Canvas _marqueeLayer;
    private Rectangle? _marqueeRect;
    private bool _isMarqueeDragging;

    public MarqueeSelectionBehavior(LayoutEditorView view, Canvas canvas, Canvas marqueeLayer)
    {
        _view = view;
        _canvas = canvas;
        _marqueeLayer = marqueeLayer;
    }

    /// <summary>Začne marquee výber.</summary>
    public void StartMarquee(Point position, LayoutEditorViewModel vm)
    {
        _isMarqueeDragging = true;
        vm.Selection.StartMarquee(position.X, position.Y);

        // Vytvoríme vizuálny obdĺžnik marquee
        _marqueeRect = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.Parse("#1565C0")),
            StrokeThickness = 1,
            Fill = new SolidColorBrush(Color.FromArgb(40, 21, 101, 192)),
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 2 },
            IsHitTestVisible = false
        };

        _marqueeLayer.Children.Add(_marqueeRect);
        UpdateMarqueeVisual(vm);
    }

    /// <summary>Aktualizuje marquee výber.</summary>
    public void UpdateMarquee(Point position, LayoutEditorViewModel vm)
    {
        if (!_isMarqueeDragging) return;

        vm.Selection.UpdateMarquee(position.X, position.Y);
        UpdateMarqueeVisual(vm);
    }

    /// <summary>Ukončí marquee výber.</summary>
    public void EndMarquee(LayoutEditorViewModel vm, bool addToSelection = false)
    {
        if (!_isMarqueeDragging) return;

        vm.Selection.SelectElementsInMarquee(vm.Elements, LayoutEditorViewModel.CellSize, addToSelection);
        vm.Selection.EndMarquee();

        ClearMarqueeVisual();
        _isMarqueeDragging = false;
    }

    /// <summary>Zruší marquee výber.</summary>
    public void CancelMarquee(LayoutEditorViewModel vm)
    {
        if (!_isMarqueeDragging) return;

        vm.Selection.CancelMarquee();
        ClearMarqueeVisual();
        _isMarqueeDragging = false;
    }

    /// <summary>Aktualizuje vizuálny obdĺžnik marquee.</summary>
    private void UpdateMarqueeVisual(LayoutEditorViewModel vm)
    {
        if (_marqueeRect == null) return;

        var (left, top, width, height) = vm.Selection.GetMarqueeRect();

        Canvas.SetLeft(_marqueeRect, left);
        Canvas.SetTop(_marqueeRect, top);
        _marqueeRect.Width = width;
        _marqueeRect.Height = height;
    }

    /// <summary>Vyčistí vizuálny obdĺžnik marquee.</summary>
    private void ClearMarqueeVisual()
    {
        if (_marqueeRect != null)
        {
            _marqueeLayer.Children.Remove(_marqueeRect);
            _marqueeRect = null;
        }
    }

    public bool IsMarqueeDragging => _isMarqueeDragging;
}

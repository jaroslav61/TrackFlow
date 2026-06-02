using System;
using System.Collections.Generic;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

/// <summary>
/// Service pre správu marquee (obdĺžnikového) výberu markerov v editore layoutu.
/// </summary>
public class MarqueeSelectionService
{
    /// <summary>Aktuálne vybrané prvky.</summary>
    public HashSet<LayoutElement> SelectedElements { get; } = new();

    /// <summary>Event vyvolaný pri zmene výberu.</summary>
    public event Action? SelectionChanged;

    /// <summary>Súradnice začiatku marquee výberu (v px).</summary>
    public double MarqueeStartX { get; private set; }
    public double MarqueeStartY { get; private set; }

    /// <summary>Aktuálne súradnice konca marquee výberu (v px).</summary>
    public double MarqueeEndX { get; private set; }
    public double MarqueeEndY { get; private set; }

    /// <summary>Či je práve aktívny marquee výber.</summary>
    public bool IsMarqueeActive { get; private set; }

    /// <summary>Začne marquee výber na danej pozícii.</summary>
    public void StartMarquee(double x, double y)
    {
        MarqueeStartX = x;
        MarqueeStartY = y;
        MarqueeEndX = x;
        MarqueeEndY = y;
        IsMarqueeActive = true;
    }

    /// <summary>Aktualizuje koncovú pozíciu marquee výberu.</summary>
    public void UpdateMarquee(double x, double y)
    {
        if (!IsMarqueeActive) return;
        MarqueeEndX = x;
        MarqueeEndY = y;
    }

    /// <summary>Ukončí marquee výber a vráti obdĺžnik výberu.</summary>
    public (double left, double top, double width, double height) EndMarquee()
    {
        IsMarqueeActive = false;
        
        double left = Math.Min(MarqueeStartX, MarqueeEndX);
        double top = Math.Min(MarqueeStartY, MarqueeEndY);
        double width = Math.Abs(MarqueeEndX - MarqueeStartX);
        double height = Math.Abs(MarqueeEndY - MarqueeStartY);
        
        return (left, top, width, height);
    }

    /// <summary>Vráti obdĺžnik aktuálneho marquee výberu.</summary>
    public (double left, double top, double width, double height) GetMarqueeRect()
    {
        double left = Math.Min(MarqueeStartX, MarqueeEndX);
        double top = Math.Min(MarqueeStartY, MarqueeEndY);
        double width = Math.Abs(MarqueeEndX - MarqueeStartX);
        double height = Math.Abs(MarqueeEndY - MarqueeStartY);
        
        return (left, top, width, height);
    }

    /// <summary>Zruší marquee výber bez zmeny vybraných prvkov.</summary>
    public void CancelMarquee()
    {
        IsMarqueeActive = false;
    }

    /// <summary>Vyberie prvky v obdĺžníku marquee.</summary>
    public void SelectElementsInMarquee(IEnumerable<LayoutElement> allElements, double cellSize, bool addToSelection = false)
    {
        if (!addToSelection)
            SelectedElements.Clear();

        var (left, top, width, height) = GetMarqueeRect();

        foreach (var element in allElements)
        {
            if (IsElementInRect(element, left, top, width, height, cellSize))
            {
                SelectedElements.Add(element);
            }
        }

        SelectionChanged?.Invoke();
    }

    /// <summary>Skontroluje, či prvok zasahuje do daného obdĺžníka.</summary>
    private bool IsElementInRect(LayoutElement element, double rectLeft, double rectTop, double rectWidth, double rectHeight, double cellSize)
    {
        var (elemWidth, elemHeight) = LayoutElementFootprintHelper.GetFootprint(
            element,
            cellSize,
            compactTwoAspectSignals: true);

        // Kontrola prekrytia obdĺžnikov
        double elemRight = element.X + elemWidth;
        double elemBottom = element.Y + elemHeight;
        double rectRight = rectLeft + rectWidth;
        double rectBottom = rectTop + rectHeight;

        return !(element.X >= rectRight || elemRight <= rectLeft ||
                 element.Y >= rectBottom || elemBottom <= rectTop);
    }

    /// <summary>Vymaže všetky vybrané prvky.</summary>
    public void ClearSelection()
    {
        if (SelectedElements.Count > 0)
        {
            SelectedElements.Clear();
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>Pridá prvok do výberu.</summary>
    public void AddToSelection(LayoutElement element)
    {
        if (SelectedElements.Add(element))
            SelectionChanged?.Invoke();
    }

    /// <summary>Odstráni prvok z výberu.</summary>
    public void RemoveFromSelection(LayoutElement element)
    {
        if (SelectedElements.Remove(element))
            SelectionChanged?.Invoke();
    }

    /// <summary>Toggle výber prvku.</summary>
    public void ToggleSelection(LayoutElement element)
    {
        if (SelectedElements.Contains(element))
            RemoveFromSelection(element);
        else
            AddToSelection(element);
    }

    /// <summary>Vyberie všetky prvky.</summary>
    public void SelectAll(IEnumerable<LayoutElement> allElements)
    {
        SelectedElements.Clear();
        foreach (var elem in allElements)
            SelectedElements.Add(elem);
        SelectionChanged?.Invoke();
    }

    /// <summary>Či je daný prvok vybraný.</summary>
    public bool IsSelected(LayoutElement element) => SelectedElements.Contains(element);

    /// <summary>Počet vybraných prvkov.</summary>
    public int SelectionCount => SelectedElements.Count;

}

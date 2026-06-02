using System.Text.Json.Serialization;

namespace TrackFlow.Models.Layout;

/// <summary>
/// Textový popis v layoute – ľubovoľný text s formátovaním.
/// Zväčšuje sa/zmenšuje v krokoch gridových buniek.
/// </summary>
public sealed class TextElement : LayoutElement
{
    [JsonIgnore]
    public override LayoutElementType ElementType => LayoutElementType.Text;

    /// <summary>Vlastný text (môže byť viacriadkový).</summary>
    public string Text { get; set; } = "Text";

    /// <summary>Viditeľný len v režime editácie.</summary>
    public bool VisibleInEditModeOnly { get; set; }

    /// <summary>Farba pozadia (hex formát, napr. "#FFFFFF" alebo prázdne = transparentné).</summary>
    public string BackgroundColor { get; set; } = string.Empty;

    /// <summary>Názov fontu (napr. "Arial", "Segoe UI").</summary>
    public string FontName { get; set; } = "Segoe UI";

    /// <summary>Veľkosť fontu v px.</summary>
    public double FontSize { get; set; } = 12;

    /// <summary>Farba pozadia markeru textu (hex formát, napr. "#FFFFFF" alebo "Automatic").</summary>
    public string FillColor { get; set; } = "Automatic";

    /// <summary>Farba oramovania markeru textu (hex formát alebo "Automatic" / prázdne = bez rámu).</summary>
    public string FrameColor { get; set; } = "Automatic";

    /// <summary>Hrúbka rámu v px (0 = bez rámu, default).</summary>
    public double FrameThickness { get; set; } = 0;

    /// <summary>Horizontálne zarovnanie (Left, Center, Right).</summary>
    public string HorizontalAlignment { get; set; } = "Center";

    /// <summary>Vertikálne zarovnanie (Top, Center, Bottom).</summary>
    public string VerticalAlignment { get; set; } = "Center";

    /// <summary>Šírka v gridových bunkách (min 1).</summary>
    public int WidthInCells { get; set; } = 1;

    /// <summary>Výška v gridových bunkách (min 1).</summary>
    public int HeightInCells { get; set; } = 1;
}



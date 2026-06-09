using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Markup.Xaml;
using TrackFlow.Services;
using TrackFlow.ViewModels;
using TrackFlow.ViewModels.Backstage;
using TrackFlow.Views.Backstage;

namespace TrackFlow.Ribbon;

public partial class MainRibbonView : UserControl
{
    // pamätá poslednú záložku mimo "Súbor"
    private int _lastNonFileIndex = 1;
    private TabControl? _ribbonTabs;

    public MainRibbonView()
    {
        AvaloniaXamlLoader.Load(this);

        _ribbonTabs = this.FindControl<TabControl>("RibbonTabs");
        
        // Klik na tab "Súbor" -> otvor backstage a vráť sa na posledný non-file tab
        if (_ribbonTabs != null)
        {
            _ribbonTabs.SelectionChanged += async (_, _) =>
            {
                if (DataContext is not MainWindowViewModel vm)
                    return;

                var idx = _ribbonTabs.SelectedIndex;
                if (idx < 0) return;

                if (idx == 0)
                {
                    var owner = this.GetVisualRoot() as Window;
                    var dlg = new FileBackstageWindow
                    {
                        DataContext = new FileBackstageViewModel(vm)
                    };
                    TooltipPreferenceService.Attach(dlg);

                    if (owner != null)
                        await dlg.ShowDialog(owner);
                    else
                        dlg.Show();
                    // po zavretí vráť poslednú "normálnu" záložku
                    _ribbonTabs.SelectedIndex = _lastNonFileIndex;
                }
                else
                {
                    _lastNonFileIndex = idx;
                }
            };
        }
        
        // Aplikuj štýl oramovania na všetky outline markery
        this.AttachedToVisualTree += (_, _) => ApplyOutlineStyles();
    }

    private void ApplyOutlineStyles()
    {
        // Markery teraz majú outline priamo v XAML definícii (CurvePathOutline, StraightLineOutline)
        // Pre výhybky to už nie je potrebné
        // Pre ostatné markery (koľaje, oblúky, mosty) ešte aplikujeme pôvodný spôsob
        
        // Koľaje a oblúky
        for (int i = 1; i <= 4; i++)
        {
            var outline = this.FindControl<Control>($"Outline{i}");
            if (outline != null)
            {
                ApplyOutlineStyle(outline);
            }
        }
        
        // Križovatky a mosty (11-16)
        for (int i = 11; i <= 16; i++)
        {
            var outline = this.FindControl<Control>($"Outline{i}");
            if (outline != null)
            {
                ApplyOutlineStyle(outline);
            }
        }
        
        // Signál a Senzor
        var outlineSignal = this.FindControl<Control>("OutlineSignal");
        if (outlineSignal != null) ApplyOutlineStyle(outlineSignal);
        
        var outlineSensor = this.FindControl<Control>("OutlineSensor");
        if (outlineSensor != null) ApplyOutlineStyle(outlineSensor);
    }

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
                    line.Stroke = new SolidColorBrush(Colors.Black);
                    line.StrokeThickness += 2;
                }
                else if (child is Path path)
                {
                    path.Stroke = new SolidColorBrush(Colors.Black);
                    path.StrokeThickness += 2;
                }
            }
        }
        // Variant 2: Path priamo ako obsah (Curve45, Curve90)
        else if (uc.Content is Path pathDirect)
        {
            pathDirect.Stroke = new SolidColorBrush(Colors.Black);
            pathDirect.StrokeThickness += 2;
        }
    }

}

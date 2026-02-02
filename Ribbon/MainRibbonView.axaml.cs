using Avalonia.Controls;
using Avalonia.VisualTree;
using TrackFlow.ViewModels;
using TrackFlow.ViewModels.Backstage;
using TrackFlow.Views.Backstage;

namespace TrackFlow.Ribbon;

public partial class MainRibbonView : UserControl
{
    // pamätá poslednú záložku mimo "Súbor"
    private int _lastNonFileIndex = 1;

    public MainRibbonView()
    {
        InitializeComponent();

        // Klik na tab "Súbor" -> otvor backstage a vráť sa na posledný non-file tab
        RibbonTabs.SelectionChanged += async (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var idx = RibbonTabs.SelectedIndex;
            if (idx < 0) return;

            if (idx == 0)
            {
                var owner = this.GetVisualRoot() as Window;
                var dlg = new FileBackstageWindow
                {
                    DataContext = new FileBackstageViewModel(vm)
                };

                if (owner != null)
                    await dlg.ShowDialog(owner);
                else
                    dlg.Show();
                // po zavretí vráť poslednú "normálnu" záložku
                RibbonTabs.SelectedIndex = _lastNonFileIndex;
            }
            else
            {
                _lastNonFileIndex = idx;
            }
        };
    }

}

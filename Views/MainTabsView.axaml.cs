using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views;

public partial class MainTabsView : UserControl
{
    public MainTabsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

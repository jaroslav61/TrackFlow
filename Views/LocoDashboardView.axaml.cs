using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views;

public partial class LocoDashboardView : UserControl
{
    public LocoDashboardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Cab;

public partial class CabStripView : UserControl
{
    public CabStripView()
    {
        InitializeComponent();
    }
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Cab;

public partial class CabStripWindow : Window
{
    public CabStripWindow()
    {
        InitializeComponent();
    }
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

}

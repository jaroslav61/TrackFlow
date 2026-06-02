using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views;

public partial class ProgressWindow : Window
{
    public ProgressWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

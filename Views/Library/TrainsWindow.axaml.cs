using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Library;

public partial class TrainsWindow : Window
{
    public TrainsWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

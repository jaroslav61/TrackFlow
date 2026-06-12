using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrackFlow.ViewModels.Library;

namespace TrackFlow.Views.Library;

public partial class TrainsWindow : Window
{
    public TrainsWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

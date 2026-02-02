using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TrackFlow.ViewModels;
using TrackFlow.Views;

namespace TrackFlow;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mw = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
            desktop.MainWindow = mw;
        }

        base.OnFrameworkInitializationCompleted();
    }
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.IO;
using System.Diagnostics;
using TrackFlow.ViewModels;
using TrackFlow.Views;
using TrackFlow.Services;

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
            // Register available loco icons so converters can resolve by name
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
                var iconsDir = Path.Combine(baseDir, "Assets", "LocoIcons");
                Debug.WriteLine($"App: looking for icons in {iconsDir}");
                if (!Directory.Exists(iconsDir))
                {
                    // try a project-relative fallback
                    iconsDir = Path.Combine(baseDir, "..", "..", "Assets", "LocoIcons");
                    Debug.WriteLine($"App: fallback icons path {iconsDir}");
                }

                if (Directory.Exists(iconsDir))
                {
                    foreach (var f in Directory.GetFiles(iconsDir, "*.png"))
                    {
                        var name = Path.GetFileName(f);
                        IconRegistry.Register(name, Path.GetFullPath(f));
                        Debug.WriteLine($"App: registered icon {name} -> {f}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: error registering icons: {ex}");
            }

            var mw = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
            desktop.MainWindow = mw;
        }

        base.OnFrameworkInitializationCompleted();
    }
}

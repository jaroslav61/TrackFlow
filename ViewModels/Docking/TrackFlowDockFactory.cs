using Avalonia.Controls;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Avalonia.Core;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Avalonia.Controls;
using TrackFlow.Cab;
using TrackFlow.ViewModels;
using TrackFlow.ViewModels.Cab;
using TrackFlow.Views;

namespace TrackFlow.ViewModels.Docking;

public sealed class TrackFlowDockFactory : Factory
{
    private readonly MainTabsViewModel _tabs;
    private readonly CabStripHostViewModel _cabHost;

    public TrackFlowDockFactory(MainTabsViewModel tabs, CabStripHostViewModel cabHost)
    {
        _tabs = tabs;
        _cabHost = cabHost;

        DefaultHostWindowLocator = () =>
        {
            var sized = false;
            var w = new HostWindow
            {
                Background = Avalonia.Media.Brushes.White,
                TransparencyLevelHint = new[] { Avalonia.Controls.WindowTransparencyLevel.None },
                IsToolWindow = true,
                Width = 900,
                Height = 260,
                MaxWidth = 1000
            };

            void EnsureSize()
            {
                if (sized)
                {
                    return;
                }

                // Run after Dock has done its sizing.
                w.Width = 900;
                w.Height = 260;
                sized = true;
            }

            w.Opened += (_, _) => EnsureSize();
            w.Activated += (_, _) => EnsureSize();

            return w;
        };
    }

    private Control CreateMainTabsView() => new MainTabsView { DataContext = _tabs };
    private Control CreateCabStripView() => new CabStripView { DataContext = _cabHost };

    private ProportionalDock CreateMainLayout()
    {
        var mainDocument = new Document
        {
            Id = "MainWorkspace",
            Title = null,
            Content = CreateMainTabsView(),
            CanClose = false
        };

        var documents = new DocumentDock
        {
            Id = "Documents",
            IsCollapsable = false,
            Proportion = 0.80,
            VisibleDockables = CreateList<IDockable>(mainDocument),
            ActiveDockable = mainDocument,
            CanClose = false
        };

        var cabTool = new Tool
        {
            Id = "Cab",
            Title = "CAB",
            Content = CreateCabStripView()
        };


        var bottomTools = new ToolDock
        {
            Id = "BottomTools",
            Alignment = Alignment.Bottom,
            IsCollapsable = true,
            Proportion = 0.20,
            VisibleDockables = CreateList<IDockable>(cabTool),
            ActiveDockable = cabTool,
            MinHeight = 170
        };

        return new ProportionalDock
        {
            Id = "Main",
            Orientation = Orientation.Vertical,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(
                documents,
                new ProportionalDockSplitter(),
                bottomTools),
            ActiveDockable = documents
        };
    }

    public override IRootDock CreateLayout()
    {
        var main = CreateMainLayout();
        // Ensure global docking targets are always available for edge docking.
        main.EnableGlobalDocking = true;

        // Root used by DockControl in MainWindow
        var root = (RootDock)CreateRootDock();
        root.Id = "Root";
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(main);
        root.ActiveDockable = main;
        root.DefaultDockable = main;
        root.EnableAdaptiveGlobalDockTargets = false;
        root.EnableGlobalDocking = true;

        // Do not register a DockWindow at startup.
        // InitLayout triggers RootDock.ShowWindows, which would present registered windows
        // and create an extra (empty) window. Floating windows will be created on-demand
        // via DefaultHostWindowLocator when undocking.
        root.Window = null;
        root.Windows = CreateList<IDockWindow>();

        InitLayout(root);
        return root;
    }
}

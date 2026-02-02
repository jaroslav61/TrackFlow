using Dock.Model.Controls;
using Dock.Model.Core;
using TrackFlow.ViewModels;
using TrackFlow.ViewModels.Cab;

namespace TrackFlow.ViewModels.Docking;

public sealed class TrackFlowDockLayoutViewModel
{
    public IFactory Factory { get; }
    public IRootDock Layout { get; }

    public TrackFlowDockLayoutViewModel(MainTabsViewModel tabs, CabStripHostViewModel cabHost)
    {
        Factory = new TrackFlowDockFactory(tabs, cabHost);
        Layout = Factory.CreateLayout();
    }
}

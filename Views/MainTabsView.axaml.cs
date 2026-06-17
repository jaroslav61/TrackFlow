using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.ViewModels;

namespace TrackFlow.Views;

public partial class MainTabsView : UserControl
{
    private TabControl? _tabControl;
    private bool _internalChange;

    public MainTabsView()
    {
        AvaloniaXamlLoader.Load(this);

        _tabControl = this.FindControl<TabControl>("MainTabControl");
        if (_tabControl != null)
        {
            _tabControl.SelectionChanged += OnTabSelectionChanged;
            _tabControl.SelectedIndex = 1;
        }
    }

    /// <summary>Prepne záložku programaticky bez spustenia logiky v handleri.</summary>
    public void SwitchTab(int index)
    {
        if (_tabControl == null || _tabControl.SelectedIndex == index) return;
        _internalChange = true;
        _tabControl.SelectedIndex = index;
        _internalChange = false;
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_tabControl == null || _internalChange) return;

        var mainWindow = this.FindAncestorOfType<MainWindow>();
        if (mainWindow?.DataContext is not MainWindowViewModel mainVm) return;

        // IsEnabled=false na TabItem zastaví klik počas simulácie fyzicky pred týmto handlerorm.
        // Sem sa dostaneme len pri bežnom prepínaní.
        if (_tabControl.SelectedIndex == 1)
            mainVm.ModeManager.ForceMode(OperationMode.Edit);
        else
            mainVm.ModeManager.SwitchToOffline();
    }
}
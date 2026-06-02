using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using TrackFlow.Models;
using TrackFlow.ViewModels;

namespace TrackFlow.Views;

public partial class MainTabsView : UserControl
{
    private TabControl? _tabControl;

    public MainTabsView()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Nájsť TabControl a pripojiť event handler
        _tabControl = this.FindControl<TabControl>("MainTabControl");
        if (_tabControl != null)
        {
            _tabControl.SelectionChanged += OnTabSelectionChanged;
            // Nastaviť default záložku na Editor (index 1)
            _tabControl.SelectedIndex = 1;
        }
    }
    
    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_tabControl == null) return;
        
        // Nájsť MainWindowViewModel cez hierarchiu
        var mainWindow = this.FindAncestorOfType<MainWindow>();
        if (mainWindow?.DataContext is MainWindowViewModel mainVm)
        {
            // Index 0 = Prevádzka, Index 1 = Editor
            mainVm.CurrentMode = _tabControl.SelectedIndex == 0 
                ? AppMode.Operation 
                : AppMode.Editor;
        }
    }
}

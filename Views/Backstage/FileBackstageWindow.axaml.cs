using Avalonia.Controls;
using TrackFlow.ViewModels.Backstage;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Backstage;

public partial class FileBackstageWindow : Window
{
    public FileBackstageWindow()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Prihlásenie na event zatvorenia z ViewModelu
        this.DataContextChanged += OnDataContextChanged;
    }
    
    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is FileBackstageViewModel vm)
        {
            vm.CloseRequested += OnCloseRequested;
        }
    }
    
    private void OnCloseRequested()
    {
        Close();
    }
}

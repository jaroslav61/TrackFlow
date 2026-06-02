using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using TrackFlow.ViewModels;

namespace TrackFlow.Views;

public partial class StatusBarView : UserControl
{
    public StatusBarView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnTelemetryWidgetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control button)
            return;

        if (button.DataContext is not StatusBarCentralItem item)
            return;

        if (TopLevel.GetTopLevel(this) is not Window window)
            return;

        if (window.DataContext is not MainWindowViewModel vm)
            return;

        if (vm.OpenTelemetryWidgetCommand.CanExecute(item))
            vm.OpenTelemetryWidgetCommand.Execute(item);
    }
}

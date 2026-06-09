using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using TrackFlow.ViewModels;

namespace TrackFlow.Views;

public partial class ClockView : Window
{
    public ClockView()
        : this(showStartPauseButton: true)
    {
    }

    public ClockView(bool showStartPauseButton)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new ClockViewModel
        {
            ShowStartPauseButton = showStartPauseButton
        };

        Closed += (_, _) =>
        {
            if (DataContext is ClockViewModel vm)
                vm.Dispose();
        };
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button)
            return;

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    public void SetStartPauseButtonVisible(bool isVisible)
    {
        if (DataContext is ClockViewModel vm)
            vm.ShowStartPauseButton = isVisible;
    }
}


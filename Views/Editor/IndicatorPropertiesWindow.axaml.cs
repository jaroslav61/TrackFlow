using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrackFlow.ViewModels.Editor;

namespace TrackFlow.Views.Editor;

public partial class IndicatorPropertiesWindow : Window
{
    private IndicatorPropertiesViewModel? _vm;

    public IndicatorPropertiesWindow()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) =>
        {
            AttachVm(DataContext as IndicatorPropertiesViewModel);
        };

        AttachVm(DataContext as IndicatorPropertiesViewModel);

        Closing += (_, _) =>
        {
            _vm?.Dispose();
            AttachVm(null);
        };
    }

    private void AttachVm(IndicatorPropertiesViewModel? vm)
    {
        if (_vm != null)
            _vm.CloseRequested -= OnCloseRequested;

        _vm = vm;

        if (_vm != null)
            _vm.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(bool saved) => Close(saved);
}


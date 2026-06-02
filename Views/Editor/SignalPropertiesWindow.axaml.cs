using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrackFlow.ViewModels.Editor;

namespace TrackFlow.Views.Editor;

public partial class SignalPropertiesWindow : Window
{
    private SignalPropertiesViewModel? _vm;

    public SignalPropertiesWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => AttachToVm(DataContext as SignalPropertiesViewModel);
    }

    private void AttachToVm(SignalPropertiesViewModel? vm)
    {
        if (_vm != null)
            _vm.CloseRequested -= OnCloseRequested;

        _vm = vm;

        if (_vm != null)
            _vm.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(bool saved) => Close(saved);
}


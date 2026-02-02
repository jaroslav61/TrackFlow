using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Operation;

public partial class OperationView : UserControl
{
    public OperationView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

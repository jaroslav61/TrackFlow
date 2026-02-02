using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Editor;

public partial class LayoutEditorView : UserControl
{
    public LayoutEditorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

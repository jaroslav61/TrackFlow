using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using TrackFlow.Models;
using TrackFlow.ViewModels.SmartStrips;

namespace TrackFlow.Views.SmartStrips;

public partial class SmartStripsView : UserControl
{
    public const string WagonDataFormat = "trackflow/wagon";

    public SmartStripsView()
    {
        InitializeComponent();
        this.AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // DragDropBehavior now auto-attaches via attached property change subscribers.
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Wagon pointer pressed is now handled by VehicleStripItem control itself.

    private void OnLocoDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not SmartStripsViewModel vm)
            return;

        if (sender is not Control c)
            return;

        if (c.DataContext is not LocoRecord loco)
            return;

        if (!e.Data.Contains(WagonDataFormat))
            return;

        if (e.Data.Get(WagonDataFormat) is not Wagon wagon)
            return;

        vm.AttachWagonToLocoRecord(loco, wagon);
        e.Handled = true;
    }
}

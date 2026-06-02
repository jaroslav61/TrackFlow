using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using TrackFlow.Helpers;
using TrackFlow.Models;
using TrackFlow.ViewModels.SmartStrips;

namespace TrackFlow.Views.SmartStrips;

public partial class SmartStripsView : UserControl
{
    public const string WagonDataFormat = "trackflow/wagon";

    public SmartStripsView()
    {
        AvaloniaXamlLoader.Load(this);
        this.AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // DragDropBehavior now auto-attaches via attached property change subscribers.
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

        if (!DragDropCompat.Contains(e, WagonDataFormat))
            return;

        if (!DragDropCompat.TryGet(e, WagonDataFormat, out Wagon wagon))
            return;

        vm.AttachWagonToLocoRecord(loco, wagon);
        e.Handled = true;
    }
}

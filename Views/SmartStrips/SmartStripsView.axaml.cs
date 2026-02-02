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
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnWagonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c)
            return;

        if (c.DataContext is not Wagon wagon)
            return;

        var data = new DataObject();
        data.Set(WagonDataFormat, wagon);

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        e.Handled = true;
    }

    private void OnLocoDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not SmartStripsViewModel vm)
            return;

        if (sender is not Control c)
            return;

        if (c.DataContext is not Locomotive loco)
            return;

        if (!e.Data.Contains(WagonDataFormat))
            return;

        if (e.Data.Get(WagonDataFormat) is not Wagon wagon)
            return;

        vm.AttachWagon(loco, wagon);
        e.Handled = true;
    }
}

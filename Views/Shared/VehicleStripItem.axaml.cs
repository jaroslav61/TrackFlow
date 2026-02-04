using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using TrackFlow.Models;
using Avalonia.VisualTree;

namespace TrackFlow.Views.Shared;

public partial class VehicleStripItem : UserControl
{
    public static readonly StyledProperty<string?> IconNameProperty =
        AvaloniaProperty.Register<VehicleStripItem, string?>(nameof(IconName));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<VehicleStripItem, string?>(nameof(Title));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> PointerPressedCommandProperty =
        AvaloniaProperty.Register<VehicleStripItem, System.Windows.Input.ICommand?>("PointerPressedCommand");

    public static readonly StyledProperty<System.Windows.Input.ICommand?> DropCommandProperty =
        AvaloniaProperty.Register<VehicleStripItem, System.Windows.Input.ICommand?>("DropCommand");

    public string? IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public const string WagonDataFormat = "trackflow/wagon";

    public System.Windows.Input.ICommand? PointerPressedCommand
    {
        get => GetValue(PointerPressedCommandProperty);
        set => SetValue(PointerPressedCommandProperty, value);
    }

    public System.Windows.Input.ICommand? DropCommand
    {
        get => GetValue(DropCommandProperty);
        set => SetValue(DropCommandProperty, value);
    }

    public VehicleStripItem()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // First, notify ViewModel via ICommand if bound
        var cmd = GetValue(PointerPressedCommandProperty) as System.Windows.Input.ICommand;
        if (cmd != null && cmd.CanExecute(DataContext))
        {
            cmd.Execute(DataContext);
        }
        else
        {
            // Fallback: walk visual parents to find a VM exposing ItemPressedCommand
            // No-op: prefer explicit binding via PointerPressedCommand property. Fallback removed to simplify template resolution.
        }

        // If data context is a Wagon, start drag in the view (UI concern)
        if (DataContext is Wagon wagon)
        {
            var data = new DataObject();
            data.Set(WagonDataFormat, wagon);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
            e.Handled = true;
            return;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        // If DropCommand is bound, extract wagon from drag data and forward to VM
        var cmd = GetValue(DropCommandProperty) as System.Windows.Input.ICommand;
        if (!e.Data.Contains(WagonDataFormat))
            return;

        if (e.Data.Get(WagonDataFormat) is not Wagon wagon)
            return;

        var target = DataContext; // e.g., LocoRecord
        var param = new object[] { target, wagon };

        if (cmd != null)
        {
            if (cmd.CanExecute(param))
                cmd.Execute(param);
            e.Handled = true;
            return;
        }

        // Fallback: walk visual parents to find a VM exposing ItemDropCommand
        // No-op fallback removed. Prefer DropCommand binding.
    }
}

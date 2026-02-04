using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using TrackFlow.Models;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Media;
using System.Threading.Tasks;

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
        // Track DataContext property-changed so we can update opacity when Locomotive.IsActive changes
        System.ComponentModel.INotifyPropertyChanged? notifier = null;
        this.DataContextChanged += (_, _) =>
        {
            // unsubscribe previous
            if (notifier != null)
                notifier.PropertyChanged -= Locomotive_PropertyChanged;

            notifier = DataContext as System.ComponentModel.INotifyPropertyChanged;

            if (notifier != null)
                notifier.PropertyChanged += Locomotive_PropertyChanged;

            UpdateOpacityFromDataContext();
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Locomotive_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrackFlow.Models.Locomotive.IsActive))
            UpdateOpacityFromDataContext();
    }

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // First, notify ViewModel via ICommand if bound
        var cmd = GetValue(PointerPressedCommandProperty) as System.Windows.Input.ICommand;
        if (cmd != null && cmd.CanExecute(DataContext))
        {
            cmd.Execute(DataContext);
            // Prevent parent controls (ListBox) from changing selection and overriding activation
            e.Handled = true;
            return;
        }
        else
        {
            // Fallback: walk visual parents to find a SmartStripsViewModel and invoke its ItemPressedCommand
            foreach (var anc in this.GetVisualAncestors())
            {
                if (anc is Control c && c.DataContext is TrackFlow.ViewModels.SmartStrips.SmartStripsViewModel svm)
                {
                    var vmCmd = svm.ItemPressedCommand;
                    if (vmCmd != null && vmCmd.CanExecute(DataContext))
                    {
                        vmCmd.Execute(DataContext);
                        e.Handled = true;
                        UpdateOpacityFromDataContext();
                        return;
                    }
                }
            }
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

    private void UpdateOpacityFromDataContext()
    {
        var border = this.FindControl<Border>("BorderRoot");
        if (border == null)
            return;
        var loco = DataContext as TrackFlow.Models.Locomotive;
        var icon = this.FindControl<Image>("IconImage");

        if (loco != null)
        {
            var converter = (TrackFlow.Converters.BoolToOpacityConverter?)this.FindResource("BoolToOpacity");
            double opacity = loco.IsActive ? 1.0 : 0.4;

            if (converter != null)
            {
                var val = converter.Convert(loco.IsActive, typeof(double), null, System.Globalization.CultureInfo.InvariantCulture);
                if (val is double d)
                    opacity = d;
            }

            border.Opacity = opacity; // border follows loco active state
            if (icon != null) icon.Opacity = opacity; // icon follows loco active state

            // Trigger simple activation animation (scale) when becoming active
            if (loco.IsActive)
                _ = AnimateActivateAsync(border);
            else
                _ = AnimateDeactivateAsync(border);
        }
        else
        {
            // Not a locomotive (e.g., Wagon) — wagons should always be fully opaque
            border.Opacity = 1.0;
            if (icon != null) icon.Opacity = 1.0;
        }
        }

    private async Task AnimateActivateAsync(Border border)
    {
        if (border == null) return;
        // ensure ScaleTransform
        if (border.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            border.RenderTransform = st;
            border.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        // quick pulse: 0.96 -> 1.06 -> 1.0
        var seq = new double[] { 0.96, 1.06, 1.0 };
        foreach (var s in seq)
        {
            // animate in small steps
            var startX = st.ScaleX;
            var startY = st.ScaleY;
            var steps = 6;
            for (int i = 1; i <= steps; i++)
            {
                var t = (double)i / steps;
                st.ScaleX = startX + (s - startX) * t;
                st.ScaleY = startY + (s - startY) * t;
                await Task.Delay(16);
            }
        }
    }

    private async Task AnimateDeactivateAsync(Border border)
    {
        if (border == null) return;
        if (border.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            border.RenderTransform = st;
            border.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        // slight shrink to 0.98 -> 1.0
        var seq = new double[] { 0.98, 1.0 };
        foreach (var s in seq)
        {
            var startX = st.ScaleX;
            var startY = st.ScaleY;
            var steps = 6;
            for (int i = 1; i <= steps; i++)
            {
                var t = (double)i / steps;
                st.ScaleX = startX + (s - startX) * t;
                st.ScaleY = startY + (s - startY) * t;
                await Task.Delay(16);
            }
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

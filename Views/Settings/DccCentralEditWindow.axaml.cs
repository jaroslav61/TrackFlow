using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrackFlow.ViewModels.Settings;

namespace TrackFlow.Views.Settings;

public partial class DccCentralEditWindow : Window
{
    public DccCentralEditWindow()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is DccCentralEditViewModel vm)
                vm.CloseRequested += result => Close(result);
        };

        // Napoj handler kliknutia na uzol stromu
        if (this.FindControl<TreeView>("CentralTreeView") is { } tree)
        {
            tree.Tapped += OnCentralTreeItemTapped;
        }
    }

    private void OnCentralTreeItemTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is not DccCentralEditViewModel vm)
            return;

        // Prehľadávame logický strom hore od kliknutého prvku
        // a hľadáme DataContext typu DccCentralTreeNode
        var element = e.Source as Control;
        while (element != null && element is not TreeView)
        {
            if (element.DataContext is DccCentralTreeNode node)
            {
                vm.SelectCentralItemFromTree(node);
                return;
            }
            element = element.Parent as Control;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using TrackFlow.Models;
using TrackFlow.ViewModels;

namespace TrackFlow.Views;

public partial class LocoDashboardView : UserControl
{
    public LocoDashboardView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Pomocník: nájde MainWindowViewModel cez vizuálny strom
    private MainWindowViewModel? GetMainVm()
    {
        foreach (var anc in this.GetVisualAncestors())
            if (anc is Control c && c.DataContext is MainWindowViewModel vm)
                return vm;
        return null;
    }

    // Handler pre tlačidlo EMERGENCY STOP
    private void OnEmergencyStopClick(object? sender, RoutedEventArgs e)
    {
        // Použi globálny STOP (zastaví simulácie aj Live DCC podľa režimu).
        var vm = GetMainVm();
        if (vm?.StopCommand.CanExecute(null) == true)
            vm.StopCommand.Execute(null);
    }

    // Handler pre F-tlačidlá (Click event, Tag = Slot funkcie ako int alebo string)
    private void OnFunctionButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (DataContext is not Locomotive loco) return;

        var address = loco.DccAddress;
        if (address < 1) return;

        // Tag môže byť int (z bindingu na LocoFunctionDef.Slot) alebo string (z pevných fallback tlačidiel)
        int fnIndex;
        if (btn.Tag is int tagInt)
            fnIndex = tagInt;
        else if (btn.Tag is string tagStr && int.TryParse(tagStr, out var parsed))
            fnIndex = parsed;
        else
            return;

        var bridge = GetMainVm()?.LocoDccBridge;
        if (bridge == null) return;

        // Toggle stav funkcie
        bridge.ToggleFunction(address, fnIndex);

        // Vizuálna spätná väzba – prepnutie CSS triedy active
        var isActive = bridge.GetFunctionState(address, fnIndex);
        if (isActive)
            btn.Classes.Add("active");
        else
            btn.Classes.Remove("active");
    }
}


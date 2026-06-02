using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrackFlow.ViewModels.Editor;

namespace TrackFlow.Views.Editor;

public partial class RouteEditorWindow : Window
{
    public RouteEditorWindow()
    {
        AvaloniaXamlLoader.Load(this);
        AttachEventHandlers();
    }

    private void AttachEventHandlers()
    {
        this.DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is RouteEditorViewModel vm)
        {
            vm.CloseRequested += OnCloseRequested;
            
            // Nastavíme funkčnosť indikátorov v záložke "Indikátory"
            SetupIndicatorListBoxes(vm);
        }
    }

    private void OnCloseRequested(bool saved)
    {
        Close(saved);
    }

    private void SetupIndicatorListBoxes(RouteEditorViewModel vm)
    {
        var availableListBox = this.FindControl<ListBox>("AvailableIndicatorsListBox");
        var assignedListBox = this.FindControl<ListBox>("AssignedIndicatorsListBox");
        var addButton = this.FindControl<Button>("AddIndicatorButton");
        var removeButton = this.FindControl<Button>("RemoveIndicatorButton");

        if (availableListBox == null || assignedListBox == null || addButton == null || removeButton == null)
            return;

        // Rozdelíme indikátory na dostupné a priradené
        var available = new System.Collections.ObjectModel.ObservableCollection<SensorItem>();
        var assigned = new System.Collections.ObjectModel.ObservableCollection<SensorItem>();

        foreach (var sensor in vm.AvailableSensors)
        {
            if (sensor.IsSelected)
                assigned.Add(sensor);
            else
                available.Add(sensor);
        }

        availableListBox.ItemsSource = available;
        assignedListBox.ItemsSource = assigned;

        // Handler pre pridanie indikátora
        addButton.Click += (s, e) =>
        {
            var selected = availableListBox.SelectedItems?.Cast<SensorItem>().ToList();
            if (selected == null || selected.Count == 0) return;

            foreach (var item in selected)
            {
                available.Remove(item);
                assigned.Add(item);
                item.IsSelected = true;
            }
        };

        // Handler pre odstránenie indikátora
        removeButton.Click += (s, e) =>
        {
            var selected = assignedListBox.SelectedItems?.Cast<SensorItem>().ToList();
            if (selected == null || selected.Count == 0) return;

            foreach (var item in selected)
            {
                assigned.Remove(item);
                available.Add(item);
                item.IsSelected = false;
            }
        };
    }
}


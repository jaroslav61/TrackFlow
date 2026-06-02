using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Threading.Tasks;
using TrackFlow.Services;
using TrackFlow.ViewModels.Backstage;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Backstage;

public partial class FileBackstageView : UserControl
{
    public FileBackstageView()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Auto-select first row when control is loaded
        this.Loaded += OnViewLoaded;
    }

    private async void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Small delay to ensure DataGrid is fully loaded
            await Task.Delay(50);
            
            var grid = this.FindControl<DataGrid>("RecentFilesGrid");
            if (grid != null && DataContext is FileBackstageViewModel vm && vm.RecentFiles.Count > 0)
            {
                grid.SelectedIndex = 0;
                grid.Focus();
            }
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("FileBackstageView.OnViewLoaded", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose("Súbor", $"Inicializácia Recent Files backstage view zlyhala: {ex.Message}", DiagnosticLevel.Warning);
        }
    }

    private void OnRecentFileDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileBackstageViewModel vm)
            return;

        if (sender is not DataGrid grid)
            return;

        if (grid.SelectedItem is not FileBackstageViewModel.RecentFile recentFile)
            return;

        // Prevent event bubbling
        e.Handled = true;

        // Close the backstage window
        var window = this.FindAncestorOfType<Window>();
        window?.Close();

        // Open the recent project
        vm.OpenRecentCommand.Execute(recentFile.FilePath);
    }
}

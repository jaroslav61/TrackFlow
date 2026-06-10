using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TrackFlow.ViewModels.Settings;

namespace TrackFlow.Views.Settings.SettingsPages;

public partial class GeneralSettingsView : UserControl
{
    public GeneralSettingsView()
    {
        InitializeComponent();
    }

    private async void OnPickProjectsDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is not TopLevel topLevel || topLevel.StorageProvider == null)
            return;

        IStorageFolder? suggestedStart = null;
        if (DataContext is SettingsViewModel vm &&
            !string.IsNullOrWhiteSpace(vm.DefaultProjectsDirectory) &&
            Directory.Exists(vm.DefaultProjectsDirectory))
        {
            suggestedStart = await topLevel.StorageProvider.TryGetFolderFromPathAsync(vm.DefaultProjectsDirectory);
        }

        var picked = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Vyberte predvolený adresár projektov",
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStart
        });

        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (DataContext is SettingsViewModel targetVm)
            targetVm.DefaultProjectsDirectory = path;
    }
}


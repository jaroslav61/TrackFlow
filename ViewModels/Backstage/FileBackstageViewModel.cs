using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using TrackFlow.ViewModels;

namespace TrackFlow.ViewModels.Backstage;

public partial class FileBackstageViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;

    public ObservableCollection<string> RecentFiles { get; } = new();

    public FileBackstageViewModel(MainWindowViewModel main)
    {
        _main = main;
        ReloadRecent();
    }

    public void ReloadRecent()
    {
        RecentFiles.Clear();

        var last = _main.SettingsManager.App.LastProjectPath;
        if (!string.IsNullOrWhiteSpace(last) && File.Exists(last))
            RecentFiles.Add(last);
    }

    [RelayCommand]
    private void Close() => _main.CloseFileBackstageCommand.Execute(null);

    [RelayCommand]
    private void New()
    {
        _main.StatusBar.Message = "Nový: zatiaľ nie je implementované.";
        _main.CloseFileBackstageCommand.Execute(null);
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        _main.CloseFileBackstageCommand.Execute(null);
        await _main.OpenProjectCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task SettingsAsync()
    {
        _main.CloseFileBackstageCommand.Execute(null);
        await _main.OpenSettingsCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void Save()
    {
        _main.CloseFileBackstageCommand.Execute(null);
        _main.SaveProjectCommand.Execute(null);
    }

    [RelayCommand]
    private void SaveAs()
    {
        _main.CloseFileBackstageCommand.Execute(null);
        _main.SaveProjectAsCommand.Execute(null);
    }

    [RelayCommand]
    private void Print()
    {
        _main.StatusBar.Message = "Tlačiť: zatiaľ nie je implementované.";
        _main.CloseFileBackstageCommand.Execute(null);
    }

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        _main.CloseFileBackstageCommand.Execute(null);
        _main.OpenProjectByPath(path);
    }
}

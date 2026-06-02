using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrackFlow.ViewModels;

namespace TrackFlow.ViewModels.Backstage;

public partial class FileBackstageViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;

    public class RecentFile
    {
        public string FileName { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }

    public ObservableCollection<RecentFile> RecentFiles { get; } = new();
    
    /// <summary>Event na zatvorenie Backstage okna</summary>
    public event Action? CloseRequested;

    public FileBackstageViewModel(MainWindowViewModel main)
    {
        _main = main;
        ReloadRecent();
    }

    public void ReloadRecent()
    {
        RecentFiles.Clear();

        var recentPaths = _main.SettingsManager.App.RecentProjectPaths;
        if (recentPaths != null)
        {
            foreach (var path in recentPaths.Take(10))
            {
                if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    RecentFiles.Add(new RecentFile
                    {
                        FileName = fileInfo.Name,
                        FileSize = (fileInfo.Length / 1024).ToString() + " KB",
                        LastModified = fileInfo.LastWriteTime.ToString("g"),
                        FilePath = path
                    });
                }
            }
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    [RelayCommand]
    private async Task NewAsync()
    {
        await _main.CreateNewProjectAsync();
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        CloseRequested?.Invoke();
        await _main.OpenProjectCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task SettingsAsync()
    {
        CloseRequested?.Invoke();
        await _main.OpenSettingsCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _main.SaveProjectCommand.ExecuteAsync(null);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        await _main.SaveProjectAsCommand.ExecuteAsync(null);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Print()
    {
        _main.StatusBar.Message = "Tlačiť: zatiaľ nie je implementované.";
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        CloseRequested?.Invoke();
        _main.OpenProjectByPath(path);
    }
}

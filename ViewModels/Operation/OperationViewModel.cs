using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime;
using System.Xml.Linq;
using Tmds.DBus.Protocol;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.ViewModels.Operation;

namespace TrackFlow.ViewModels.Operation;

public partial class OperationViewModel : ObservableObject
{
    private readonly SettingsManager _settings;

    public ObservableCollection<LocoItemViewModel> Locomotives { get; } = new();

    [ObservableProperty]
    private LocoItemViewModel? selectedLoco;

    public bool HasProject => _settings.CurrentProject != null;

    public OperationViewModel(SettingsManager settingsManager)
    {
        _settings = settingsManager;
        RefreshFromProject();
    }

    public void RefreshFromProject()
    {
        Locomotives.Clear();

        var p = _settings.CurrentProject;
        if (p == null)
        {
            SelectedLoco = null;
            OnPropertyChanged(nameof(HasProject));
            return;
        }

        foreach (var m in p.Locomotives)
Locomotives.Add(new LocoItemViewModel(m, MarkDirty));

SelectedLoco = Locomotives.FirstOrDefault();

OnPropertyChanged(nameof(HasProject));
    }

    private void MarkDirty()
    {
var p = _settings.CurrentProject;
        if (p == null) return;
p.IsDirty = true;
    }

 [RelayCommand]
    private void AddLoco()
    {
var p = _settings.CurrentProject;
        if (p == null) return;

var model = new LocoRecord
        {
Name = $"Lokomotíva {p.Locomotives.Count + 1}",
Address = 3
        }
    ;

p.Locomotives.Add(model);
var vm = new LocoItemViewModel(model, MarkDirty);
Locomotives.Add(vm);
SelectedLoco = vm;

MarkDirty();
    }

 [RelayCommand]
    private void RemoveSelected()
    {
var p = _settings.CurrentProject;
        if (p == null) return;
        if (SelectedLoco == null) return;

var id = SelectedLoco.Id;
var m = p.Locomotives.FirstOrDefault(x => x.Id == id);
        if (m != null)
p.Locomotives.Remove(m);

Locomotives.Remove(SelectedLoco);
SelectedLoco = Locomotives.FirstOrDefault();

MarkDirty();
    }
}

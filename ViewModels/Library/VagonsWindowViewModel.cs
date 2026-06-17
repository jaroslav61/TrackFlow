using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using TrackFlow.Models;
using TrackFlow.Services;
using System.IO;

namespace TrackFlow.ViewModels.Library;

public partial class VagonsWindowViewModel : ObservableObject
{
    public enum EditorMode
    {
        Viewing,
        Adding,
        Editing,
    }

    private readonly SettingsManager _settings;

    public ObservableCollection<Wagon> Vagons { get; }

    public ObservableCollection<IconItem> AvailableIcons { get; } = new();
    public ObservableCollection<IconItem> IconComboItems { get; } = new();
    public ObservableCollection<string> AvailableIconNames { get; } = new();
    public ObservableCollection<string> VagonCategories { get; } = new();
    public ObservableCollection<string> VagonTypes { get; } = new();

    private IconItem? _selectedIcon;

    public IconItem? SelectedIcon
    {
        get => _selectedIcon;
        set
        {
            if (_selectedIcon == value)
                return;
            _selectedIcon = value;
            OnPropertyChanged(nameof(SelectedIcon));
            // Udržiavať EditorIconName synchronizované pre persistenciu / spätnú kompatibilitu
            EditorIconName = _selectedIcon?.Name ?? string.Empty;
            if (Selected != null)
                Selected.IconName = _selectedIcon?.Name ?? string.Empty;
            MarkDirtyAndRevalidate();
        }
    }

    [ObservableProperty] private Wagon? selected;

    private string _editorIconName = "";

    public string EditorIconName
    {
        get => _editorIconName;
        set
        {
            if (_editorIconName == (value ?? string.Empty))
                return;
            _editorIconName = value ?? string.Empty;
            OnPropertyChanged(nameof(EditorIconName));
            MarkDirtyAndRevalidate();
        }
    }

    [ObservableProperty] private EditorMode mode = EditorMode.Viewing;

    [ObservableProperty] private string saveButtonText = "Uložiť zmeny";

    public bool IsGridEnabled => Mode == EditorMode.Viewing;

    [ObservableProperty] private bool isDirty;

    [ObservableProperty] private string code = "";
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private double weight;
    [ObservableProperty] private double lengthOverBuffers;
    [ObservableProperty] private string vagonType = "";
    [ObservableProperty] private string vagonCategory = "";

    [ObservableProperty] private string validationMessage = "";

    public Action? RequestClose { get; set; }

    private Wagon? _selectionBeforeAdd;

    public VagonsWindowViewModel(SettingsManager settings)
    {
        _settings = settings;
        
        // Zabezpečiť, že projekt existuje a naviazať sa na kolekcie SettingsManagera
        _settings.EnsureProjectSettings();
      
        // Musíme pracovať priamo s touto kolekciou, nie s jej kópiou
        Vagons = _settings.ProjectWagons;

        Selected = Vagons.FirstOrDefault();

        // Načítať dostupné názvy súborov ikon z Assets/WagonIcons (vyhľadávať v niekoľkých pravdepodobných umiestneniach)
        try
        {
            var start = AppDomain.CurrentDomain.BaseDirectory ??
                        AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            var allowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

            var dir = start;
            var maxUp = 6; // vyšplhať sa až 6 úrovní smerom nahor
            var foundDir = (string?)null;

            for (var i = 0; i <= maxUp; i++)
            {
                var candidate = Path.Combine(dir, "Assets", "WagonIcons");
                if (Directory.Exists(candidate))
                {
                    foundDir = Path.GetFullPath(candidate);
                    break;
                }

                var parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || parent == dir)
                    break;
                dir = parent;
            }

            if (!string.IsNullOrEmpty(foundDir))
            {
                var files = Directory.EnumerateFiles(foundDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => allowedExt.Contains(Path.GetExtension(f)));

                var count = 0;
                foreach (var f in files)
                {
                    var name = Path.GetFileName(f);
                    if (!AvailableIcons.Any(i => i.Name == name))
                    {
                        var full = Path.GetFullPath(f);
                        AvailableIcons.Add(new IconItem(name, full));
                        // Registrovať do globálneho registra, aby konvertor vedel nájsť podľa mena
                        TrackFlow.Services.IconRegistry.Register(name, full);
                        count++;
                    }
                }
            }
            else
            {
                // no icons found
            }
        }
        catch (Exception)
        {
            // ignore icon discovery errors
        }

        // Vytvoriť položky ComboBoxu: najprv zástupný prvok, potom skutočné ikony
        IconComboItems.Clear();
        IconComboItems.Add(new IconItem("-- Vyberte vagón --", string.Empty));
        foreach (var it in AvailableIcons)
            IconComboItems.Add(it);

        // Naplniť kategórie
        VagonCategories.Clear();
        VagonCategories.Add("Krytý");
        VagonCategories.Add("Plošinový");
        VagonCategories.Add("Cisternový");

        // Naplniť typy
        VagonTypes.Clear();
        VagonTypes.Add("Osobný");
        VagonTypes.Add("Nákladný");
        VagonTypes.Add("Lôžkový");
        VagonTypes.Add("Lehatkový");
        VagonTypes.Add("Reštauračný");
        VagonTypes.Add("Služobný");

        LoadSelectedToEditor();
        OnPropertyChanged(nameof(Selected));
    }

    public Wagon? SelectedVagon
    {
        get => Selected;
        set => Selected = value;
    }

    partial void OnSelectedChanged(Wagon? value)
    {
        // V režime Adding nechceme, aby klik do zoznamu prepisoval rozpracované polia.
        if (Mode == EditorMode.Adding)
            return;

        LoadSelectedToEditor();
    }

    partial void OnNameChanged(string value) => MarkDirtyAndRevalidate();

    private void LoadSelectedToEditor()
    {
        if (Selected == null)
        {
            Code = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            Weight = 0;
            LengthOverBuffers = 0;
            VagonType = string.Empty;
            ValidationMessage = "";
            EditorIconName = "";
            SelectedIcon = IconComboItems.FirstOrDefault();
        }
        else
        {
            var selected = Selected;
            if (selected == null)
                return;

            Code = selected.Code;
            Name = selected.Name ?? string.Empty;
            Description = selected.Description ?? string.Empty;
            Weight = selected.Weight;
            LengthOverBuffers = selected.LengthOverBuffers;
            VagonType = selected.VagonType;
            ValidationMessage = "";
            EditorIconName = selected.IconName ?? string.Empty;

            var match = IconComboItems.FirstOrDefault(i => i.Name == EditorIconName);
            if (Mode == EditorMode.Adding)
            {
                SelectedIcon = IconComboItems.FirstOrDefault();
            }
            else if (match != null)
            {
                SelectedIcon = match;
            }
            else
            {
                SelectedIcon = IconComboItems.Skip(1).FirstOrDefault() ?? IconComboItems.FirstOrDefault();
            }
        }

        IsDirty = false;
        SetMode(EditorMode.Viewing);

        NotifyAllCanExecutes();
    }

    private void SetMode(EditorMode newMode)
    {
        Mode = newMode;
        SaveButtonText = (Mode == EditorMode.Adding) ? "Uložiť" : "Uložiť zmeny";
        OnPropertyChanged(nameof(IsGridEnabled));
        NotifyAllCanExecutes();
        if (Mode == EditorMode.Adding)
            SelectedIcon = IconComboItems.FirstOrDefault();
    }

    private void NotifyAllCanExecutes()
    {
        AddCommand.NotifyCanExecuteChanged();
        SaveChangesCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void MarkDirtyAndRevalidate()
    {
        if (Mode == EditorMode.Viewing && Selected != null)
            SetMode(EditorMode.Editing);

        if (Mode == EditorMode.Adding || Mode == EditorMode.Editing)
            IsDirty = true;

        Revalidate();
    }

    private void Revalidate()
    {
        ValidationMessage = "";
        SaveChangesCommand.NotifyCanExecuteChanged();
    }

    private bool CanBeginAdd() => Mode == EditorMode.Viewing;

    private bool CanSave()
    {
        if (Mode == EditorMode.Viewing)
            return false;

        if (Mode == EditorMode.Editing && !IsDirty)
            return false;

        return true;
    }

    private bool CanDelete() => Mode == EditorMode.Viewing && Selected != null;

    private bool CanCancel() => Mode == EditorMode.Adding || Mode == EditorMode.Editing;

    [RelayCommand(CanExecute = nameof(CanBeginAdd))]
    private void Add()
    {
        _selectionBeforeAdd = Selected;

        SetMode(EditorMode.Adding);

        Selected = null;

        Code = string.Empty;
        Name = string.Empty;
        Description = string.Empty;
        Weight = 0;
        LengthOverBuffers = 0;
        VagonType = string.Empty;
        ValidationMessage = "";
        IsDirty = false;

        NotifyAllCanExecutes();
        SelectedIcon = IconComboItems.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveChanges()
    {
        // Základné ukladanie: vytvoriť nový objekt Wagon alebo aktualizovať existujúci
        if (Mode == EditorMode.Adding)
        {
            var rec = new Wagon(Guid.NewGuid().ToString("N"), Name ?? "Nový vagón")
            {
                Weight = Weight,
                LengthOverBuffers = LengthOverBuffers,
                VagonType = VagonType ?? string.Empty,
                // Uprednostniť SelectedIcon.Name, ak nie je, použiť EditorIconName, inak rozumný predvolený názov
                IconName = SelectedIcon?.Name ?? EditorIconName ?? "wagon_box"
            };
            // Popis, ak je k dispozícii
            rec.Description = Description ?? string.Empty;

            // Pridať do lokálnej kolekcie a vybrať
            Vagons.Add(rec);
            Selected = rec;

            // Zabezpečiť aktualizáciu projektových/globálnych kolekcií
            // Udržať SettingsManager.ProjectWagons v synchronizácii
            if (!_settings.ProjectWagons.Any(w => w.Code == rec.Code))
                _settings.ProjectWagons.Add(rec);

            if (_settings.CurrentProject != null)
            {
                // Zabezpečiť, že model CurrentProject obsahuje aj tento vagón
                if (!_settings.CurrentProject.Wagons.Any(w => w.Code == rec.Code))
                    _settings.CurrentProject.Wagons.Add(rec);

                _settings.CurrentProject.IsDirty = true;
                _settings.SaveProject();
            }
            else
            {
                _settings.SaveCatalog(new TrackFlowProject { Wagons = _settings.ProjectWagons.ToList() });
            }

            // Vždy uložiť snapshot katalógu a notifikovať UI odoberateľov
            _settings.SaveCatalog(_settings.CurrentProject ??
                                  new TrackFlowProject { Wagons = _settings.ProjectWagons.ToList() });
            _settings.NotifyProjectChanged();

            IsDirty = false;
            ValidationMessage = string.Empty;
            SetMode(EditorMode.Viewing);
            return;
        }

        if (Selected == null)
            return;

        // Aktualizovať vybraný z editačných polí; zabezpečiť, že IconName nebude prepísané prázdnou hodnotou
        Selected.Name = Name ?? string.Empty;
        Selected.Description = Description ?? string.Empty;
        Selected.Weight = Weight;
        Selected.LengthOverBuffers = LengthOverBuffers;
        Selected.VagonType = VagonType ?? string.Empty;
        Selected.IconName = SelectedIcon?.Name ?? EditorIconName ?? string.Empty;

        // Zachovať zmeny v kolekciách SettingsManagera
        // Aktualizovať ProjectWagons (globálna kolekcia)
        var projEx = _settings.ProjectWagons.FirstOrDefault(w => w.Code == Selected.Code);
        if (projEx != null)
        {
            projEx.Name = Selected.Name;
            projEx.Description = Selected.Description;
            projEx.Weight = Selected.Weight;
            projEx.LengthOverBuffers = Selected.LengthOverBuffers;
            projEx.VagonType = Selected.VagonType;
            projEx.IconName = Selected.IconName;
        }
        else
        {
            _settings.ProjectWagons.Add(Selected);
        }

        if (_settings.CurrentProject != null)
        {
            var existing = _settings.CurrentProject.Wagons.FirstOrDefault(w => w.Code == Selected.Code);
            if (existing != null)
            {
                existing.Name = Selected.Name;
                existing.Description = Selected.Description;
                existing.Weight = Selected.Weight;
                existing.LengthOverBuffers = Selected.LengthOverBuffers;
                existing.VagonType = Selected.VagonType;
                existing.IconName = Selected.IconName;
            }
            else
            {
                _settings.CurrentProject.Wagons.Add(Selected);
            }

            _settings.CurrentProject.IsDirty = true;
            _settings.SaveProject();
        }
        else
        {
            _settings.SaveCatalog(new TrackFlowProject { Wagons = _settings.ProjectWagons.ToList() });
        }

        // Vždy snapshot katalógu a notifikovať
        _settings.SaveCatalog(_settings.CurrentProject ??
                              new TrackFlowProject { Wagons = _settings.ProjectWagons.ToList() });
        _settings.NotifyProjectChanged();

        IsDirty = false;
        ValidationMessage = string.Empty;
        SetMode(EditorMode.Viewing);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (Selected == null) return;

        var toRemove = Selected;
        var idx = Vagons.IndexOf(toRemove);

        Vagons.Remove(toRemove);
        Selected = Vagons.Count == 0
            ? null
            : Vagons[Math.Min(idx, Vagons.Count - 1)];

        // Odstrániť z globálnej ProjectWagons v SettingsManager, ak je prítomný
        var projItem = _settings.ProjectWagons.FirstOrDefault(w => w.Code == toRemove.Code);
        if (projItem != null)
            _settings.ProjectWagons.Remove(projItem);

        // Odstrániť z modelu projektu a uložiť (alebo z globálneho katalógu)
        if (_settings.CurrentProject != null)
        {
            var ex = _settings.CurrentProject.Wagons.FirstOrDefault(w => w.Code == toRemove.Code);
            if (ex != null) _settings.CurrentProject.Wagons.Remove(ex);
            _settings.Dirty.MarkDirty("wagon-delete");
            _settings.SaveProject();
        }
        else
        {
            var catalog = new TrackFlowProject()
            {
                Wagons = Vagons.ToList(),
                Locomotives = _settings.CurrentProject?.Locomotives ?? new List<LocoRecord>()
            };
            _settings.SaveCatalog(catalog);
        }

        // Zabezpečiť snapshot katalógu a informovať poslucháčov
        _settings.SaveCatalog(_settings.CurrentProject ?? new TrackFlowProject
        {
            Wagons = _settings.ProjectWagons.ToList(),
            Locomotives = _settings.CurrentProject?.Locomotives ?? new List<LocoRecord>()
        });
        _settings.NotifyProjectChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (Mode == EditorMode.Adding)
        {
            SetMode(EditorMode.Viewing);
            Selected = _selectionBeforeAdd ?? Vagons.FirstOrDefault();
            _selectionBeforeAdd = null;

            LoadSelectedToEditor();
            return;
        }

        LoadSelectedToEditor();
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();
}
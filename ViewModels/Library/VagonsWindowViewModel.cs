using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using TrackFlow.Models;
using TrackFlow.Services;
using System.IO;
using System.Diagnostics;

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
            // keep EditorIconName in sync for persistence/backwards compatibility
            EditorIconName = _selectedIcon?.Name ?? string.Empty;
            if (Selected != null)
                Selected.IconName = _selectedIcon?.Name ?? string.Empty;
            MarkDirtyAndRevalidate();
        }
    }

    [ObservableProperty]
    private Wagon? selected;

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

    [ObservableProperty]
    private EditorMode mode = EditorMode.Viewing;

    [ObservableProperty]
    private string saveButtonText = "Uloži zmeny";

    public bool IsGridEnabled => Mode == EditorMode.Viewing;

    [ObservableProperty]
    private bool isDirty;

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
        // Ensure project exists
        var ps = _settings.EnsureProjectSettings();
        // Try to load existing wagons from current project (new format) or legacy settings (old format)
        var sourceList = (_settings.CurrentProject?.Wagons ?? _settings.Project?.Wagons) ?? new List<Wagon>();
        // Clone into observable collection (keep same instances)
        Vagons = new ObservableCollection<Wagon>(sourceList);
        Selected = Vagons.FirstOrDefault();

        // Load available icon file names from Assets/VagonIcons (search several likely locations)
        try
        {
            Debug.WriteLine("VagonsWindowViewModel: Searching for Assets/VagonIcons starting from base directory and moving up...");
            var start = AppDomain.CurrentDomain.BaseDirectory ?? AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            var allowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

            var dir = start;
            var maxUp = 6; // climb up to 6 levels
            var foundDir = (string?)null;

            for (var i = 0; i <= maxUp; i++)
            {
                var candidate = Path.Combine(dir, "Assets", "VagonIcons");
                Debug.WriteLine($"  Checking: {candidate}");
                if (Directory.Exists(candidate))
                {
                    foundDir = Path.GetFullPath(candidate);
                    Debug.WriteLine($"    Found folder: {foundDir}");
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
                        // Register in global registry for converter lookup by name
                        TrackFlow.Services.IconRegistry.Register(name, full);
                        Debug.WriteLine($"    Found icon: {name} (full: {full})");
                        count++;
                    }
                }

                Debug.WriteLine($"    Found {count} icon(s) in {foundDir}");
            }
            else
            {
                Debug.WriteLine("    No Assets/VagonIcons folder found while searching upwards from base directory.");
            }

            Debug.WriteLine($"VagonsWindowViewModel: Total available icons = {AvailableIcons.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"VagonsWindowViewModel: Exception while loading icons: {ex}");
        }

        // Build ComboBox items: placeholder first, then actual icons
        IconComboItems.Clear();
        IconComboItems.Add(new IconItem("-- Vyberte vagón --", string.Empty));
        foreach (var it in AvailableIcons)
            IconComboItems.Add(it);

        // Populate categories
        VagonCategories.Clear();
        VagonCategories.Add("krytý");
        VagonCategories.Add("plošinový");
        VagonCategories.Add("cisternový");

        // Populate types
        VagonTypes.Clear();
        VagonTypes.Add("osobný");
        VagonTypes.Add("nákladný");
        VagonTypes.Add("lôžkový");
        VagonTypes.Add("ležadlový");
        VagonTypes.Add("reštauraèný");
        VagonTypes.Add("služobný");

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
            Code = Selected.Code;
            Name = Selected.Name ?? string.Empty;
            Description = Selected.Description ?? string.Empty;
            Weight = Selected is { } ? Selected.Weight : 0;
            LengthOverBuffers = Selected is { } ? Selected.LengthOverBuffers : 0;
            VagonType = Selected is { } ? Selected.VagonType : string.Empty;
            ValidationMessage = "";
            EditorIconName = Selected.IconName ?? string.Empty;

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
        SaveButtonText = (Mode == EditorMode.Adding) ? "Uloži" : "Uloži zmeny";
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
        // Basic save behavior: create new Wagon instance or update existing
        if (Mode == EditorMode.Adding)
        {
            var rec = new Wagon(Code ?? Guid.NewGuid().ToString("N"), Name ?? string.Empty)
            {
                Weight = Weight,
                LengthOverBuffers = LengthOverBuffers,
                VagonType = VagonType ?? string.Empty,
                IconName = EditorIconName ?? string.Empty
            };

            // Description if available
            rec.Description = Description ?? string.Empty;

            Vagons.Add(rec);
            Selected = rec;
            // Persist to project model
            if (_settings.CurrentProject != null)
            {
                _settings.CurrentProject.Wagons.Add(rec);
                _settings.CurrentProject.IsDirty = true;
            }
            if (_settings.Project != null)
            {
                _settings.Project.Wagons.Add(rec);
            }
            _settings.SaveProject();
            IsDirty = false;
            ValidationMessage = string.Empty;
            SetMode(EditorMode.Viewing);
            return;
        }

        if (Selected == null)
            return;

        Selected.Name = Name ?? string.Empty;
        Selected.Description = Description ?? string.Empty;
        Selected.Weight = Weight;
        Selected.LengthOverBuffers = LengthOverBuffers;
        Selected.VagonType = VagonType ?? string.Empty;
        Selected.IconName = EditorIconName ?? string.Empty;
        // Persist updated wagon to project model
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
        }
        if (_settings.Project != null)
        {
            var existing2 = _settings.Project.Wagons.FirstOrDefault(w => w.Code == Selected.Code);
            if (existing2 != null)
            {
                existing2.Name = Selected.Name;
                existing2.Description = Selected.Description;
                existing2.Weight = Selected.Weight;
                existing2.LengthOverBuffers = Selected.LengthOverBuffers;
                existing2.VagonType = Selected.VagonType;
                existing2.IconName = Selected.IconName;
            }
            else
            {
                _settings.Project.Wagons.Add(Selected);
            }
        }
        _settings.SaveProject();
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

        // Remove from project model and persist
        if (_settings.CurrentProject != null)
        {
            var ex = _settings.CurrentProject.Wagons.FirstOrDefault(w => w.Code == toRemove.Code);
            if (ex != null) _settings.CurrentProject.Wagons.Remove(ex);
            _settings.CurrentProject.IsDirty = true;
        }
        if (_settings.Project != null)
        {
            var ex2 = _settings.Project.Wagons.FirstOrDefault(w => w.Code == toRemove.Code);
            if (ex2 != null) _settings.Project.Wagons.Remove(ex2);
        }
        _settings.SaveProject();
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

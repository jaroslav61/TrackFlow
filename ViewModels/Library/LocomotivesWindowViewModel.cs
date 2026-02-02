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

public partial class LocomotivesWindowViewModel : ObservableObject
{
    public enum EditorMode
    {
        Viewing,
        Adding,
        Editing,
    }

    private readonly SettingsManager _settings;

    public ObservableCollection<LocoRecord> Locomotives { get; }

    public ObservableCollection<IconItem> AvailableIcons { get; } = new();
    public ObservableCollection<string> AvailableIconNames { get; } = new();

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
    private LocoRecord? selected;

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
    private string saveButtonText = "Uložiť zmeny";

    public bool IsGridEnabled => Mode == EditorMode.Viewing;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string addressText = "3";

    // Image path/bitmap removed in refactor

    [ObservableProperty] private string validationMessage = "";

    public Action? RequestClose { get; set; }

    private LocoRecord? _selectionBeforeAdd;

    public LocomotivesWindowViewModel(SettingsManager settings)
    {
        _settings = settings;
        var project = _settings.EnsureProjectSettings();

        Locomotives = new ObservableCollection<LocoRecord>(project.Locomotives);
        Selected = Locomotives.FirstOrDefault();

        // Load available icon file names from Assets/LocoIcons (search several likely locations)
        try
        {
            Debug.WriteLine("LocomotivesWindowViewModel: Searching for Assets/LocoIcons starting from base directory and moving up...");
            var start = AppDomain.CurrentDomain.BaseDirectory ?? AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            var allowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

            var dir = start;
            var maxUp = 6; // climb up to 6 levels
            var foundDir = (string?)null;

            for (var i = 0; i <= maxUp; i++)
            {
                var candidate = Path.Combine(dir, "Assets", "LocoIcons");
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
                Debug.WriteLine("    No Assets/LocoIcons folder found while searching upwards from base directory.");
            }

            Debug.WriteLine($"LocomotivesWindowViewModel: Total available icons = {AvailableIcons.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LocomotivesWindowViewModel: Exception while loading icons: {ex}");
        }

        // If we have icons, select the first by default so the ComboBox shows content
        if (AvailableIcons.Count > 0)
            SelectedIcon = AvailableIcons.First();

        LoadSelectedToEditor();
        OnPropertyChanged(nameof(SelectedLocomotive));
    }

    public LocoRecord? SelectedLocomotive
    {
        get => Selected;
        set => Selected = value;
    }

    partial void OnSelectedChanged(LocoRecord? value)
    {
        // V režime Adding nechceme, aby klik do zoznamu prepisoval rozpracované polia.
        if (Mode == EditorMode.Adding)
            return;

        LoadSelectedToEditor();
    }

    partial void OnNameChanged(string value) => MarkDirtyAndRevalidate();
    partial void OnDescriptionChanged(string value) => MarkDirtyAndRevalidate();
    partial void OnAddressTextChanged(string value)
    {
        MarkDirtyAndRevalidate();
        OnPropertyChanged(nameof(AddressKindText));
    }

    // ImagePath and related change handler removed

    public string AddressKindText
    {
        get
        {
            if (!int.TryParse(AddressText, out var a))
                return "(1…10239)";

            if (a < 1 || a > 10239)
                return "Neplatná (1…10239)";

            return a <= 127
                ? "Krátka (1…127)"
                : "Dlhá (128…10239)";
        }
    }

    private void LoadSelectedToEditor()
    {
        if (Selected == null)
        {
            Name = "";
            Description = "";
            AddressText = "3";
        // ImagePath removed
            ValidationMessage = "";
            EditorIconName = "";
            SelectedIcon = null;
        }
        else
        {
            Name = Selected.Name ?? "";
            Description = Selected.Description ?? "";
            AddressText = Selected.Address.ToString();
            // ImagePath removed
            ValidationMessage = "";
            EditorIconName = Selected.IconName ?? string.Empty;
            // set SelectedIcon to match the loaded icon name
            SelectedIcon = AvailableIcons.FirstOrDefault(i => i.Name == EditorIconName);
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
    }

    private void NotifyAllCanExecutes()
    {
        AddCommand.NotifyCanExecuteChanged();
        SaveChangesCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        // image commands removed
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
        ValidationMessage = Validate(out _);
        SaveChangesCommand.NotifyCanExecuteChanged();
    }

    private string Validate(out int addr)
    {
        addr = 0;

        // Vo Viewing nič nevalidujeme (nemá sa ukladať).
        if (Mode == EditorMode.Viewing)
            return "";

        if (string.IsNullOrWhiteSpace(Name))
            return "Zadajte názov.";

        if (!int.TryParse(AddressText, out addr))
            return "Adresa musí byť číslo.";

        if (addr < 1 || addr > 10239)
            return "Adresa musí byť v rozsahu 1…10239.";

        return "";
    }

    private bool CanBeginAdd() => Mode == EditorMode.Viewing;

    private bool CanSave()
    {
        if (Mode == EditorMode.Viewing)
            return false;

        // v Editing ukladať len ak sú zmeny
        if (Mode == EditorMode.Editing && !IsDirty)
            return false;

        return string.IsNullOrWhiteSpace(Validate(out _));
    }

    private bool CanDelete() => Mode == EditorMode.Viewing && Selected != null;

    private bool CanCancel() => Mode == EditorMode.Adding || Mode == EditorMode.Editing;

    // image commands removed

    [RelayCommand(CanExecute = nameof(CanBeginAdd))]
    private void Add()
    {
        _selectionBeforeAdd = Selected;

        SetMode(EditorMode.Adding);

        // zruš selection, aby bolo jasné, že nevykonávaš zmeny na existujúcom zázname
        Selected = null;

        Name = "";
        Description = "";
        AddressText = NextFreeAddress().ToString();
            // ImagePath removed
        ValidationMessage = "";
        IsDirty = false;

        NotifyAllCanExecutes();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveChanges()
    {
        var msg = Validate(out var addr);
        if (!string.IsNullOrWhiteSpace(msg))
        {
            ValidationMessage = msg;
            return;
        }

        if (Mode == EditorMode.Adding)
        {
                var rec = new LocoRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = Name.Trim(),
                Address = addr,
                Description = Description ?? "",
                IconName = EditorIconName ?? string.Empty
            };

            Locomotives.Add(rec);
            Selected = rec;

            PersistAndSave();

            IsDirty = false;
            ValidationMessage = "";
            SetMode(EditorMode.Viewing);
            return;
        }

        // Editing
        if (Selected == null)
            return;

        Selected.Name = Name.Trim();
        Selected.Description = Description ?? "";
        Selected.Address = addr;
        Selected.IconName = EditorIconName ?? string.Empty;

        PersistAndSave();

        IsDirty = false;
        ValidationMessage = "";
        SetMode(EditorMode.Viewing);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (Selected == null) return;

        var toRemove = Selected;
        var idx = Locomotives.IndexOf(toRemove);

        Locomotives.Remove(toRemove);
        Selected = Locomotives.Count == 0
            ? null
            : Locomotives[Math.Min(idx, Locomotives.Count - 1)];

        PersistAndSave();
        // zostávame vo Viewing – výber sa zmenil, polia sa obnovia cez OnSelectedChanged
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (Mode == EditorMode.Adding)
        {
            SetMode(EditorMode.Viewing);
            Selected = _selectionBeforeAdd ?? Locomotives.FirstOrDefault();
            _selectionBeforeAdd = null;

            LoadSelectedToEditor();
            return;
        }

        // Editing
        LoadSelectedToEditor();
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    // image commands removed

    private int NextFreeAddress()
    {
        var used = Locomotives.Select(l => l.Address).Where(a => a > 0).ToHashSet();
        for (var a = 3; a <= 10239; a++)
            if (!used.Contains(a))
                return a;
        return 3;
    }

    private void PersistAndSave()
    {
        var project = _settings.EnsureProjectSettings();
        project.Locomotives = Locomotives.ToList();

        if (!_settings.SaveProject())
            ValidationMessage = "Projekt nie je uložený na disk. Použi Súbor → Uložiť ako…";
    }

    // Image bitmap handling removed
}

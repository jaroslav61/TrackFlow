using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrackFlow.Models;
using TrackFlow.Services;

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

    [ObservableProperty]
    private LocoRecord? selected;

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

    [ObservableProperty] private string? imagePath;
    [ObservableProperty] private Bitmap? imageBitmap;

    [ObservableProperty] private string validationMessage = "";

    public Action? RequestClose { get; set; }
    public Func<Task<string?>>? PickImagePathAsync { get; set; }

    private LocoRecord? _selectionBeforeAdd;

    public LocomotivesWindowViewModel(SettingsManager settings)
    {
        _settings = settings;
        var project = _settings.EnsureProjectSettings();

        Locomotives = new ObservableCollection<LocoRecord>(project.Locomotives);
        Selected = Locomotives.FirstOrDefault();

        LoadSelectedToEditor();
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

    partial void OnImagePathChanged(string? value)
    {
        UpdateImageBitmap(value);
        MarkDirtyAndRevalidate();
    }

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
            ImagePath = null; // spustí OnImagePathChanged -> ImageBitmap=null
            ValidationMessage = "";
        }
        else
        {
            Name = Selected.Name ?? "";
            Description = Selected.Description ?? "";
            AddressText = Selected.Address.ToString();
            ImagePath = Selected.ImagePath; // spustí OnImagePathChanged
            ValidationMessage = "";
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
        ChooseImageCommand.NotifyCanExecuteChanged();
        ClearImageCommand.NotifyCanExecuteChanged();
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

    // Obrázok chceš vedieť vybrať aj v Adding (draft), aj v Editing.
    private bool CanChooseImage() => Mode == EditorMode.Adding || Mode == EditorMode.Editing;

    private bool CanClearImage() => (Mode == EditorMode.Adding || Mode == EditorMode.Editing) && !string.IsNullOrWhiteSpace(ImagePath);

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
        ImagePath = null;
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
                ImagePath = ImagePath
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
        Selected.ImagePath = ImagePath;

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

    [RelayCommand(CanExecute = nameof(CanChooseImage))]
    private async Task ChooseImage()
    {
        if (PickImagePathAsync == null)
            return;

        var path = await PickImagePathAsync();
        if (!string.IsNullOrWhiteSpace(path))
            ImagePath = path;
    }

    [RelayCommand(CanExecute = nameof(CanClearImage))]
    private void ClearImage() => ImagePath = null;

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

    private void UpdateImageBitmap(string? path)
    {
        ImageBitmap?.Dispose();
        ImageBitmap = null;

        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (!File.Exists(path))
                return;

            ImageBitmap = new Bitmap(path);
        }
        catch
        {
            ImageBitmap = null;
        }
    }
}

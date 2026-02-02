using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System.Text;
using System.Threading.Tasks;
using TrackFlow.ViewModels.Library;
using Avalonia.Threading;
using System.ComponentModel;


namespace TrackFlow.Views.Library;

public partial class LocomotivesWindow : Window
{
    private LocomotivesWindowViewModel? _vm;
    private bool _addressSanitizeGuard;

    public LocomotivesWindow()

   {
        InitializeComponent();

        DataContextChanged += (_, _) =>
         {
            AttachVm(DataContext as LocomotivesWindowViewModel);
        };

        this.Opened += (_, _) =>
        {
            if (DataContext == null)
                Title = "Editor lokomotív  [DataContext = NULL]";
        };


        // digits-only pre decoder address
        var box = this.FindControl<TextBox>("AddressBox");
        if (box != null)
        {
            box.AddHandler(TextInputEvent, OnAddressTextInput, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            // Zachytí aj paste / drag-drop / IME – všetko sa prefiltruje na číslice.
            box.TextChanging += OnAddressTextChanging;
        }

      AttachVm(DataContext as LocomotivesWindowViewModel);
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName != nameof(LocomotivesWindowViewModel.Mode))
        return;

    if (sender is not LocomotivesWindowViewModel vm)
        return;

    if (vm.Mode != LocomotivesWindowViewModel.EditorMode.Adding)
        return;

    // Po prepnutí do Adding daj fokus do názvu (a vyber text).
    Dispatcher.UIThread.Post(() =>
    {
        NameBox.Focus();
        NameBox.SelectAll();
    });
}

    private void OnAddressTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        foreach (var ch in e.Text)
        {
            if (!char.IsDigit(ch))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void OnAddressTextChanging(object? sender, TextChangingEventArgs e)
    {
        if (_addressSanitizeGuard)
            return;

        if (sender is not TextBox box)
            return;

        var text = box.Text ?? string.Empty;

        // Ak sú tam len číslice, nerob nič.
        var allDigits = true;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]))
            {
                allDigits = false;
                break;
            }
        }
        if (allDigits)
            return;

        // Prefiltruj na číslice (funguje aj po paste).
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (char.IsDigit(ch))
                sb.Append(ch);

        var caret = box.CaretIndex;
        _addressSanitizeGuard = true;
        box.Text = sb.ToString();
        box.CaretIndex = caret > box.Text.Length ? box.Text.Length : caret;
        _addressSanitizeGuard = false;
    }

    private void AttachVm(LocomotivesWindowViewModel? vm)
    {
        if (_vm == vm)
            return;

        if (_vm != null)
        {
            _vm.PropertyChanged -= VmOnPropertyChanged;
            _vm.RequestClose = null;
            _vm.PickImagePathAsync = null;
        }

        _vm = vm;
        if (_vm == null)
            return;

        _vm.PropertyChanged += VmOnPropertyChanged;
        _vm.RequestClose = Close;
        _vm.PickImagePathAsync = PickImagePathAsync;
    }

    private async Task<string?> PickImagePathAsync()
    {
        var sp = StorageProvider;
        if (sp == null)
            return null;

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Vyberte obrázok lokomotívy",
            AllowMultiple = false,
            FileTypeFilter =
         [
                        new FilePickerFileType("Obrázky")
                {
Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"],
MimeTypes = ["image/png", "image/jpeg", "image/webp", "image/bmp"]
                },
FilePickerFileTypes.All
                    ]
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file == null)
            return null;

        try { return file.TryGetLocalPath(); }
        catch { return null; }
    }
}
using Avalonia.Controls;
using Avalonia.Input;
using System.Text;
using TrackFlow.ViewModels.Library;
using Avalonia.Threading;
using System.ComponentModel;


namespace TrackFlow.Views.Library;

public partial class VagonsWindow : Window
{
    private VagonsWindowViewModel? _vm;
    private bool _addressSanitizeGuard;

    public VagonsWindow()

   {
        InitializeComponent();

        DataContextChanged += (_, _) =>
         {
            AttachVm(DataContext as VagonsWindowViewModel);
        };

        this.Opened += (_, _) =>
        {
            if (DataContext == null)
                Title = "Editor vozòov  [DataContext = NULL]";
        };

      AttachVm(DataContext as VagonsWindowViewModel);
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName != nameof(VagonsWindowViewModel.Mode))
        return;

    if (sender is not VagonsWindowViewModel vm)
        return;

    if (vm.Mode != VagonsWindowViewModel.EditorMode.Adding)
        return;

    // Po prepnutí do Adding daj fokus do kódu (a vyber text).
    Dispatcher.UIThread.Post(() =>
    {
        var box = this.FindControl<TextBox>("CodeBox");
        box?.Focus();
        box?.SelectAll();
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

        // Ak sú tam len èíslice, nerob niè.
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

        // Prefiltruj na èíslice (funguje aj po paste).
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

    private void AttachVm(VagonsWindowViewModel? vm)
    {
        if (_vm == vm)
            return;

        if (_vm != null)
        {
            _vm.PropertyChanged -= VmOnPropertyChanged;
            _vm.RequestClose = null;
        }

        _vm = vm;
        if (_vm == null)
            return;

        _vm.PropertyChanged += VmOnPropertyChanged;
        _vm.RequestClose = Close;
    }

    // Image picker removed
}

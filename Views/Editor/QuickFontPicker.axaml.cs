using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using System.Linq;

namespace TrackFlow.Views.Editor;

public partial class QuickFontPicker : Window
{
    public string? SelectedFont { get; private set; }
    public double SelectedSize { get; private set; } = 12;
    public bool DialogResultOk { get; private set; }

    public QuickFontPicker()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public QuickFontPicker(string currentFont, double currentSize) : this()
    {
        var fontListBox = this.FindControl<ListBox>("FontListBox");
        var fontSizeInput = this.FindControl<NumericUpDown>("FontSizeInput");
        
        if (fontListBox != null)
        {
            // Nájdi a vyber aktuálny font
            foreach (var item in (fontListBox.Items?.OfType<ListBoxItem>() ?? Enumerable.Empty<ListBoxItem>()))
            {
                if (item.Content?.ToString() == currentFont)
                {
                    fontListBox.SelectedItem = item;
                    break;
                }
            }
            // Ak sa nenašie, vyber prvý
            if (fontListBox.SelectedIndex < 0 && fontListBox.ItemCount > 0)
                fontListBox.SelectedIndex = 0;
        }
        
        if (fontSizeInput != null)
        {
            fontSizeInput.Value = (decimal)currentSize;
        }
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        var fontListBox = this.FindControl<ListBox>("FontListBox");
        var fontSizeInput = this.FindControl<NumericUpDown>("FontSizeInput");
        
        if (fontListBox?.SelectedItem is ListBoxItem selectedItem)
        {
            SelectedFont = selectedItem.Content?.ToString();
        }
        
        if (fontSizeInput != null)
        {
            SelectedSize = (double)(fontSizeInput.Value ?? 12m);
        }
        
        DialogResultOk = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        DialogResultOk = false;
        Close();
    }

    private void SystemFontPicker_Click(object? sender, RoutedEventArgs e)
    {
        // Signalizuje že user chce použiť systémový dialog
        SelectedFont = "__SYSTEM__";
        DialogResultOk = true;
        Close();
    }
}


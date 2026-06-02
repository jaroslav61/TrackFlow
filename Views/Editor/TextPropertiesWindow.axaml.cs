using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrackFlow.ViewModels.Editor;
using System.Windows.Forms; // Windows Forms for FontDialog and ColorDialog
using System.Drawing;
using System;
using System.Runtime.InteropServices;

namespace TrackFlow.Views.Editor;

public partial class TextPropertiesWindow : Window
{
    // Win32Window wrapper pre Windows Forms dialógy
    private class Win32Window : IWin32Window
    {
        public IntPtr Handle { get; }
        public Win32Window(IntPtr handle) => Handle = handle;
    }

    public TextPropertiesWindow()
    {
        AvaloniaXamlLoader.Load(this);
        
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TextPropertiesViewModel vm)
            {
                vm.RequestClose = () => Close(vm.DialogResult);
                vm.ShowFontPicker = () => ShowFontPicker(vm);
                vm.ShowBackgroundColorPicker = ShowColorPicker;
                vm.ShowFrameColorPicker = ShowColorPicker;
            }
        };
    }

    private IWin32Window? GetWin32Window()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var handle = TryGetPlatformHandle()?.Handle;
            return handle.HasValue ? new Win32Window(handle.Value) : null;
        }
        return null;
    }

    private void ShowFontPicker(TextPropertiesViewModel vm)
    {
        try
        {
            var owner = GetWin32Window();
            
            // Vytvorenie Font style z boolean flags
            System.Drawing.FontStyle style = System.Drawing.FontStyle.Regular;
            if (vm.IsBold) style |= System.Drawing.FontStyle.Bold;
            if (vm.IsItalic) style |= System.Drawing.FontStyle.Italic;
            if (vm.IsUnderline) style |= System.Drawing.FontStyle.Underline;
            if (vm.IsStrikethrough) style |= System.Drawing.FontStyle.Strikeout;

            using var fontDialog = new FontDialog
            {
                FontMustExist = true,
                AllowScriptChange = false,
                ShowColor = true,
                ShowEffects = true,
                AllowVectorFonts = true,
                AllowVerticalFonts = false,
                MaxSize = 72,
                MinSize = 6,
                ShowApply = false,
                Font = new Font(vm.SelectedFont, (float)vm.FontSize, style)
            };

            // Nastavenie farby ak nie je "Automatický"
            if (vm.FillColor != "Automatický" && vm.FillColor.StartsWith("#"))
            {
                try
                {
                    var color = ColorTranslator.FromHtml(vm.FillColor);
                    fontDialog.Color = color;
                }
                catch { }
            }

            var result = owner != null 
                ? fontDialog.ShowDialog(owner) 
                : fontDialog.ShowDialog();
                
            if (result == DialogResult.OK)
            {
                vm.SelectedFont = fontDialog.Font.FontFamily.Name;
                vm.FontSize = fontDialog.Font.Size;
                
                // Uloženie štýlov
                vm.IsBold = fontDialog.Font.Bold;
                vm.IsItalic = fontDialog.Font.Italic;
                vm.IsUnderline = fontDialog.Font.Underline;
                vm.IsStrikethrough = fontDialog.Font.Strikeout;
                
                // Uloženie farby
                var selectedColor = fontDialog.Color;
                vm.FillColor = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
            }
        }
        catch (Exception ex)
        {
            // Log error for debugging
            System.Diagnostics.Debug.WriteLine($"FontDialog error: {ex.Message}");
        }
    }

    private string? ShowColorPicker(string currentColor)
    {
        try
        {
            var owner = GetWin32Window();
            
            using var colorDialog = new ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                SolidColorOnly = false,
                AllowFullOpen = true
            };

            // Nastavenie aktuálnej farby
            if (currentColor.StartsWith("#") && currentColor.Length == 7)
            {
                try
                {
                    var color = ColorTranslator.FromHtml(currentColor);
                    colorDialog.Color = color;
                }
                catch
                {
                    colorDialog.Color = System.Drawing.Color.Black;
                }
            }
            else if (currentColor == "Transparent")
            {
                colorDialog.Color = System.Drawing.Color.White;
            }
            else
            {
                colorDialog.Color = System.Drawing.Color.Black;
            }

            var result = owner != null 
                ? colorDialog.ShowDialog(owner) 
                : colorDialog.ShowDialog();
                
            if (result == DialogResult.OK)
            {
                var selectedColor = colorDialog.Color;
                return $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}









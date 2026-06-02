using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using System;

namespace TrackFlow.Views.Library;

public partial class RenameTrainWindow : Window
{
    public RenameTrainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        // subscribe to Opened to ensure selection after window is shown
        this.Opened += Window_Opened;
    }

    public override void EndInit()
    {
        base.EndInit();
        // When window finishes initialization, schedule focus on the NameTextBox
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var tb = this.FindControl<TextBox>("NameTextBox");
            tb?.Focus();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        var tb = this.FindControl<TextBox>("NameTextBox");
        Close(tb?.Text);
    }

    private void Window_Opened(object? sender, EventArgs e)
    {
        // After window opened, focus and select all text so user can type immediately
        Dispatcher.UIThread.Post(() =>
        {
            var tb = this.FindControl<TextBox>("NameTextBox");
            if (tb != null)
            {
                tb.Focus();
                try
                {
                    tb.SelectAll();
                }
                catch
                {
                    tb.SelectionStart = 0;
                    tb.SelectionEnd = tb.Text?.Length ?? 0;
                }
            }
        }, DispatcherPriority.Input);
    }
}


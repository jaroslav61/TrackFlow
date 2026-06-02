using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace TrackFlow.Views.Dialogs;

public partial class ConfirmDialog : Window
{
    public enum DialogResult
    {
        Yes,
        No,
        Cancel
    }

    public DialogResult Result { get; private set; } = DialogResult.Cancel;

    public ConfirmDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public ConfirmDialog(string title, string message) : this()
    {
        var titleText = this.FindControl<TextBlock>("TitleText");
        var messageText = this.FindControl<TextBlock>("MessageText");
        
        if (titleText != null)
            titleText.Text = title;
        if (messageText != null)
            messageText.Text = message;
        
        Title = title;
    }

    private void YesButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = DialogResult.Yes;
        Close();
    }

    private void NoButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = DialogResult.No;
        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = DialogResult.Cancel;
        Close();
    }
}


using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop.Views;

public enum DialogKind
{
    Info,
    Question,
    Warning,
    Error
}

/// <summary>Which button closed the dialog.</summary>
public enum DialogChoice
{
    Primary,
    Secondary,
    Cancel
}

/// <summary>
/// The program's own message box: same look as the other windows (Mica, rounded buttons).
/// Up to three buttons: primary (Enter), secondary and cancel (Esc).
/// </summary>
public partial class DialogWindow : FluentWindow
{
    public DialogChoice Choice { get; private set; } = DialogChoice.Cancel;

    public DialogWindow(
        string title,
        string message,
        DialogKind kind,
        string primaryText,
        string? secondaryText = null,
        string? cancelText = null)
    {
        InitializeComponent();

        Title = title;
        Bar.Title = title;
        MessageText.Text = message;

        (SymbolRegular symbol, Color color) = kind switch
        {
            DialogKind.Error => (SymbolRegular.ErrorCircle24, Color.FromRgb(0xFF, 0x6B, 0x6B)),
            DialogKind.Warning => (SymbolRegular.Warning24, Color.FromRgb(0xFF, 0xC1, 0x5C)),
            DialogKind.Question => (SymbolRegular.QuestionCircle24, Color.FromRgb(0xB7, 0xAF, 0xE9)),
            _ => (SymbolRegular.Info24, Color.FromRgb(0x46, 0xD2, 0xC8))
        };

        KindIcon.Symbol = symbol;
        KindIcon.Foreground = new SolidColorBrush(color);

        PrimaryButton.Content = primaryText;
        ShowOptional(SecondaryButton, secondaryText);
        ShowOptional(CancelButton, cancelText);
    }

    /// <summary>Shows the dialog over the active window and waits for the answer.</summary>
    public static DialogChoice Show(
        string title,
        string message,
        DialogKind kind,
        string primaryText,
        string? secondaryText = null,
        string? cancelText = null)
    {
        var dialog = new DialogWindow(title, message, kind, primaryText, secondaryText, cancelText);

        Window? owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive && window.IsLoaded && !ReferenceEquals(window, dialog))
            ?? Application.Current.MainWindow;

        if (owner is { IsLoaded: true })
            dialog.Owner = owner;

        dialog.ShowDialog();
        return dialog.Choice;
    }

    private static void ShowOptional(Wpf.Ui.Controls.Button button, string? text)
    {
        button.Content = text;
        button.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool _finished;

    private void Finish(DialogChoice choice)
    {
        // Esc can arrive twice (key handler and the cancel button): close only once.
        if (_finished)
            return;

        _finished = true;
        Choice = choice;
        Close();
    }

    private void OnPrimary(object sender, RoutedEventArgs e) => Finish(DialogChoice.Primary);

    private void OnSecondary(object sender, RoutedEventArgs e) => Finish(DialogChoice.Secondary);

    private void OnCancel(object sender, RoutedEventArgs e) => Finish(DialogChoice.Cancel);

    // Esc closes the dialog even when there is no visible cancel button (counts as cancel).
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Finish(DialogChoice.Cancel);
    }
}

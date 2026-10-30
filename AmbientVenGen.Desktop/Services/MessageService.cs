using System.Windows;

namespace AmbientVenGen.Desktop.Services;

public sealed class MessageService
{
    private const string Caption = "Ambient Forge";

    public void ShowError(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, Caption, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, Caption, MessageBoxButton.YesNo, MessageBoxImage.Question)
        == MessageBoxResult.Yes;

    /// <summary>Yes = save, No = discard, Cancel = abort the operation.</summary>
    public MessageBoxResult AskSaveChanges(string projectName) =>
        MessageBox.Show(
            Application.Current.MainWindow,
            $"Save changes to \"{projectName}\"?",
            Caption,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
}

using AmbientVenGen.Desktop.Views;
using System.Windows;

namespace AmbientVenGen.Desktop.Services;

/// <summary>All message boxes of the program (styled <see cref="DialogWindow"/> instead of the system MessageBox).</summary>
public sealed class MessageService
{
    private const string Caption = "Ambient Forge";

    public void ShowError(string message) =>
        DialogWindow.Show(Caption, message, DialogKind.Error, "OK");

    public bool Confirm(string message, string yesText = "Yes", string noText = "No") =>
        DialogWindow.Show(Caption, message, DialogKind.Question, yesText, null, noText) == DialogChoice.Primary;

    /// <summary>Yes = save, No = discard, Cancel = abort the operation.</summary>
    public MessageBoxResult AskSaveChanges(string projectName) =>
        DialogWindow.Show(
            Caption,
            $"Save changes to \"{projectName}\"?",
            DialogKind.Question,
            "Save",
            "Don't save",
            "Cancel") switch
        {
            DialogChoice.Primary => MessageBoxResult.Yes,
            DialogChoice.Secondary => MessageBoxResult.No,
            _ => MessageBoxResult.Cancel
        };
}

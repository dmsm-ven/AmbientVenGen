using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop.Views;

/// <summary>A small window that asks for one tag to put on every file that was just added.</summary>
public partial class CommonTagWindow : FluentWindow
{
    /// <summary>The chosen tag; empty when the window was skipped.</summary>
    public string Tag { get; private set; } = string.Empty;

    public CommonTagWindow(int count, IEnumerable<string> existingTags)
    {
        InitializeComponent();

        MessageText.Text = $"{count} files were added. Tag all of them with:";
        TagBox.ItemsSource = existingTags.ToList();
    }

    /// <summary>Returns the tag to put on all the new files, or an empty string.</summary>
    public static string Ask(int count, IEnumerable<string> existingTags)
    {
        var window = new CommonTagWindow(count, existingTags);

        Window? owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive && w.IsLoaded && !ReferenceEquals(w, window));

        if (owner is not null)
            window.Owner = owner;

        window.ShowDialog();
        return window.Tag;
    }

    private bool _finished;

    private void Finish(string tag)
    {
        if (_finished)
            return;

        _finished = true;
        Tag = tag;
        Close();
    }

    private void OnApply(object sender, RoutedEventArgs e) => Finish((TagBox.Text ?? string.Empty).Trim());

    private void OnSkip(object sender, RoutedEventArgs e) => Finish(string.Empty);

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Finish(string.Empty);
    }
}

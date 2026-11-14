using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop.Views;

/// <summary>What was chosen in the "Add layer" window.</summary>
public enum AddLayerChoice { Drone, Pad, Noise, FieldRecord, OpenFile }

/// <summary>A modal window with big tiles: a click on a tile closes it with that choice.</summary>
public partial class AddLayerWindow : FluentWindow
{
    private AddLayerChoice? _choice;

    public AddLayerWindow() => InitializeComponent();

    /// <summary>Shows the window; returns null when it was closed without a choice.</summary>
    public static AddLayerChoice? Ask()
    {
        var window = new AddLayerWindow();

        Window? owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive && w.IsLoaded && !ReferenceEquals(w, window));

        if (owner is not null)
            window.Owner = owner;

        window.ShowDialog();
        return window._choice;
    }

    private void OnTile(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string tag } && Enum.TryParse(tag, out AddLayerChoice choice))
        {
            _choice = choice;
            Close();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}

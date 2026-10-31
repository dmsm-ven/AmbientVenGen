using AmbientVenGen.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace AmbientVenGen.Desktop.Services;

/// <summary>
/// Opens the preset library as a normal (non-modal) window, only one at a time.
/// A new window is created on every open: a closed WPF window cannot be shown again.
/// </summary>
public sealed class PresetLibraryWindowService(IServiceProvider services)
{
    private PresetLibraryWindow? _window;

    /// <summary>Raised when the library window has been closed.</summary>
    public event Action? WindowClosed;

    public void Show()
    {
        if (_window is not null)
        {
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;

            _window.Activate();
            return;
        }

        PresetLibraryWindow window = services.GetRequiredService<PresetLibraryWindow>();

        // An owned window closes together with the main window.
        window.Owner = Application.Current.MainWindow;
        window.Closed += (_, _) =>
        {
            _window = null;
            WindowClosed?.Invoke();
        };

        _window = window;
        window.Show();
    }
}

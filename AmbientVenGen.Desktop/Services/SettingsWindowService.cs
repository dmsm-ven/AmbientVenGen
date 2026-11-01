using AmbientVenGen.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace AmbientVenGen.Desktop.Services;

/// <summary>Opens the settings window (modal, over the main window).</summary>
public sealed class SettingsWindowService(IServiceProvider services)
{
    public void Show()
    {
        SettingsWindow window = services.GetRequiredService<SettingsWindow>();

        if (Application.Current.MainWindow is { IsLoaded: true } owner)
            window.Owner = owner;

        window.ShowDialog();
    }
}

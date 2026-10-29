using AmbientVenGen.Desktop.Audio;
using AmbientVenGen.Desktop.Presets;
using AmbientVenGen.Desktop.Services;
using AmbientVenGen.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace AmbientVenGen.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IPresetService, PresetService>();
                services.AddSingleton<CompositionSettingsViewModel>();

                services.AddSingleton<AudioPlaybackService>();
                services.AddSingleton<AudioExportService>();
                services.AddSingleton<FileDialogService>();

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _host.StartAsync().GetAwaiter().GetResult();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.StopAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}

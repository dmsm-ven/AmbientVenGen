using AmbientVenGen.Desktop.Audio;
using AmbientVenGen.Desktop.Services;
using AmbientVenGen.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace AmbientVenGen.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<TemplateService>();
                services.AddSingleton<ProjectFileService>();
                services.AddSingleton<MessageService>();

                services.AddSingleton<AudioPlaybackService>();
                services.AddSingleton<AudioExportService>();
                services.AddSingleton<FileDialogService>();
                services.AddSingleton<ExportOptionsDialogService>();

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            await _host?.StopAsync();
        }
        finally
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}

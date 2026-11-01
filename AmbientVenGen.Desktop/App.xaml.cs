using AmbientVenGen.Core.Projects;
using AmbientVenGen.Desktop.Audio;
using AmbientVenGen.Desktop.DataAccess.Repositories;
using AmbientVenGen.Desktop.Services;
using AmbientVenGen.Desktop.ViewModels;
using AmbientVenGen.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
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
                services.AddSingleton<PresetPreviewService>();
                services.AddSingleton<AppSettings>();
                services.AddSingleton<FileAssociationService>();
                services.AddSingleton<SettingsWindowService>();
                services.AddTransient<SettingsWindow>();
                services.AddSingleton<AudioExportService>();
                services.AddSingleton<FileDialogService>();
                services.AddSingleton<ExportOptionsDialogService>();
                services.AddSingleton<PresetLibraryWindowService>();

                services.AddSingleton<FieldPresetRepository>();
                services.AddSingleton<AudioFileAnalyzer>();

                // A new library window is created on every open (a closed WPF window cannot be shown again).
                services.AddTransient<PresetLibraryViewModel>();
                services.AddTransient<PresetLibraryWindow>();

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;

        // Double click on a .afproj file starts the program with the path as the first argument.
        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            _host.Services.GetRequiredService<MainViewModel>().OpenProjectFile(e.Args[0]);

        mainWindow.Show();
        _host.Services.GetRequiredService<FileAssociationService>().EnsureRegistered();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            await _host!.StopAsync();
        }
        finally
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}

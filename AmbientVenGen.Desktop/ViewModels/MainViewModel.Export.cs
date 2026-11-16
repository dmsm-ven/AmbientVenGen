using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;
using AmbientVenGen.Core.Export;
using AmbientVenGen.Core.Projects;
using AmbientVenGen.Desktop.Services.Audio;
using AmbientVenGen.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>Export and the auxiliary windows (settings, preset library).</summary>
public partial class MainViewModel
{
    // ---------------------------------------------------------------- preset library

    [RelayCommand]
    private void OpenSettings() => _settingsWindowService.Show();

    [RelayCommand]
    private void OpenPresetLibrary() => _presetLibraryWindowService.Show();

    // ---------------------------------------------------------------- export

    private bool CanExport() => !IsExporting;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        if (Project.Layers.Count == 0)
        {
            StatusMessage = "Nothing to export: add at least one layer";
            return;
        }

        IsExporting = true;

        try
        {
            // The window shows the options and then the progress; it calls RunExportAsync and closes itself afterwards.
            _exportOptionsDialogService.Show(FormatTime(Project.DurationSeconds), _lastExportOptions, RunExportAsync);
        }
        finally
        {
            IsExporting = false;
        }
    }

    /// <summary>Asks for the file and renders into it. True when the export completed (the window closes then).</summary>
    private async Task<bool> RunExportAsync(ExportOptions options, ExportOptionsViewModel dialog, CancellationToken token)
    {
        _lastExportOptions = options;
        Settings.ExportOptions = options;

        string? path = _fileDialogService.ChooseExportPath(options.Format == ExportFormat.Mp3, ProjectName);
        if (string.IsNullOrWhiteSpace(path))
            return false;

        StatusMessage = "Rendering composition...";
        var clock = Stopwatch.StartNew();
        var progress = new Progress<double>(value => dialog.ReportProgress(value, clock));

        try
        {
            ProjectSpec spec = Project.ToSpec();

            if (options.TruncateToLastLayer)
            {
                // the end of the last audible layer (with its repeats); never beyond the canvas
                double end = Project.Layers.Where(l => !l.Muted).Select(l => l.StartSeconds + l.TotalSeconds).DefaultIfEmpty(spec.DurationSeconds).Max();
                spec = spec with { DurationSeconds = Math.Clamp(Math.Ceiling(end), 1, spec.DurationSeconds) };
            }

            await _exportService.ExportAsync(path, spec, options, progress, token);
            StatusMessage = $"Export completed: {path}";
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Export canceled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
            _messageService.ShowError($"Export error:\n{ex.Message}");
        }

        return false;
    }
}

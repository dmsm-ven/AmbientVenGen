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
    private async Task ExportAsync()
    {
        if (Project.Layers.Count == 0)
        {
            StatusMessage = "Nothing to export: add at least one layer";
            return;
        }

        ExportOptions? options = _exportOptionsDialogService.Show(
            FormatTime(Project.DurationSeconds),
            _lastExportOptions);

        if (options is null)
            return;

        _lastExportOptions = options;

        string? path = _fileDialogService.ChooseExportPath(options.Format == ExportFormat.Mp3, Project.Seed);
        if (string.IsNullOrWhiteSpace(path))
            return;

        using var cancellation = new CancellationTokenSource();
        _exportCts = cancellation;

        ExportProgress = 0;
        IsExporting = true;
        StatusMessage = "Rendering composition...";

        var progress = new Progress<double>(value =>
        {
            if (IsExporting)
                ExportProgress = value;
        });

        try
        {
            ProjectSpec spec = Project.ToSpec();

            if (options.TruncateToLastLayer)
            {
                // the end of the last audible layer (with its repeats); never beyond the canvas
                double end = Project.Layers.Where(l => !l.Muted).Select(l => l.StartSeconds + l.TotalSeconds).DefaultIfEmpty(spec.DurationSeconds).Max();
                spec = spec with { DurationSeconds = Math.Clamp(Math.Ceiling(end), 1, spec.DurationSeconds) };
            }

            await _exportService.ExportAsync(path, spec, options, progress, cancellation.Token);
            StatusMessage = $"Export completed: {path}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Export canceled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            _exportCts = null;
            IsExporting = false;
            ExportProgress = 0;
        }
    }

    [RelayCommand]
    private void CancelExport() => _exportCts?.Cancel();
}

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

/// <summary>Project files: new, open, save, load and the unsaved-changes check.</summary>
public partial class MainViewModel
{
    // ---------------------------------------------------------------- project files

    [RelayCommand]
    private void NewProject()
    {
        if (!ConfirmDiscardChanges())
            return;

        LoadSpec(ProjectSpec.Empty with { Seed = Random.Shared.Next(1, int.MaxValue) }, null);
        StatusMessage = "New project";
    }

    [RelayCommand]
    private void OpenProject()
    {
        if (!ConfirmDiscardChanges())
            return;

        string? path = _fileDialogService.ChooseProjectToOpen();
        if (path is null)
            return;

        LoadFromPath(path);
    }

    /// <summary>Opens a project from the "Open recent" list.</summary>
    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (string.IsNullOrEmpty(path) || !ConfirmDiscardChanges())
            return;

        if (!File.Exists(path))
        {
            Settings.RemoveRecentProject(path);
            _messageService.ShowError($"The project file does not exist any more:\n{path}");
            return;
        }

        LoadFromPath(path);
    }

    private void LoadFromPath(string path)
    {
        try
        {
            ProjectSpec spec = _projectFileService.Load(path);
            LoadSpec(spec, path);
            StatusMessage = $"Opened {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot open the project:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void SaveProject() => TrySave(saveAs: false);

    [RelayCommand]
    private void SaveProjectAs() => TrySave(saveAs: true);

    private bool TrySave(bool saveAs)
    {
        string? path = CurrentFilePath;

        if (saveAs || path is null)
        {
            path = _fileDialogService.ChooseProjectSavePath(ProjectName);
            if (path is null)
                return false;
        }

        try
        {
            _projectFileService.Save(path, Project.ToSpec());
            CurrentFilePath = path;
            Settings.LastProjectPath = path;
            Settings.AddRecentProject(path);
            _savedSnapshot = TakeSnapshot();
            IsDirty = false;
            StatusMessage = $"Saved {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot save the project:\n{ex.Message}");
            return false;
        }
    }

    /// <summary>Asks to save unsaved changes. Returns false if the user cancels.</summary>
    public bool ConfirmDiscardChanges()
    {
        RefreshDirty();
        if (!IsDirty)
            return true;

        return _messageService.AskSaveChanges(ProjectName) switch
        {
            MessageBoxResult.Yes => TrySave(saveAs: false),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    /// <summary>Opens a project given by path (started from Explorer by a double click). Errors are shown to the user.</summary>
    public void OpenProjectFile(string path)
    {
        try
        {
            LoadSpec(_projectFileService.Load(path), path);
            StatusMessage = $"Opened {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot open the project:\n{ex.Message}");
        }
    }

    // Reopens the project that was used last time; a missing or broken file silently leaves the default composition.
    private void TryOpenLastProject()
    {
        string? path = Settings.LastProjectPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            LoadSpec(_projectFileService.Load(path), path);
            StatusMessage = $"Opened {Path.GetFileName(path)}";
        }
        catch (Exception)
        {
            // Keep the default composition.
        }
    }

    private void LoadSpec(ProjectSpec spec, string? path)
    {
        StopPlaybackSilently();
        SelectedLayer = null;
        ResetSolo();
        ClearDeleted();
        EnsureLengthOption(spec.DurationSeconds);

        _loading = true;
        try
        {
            Project.Load(spec);
        }
        finally
        {
            _loading = false;
        }

        PlayheadSeconds = 0;
        CurrentFilePath = path;
        _savedSnapshot = TakeSnapshot();
        IsDirty = false;
        if (path is not null)
        {
            Settings.LastProjectPath = path;
            Settings.AddRecentProject(path);
        }

        OnPropertyChanged(nameof(TimeText));
    }
}

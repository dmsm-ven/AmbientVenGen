using AmbientVenGen.Core.Projects;
using Microsoft.Win32;
using System.IO;

namespace AmbientVenGen.Desktop.Services;

public sealed class FileDialogService(AppSettings settings)
{
    private const string ProjectFilter = "Ambient Forge project (*.afproj)|*.afproj";

    public string? ChooseExportPath(bool mp3, string projectName)
    {
        var dialog = new SaveFileDialog
        {
            Title = mp3 ? "Export MP3" : "Export WAV",
            Filter = mp3
                ? "MP3 audio (*.mp3)|*.mp3"
                : "WAV audio (*.wav)|*.wav",
            FileName = SafeName(projectName) + (mp3 ? ".mp3" : ".wav"),
            InitialDirectory = ExistingOrNull(settings.LastExportDirectory),
            AddExtension = true,
            DefaultExt = mp3 ? ".mp3" : ".wav",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
            return null;

        settings.LastExportDirectory = Path.GetDirectoryName(dialog.FileName);
        return dialog.FileName;
    }

    public IReadOnlyList<string> ChooseFieldPresetFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add field recordings",
            Filter = "Audio and video (*.flac;*.mp3;*.wav;*.m4a;*.mp4)|*.flac;*.mp3;*.wav;*.m4a;*.mp4|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public string? ChooseProjectToOpen()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open project",
            Filter = ProjectFilter,
            InitialDirectory = ExistingOrNull(settings.LastProjectDirectory),
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return null;

        settings.LastProjectDirectory = Path.GetDirectoryName(dialog.FileName);
        return dialog.FileName;
    }

    public string? ChooseProjectSavePath(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save project",
            Filter = ProjectFilter,
            FileName = suggestedName,
            InitialDirectory = ExistingOrNull(settings.LastProjectDirectory),
            AddExtension = true,
            DefaultExt = ProjectFileService.Extension,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
            return null;

        settings.LastProjectDirectory = Path.GetDirectoryName(dialog.FileName);
        return dialog.FileName;
    }

    private static string? ExistingOrNull(string? directory) =>
        !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) ? directory : null;

    // The project name as a file name (without characters a file name cannot have).
    private static string SafeName(string name)
    {
        string cleaned = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return cleaned.Length == 0 ? "AmbientForge" : cleaned;
    }
}

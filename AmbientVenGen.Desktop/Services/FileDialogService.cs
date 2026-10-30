using Microsoft.Win32;

namespace AmbientVenGen.Desktop.Services;

public sealed class FileDialogService
{
    private const string ProjectFilter = "Ambient Forge project (*.afproj)|*.afproj";

    public string? ChooseExportPath(bool mp3, int seed)
    {
        var dialog = new SaveFileDialog
        {
            Title = mp3 ? "Export MP3" : "Export WAV",
            Filter = mp3
                ? "MP3 audio (*.mp3)|*.mp3"
                : "WAV audio (*.wav)|*.wav",
            FileName = $"AmbientForge_{seed}" + (mp3 ? ".mp3" : ".wav"),
            AddExtension = true,
            DefaultExt = mp3 ? ".mp3" : ".wav",
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseFieldPresetFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add field recording",
            Filter = "Audio and video (*.flac;*.mp3;*.wav;*.m4a;*.mp4)|*.flac;*.mp3;*.wav;*.m4a;*.mp4|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseProjectToOpen()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open project",
            Filter = ProjectFilter,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseProjectSavePath(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save project",
            Filter = ProjectFilter,
            FileName = suggestedName,
            AddExtension = true,
            DefaultExt = ProjectFileService.Extension,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

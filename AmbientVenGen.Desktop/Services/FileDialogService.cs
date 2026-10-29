using Microsoft.Win32;

namespace AmbientVenGen.Desktop.Services;

public sealed class FileDialogService
{
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
}

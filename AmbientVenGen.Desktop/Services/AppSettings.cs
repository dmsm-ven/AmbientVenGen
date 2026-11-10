using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace AmbientVenGen.Desktop.Services;

/// <summary>
/// User settings of the program. Loaded at start and saved to %LocalAppData%/AmbientForge/settings.json
/// on every change; a missing or broken file simply means the defaults.
/// </summary>
public sealed partial class AppSettings : ObservableObject
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    /// <summary>The project file that was opened or saved last; it is reopened at the next start.</summary>
    /// <summary>Whether the "Mathematical ratios" group of the pitch interval panel is open.</summary>
    public bool IntervalMathExpanded
    {
        get => _intervalMathExpanded;
        set
        {
            if (_intervalMathExpanded == value)
                return;

            _intervalMathExpanded = value;
            OnPropertyChanged();
            Save();
        }
    }

    private bool _intervalMathExpanded = false;

    /// <summary>Whether the spectrograms of the layers are drawn on the timeline.</summary>
    public bool ShowSpectrogram
    {
        get => _showSpectrogram;
        set
        {
            if (_showSpectrogram == value)
                return;

            _showSpectrogram = value;
            OnPropertyChanged();
            Save();
        }
    }

    private bool _showSpectrogram = false;

    /// <summary>Colours offered for the bars of the timeline spectrogram.</summary>
    public static IReadOnlyList<Color> SpectrogramPalette { get; } =
    [
        Color.FromRgb(0x5B, 0x9B, 0xFF),
        Color.FromRgb(0x4F, 0xD8, 0xE8),
        Color.FromRgb(0x5F, 0xD3, 0x8A),
        Color.FromRgb(0xE6, 0xD2, 0x5A),
        Color.FromRgb(0xFF, 0x9A, 0x4D),
        Color.FromRgb(0xF2, 0x6E, 0x9E),
        Color.FromRgb(0xB7, 0x8B, 0xFF),
        Color.FromRgb(0xE8, 0xE8, 0xF0)
    ];

    /// <summary>Colour of the spectrogram bars on the timeline.</summary>
    public Color SpectrogramColor
    {
        get => _spectrogramColor;
        set
        {
            if (_spectrogramColor == value)
                return;

            _spectrogramColor = value;
            OnPropertyChanged();
            Save();
        }
    }

    private Color _spectrogramColor = SpectrogramPalette[0];

    public string? LastProjectPath
    {
        get => _lastProjectPath;
        set
        {
            if (_lastProjectPath == value)
                return;

            _lastProjectPath = value;
            OnPropertyChanged();
            Save();
        }
    }

    private string? _lastProjectPath;

    /// <summary>Project files opened or saved lately, the newest first.</summary>
    public IReadOnlyList<string> RecentProjects => _recentProjects;

    private List<string> _recentProjects = [];

    private const int MaxRecent = 10;

    public void AddRecentProject(string path)
    {
        _recentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _recentProjects.Insert(0, path);

        if (_recentProjects.Count > MaxRecent)
            _recentProjects.RemoveRange(MaxRecent, _recentProjects.Count - MaxRecent);

        OnPropertyChanged(nameof(RecentProjects));
        Save();
    }

    public void RemoveRecentProject(string path)
    {
        if (_recentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            OnPropertyChanged(nameof(RecentProjects));
            Save();
        }
    }

    public AppSettings()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AmbientForge");

        _path = Path.Combine(directory, "settings.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;

            SettingsFile? file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(_path), Options);
            if (file is not null)
            {
                _lastProjectPath = file.LastProjectPath;
                _intervalMathExpanded = file.IntervalMathExpanded;
                _showSpectrogram = file.ShowSpectrogram;
                _recentProjects = (file.RecentProjects ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Take(MaxRecent).ToList();

                if (!string.IsNullOrWhiteSpace(file.SpectrogramColor))
                    _spectrogramColor = (Color)ColorConverter.ConvertFromString(file.SpectrogramColor);
            }
        }
        catch (Exception)
        {
            // A damaged settings file is not worth stopping the program: keep the defaults.
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new SettingsFile { LastProjectPath = LastProjectPath, IntervalMathExpanded = IntervalMathExpanded, ShowSpectrogram = ShowSpectrogram, SpectrogramColor = SpectrogramColor.ToString(), RecentProjects = _recentProjects }, Options));
        }
        catch (Exception)
        {
            // Not being able to remember the setting is not an error worth showing.
        }
    }

    private sealed class SettingsFile
    {
        public string? LastProjectPath { get; set; }
        public bool IntervalMathExpanded { get; set; } = true;
        public bool ShowSpectrogram { get; set; }
        public string? SpectrogramColor { get; set; }
        public List<string>? RecentProjects { get; set; }
    }
}

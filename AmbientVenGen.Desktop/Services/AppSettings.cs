using AmbientVenGen.Core.Export;
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

    /// <summary>Whether the main group (unison, octave...) of the pitch interval panel is open.</summary>
    public bool IntervalMainExpanded
    {
        get => _intervalMainExpanded;
        set
        {
            if (_intervalMainExpanded == value)
                return;

            _intervalMainExpanded = value;
            OnPropertyChanged();
            Save();
        }
    }

    private bool _intervalMainExpanded = true;

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

    private const int MaxRecent = 6;

    private Dictionary<string, DateTime> _recentDates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When a recent project was last opened or saved here (local time), if it is known.</summary>
    public DateTime? RecentProjectDate(string path) =>
        _recentDates.TryGetValue(path, out DateTime date) ? date : File.Exists(path) ? File.GetLastWriteTime(path) : null;

    /// <summary>The folder of the last opened or saved project (offered by the next Save / Open dialog).</summary>
    public string? LastProjectDirectory
    {
        get => _lastProjectDirectory;
        set { if (_lastProjectDirectory != value) { _lastProjectDirectory = value; OnPropertyChanged(); Save(); } }
    }

    private string? _lastProjectDirectory;

    /// <summary>The folder of the last exported file.</summary>
    public string? LastExportDirectory
    {
        get => _lastExportDirectory;
        set { if (_lastExportDirectory != value) { _lastExportDirectory = value; OnPropertyChanged(); Save(); } }
    }

    private string? _lastExportDirectory;

    /// <summary>The folder of the last saved or opened layer file (.aflay).</summary>
    public string? LastLayerDirectory
    {
        get => _lastLayerDirectory;
        set { if (_lastLayerDirectory != value) { _lastLayerDirectory = value; OnPropertyChanged(); Save(); } }
    }

    private string? _lastLayerDirectory;

    /// <summary>The export options of the last export (format, bitrate, fades, truncation).</summary>
    public ExportOptions? ExportOptions
    {
        get => _exportOptions;
        set { _exportOptions = value; OnPropertyChanged(); Save(); }
    }

    private ExportOptions? _exportOptions;

    /// <summary>Volume of the preview player of the preset library, 0..1.</summary>
    public double PresetVolume
    {
        get => _presetVolume;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(_presetVolume - value) < 1e-9)
                return;

            _presetVolume = value;
            OnPropertyChanged();
            Save();
        }
    }

    private double _presetVolume = 0.8;

    public void AddRecentProject(string path)
    {
        _recentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _recentProjects.Insert(0, path);
        _recentDates[path] = DateTime.Now;
        LastProjectDirectory = Path.GetDirectoryName(path) ?? LastProjectDirectory;

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
                _intervalMainExpanded = file.IntervalMainExpanded;
                _showSpectrogram = file.ShowSpectrogram;
                _recentProjects = (file.RecentProjects ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Take(MaxRecent).ToList();
                _recentDates = new Dictionary<string, DateTime>(file.RecentDates ?? [], StringComparer.OrdinalIgnoreCase);
                _lastProjectDirectory = file.LastProjectDirectory;
                _lastExportDirectory = file.LastExportDirectory;
                _lastLayerDirectory = file.LastLayerDirectory;
                _exportOptions = file.ExportOptions;
                _presetVolume = Math.Clamp(file.PresetVolume ?? 0.8, 0, 1);

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
            File.WriteAllText(_path, JsonSerializer.Serialize(new SettingsFile { LastProjectPath = LastProjectPath, IntervalMathExpanded = IntervalMathExpanded, IntervalMainExpanded = IntervalMainExpanded, ShowSpectrogram = ShowSpectrogram, SpectrogramColor = SpectrogramColor.ToString(), RecentProjects = _recentProjects, RecentDates = _recentDates, LastProjectDirectory = _lastProjectDirectory, LastExportDirectory = _lastExportDirectory, LastLayerDirectory = _lastLayerDirectory, ExportOptions = _exportOptions, PresetVolume = _presetVolume }, Options));
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
        public bool IntervalMainExpanded { get; set; } = true;
        public bool ShowSpectrogram { get; set; }
        public string? SpectrogramColor { get; set; }
        public List<string>? RecentProjects { get; set; }
        public Dictionary<string, DateTime>? RecentDates { get; set; }
        public string? LastProjectDirectory { get; set; }
        public string? LastExportDirectory { get; set; }
        public string? LastLayerDirectory { get; set; }
        public ExportOptions? ExportOptions { get; set; }
        public double? PresetVolume { get; set; }
    }
}

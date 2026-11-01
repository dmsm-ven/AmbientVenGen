using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmbientVenGen.Desktop.Services;

/// <summary>How parameters are edited: with sliders (default) or with gauge knobs.</summary>
public enum ControlStyle
{
    Slider,
    Knob
}

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseKnobs))]
    [NotifyPropertyChangedFor(nameof(IsSliderStyle))]
    [NotifyPropertyChangedFor(nameof(IsKnobStyle))]
    private ControlStyle _controlStyle = ControlStyle.Slider;

    public bool UseKnobs => ControlStyle == ControlStyle.Knob;

    // Two-way properties for the radio buttons of the settings window.
    public bool IsSliderStyle
    {
        get => ControlStyle == ControlStyle.Slider;
        set
        {
            if (value)
                ControlStyle = ControlStyle.Slider;
        }
    }

    public bool IsKnobStyle
    {
        get => ControlStyle == ControlStyle.Knob;
        set
        {
            if (value)
                ControlStyle = ControlStyle.Knob;
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

    partial void OnControlStyleChanged(ControlStyle value) => Save();

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;

            SettingsFile? file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(_path), Options);
            if (file is not null)
                _controlStyle = file.ControlStyle;
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
            File.WriteAllText(_path, JsonSerializer.Serialize(new SettingsFile { ControlStyle = ControlStyle }, Options));
        }
        catch (Exception)
        {
            // Not being able to remember the setting is not an error worth showing.
        }
    }

    private sealed class SettingsFile
    {
        public ControlStyle ControlStyle { get; set; }
    }
}


using AmbientVenGen.Desktop.Audio;
using AmbientVenGen.Desktop.Models;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AmbientVenGen.Desktop;

public partial class MainWindow : Window
{
    private WaveOut? _player;
    private DroneSynthProvider? _liveSynth;

    private int _seed = 12345;

    public MainWindow()
    {
        InitializeComponent();
        UpdateStatus("Ready");
    }

    private CompositionSettings ReadSettings() => new(
        (float)DroneSlider.Value,
        (float)AtmosphereSlider.Value,
        (float)BrightnessSlider.Value,
        (float)ModulationSlider.Value,
        _seed);

    private void StartPlayback()
    {
        StopPlayback();

        _liveSynth = new DroneSynthProvider(ReadSettings());

        _player = new WaveOut();
        _player.Init(new SampleToWaveProvider16(_liveSynth));
        _player.Play();

        UpdateStatus($"Playing · Seed: {_seed}");
    }

    private void StopPlayback()
    {
        _player?.Stop();
        _player?.Dispose();

        _player = null;
        _liveSynth = null;
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartPlayback();
        }
        catch (Exception ex)
        {
            StopPlayback();
            UpdateStatus($"Playback error: {ex.Message}");
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        UpdateStatus("Stopped");
    }

    private void Regenerate_Click(
        object sender, RoutedEventArgs e)
    {
        _seed = Random.Shared.Next(1, int.MaxValue);

        if (_player is not null)
        {
            try
            {
                StartPlayback();
            }
            catch (Exception ex)
            {
                StopPlayback();
                UpdateStatus($"Playback error: {ex.Message}");
                return;
            }
        }
        else
        {
            UpdateStatus($"New composition seed: {_seed}");
        }
    }

    private void Parameter_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_liveSynth is null)
            return;

        _liveSynth.DroneLevel = (float)DroneSlider.Value;
        _liveSynth.AtmosphereLevel =
            (float)AtmosphereSlider.Value;
        _liveSynth.Brightness =
            (float)BrightnessSlider.Value;
        _liveSynth.Modulation =
            (float)ModulationSlider.Value;
    }

    private void PresetBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DroneSlider is null)
            return;

        switch (PresetBox.SelectedIndex)
        {
            case 0: // Deep Space
                DroneSlider.Value = 0.65;
                AtmosphereSlider.Value = 0.25;
                BrightnessSlider.Value = 0.35;
                ModulationSlider.Value = 0.35;
                break;

            case 1: // Dark Drone
                DroneSlider.Value = 0.80;
                AtmosphereSlider.Value = 0.15;
                BrightnessSlider.Value = 0.15;
                ModulationSlider.Value = 0.15;
                break;

            case 2: // Distant Atmosphere
                DroneSlider.Value = 0.35;
                AtmosphereSlider.Value = 0.60;
                BrightnessSlider.Value = 0.65;
                ModulationSlider.Value = 0.70;
                break;
        }

        Parameter_ValueChanged(
            this,
            new RoutedPropertyChangedEventArgs<double>(
                0, 0));

        UpdateStatus("Preset selected");
    }

    private int GetDurationMinutes() =>
        int.Parse(DurationBox.SelectedValue?.ToString() ?? "10");

    private static void RenderWav(
        string path,
        CompositionSettings settings,
        int minutes)
    {
        var synth = new DroneSynthProvider(settings);

        var limited = new DurationSampleProvider(
            synth,
            TimeSpan.FromMinutes(minutes));

        WaveFileWriter.CreateWaveFile16(path, limited);
    }

    private async Task ExportAsync(bool mp3)
    {
        var dialog = new SaveFileDialog
        {
            Title = mp3 ? "Export MP3" : "Export WAV",
            Filter = mp3
                ? "MP3 audio (*.mp3)|*.mp3"
                : "WAV audio (*.wav)|*.wav",
            FileName = $"AmbientForge_{_seed}" +
                       (mp3 ? ".mp3" : ".wav"),
            AddExtension = true,
            DefaultExt = mp3 ? ".mp3" : ".wav"
        };

        if (dialog.ShowDialog() != true)
            return;

        var settings = ReadSettings();
        int minutes = GetDurationMinutes();

        string? tempWav = null;

        try
        {
            UpdateStatus("Rendering composition...");
            SetExportButtonsEnabled(false);

            await Task.Run(() =>
            {
                if (!mp3)
                {
                    RenderWav(dialog.FileName, settings, minutes);
                    return;
                }

                tempWav = Path.Combine(
                    Path.GetTempPath(),
                    $"{Guid.NewGuid():N}.wav");

                RenderWav(tempWav, settings, minutes);

                using var reader = new WaveFileReader(tempWav);

                MediaFoundationEncoder.EncodeToMp3(
                    reader,
                    dialog.FileName,
                    192_000);
            });

            UpdateStatus($"Export completed: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Export error: {ex.Message}");

            MessageBox.Show(
                ex.Message,
                "Export error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetExportButtonsEnabled(true);

            if (tempWav is not null)
            {
                try
                {
                    File.Delete(tempWav);
                }
                catch (IOException)
                {
                    // Temporary file cleanup can be retried later.
                }
                catch (UnauthorizedAccessException)
                {
                    // Do not mask the export result.
                }
            }
        }
    }

    private async void ExportWav_Click(
        object sender, RoutedEventArgs e)
    {
        await ExportAsync(mp3: false);
    }

    private async void ExportMp3_Click(
        object sender, RoutedEventArgs e)
    {
        await ExportAsync(mp3: true);
    }

    private void SetExportButtonsEnabled(bool enabled)
    {
        // Keep playback controls available during export.
        // In the full MVVM version, bind IsEnabled to a property.
        ExportWavButton.IsEnabled = enabled;
        ExportMp3Button.IsEnabled = enabled;
    }

    private void UpdateStatus(string text)
    {
        StatusText.Text = text;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPlayback();
        base.OnClosed(e);
    }
}

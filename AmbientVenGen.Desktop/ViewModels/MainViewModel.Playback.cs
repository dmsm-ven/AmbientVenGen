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

/// <summary>Playback, seek, regenerate and the live meters.</summary>
public partial class MainViewModel
{
    // ---------------------------------------------------------------- playback

    /// <summary>Single Play/Stop button: the icon in the view follows <see cref="IsPlaying"/>.</summary>
    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying)
            StopPlayback();
        else
            StartPlayback();
    }

    private void StartPlayback()
    {
        try
        {
            double start = PlayheadSeconds >= Project.DurationSeconds - 0.05 ? 0 : PlayheadSeconds;
            PlayheadSeconds = start;

            _playbackService.Start(Project.ToSpec(), start);
            IsPlaying = true;
            StatusMessage = $"Playing - seed {Project.Seed}";
        }
        catch (Exception ex)
        {
            IsPlaying = false;
            StatusMessage = $"Playback error: {ex.Message}";
        }
    }

    private void StopPlayback()
    {
        StopPlaybackSilently();
        StatusMessage = "Stopped";
    }

    // Stops playback and keeps the playhead where it was (works like a pause).
    private void StopPlaybackSilently()
    {
        if (IsPlaying)
            PlayheadSeconds = Math.Min(_playbackService.PositionSeconds, Project.DurationSeconds);

        _playbackService.Stop();
        IsPlaying = false;
    }

    private void OnPlaybackEnded()
    {
        IsPlaying = false;
        PlayheadSeconds = Project.DurationSeconds;
        StatusMessage = "Finished";
    }

    [RelayCommand]
    private void Seek(double seconds)
    {
        PlayheadSeconds = Math.Clamp(seconds, 0, Project.DurationSeconds);

        if (IsPlaying)
            _playbackService.Seek(PlayheadSeconds);
    }

    [RelayCommand]
    private void Regenerate()
    {
        Project.Seed = Random.Shared.Next(1, int.MaxValue);

        foreach (Layer layer in Project.Layers)
            layer.Seed = Random.Shared.Next(1, int.MaxValue);

        if (IsPlaying)
        {
            PlayheadSeconds = _playbackService.PositionSeconds;
            StartPlayback();
            return;
        }

        StatusMessage = $"New composition seed: {Project.Seed}";
    }

    // ---------------------------------------------------------------- live meters (30 fps)

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (IsPlaying)
            PlayheadSeconds = Math.Min(_playbackService.PositionSeconds, Project.DurationSeconds);

        _playbackService.Analyzer.TakePeaks(out float left, out float right, out float over);

        LevelLeft = Fall(left, LevelLeft);
        LevelRight = Fall(right, LevelRight);

        if (over > 1.0f)
            _clipHoldTicks = 45;
        else if (_clipHoldTicks > 0)
            _clipHoldTicks--;

        IsClipping = _clipHoldTicks > 0;
        SpectrumBands = _spectrum.Update(_playbackService.Analyzer, IsPlaying);
    }

    // Peak meter ballistics: jumps up instantly, falls back smoothly.
    private static double Fall(double newPeak, double current)
    {
        double value = Math.Max(newPeak, current * 0.88);
        return value < 1e-5 ? 0 : value;
    }

    private static string FormatTime(double seconds)
    {
        int total = (int)Math.Round(Math.Max(0, seconds));

        return total >= 3600
            ? $"{total / 3600}:{total % 3600 / 60:00}:{total % 60:00}"
            : $"{total / 60}:{total % 60:00}";
    }
}

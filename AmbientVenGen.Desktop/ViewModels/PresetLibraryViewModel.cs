using AmbientVenGen.Desktop.Audio;
using AmbientVenGen.Desktop.DataAccess.Models;
using AmbientVenGen.Desktop.DataAccess.Repositories;
using AmbientVenGen.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>The library of field-recording presets (audio/video files saved in the local database).</summary>
public partial class PresetLibraryViewModel : ObservableObject
{
    private readonly FieldPresetRepository _repository;
    private readonly AudioFileAnalyzer _analyzer;
    private readonly FileDialogService _fileDialogService;
    private readonly MessageService _messageService;
    private readonly PresetPreviewService _preview;
    private readonly DispatcherTimer _progressTimer;
    private FieldPresetItem? _playingItem;

    // At most two waveforms are decoded at the same time, so scrolling does not choke the machine.
    private readonly SemaphoreSlim _thumbnailSlots = new(2);

    public ObservableCollection<FieldPresetItem> Presets { get; } = [];

    /// <summary>Every tag that exists in the database (offered by the tag boxes).</summary>
    public ObservableCollection<string> AllTags { get; } = [];

    /// <summary>One switch for all rows: repeat the previewed file or play it once.</summary>
    [ObservableProperty]
    private bool _loop;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeletePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(UsePresetCommand))]
    private FieldPresetItem? _selectedPreset;

    public PresetLibraryViewModel(
        FieldPresetRepository repository,
        AudioFileAnalyzer analyzer,
        FileDialogService fileDialogService,
        MessageService messageService,
        PresetPreviewService preview)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));

        _preview.Loop = Loop;
        _preview.Ended += OnPreviewEnded;

        _progressTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _progressTimer.Tick += (_, _) =>
        {
            if (_playingItem is { } item)
                item.PlayProgress = _preview.Progress;
        };
    }

    partial void OnLoopChanged(bool value) => _preview.Loop = value;

    /// <summary>Play (or stop, when it is already playing) the preview of a row.</summary>
    [RelayCommand]
    private void TogglePreview(FieldPresetItem? item)
    {
        if (item is null)
            return;

        bool wasPlaying = ReferenceEquals(item, _playingItem);
        StopPreview();

        if (wasPlaying)
            return;

        if (item.IsUnavailable)
            return;

        try
        {
            _preview.Play(item.FilePath);
        }
        catch (Exception ex)
        {
            item.IsUnavailable = true;
            _messageService.ShowError($"Cannot play this file:\n{ex.Message}");
            return;
        }

        _playingItem = item;
        item.PlayProgress = 0;
        item.IsPlaying = true;
        _progressTimer.Start();
    }

    private void StopPreview()
    {
        _progressTimer.Stop();
        _preview.Stop();

        if (_playingItem is { } item)
        {
            item.IsPlaying = false;
            item.PlayProgress = 0;
        }

        _playingItem = null;
    }

    private void OnPreviewEnded() => StopPreview();

    /// <summary>Called when the window closes: stops the sound and the timer.</summary>
    public void Shutdown()
    {
        StopPreview();
        _preview.Ended -= OnPreviewEnded;
    }

    /// <summary>Puts a tag on a preset. A tag that does not exist yet is created (and remembered in the database).</summary>
    public async Task AddTagAsync(FieldPresetItem item, string? text)
    {
        string tag = (text ?? string.Empty).Trim();
        if (tag.Length == 0)
            return;

        if (tag.Length > 24)
            tag = tag[..24];

        // Reuse the spelling of an existing tag ("Rain" and "rain" are the same tag).
        string? existing = AllTags.FirstOrDefault(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            tag = existing;

        if (item.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            await _repository.AddTagAsync(item.Id, tag);
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot save the tag:\n{ex.Message}");
            return;
        }

        item.Tags.Add(tag);

        if (existing is null)
            InsertSorted(AllTags, tag);
    }

    public async Task RemoveTagAsync(FieldPresetItem item, string tag)
    {
        try
        {
            await _repository.RemoveTagAsync(item.Id, tag);
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot remove the tag:\n{ex.Message}");
            return;
        }

        item.Tags.Remove(tag);

        // A tag nobody uses any more disappears from the list.
        if (!Presets.Any(p => p.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))))
        {
            string? stale = AllTags.FirstOrDefault(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
            if (stale is not null)
                AllTags.Remove(stale);
        }
    }

    private static void InsertSorted(ObservableCollection<string> list, string tag)
    {
        int index = 0;
        while (index < list.Count && string.Compare(list[index], tag, StringComparison.CurrentCultureIgnoreCase) < 0)
            index++;

        list.Insert(index, tag);
    }

    /// <summary>Called when the window is shown: rows appear at once, thumbnails follow one by one.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IReadOnlyList<FieldPreset> stored = await _repository.GetAllAsync();
            IReadOnlyList<PresetTagRow> tagRows = await _repository.GetAllTagsAsync();

            Presets.Clear();
            foreach (FieldPreset preset in stored)
            {
                FieldPresetItem item = CreateItem(preset);

                foreach (PresetTagRow row in tagRows.Where(r => r.PresetId == preset.Id))
                    item.Tags.Add(row.Tag);

                Presets.Add(item);
            }

            AllTags.Clear();
            foreach (string tag in tagRows.Select(r => r.Tag).Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase))
            {
                AllTags.Add(tag);
            }
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot read the preset library:\n{ex.Message}");
            return;
        }

        // Thumbnails are built lazily, when a row is shown (see FieldPresetItem.RequestThumbnail).
    }

    [RelayCommand]
    private async Task AddPresetAsync()
    {
        string? path = _fileDialogService.ChooseFieldPresetFile();
        if (path is null)
            return;

        AudioFileInfo? info = await Task.Run(() => _analyzer.TryReadInfo(path));
        if (info is null)
        {
            _messageService.ShowError("This file cannot be read as audio.");
            return;
        }

        var preset = new FieldPreset
        {
            Name = Path.GetFileNameWithoutExtension(path),
            FilePath = path,
            AddedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            DurationSeconds = info.Duration.TotalSeconds,
            SizeBytes = info.SizeBytes,
            Extension = info.Extension
        };

        try
        {
            preset.Id = await _repository.AddAsync(preset);
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot save the preset:\n{ex.Message}");
            return;
        }

        FieldPresetItem item = CreateItem(preset);
        Presets.Add(item);
        SelectedPreset = item;
    }

    private FieldPresetItem CreateItem(FieldPreset preset) =>
        new(preset) { ThumbnailLoader = LoadThumbnailAsync };

    private bool CanUsePreset() => SelectedPreset is { IsUnavailable: false };

    /// <summary>Adds the selected recording to the project as a "Field record" layer.</summary>
    [RelayCommand(CanExecute = nameof(CanUsePreset))]
    private void UsePreset()
    {
        if (SelectedPreset is { } item)
            WeakReferenceMessenger.Default.Send(new UseFieldPresetMessage(item.FilePath, item.Name));
    }

    private bool CanDeletePreset() => SelectedPreset is not null;

    /// <summary>Removes the preset from the library. The file itself is not touched.</summary>
    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private async Task DeletePresetAsync()
    {
        FieldPresetItem? item = SelectedPreset;
        if (item is null)
            return;

        try
        {
            await _repository.DeleteAsync(item.Id);
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot delete the preset:\n{ex.Message}");
            return;
        }

        if (ReferenceEquals(item, _playingItem))
            StopPreview();

        SelectedPreset = null;
        Presets.Remove(item);
    }

    // Decoding happens on a background thread; the bitmap is drawn on the UI thread.
    private async Task LoadThumbnailAsync(FieldPresetItem item)
    {
        try
        {
            await _thumbnailSlots.WaitAsync();

            float[]? peaks;
            try
            {
                peaks = await Task.Run(() => _analyzer.TryReadPeaks(item.FilePath, WaveformRenderer.ColumnCount));
            }
            finally
            {
                _thumbnailSlots.Release();
            }

            if (peaks is null)
            {
                item.IsUnavailable = true;
                return;
            }

            item.Thumbnail = WaveformRenderer.Render(peaks);
        }
        finally
        {
            item.IsThumbnailLoading = false;
        }
    }
}

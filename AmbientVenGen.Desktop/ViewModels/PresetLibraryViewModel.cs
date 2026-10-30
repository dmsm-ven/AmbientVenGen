using AmbientVenGen.Desktop.DataAccess;
using AmbientVenGen.Desktop.Models;
using AmbientVenGen.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>The library of field-recording presets (audio/video files saved in the local database).</summary>
public partial class PresetLibraryViewModel : ObservableObject
{
    private readonly FieldPresetRepository _repository;
    private readonly AudioFileAnalyzer _analyzer;
    private readonly FileDialogService _fileDialogService;
    private readonly MessageService _messageService;

    public ObservableCollection<FieldPresetItem> Presets { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeletePresetCommand))]
    private FieldPresetItem? _selectedPreset;

    public PresetLibraryViewModel(
        FieldPresetRepository repository,
        AudioFileAnalyzer analyzer,
        FileDialogService fileDialogService,
        MessageService messageService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
    }

    /// <summary>Called when the window is shown: rows appear at once, thumbnails follow one by one.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IReadOnlyList<FieldPreset> stored = await _repository.GetAllAsync();

            Presets.Clear();
            foreach (FieldPreset preset in stored)
                Presets.Add(new FieldPresetItem(preset));
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot read the preset library:\n{ex.Message}");
            return;
        }

        foreach (FieldPresetItem item in Presets.ToList())
            await LoadThumbnailAsync(item);
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

        var item = new FieldPresetItem(preset);
        Presets.Add(item);
        SelectedPreset = item;

        await LoadThumbnailAsync(item);
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

        SelectedPreset = null;
        Presets.Remove(item);
    }

    // Decoding happens on a background thread; the bitmap is drawn on the UI thread.
    private async Task LoadThumbnailAsync(FieldPresetItem item)
    {
        float[]? peaks = await Task.Run(() => _analyzer.TryReadPeaks(item.FilePath, WaveformRenderer.ColumnCount));

        if (peaks is null)
        {
            item.IsUnavailable = true;
            return;
        }

        item.Thumbnail = WaveformRenderer.Render(peaks);
    }
}

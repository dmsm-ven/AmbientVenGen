using System.Diagnostics;
using AmbientVenGen.Desktop.Services.Audio;
using AmbientVenGen.Desktop.DataAccess.Models;
using AmbientVenGen.Desktop.DataAccess.Repositories;
using AmbientVenGen.Desktop.Services;
using AmbientVenGen.Desktop.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>The library of field-recording presets (audio/video files saved in the local database).</summary>
public partial class PresetLibraryViewModel : ObservableObject
{
    private readonly FieldPresetRepository _repository;
    private readonly AudioFileAnalyzer _analyzer;
    private readonly FileDialogService _fileDialogService;
    private readonly MessageService _messageService;
    private readonly AppSettings _settings;
    private readonly PresetPreviewService _preview;
    private readonly DispatcherTimer _progressTimer;
    private FieldPresetItem? _playingItem;

    // At most two waveforms are decoded at the same time, so scrolling does not choke the machine.
    private readonly SemaphoreSlim _thumbnailSlots = new(2);

    public ObservableCollection<FieldPresetItem> Presets { get; } = [];

    /// <summary>Every tag that exists in the database (offered by the tag boxes).</summary>
    public ObservableCollection<string> AllTags { get; } = [];

    /// <summary>Every tag with the number of presets that have it, the most used first. Switched-on tags filter the table.</summary>
    public ObservableCollection<TagFilterItem> TagFilters { get; } = [];

    [ObservableProperty]
    private bool _hasTagFilter;

    /// <summary>Sort the table by how often the presets were used (the most used first) instead of the order of adding.</summary>
    [ObservableProperty]
    private bool _sortByUsage;

    /// <summary>Choices of the filter by the date of adding.</summary>
    public IReadOnlyList<Choice<int>> DateFilters { get; } =
    [
        new("Any date", 0),
        new("Today", 1),
        new("Last 7 days", 7),
        new("Last 30 days", 30),
        new("Last year", 365)
    ];

    /// <summary>Only presets added within this many days are shown (0 = all).</summary>
    [ObservableProperty]
    private int _dateFilterDays;

    partial void OnDateFilterDaysChanged(int value) => CollectionViewSource.GetDefaultView(Presets).Refresh();

    /// <summary>The recording shown in the waveform panel: the one being played, else the selected one.</summary>
    [ObservableProperty]
    private FieldPresetItem? _activeItem;

    partial void OnSelectedPresetChanged(FieldPresetItem? value) => UpdateActiveItem();

    private void UpdateActiveItem() => ActiveItem = _playingItem ?? SelectedPreset;

    /// <summary>Raised after a new record was added: the table must show its top (the newest record is the first row).</summary>
    public event Action? ScrollToTopRequested;

    /// <summary>One switch for all rows: repeat the previewed file or play it once.</summary>
    [ObservableProperty]
    private bool _loop;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeletePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(UsePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceInLayerCommand))]
    private FieldPresetItem? _selectedPreset;

    /// <summary>True while a Field record layer is selected in the project: its file can be replaced.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceInLayerCommand))]
    private bool _hasReplaceTarget;

    public PresetLibraryViewModel(
        FieldPresetRepository repository,
        AudioFileAnalyzer analyzer,
        FileDialogService fileDialogService,
        MessageService messageService,
        PresetPreviewService preview,
        AppSettings settings)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _preview.Volume = _settings.PresetVolume;
        _preview.Loop = Loop;
        _preview.Ended += OnPreviewEnded;

        // Which layer would get the file: asked now, and told again whenever the selected layer changes.
        WeakReferenceMessenger.Default.Register<PresetLibraryViewModel, FieldRecordTargetMessage>(
            this,
            static (viewModel, message) => viewModel.HasReplaceTarget = message.LayerName is not null);
        FieldRecordTargetRequest target = WeakReferenceMessenger.Default.Send(new FieldRecordTargetRequest());
        HasReplaceTarget = target.HasReceivedResponse && target.Response is not null;

        // The table shows only the presets that have every switched-on tag.
        CollectionViewSource.GetDefaultView(Presets).Filter = MatchesTagFilter;

        _progressTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _progressTimer.Tick += (_, _) =>
        {
            if (_playingItem is { } item)
                item.PlayProgress = _preview.Progress;
        };
    }

    partial void OnLoopChanged(bool value) => _preview.Loop = value;

    /// <summary>Master volume of the preview, 0..1 (remembered between runs).</summary>
    public double Volume
    {
        get => _settings.PresetVolume;
        set
        {
            _settings.PresetVolume = value;
            _preview.Volume = _settings.PresetVolume;
            OnPropertyChanged();
        }
    }

    /// <summary>Moves the position of the playing preview (click on the waveform), 0..1.</summary>
    public void SeekPreview(double fraction)
    {
        if (_playingItem is not { } item)
            return;

        _preview.Seek(fraction);
        item.PlayProgress = _preview.Progress;
    }

    /// <summary>Shows the file of the selected row in Windows Explorer.</summary>
    public void RevealInExplorer()
    {
        if (_selection.Count > 1 || (_selection.Count == 0 ? SelectedPreset : _selection[0]) is not { } item)
            return;

        if (!File.Exists(item.FilePath))
        {
            _messageService.ShowError($"The file does not exist any more:\n{item.FilePath}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot open Explorer:\n{ex.Message}");
        }
    }

    /// <summary>Asks for a tag and puts it on every selected row.</summary>
    public async Task AddTagToSelectionAsync()
    {
        List<FieldPresetItem> items = _selection.Count > 0 ? [.. _selection] : SelectedPreset is { } one ? [one] : [];
        if (items.Count == 0)
            return;

        string message = items.Count == 1 ? "Add a tag to this recording:" : $"Add a tag to {items.Count} recordings:";
        string tag = CommonTagWindow.Ask(message, AllTags);

        if (tag.Length == 0)
            return;

        foreach (FieldPresetItem item in items)
            await AddTagAsync(item, tag);
    }

    // The tag box of a row is shown only while that row is the selected one.
    partial void OnSelectedPresetChanged(FieldPresetItem? oldValue, FieldPresetItem? newValue)
    {
        if (oldValue is not null)
            oldValue.IsTagEditing = false;
    }

    /// <summary>Words to look for in the file name or in the tags (every word must be found).</summary>
    [ObservableProperty]
    private string _searchText = "";

    // The table is filtered when the typing stops (half a second), not after every letter.
    private DispatcherTimer? _searchTimer;

    partial void OnSearchTextChanged(string value)
    {
        _searchTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _searchTimer.Stop();
        _searchTimer.Tick -= OnSearchTimer;
        _searchTimer.Tick += OnSearchTimer;
        _searchTimer.Start();
    }

    private void OnSearchTimer(object? sender, EventArgs e)
    {
        _searchTimer?.Stop();
        CollectionViewSource.GetDefaultView(Presets).Refresh();
    }

    private bool MatchesTagFilter(object row)
    {
        if (row is not FieldPresetItem item)
            return false;

        foreach (string word in SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool found = item.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                || item.Tags.Any(t => t.Contains(word, StringComparison.OrdinalIgnoreCase));

            if (!found)
                return false;
        }

        if (DateFilterDays > 0)
        {
            // "Today" = since local midnight; N days = the last N*24 hours
            DateTime from = DateFilterDays == 1 ? DateTime.Today : DateTime.Now.AddDays(-DateFilterDays);

            if (item.AddedAt < from)
                return false;
        }

        foreach (TagFilterItem filter in TagFilters)
        {
            if (filter.IsSelected && !item.Tags.Any(t => string.Equals(t, filter.Name, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    // Rebuilds the filter bar (counts, order) after any change of tags or rows; keeps the switched-on tags.
    private void RebuildTagFilters()
    {
        var selected = TagFilters.Where(f => f.IsSelected).Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var counts = Presets
            .SelectMany(p => p.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First(), Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        TagFilters.Clear();
        foreach ((string name, int count) in counts)
            TagFilters.Add(new TagFilterItem(name, count, selected.Contains(name), OnTagFilterChanged));

        OnTagFilterChanged();
    }

    partial void OnSortByUsageChanged(bool value)
    {
        ICollectionView view = CollectionViewSource.GetDefaultView(Presets);

        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();

            if (value)
            {
                view.SortDescriptions.Add(new SortDescription(nameof(FieldPresetItem.ViewsCount), ListSortDirection.Descending));
                view.SortDescriptions.Add(new SortDescription(nameof(FieldPresetItem.Id), ListSortDirection.Descending));
            }
        }

        ScrollToTopRequested?.Invoke();
    }

    private void OnTagFilterChanged()
    {
        HasTagFilter = TagFilters.Any(f => f.IsSelected);
        CollectionViewSource.GetDefaultView(Presets).Refresh();
    }

    [RelayCommand]
    private void ClearTagFilter()
    {
        foreach (TagFilterItem filter in TagFilters.ToList())
            filter.IsSelected = false;
    }

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
        item.RequestThumbnail();
        item.PlayProgress = 0;
        item.IsPlaying = true;
        _progressTimer.Start();
        UpdateActiveItem();
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
        UpdateActiveItem();
    }

    private void OnPreviewEnded() => StopPreview();

    /// <summary>Called when the window closes: stops the sound and the timer.</summary>
    public void Shutdown()
    {
        StopPreview();
        _preview.Ended -= OnPreviewEnded;
        WeakReferenceMessenger.Default.UnregisterAll(this);
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

        RebuildTagFilters();
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
        RebuildTagFilters();

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

    /// <summary>
    /// Called when the window has been shown (Loaded): the window opens at once and the data follows. The database is read
    /// and the rows are built on a background thread (SQLite works synchronously under the async calls), then the rows are
    /// added in small portions, so the window stays responsive. Durations and sizes come from the database, no file is opened.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        List<FieldPresetItem> items;
        List<string> allTags;

        try
        {
            (items, allTags) = await Task.Run(async () =>
            {
                IReadOnlyList<FieldPreset> stored = await _repository.GetAllAsync();
                IReadOnlyList<PresetTagRow> tagRows = await _repository.GetAllTagsAsync();

                ILookup<long, string> tagsOf = tagRows.ToLookup(r => r.PresetId, r => r.Tag);
                var rows = new List<FieldPresetItem>(stored.Count);

                foreach (FieldPreset preset in stored)
                {
                    FieldPresetItem item = CreateItem(preset);

                    foreach (string tag in tagsOf[preset.Id])
                        item.Tags.Add(tag);

                    rows.Add(item);
                }

                List<string> tags = tagRows.Select(r => r.Tag).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();

                return (rows, tags);
            });
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot read the preset library:\n{ex.Message}");
            return;
        }

        Presets.Clear();
        AllTags.Clear();
        foreach (string tag in allTags)
            AllTags.Add(tag);

        const int portion = 40;
        for (int i = 0; i < items.Count; i += portion)
        {
            foreach (FieldPresetItem item in items.Skip(i).Take(portion))
                Presets.Add(item);

            // let the window draw what has been added
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        }

        RebuildTagFilters();

        // Waveforms are built only for files that were played (see TogglePreview).
    }

    /// <summary>Adds one or several files: each is read, saved and gets the existing tags that its name mentions.</summary>
    [RelayCommand]
    private async Task AddPresetAsync()
    {
        IReadOnlyList<string> paths = _fileDialogService.ChooseFieldPresetFiles();
        if (paths.Count == 0)
            return;

        var failed = new List<string>();
        var duplicates = new List<string>();
        FieldPresetItem? last = null;
        var newItems = new List<FieldPresetItem>();
        int added = 0;

        foreach (string path in paths)
        {
            // A file that is already in the library (or is chosen twice) is skipped.
            if (IsInLibrary(path))
            {
                duplicates.Add(Path.GetFileName(path));
                continue;
            }

            (FieldPresetItem? item, string? error) = await AddFileAsync(path);

            if (item is null)
            {
                failed.Add($"{Path.GetFileName(path)}: {error}");
                continue;
            }

            last = item;
            newItems.Add(item);
            added++;
        }

        if (last is not null)
        {
            SelectedPreset = last;

            // The new records are on top, and they must not be hidden by a tag filter.
            if (HasTagFilter)
                ClearTagFilter();

            ScrollToTopRequested?.Invoke();

            // One new record: the tag box is open at once. A batch gets its tags from the names only.
            if (added == 1)
                last.IsTagEditing = true;
        }

        // Several files: one tag for all of them may be chosen in a small window.
        if (added > 1)
        {
            string common = CommonTagWindow.Ask(added, AllTags);

            foreach (FieldPresetItem item in newItems)
                await AddTagAsync(item, common);
        }

        if (added == 0 && failed.Count == 0 && duplicates.Count > 0)
            _messageService.ShowError($"Already in the library:\n{string.Join("\n", duplicates)}");

        if (failed.Count > 0)
            _messageService.ShowError($"{failed.Count} of {paths.Count} files were not added:\n{string.Join("\n", failed)}");
    }

    private bool IsInLibrary(string path)
    {
        string full = Path.GetFullPath(path);
        return Presets.Any(p => string.Equals(Path.GetFullPath(p.FilePath), full, StringComparison.OrdinalIgnoreCase));
    }

    // Reads, saves and tags one file; the record goes to the top of the table.
    private async Task<(FieldPresetItem? Item, string? Error)> AddFileAsync(string path)
    {
        AudioFileInfo? info = await Task.Run(() => _analyzer.TryReadInfo(path));
        if (info is null)
            return (null, "cannot be read as audio");

        var preset = new FieldPreset
        {
            FilePath = path,
            AddedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            DurationSeconds = info.Duration.TotalSeconds,
            SizeBytes = info.SizeBytes
        };

        try
        {
            preset.Id = await _repository.AddAsync(preset);
        }
        catch (Exception ex)
        {
            return (null, $"cannot be saved ({ex.Message})");
        }

        FieldPresetItem item = CreateItem(preset);
        Presets.Insert(0, item);

        // Tags that already exist and are named in the file name are put on at once ("123_wind_howl.wav": Wind, Howl).
        await AddTagsFromNameAsync(item);

        return (item, null);
    }

    // Puts on every existing tag whose words are found in the name of the file (whole words, any case).
    // Only the tags that are already in the database are used: the file name never creates a new one.
    private async Task AddTagsFromNameAsync(FieldPresetItem item)
    {
        string name = $" {Words(item.Name)} ";

        foreach (string tag in AllTags.ToList())
        {
            string words = Words(tag);
            if (words.Length < 2 || !name.Contains($" {words} ", StringComparison.Ordinal))
                continue;

            await AddTagAsync(item, tag);
        }
    }

    // Letters and digits only, one space between the words, lower case. Words written together are split
    // at capitals and between letters and digits: "123_Wind-howl" -> "123 wind howl", "FrostArrow1Hist" -> "frost arrow 1 hist".
    private static string Words(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length + 8);
        bool space = true;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (!char.IsLetterOrDigit(c))
            {
                if (!space)
                {
                    builder.Append(' ');
                    space = true;
                }

                continue;
            }

            if (!space && i > 0)
            {
                char prev = text[i - 1];
                bool next = i + 1 < text.Length && char.IsLower(text[i + 1]);

                bool boundary =
                    (char.IsUpper(c) && (char.IsLower(prev) || char.IsDigit(prev)))
                    || (char.IsUpper(c) && char.IsUpper(prev) && next)            // "HTTPServer" -> "http server"
                    || (char.IsDigit(c) != char.IsDigit(prev) && char.IsLetterOrDigit(prev));

                if (boundary)
                    builder.Append(' ');
            }

            builder.Append(char.ToLowerInvariant(c));
            space = false;
        }

        return builder.ToString().TrimEnd();
    }

    private FieldPresetItem CreateItem(FieldPreset preset) =>
        new(preset) { ThumbnailLoader = LoadThumbnailAsync };

    private bool CanUsePreset() => SelectedPreset is { IsUnavailable: false };

    /// <summary>Adds the selected recording to the project as a "Field record" layer.</summary>
    [RelayCommand(CanExecute = nameof(CanUsePreset))]
    private void UsePreset()
    {
        if (SelectedPreset is not { } item)
            return;

        WeakReferenceMessenger.Default.Send(new UseFieldPresetMessage(item.FilePath, item.Name, DurationSeconds: item.Duration.TotalSeconds));
        _ = CountUseAsync(item);
    }

    // One more use: the counter grows in the table at once and in the database in the background.
    // The sorting by usage is not redone here, so the rows do not jump under the mouse; it follows on the next sort.
    private async Task CountUseAsync(FieldPresetItem item)
    {
        item.ViewsCount++;

        try
        {
            await _repository.IncrementViewsAsync(item.Id);
        }
        catch
        {
            // the counter is a convenience: a failure must not disturb the work
        }
    }

    private bool CanReplace() => HasReplaceTarget && SelectedPreset is { IsUnavailable: false };

    /// <summary>Puts the selected recording into the selected Field record layer instead of its current file.</summary>
    [RelayCommand(CanExecute = nameof(CanReplace))]
    private void ReplaceInLayer()
    {
        if (SelectedPreset is not { } item)
            return;

        WeakReferenceMessenger.Default.Send(new UseFieldPresetMessage(item.FilePath, item.Name, ReplaceCurrent: true));
        _ = CountUseAsync(item);
    }

    private List<FieldPresetItem> _selection = [];

    /// <summary>The rows selected in the table (the table allows several).</summary>
    public void SetSelection(List<FieldPresetItem> items) => _selection = items;

    private bool CanDeletePreset() => SelectedPreset is not null;

    /// <summary>Removes the preset from the library. The file itself is not touched.</summary>
    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private async Task DeletePresetAsync()
    {
        List<FieldPresetItem> items = _selection.Count > 0 ? [.. _selection] : SelectedPreset is { } one ? [one] : [];
        if (items.Count == 0)
            return;

        if (items.Count > 1
            && !_messageService.Confirm($"Remove {items.Count} recordings from the library?\nThe files themselves are not deleted.", "Remove", "Cancel"))
            return;

        var failed = new List<string>();

        foreach (FieldPresetItem item in items)
        {
            try
            {
                await _repository.DeleteAsync(item.Id);
            }
            catch (Exception ex)
            {
                failed.Add($"{item.Name}: {ex.Message}");
                continue;
            }

            if (ReferenceEquals(item, _playingItem))
                StopPreview();

            Presets.Remove(item);
        }

        SelectedPreset = null;
        _selection = [];
        RebuildTagFilters();

        if (failed.Count > 0)
            _messageService.ShowError("Cannot delete:\n" + string.Join("\n", failed));
    }

    // Decoding happens on a background thread; the bitmap is drawn on the UI thread.
    // Enough columns for a wide panel; the control makes the bars from them.
    private const int PeakColumns = 800;

    private async Task LoadThumbnailAsync(FieldPresetItem item)
    {
        try
        {
            await _thumbnailSlots.WaitAsync();

            float[]? peaks;
            try
            {
                peaks = await Task.Run(() => _analyzer.TryReadPeaks(item.FilePath, PeakColumns));
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

            item.Peaks = peaks;
        }
        finally
        {
            item.IsThumbnailLoading = false;
        }
    }
}

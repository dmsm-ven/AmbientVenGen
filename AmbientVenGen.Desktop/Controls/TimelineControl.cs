using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;
using AmbientVenGen.Desktop.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// The layer timeline: a time ruler, one lane per layer, translucent clips that show their
/// loudness envelope, and the playhead. Everything is drawn by hand for full control of the look.
/// <para>Mouse: drag a clip to move it, drag its edges to resize, drag the round handles to change
/// the fades, click the ruler to move the playhead, Ctrl+wheel zooms, Shift+wheel scrolls,
/// Delete removes the selected layer.</para>
/// </summary>
public sealed partial class TimelineControl : FrameworkElement
{
    private const double HeaderWidth = 230;
    private const double RulerHeight = 22;
    private const double LaneHeight = 68;
    private const double LanePadding = 7;
    private const double RightPadding = 16;
    private const double EdgeGrab = 7;
    private const double HandleRadius = 5;
    private const double HandleInset = 10;
    private const double MinClipSeconds = 2;
    private const double MinViewSeconds = 20;
    private const int MinVisibleLanes = 5;

    private static readonly double[] TickSteps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];
    private static readonly int[] TickDivisions = [1, 2, 5, 5, 3, 6, 6, 4, 5, 5, 3, 6, 6];

    private static readonly Typeface UiFont = new(
        new FontFamily("Segoe UI Variable, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static readonly Typeface UiFontBold = new(
        new FontFamily("Segoe UI Variable, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();

    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project), typeof(ProjectModel), typeof(TimelineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnProjectChanged));

    public static readonly DependencyProperty SelectedLayerProperty = DependencyProperty.Register(
        nameof(SelectedLayer), typeof(Layer), typeof(TimelineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectionChanged));

    public static readonly DependencyProperty PlayheadSecondsProperty = DependencyProperty.Register(
        nameof(PlayheadSeconds), typeof(double), typeof(TimelineControl),
        new FrameworkPropertyMetadata(0.0, OnPlayheadChanged));

    /// <summary>Executed with the new playhead time (double) when the user clicks or drags on the ruler.</summary>
    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(TimelineControl), new PropertyMetadata(null));

    /// <summary>Executed when Delete is pressed on the timeline.</summary>
    public static readonly DependencyProperty DeleteCommandProperty = DependencyProperty.Register(
        nameof(DeleteCommand), typeof(ICommand), typeof(TimelineControl), new PropertyMetadata(null));

    /// <summary>Source of the spectrogram pictures of the layers.</summary>
    public static readonly DependencyProperty SpectrogramsProperty = DependencyProperty.Register(
        nameof(Spectrograms), typeof(SpectrogramService), typeof(TimelineControl),
        new PropertyMetadata(null, OnSpectrogramsChanged));

    /// <summary>Draws the spectrogram of every layer inside its clip.</summary>
    public static readonly DependencyProperty ShowSpectrogramProperty = DependencyProperty.Register(
        nameof(ShowSpectrogram), typeof(bool), typeof(TimelineControl),
        new PropertyMetadata(false, (d, _) => ((TimelineControl)d).InvalidateContent()));

    /// <summary>Colour of the spectrogram bars.</summary>
    public static readonly DependencyProperty SpectrogramColorProperty = DependencyProperty.Register(
        nameof(SpectrogramColor), typeof(Color), typeof(TimelineControl),
        new PropertyMetadata(Color.FromRgb(0x5B, 0x9B, 0xFF), (d, _) => ((TimelineControl)d).OnSpectrogramColorChanged()));

    /// <summary>Command of the "solo" badge in a layer header (mute all except this layer).</summary>
    public static readonly DependencyProperty SoloCommandProperty = DependencyProperty.Register(
        nameof(SoloCommand), typeof(ICommand), typeof(TimelineControl), new PropertyMetadata(null));

    /// <summary>True while "mute all except the selected layer" is on.</summary>
    public static readonly DependencyProperty IsSoloActiveProperty = DependencyProperty.Register(
        nameof(IsSoloActive), typeof(bool), typeof(TimelineControl),
        new PropertyMetadata(false, (d, _) => ((TimelineControl)d).InvalidateContent()));

    public ICommand? SoloCommand
    {
        get => (ICommand?)GetValue(SoloCommandProperty);
        set => SetValue(SoloCommandProperty, value);
    }

    public bool IsSoloActive
    {
        get => (bool)GetValue(IsSoloActiveProperty);
        set => SetValue(IsSoloActiveProperty, value);
    }

    /// <summary>Raised when the view or the playhead was redrawn (the bottom ruler follows it).</summary>
    public event Action? ViewChanged;

    /// <summary>Height of the ruler (the bottom copy of it is a <see cref="TimelineRuler"/>).</summary>
    public static double RulerStripHeight => RulerHeight;

    private enum DragMode { None, Move, ResizeLeft, ResizeRight, FadeIn, FadeOut, Scrub }

    private enum HitZone { None, Body, LeftEdge, RightEdge, FadeInHandle, FadeOutHandle }

    private readonly DrawingVisual _contentVisual = new();
    private readonly DrawingVisual _playheadVisual = new();

    private bool _contentQueued;
    private bool _fit = true;
    private double _viewStart;
    private double _viewSpan = 600;

    private DragMode _mode = DragMode.None;
    private Layer? _dragLayer;

    // Header hover: the row under the mouse and which arrow (-1 up, +1 down, 0 none) is under it.
    private int _hoverLane = -1;
    private int _hoverArrow;
    private bool _hoverMute;
    private bool _hoverSolo;
    private double _dragStartTime;
    private double _originalStart;
    private double _originalDuration;

    public ProjectModel? Project
    {
        get => (ProjectModel?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public Layer? SelectedLayer
    {
        get => (Layer?)GetValue(SelectedLayerProperty);
        set => SetValue(SelectedLayerProperty, value);
    }

    public double PlayheadSeconds
    {
        get => (double)GetValue(PlayheadSecondsProperty);
        set => SetValue(PlayheadSecondsProperty, value);
    }

    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    public ICommand? DeleteCommand
    {
        get => (ICommand?)GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    public SpectrogramService? Spectrograms
    {
        get => (SpectrogramService?)GetValue(SpectrogramsProperty);
        set => SetValue(SpectrogramsProperty, value);
    }

    public Color SpectrogramColor
    {
        get => (Color)GetValue(SpectrogramColorProperty);
        set => SetValue(SpectrogramColorProperty, value);
    }

    public bool ShowSpectrogram
    {
        get => (bool)GetValue(ShowSpectrogramProperty);
        set => SetValue(ShowSpectrogramProperty, value);
    }

    public TimelineControl()
    {
        // A recording that turned out to be missing is written on its track.
        Loaded += (_, _) => SampleLibrary.StateChanged += InvalidateContent;
        Unloaded += (_, _) => SampleLibrary.StateChanged -= InvalidateContent;

        AddVisualChild(_contentVisual);
        AddVisualChild(_playheadVisual);
        InitAddSlot();

        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
    }

    protected override int VisualChildrenCount => 3;

    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _contentVisual,
        1 => _addVisual,
        _ => _playheadVisual
    };

    // ---------------------------------------------------------------- dependency property callbacks

    private static void OnProjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TimelineControl)d;

        if (e.OldValue is ProjectModel oldProject)
            oldProject.Changed -= control.OnProjectContentChanged;

        if (e.NewValue is ProjectModel newProject)
            newProject.Changed += control.OnProjectContentChanged;

        control._fit = true;
        control.ClampView();
        control.InvalidateContent();
    }

    private static void OnSpectrogramsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TimelineControl)d;

        if (e.OldValue is SpectrogramService oldService)
            oldService.Changed -= control.InvalidateContent;

        if (e.NewValue is SpectrogramService newService)
            newService.Changed += control.InvalidateContent;

        control.InvalidateContent();
    }

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TimelineControl)d).InvalidateContent();

    private static void OnPlayheadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TimelineControl)d;
        double time = (double)e.NewValue;

        // While zoomed in, keep the playhead visible.
        if (!control._fit && control._mode == DragMode.None
            && (time > control._viewStart + control._viewSpan || time < control._viewStart))
        {
            control._viewStart = Math.Max(0, time - control._viewSpan * 0.05);
            control.ClampView();
            control.InvalidateContent();
        }

        control.RenderPlayhead();
    }

    private void OnProjectContentChanged()
    {
        InvalidateMeasure();
        ClampView();
        InvalidateContent();
        RenderPlayhead();
        RenderAddSlot();
    }
}

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
    private const double HeaderWidth = 204;
    private const double RulerHeight = 30;
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

    public bool ShowSpectrogram
    {
        get => (bool)GetValue(ShowSpectrogramProperty);
        set => SetValue(ShowSpectrogramProperty, value);
    }

    public TimelineControl()
    {
        AddVisualChild(_contentVisual);
        AddVisualChild(_playheadVisual);

        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        ToolTip = "Drag clips to move, drag edges to resize, drag the round handles to set fades.\n"
                  + "Click the ruler to move the playhead. Ctrl+wheel: zoom. Shift+wheel: scroll. Del: remove layer.\n"
                  + "Hover a layer name and use the arrows (or Alt+Up/Down) to change the order of layers.";
    }

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index) => index == 0 ? _contentVisual : _playheadVisual;

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
    }
}

using AmbientVenGen.Core.Composition;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>Scissors of the Recording group: the loop piece is marked on the layer in the timeline.</summary>
public partial class MainViewModel
{
    /// <summary>The scissors are on: the timeline lets the user mark a piece of the selected recording.</summary>
    [ObservableProperty]
    private bool _isLoopCutMode;

    /// <summary>The marked piece in project seconds (NaN: nothing is marked yet); set by the timeline.</summary>
    [ObservableProperty]
    private double _loopCutStart = double.NaN;

    [ObservableProperty]
    private double _loopCutEnd = double.NaN;

    private void CancelLoopCut()
    {
        IsLoopCutMode = false;
        LoopCutStart = LoopCutEnd = double.NaN;
    }

    /// <summary>First click: switch the marking on. Second click: put the marked piece into Loop start and Loop duration.</summary>
    [RelayCommand]
    private void ToggleLoopCut()
    {
        if (SelectedLayer is not { Type: LayerType.FieldRecord } layer)
        {
            CancelLoopCut();
            return;
        }

        if (!IsLoopCutMode)
        {
            LoopCutStart = LoopCutEnd = double.NaN;
            IsLoopCutMode = true;
            return;
        }

        bool marked = !double.IsNaN(LoopCutStart) && !double.IsNaN(LoopCutEnd) && LoopCutEnd > LoopCutStart;
        double start = LoopCutStart;
        double end = LoopCutEnd;
        CancelLoopCut();

        if (marked)
            ApplyLoopCut(layer, start - layer.StartSeconds, end - layer.StartSeconds);
    }

    // The layer plays its piece from the beginning of the piece at the speed of the layer; a stretched recording is
    // longer by the stretch factor and a reversed one is played from the end of the piece.
    private void ApplyLoopCut(Layer layer, double from, double to)
    {
        double speed = Math.Max(0.05, layer.Speed);
        double stretch = Math.Max(1.0, layer.Stretch);
        double piece = layer.LoopDuration > 0 ? layer.LoopDuration : Math.Max(SampleTransform.MinPieceSeconds, SelectedSourceSeconds - layer.LoopStart);

        double a = Math.Clamp(from * speed / stretch, 0, piece);
        double b = Math.Clamp(to * speed / stretch, 0, piece);

        if (b - a < SampleTransform.MinPieceSeconds)
        {
            _messageService.ShowError($"The marked piece is too short: a loop is at least {SampleTransform.MinPieceSeconds:0.#} s of the recording.");
            return;
        }

        double start = layer.Reverse ? layer.LoopStart + piece - b : layer.LoopStart + a;
        start = Math.Clamp(start, 0, Math.Max(0, SelectedSourceSeconds - SampleTransform.MinPieceSeconds));
        double duration = Math.Min(b - a, Math.Max(SampleTransform.MinPieceSeconds, SelectedSourceSeconds - start));

        layer.LoopStart = Math.Round(start, 2);
        layer.LoopDuration = Math.Round(duration, 2);
    }
}

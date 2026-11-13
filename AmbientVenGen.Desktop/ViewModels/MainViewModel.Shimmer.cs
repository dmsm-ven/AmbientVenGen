namespace AmbientVenGen.Desktop.ViewModels;

public partial class MainViewModel
{
    /// <summary>Intervals of the shimmer in semitones: -24 ... +24 (+12 is an octave up).</summary>
    public IReadOnlyList<Choice<int>> ShimmerIntervals { get; } =
        Enumerable.Range(-24, 49).Select(s => new Choice<int>(s > 0 ? $"+{s} st" : $"{s} st", s)).ToList();
}

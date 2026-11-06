using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Tests;

public class LayerSpectrogramTests
{
    [Fact]
    public void IsolateKeepsTheSoundButDropsPlacementRepeatsAndReverb()
    {
        LayerSpec layer = TestSpecs.Layer(LayerType.Pad, start: 40, duration: 30, repeats: 3, gap: 5, muted: true, pitchHz: 130);

        LayerSpec one = LayerSpectrogram.Isolate(layer);

        Assert.Equal(0, one.StartSeconds);
        Assert.Equal(1, one.PlayCount);
        Assert.False(one.Muted);
        Assert.Equal(0, one.ReverbSend);
        Assert.Equal(layer.PitchHz, one.PitchHz);
        Assert.Equal(layer.Seed, one.Seed);
        Assert.Equal(layer.DurationSeconds, one.DurationSeconds);
    }

    [Fact]
    public void ADroneHasPeaksInRangeAndSomethingToSee()
    {
        LayerSpec drone = TestSpecs.Layer(LayerType.Drone, duration: 8, pitchHz: 110);

        SpectrogramData data = LayerSpectrogram.Compute(drone, projectSeed: 5);

        Assert.Equal(LayerSpectrogram.ColumnsFor(8), data.Columns);
        Assert.All(data.Peaks, v => Assert.InRange(v, 0f, 1f));
        Assert.True(data.Max > 0.01f);
    }

    [Fact]
    public void ACancelledRequestStopsRightAway()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            LayerSpectrogram.Compute(TestSpecs.Layer(LayerType.Drone, duration: 60), 1, token: cts.Token));
    }

    [Fact]
    public void TheNumberOfColumnsGrowsWithTheLengthButIsCapped()
    {
        Assert.Equal(16, LayerSpectrogram.ColumnsFor(1));
        Assert.Equal(240, LayerSpectrogram.ColumnsFor(60));
        Assert.Equal(1200, LayerSpectrogram.ColumnsFor(3600));
    }
}

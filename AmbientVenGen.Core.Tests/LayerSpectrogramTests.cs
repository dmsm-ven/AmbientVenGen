using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Tests;

public class LayerSpectrogramTests
{
    [Fact]
    public void FftFindsTheBinOfASine()
    {
        const int n = LayerSpectrogram.FftSize;
        const int bin = 100;
        double[] re = new double[n];
        double[] im = new double[n];

        for (int i = 0; i < n; i++)
            re[i] = Math.Sin(2 * Math.PI * bin * i / n);

        LayerSpectrogram.Fft(re, im);

        int peak = 0;
        double best = 0;
        for (int k = 1; k < n / 2; k++)
        {
            double magnitude = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
            if (magnitude > best)
            {
                best = magnitude;
                peak = k;
            }
        }

        Assert.Equal(bin, peak);
    }

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
    public void ADroneHasColumnsInRangeAndSomethingToSee()
    {
        LayerSpec drone = TestSpecs.Layer(LayerType.Drone, duration: 8, pitchHz: 110);

        SpectrogramData data = LayerSpectrogram.Compute(drone, projectSeed: 5, bands: 32);

        Assert.Equal(LayerSpectrogram.ColumnsFor(8), data.Columns);
        Assert.Equal(32, data.Bands);
        Assert.Equal(data.Columns * data.Bands, data.Values.Length);
        Assert.All(data.Values, v => Assert.InRange(v, 0f, 1f));
        Assert.Contains(data.Values, v => v > 0.05f);
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

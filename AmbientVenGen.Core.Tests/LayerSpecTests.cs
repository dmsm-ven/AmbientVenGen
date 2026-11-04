using AmbientVenGen.Core.Composition;
using Xunit;

namespace AmbientVenGen.Core.Tests;

public class LayerSpecTests
{
    [Fact]
    public void TotalLengthIsDurationTimesRepeatsPlusGaps()
    {
        // 4 plays of 60 s and 3 gaps of 15 s
        LayerSpec layer = TestSpecs.Layer(duration: 60, repeats: 4, gap: 15);

        Assert.Equal(4 * 60 + 3 * 15, layer.TotalSeconds, 6);
    }

    [Fact]
    public void WithoutRepeatsTheLengthIsTheDuration() =>
        Assert.Equal(60, TestSpecs.Layer(duration: 60).TotalSeconds, 6);

    [Fact]
    public void RepeatsBelowOneStillMeanOnePlay() =>
        Assert.Equal(1, TestSpecs.Layer(repeats: 0).PlayCount);

    [Fact]
    public void GapsAreSilentAndPlaysRestartTheirLocalTime()
    {
        LayerSpec layer = TestSpecs.Layer(duration: 60, repeats: 3, gap: 10);

        Assert.True(layer.TryGetPlayTime(30, out double within, out int iteration));
        Assert.Equal(30, within, 6);
        Assert.Equal(0, iteration);

        Assert.False(layer.TryGetPlayTime(65, out _, out _)); // inside the first gap

        Assert.True(layer.TryGetPlayTime(75, out within, out iteration)); // 70 is the start of the second play
        Assert.Equal(5, within, 6);
        Assert.Equal(1, iteration);

        Assert.False(layer.TryGetPlayTime(-1, out _, out _));
        Assert.False(layer.TryGetPlayTime(layer.TotalSeconds + 1, out _, out _));
    }

    [Fact]
    public void FadeInRisesFromSilenceAndTheMiddleIsFullGain()
    {
        LayerSpec layer = TestSpecs.Layer(duration: 100, fadeIn: 10, fadeOut: 10);

        Assert.Equal(0.0, layer.FadeGainAt(0), 6);
        Assert.Equal(0.5, layer.FadeGainAt(5), 6); // linear fade
        Assert.Equal(1.0, layer.FadeGainAt(50), 6);
        Assert.Equal(0.5, layer.FadeGainAt(95), 6);
        Assert.Equal(0.0, layer.FadeGainAt(100), 6);
    }

    [Fact]
    public void FadeOutHasItsOwnCurve()
    {
        LayerSpec layer = TestSpecs.Layer(
            duration: 100, fadeIn: 10, fadeOut: 10, fadeEasing: Easing.Linear, fadeOutEasing: Easing.ExponentialDecay);

        double inMiddle = layer.FadeGainAt(5);   // linear: 0.5
        double outMiddle = layer.FadeGainAt(95); // the other curve at the same distance from the edge

        Assert.Equal(0.5, inMiddle, 6);
        Assert.NotEqual(inMiddle, outMiddle, 3);
    }

    [Fact]
    public void ProjectsWithoutAFadeOutCurveUseTheFadeInCurve()
    {
        LayerSpec layer = TestSpecs.Layer(fadeEasing: Easing.EaseIn, fadeOutEasing: null);

        Assert.Equal(Easing.EaseIn, layer.FadeOutCurve);
    }

    [Fact]
    public void FadesLongerThanTheLayerAreShrunkInsteadOfGoingNegative()
    {
        LayerSpec layer = TestSpecs.Layer(duration: 10, fadeIn: 8, fadeOut: 8);

        for (double t = 0; t <= 10; t += 0.5)
            Assert.InRange(layer.FadeGainAt(t), 0.0, 1.0);
    }

    [Fact]
    public void CurveGoesFromStartToEnd()
    {
        var curve = new CurveSpec(0.2, 0.8, Easing.Linear);

        Assert.Equal(0.2, curve.At(0), 9);
        Assert.Equal(0.5, curve.At(0.5), 9);
        Assert.Equal(0.8, curve.At(1), 9);
    }

    [Fact]
    public void CurveMayRunBackwards()
    {
        var curve = new CurveSpec(1.0, 0.0, Easing.Linear);

        Assert.Equal(0.75, curve.At(0.25), 9);
    }
}

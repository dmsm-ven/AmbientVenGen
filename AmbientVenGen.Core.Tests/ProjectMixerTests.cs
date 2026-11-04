using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;
using Xunit;

namespace AmbientVenGen.Core.Tests;

public class ProjectMixerTests
{
    private const int Channels = 2; // stereo, as in the mixer

    /// <summary>Renders the first <paramref name="seconds"/> of the project in chunks, like the player does.</summary>
    private static float[] Render(ProjectSpec spec, double seconds)
    {
        var mixer = new ProjectMixer(spec);
        int total = (int)(seconds * ProjectMixer.SampleRate) * Channels;
        var result = new float[total];
        int done = 0;

        while (done < total)
        {
            int chunk = Math.Min(4096 * Channels, total - done);
            int read = mixer.Read(result.AsSpan(done, chunk));
            Assert.True(read > 0, "the mixer stopped before the end of the project");
            done += read;
        }

        return result;
    }

    private static bool AllFinite(float[] samples) => samples.All(float.IsFinite);

    /// <summary>
    /// Regression: the delay ring buffer was read at index "size" for some delay times (for example 0.55 s), the exception
    /// ended playback in the audio thread. These are the delays that hit it, plus a few ordinary ones.
    /// </summary>
    [Theory]
    [InlineData(0.07)]
    [InlineData(0.14)]
    [InlineData(0.17)]
    [InlineData(0.28)]
    [InlineData(0.34)]
    [InlineData(0.55)]
    [InlineData(0.56)]
    [InlineData(0.68)]
    [InlineData(1.1)]
    [InlineData(1.37)]
    [InlineData(2.0)]
    [InlineData(0.5)]
    public void DelayTimesNeverBreakTheAudioThread(double delaySeconds)
    {
        LayerSpec pad = TestSpecs.Layer(LayerType.Pad, duration: 10, delaySeconds: delaySeconds, delayFeedback: 0.45);
        ProjectSpec spec = TestSpecs.Project(10, pad);

        // long enough for the write position to pass the read position of the echo
        float[] samples = Render(spec, delaySeconds + 0.3);

        Assert.True(AllFinite(samples));
    }

    [Fact]
    public void EveryTimeOfDelayInTheRangeIsSafe()
    {
        // a 10 ms grid over the whole 0..2 s range; short renders keep it quick
        for (int hundredths = 1; hundredths <= 200; hundredths += 7)
        {
            double delay = hundredths / 100.0;
            LayerSpec drone = TestSpecs.Layer(LayerType.Drone, duration: 5, delaySeconds: delay, delayFeedback: 0.6);

            float[] samples = Render(TestSpecs.Project(5, drone), delay + 0.1);

            Assert.True(AllFinite(samples), $"delay {delay} s produced NaN or infinity");
        }
    }

    [Fact]
    public void ADroneIsAudibleAndFinite()
    {
        float[] samples = Render(TestSpecs.Project(5, TestSpecs.Layer(LayerType.Drone, duration: 5)), 2);

        Assert.True(AllFinite(samples));
        Assert.Contains(samples, s => Math.Abs(s) > 1e-4f);
    }

    [Fact]
    public void AMutedLayerIsSilent()
    {
        float[] samples = Render(TestSpecs.Project(5, TestSpecs.Layer(LayerType.Drone, duration: 5, muted: true)), 1);

        Assert.All(samples, s => Assert.InRange(Math.Abs(s), 0f, 1e-6f));
    }

    [Fact]
    public void TheSameProjectRendersTheSameSound()
    {
        ProjectSpec spec = TestSpecs.Project(5, TestSpecs.Layer(LayerType.Pad, duration: 5, seed: 42, delaySeconds: 0.3));

        Assert.Equal(Render(spec, 1), Render(spec, 1));
    }

    [Fact]
    public void ALayerThatStartsLaterIsSilentBeforeItStarts()
    {
        LayerSpec late = TestSpecs.Layer(LayerType.Drone, start: 3, duration: 5);

        float[] samples = Render(TestSpecs.Project(10, late), 2);

        Assert.All(samples, s => Assert.InRange(Math.Abs(s), 0f, 1e-6f));
    }

    [Fact]
    public void TheMixerEndsAtTheEndOfTheProject()
    {
        var mixer = new ProjectMixer(TestSpecs.Project(1, TestSpecs.Layer(LayerType.Drone, duration: 1)));
        var buffer = new float[ProjectMixer.SampleRate * Channels * 2];

        int read = mixer.Read(buffer);

        Assert.Equal(ProjectMixer.SampleRate * Channels, read);
        Assert.Equal(0, mixer.Read(buffer));
    }
}

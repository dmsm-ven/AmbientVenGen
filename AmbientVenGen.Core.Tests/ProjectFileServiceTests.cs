using AmbientVenGen.Core.Composition;
using AmbientVenGen.Core.Projects;
using Xunit;

namespace AmbientVenGen.Core.Tests;

public sealed class ProjectFileServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "afproj-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ProjectFileService _service = new();

    public ProjectFileServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveThenLoadKeepsEverything()
    {
        LayerSpec drone = TestSpecs.Layer(LayerType.Drone, duration: 120, fadeIn: 10, fadeOut: 20,
            fadeEasing: Easing.EaseIn, fadeOutEasing: Easing.ExponentialDecay, pitchHz: 55.5);
        LayerSpec pad = TestSpecs.Layer(LayerType.Pad, start: 10, duration: 50, repeats: 3, gap: 5,
            delaySeconds: 0.55, delayFeedback: 0.45);
        ProjectSpec spec = TestSpecs.Project(300, drone, pad);

        string path = Path.Combine(_dir, "a" + ProjectFileService.Extension);
        _service.Save(path, spec);
        ProjectSpec loaded = _service.Load(path);

        Assert.Equal(spec.DurationSeconds, loaded.DurationSeconds);
        Assert.Equal(spec.Seed, loaded.Seed);
        Assert.Equal(spec.ReverbMix, loaded.ReverbMix);
        Assert.Equal(spec.Layers.Length, loaded.Layers.Length);

        for (int i = 0; i < spec.Layers.Length; i++)
            Assert.Equal(spec.Layers[i], loaded.Layers[i]);
    }

    [Fact]
    public void AFileFromBeforeTheNewFieldsLoadsWithDefaults()
    {
        // Only the original fields: the later ones (noise, repeats, filter, delay, send, drift, fade-out curve) are absent.
        const string json = """
        {
          "Version": 1, "DurationSeconds": 60, "Seed": 5, "ReverbMix": 0.3,
          "Layers": [ {
            "Id": "6f1d1f6e-6d3a-4a43-8f3e-2b0f0f8a0001", "Name": "Old", "Type": "Drone",
            "StartSeconds": 0, "DurationSeconds": 60, "FadeInSeconds": 5, "FadeOutSeconds": 5, "FadeEasing": "EaseOut",
            "Level": { "From": 0.5, "To": 0.5, "Easing": "Linear" },
            "Brightness": { "From": 0.3, "To": 0.3, "Easing": "Linear" },
            "Modulation": { "From": 0.2, "To": 0.2, "Easing": "Linear" },
            "PitchHz": 110, "Pan": 0, "Muted": false, "Seed": 9
          } ]
        }
        """;
        string path = Path.Combine(_dir, "old" + ProjectFileService.Extension);
        File.WriteAllText(path, json);

        LayerSpec layer = _service.Load(path).Layers.Single();

        Assert.Equal(1, layer.Repeats);
        Assert.Equal(0, layer.DelaySeconds);
        Assert.Equal(1.0, layer.ReverbSend);
        Assert.Equal(Easing.EaseOut, layer.FadeOutCurve); // the fade-in curve is used for both
    }

    [Fact]
    public void AProjectFromANewerVersionIsRefused()
    {
        string path = Path.Combine(_dir, "new" + ProjectFileService.Extension);
        File.WriteAllText(path, "{ \"Version\": 999, \"DurationSeconds\": 60, \"Seed\": 1, \"ReverbMix\": 0.3, \"Layers\": [] }");

        Assert.Throws<InvalidDataException>(() => _service.Load(path));
    }

    [Fact]
    public void ANonProjectFileIsRefused()
    {
        string path = Path.Combine(_dir, "junk" + ProjectFileService.Extension);
        File.WriteAllText(path, "null");

        Assert.Throws<InvalidDataException>(() => _service.Load(path));
    }
}

using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Projects;

/// <summary>A ready-made starting point: replaces the layers of the project.</summary>
public sealed record ProjectTemplate(string Name, Func<ProjectSpec> Build);

public sealed class TemplateService
{
    public IReadOnlyList<ProjectTemplate> Templates { get; } =
    [
        new("Deep Space", BuildDeepSpace),
        new("Dark Drone", BuildDarkDrone),
        new("Distant Atmosphere", BuildDistantAtmosphere)
    ];

    public ProjectTemplate Default => Templates[0];

    private static ProjectSpec Build(double duration, double reverb, params Layer[] layers) =>
        new(ProjectSpec.CurrentVersion, duration, Random.Shared.Next(1, int.MaxValue), reverb,
            layers.Select(layer => layer.ToSpec()).ToArray());

    private static ProjectSpec BuildDeepSpace() => Build(600, 0.40,
        LayerDefaults.Create(LayerType.Drone, "Sub Drone", l =>
        {
            l.PitchHz = 41.2;
            l.StartSeconds = 0; l.DurationSeconds = 600; l.FadeInSeconds = 30; l.FadeOutSeconds = 45;
            l.LevelFrom = l.LevelTo = 0.70;
            l.BrightnessFrom = 0.15; l.BrightnessTo = 0.35;
        }),
        LayerDefaults.Create(LayerType.Drone, "Fifth", l =>
        {
            l.PitchHz = 61.7; l.Pan = -0.3;
            l.StartSeconds = 60; l.DurationSeconds = 480; l.FadeInSeconds = 60; l.FadeOutSeconds = 60;
            l.LevelFrom = 0.30; l.LevelTo = 0.50; l.LevelEasing = Easing.Sine;
            l.ModulationFrom = l.ModulationTo = 0.45;
        }),
        LayerDefaults.Create(LayerType.Pad, "Void Choir", l =>
        {
            l.PitchHz = 82.4; l.Pan = 0.25;
            l.StartSeconds = 120; l.DurationSeconds = 420; l.FadeInSeconds = 90; l.FadeOutSeconds = 90;
            l.LevelFrom = 0.30; l.LevelTo = 0.55; l.LevelEasing = Easing.EaseInOut;
            l.BrightnessFrom = 0.30; l.BrightnessTo = 0.60;
        }),
        LayerDefaults.Create(LayerType.Noise, "Cosmic Dust", l =>
        {
            l.StartSeconds = 0; l.DurationSeconds = 600; l.FadeInSeconds = 40; l.FadeOutSeconds = 40;
            l.LevelFrom = l.LevelTo = 0.30;
            l.BrightnessFrom = 0.20; l.BrightnessTo = 0.40;
        }));

    private static ProjectSpec BuildDarkDrone() => Build(600, 0.30,
        LayerDefaults.Create(LayerType.Drone, "Abyss", l =>
        {
            l.PitchHz = 36.7;
            l.StartSeconds = 0; l.DurationSeconds = 600; l.FadeInSeconds = 20; l.FadeOutSeconds = 40;
            l.LevelFrom = l.LevelTo = 0.85;
            l.BrightnessFrom = l.BrightnessTo = 0.10;
            l.ModulationFrom = l.ModulationTo = 0.15;
        }),
        LayerDefaults.Create(LayerType.Drone, "Low Fifth", l =>
        {
            l.PitchHz = 55.0; l.Pan = 0.2;
            l.StartSeconds = 40; l.DurationSeconds = 500; l.FadeInSeconds = 60; l.FadeOutSeconds = 60;
            l.LevelFrom = l.LevelTo = 0.45;
            l.BrightnessFrom = l.BrightnessTo = 0.15;
        }),
        LayerDefaults.Create(LayerType.Noise, "Rumble", l =>
        {
            l.StartSeconds = 0; l.DurationSeconds = 600; l.FadeInSeconds = 30; l.FadeOutSeconds = 30;
            l.LevelFrom = l.LevelTo = 0.30;
            l.BrightnessFrom = l.BrightnessTo = 0.08;
        }));

    private static ProjectSpec BuildDistantAtmosphere() => Build(600, 0.50,
        LayerDefaults.Create(LayerType.Noise, "Wind", l =>
        {
            l.StartSeconds = 0; l.DurationSeconds = 600; l.FadeInSeconds = 40; l.FadeOutSeconds = 60;
            l.LevelFrom = l.LevelTo = 0.50;
            l.BrightnessFrom = 0.45; l.BrightnessTo = 0.70;
            l.ModulationFrom = l.ModulationTo = 0.60;
        }),
        LayerDefaults.Create(LayerType.Pad, "Far Voices", l =>
        {
            l.PitchHz = 110.0; l.Pan = -0.2;
            l.StartSeconds = 60; l.DurationSeconds = 480; l.FadeInSeconds = 80; l.FadeOutSeconds = 80;
            l.LevelFrom = l.LevelTo = 0.40;
            l.BrightnessFrom = l.BrightnessTo = 0.60;
            l.ModulationFrom = l.ModulationTo = 0.55;
        }),
        LayerDefaults.Create(LayerType.Drone, "Ground", l =>
        {
            l.PitchHz = 49.0;
            l.StartSeconds = 0; l.DurationSeconds = 600; l.FadeInSeconds = 50; l.FadeOutSeconds = 60;
            l.LevelFrom = l.LevelTo = 0.30;
        }));
}

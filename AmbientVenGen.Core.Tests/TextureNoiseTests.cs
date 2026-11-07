using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;
using Xunit;

namespace AmbientVenGen.Core.Tests;

public class TextureNoiseTests
{
    public static TheoryData<NoiseType> Textures => new()
    {
        NoiseType.Crackle, NoiseType.Vinyl, NoiseType.RadioStatic, NoiseType.Wind, NoiseType.Rain
    };

    private static float[] Render(NoiseType type, int seed, double seconds = 4)
    {
        var generator = LayerGenerator.Create(LayerType.Noise, seed, null, type);
        var p = new BlockParams(0.5f, 0.5f, 220f);
        var all = new List<float>();
        var left = new float[1024];
        var right = new float[1024];

        for (int i = 0; i < seconds * 44100 / 1024; i++)
        {
            generator.Render(p, left, right);
            all.AddRange(left);
            all.AddRange(right);
        }

        return [.. all];
    }

    [Theory]
    [MemberData(nameof(Textures))]
    public void TexturesAreAudibleFiniteAndInRange(NoiseType type)
    {
        float[] samples = Render(type, 3);

        Assert.All(samples, s => Assert.InRange(s, -1f, 1f));
        double rms = Math.Sqrt(samples.Sum(s => (double)s * s) / samples.Length);
        Assert.InRange(rms, 0.005, 0.7);
    }

    [Theory]
    [MemberData(nameof(Textures))]
    public void TexturesAreDeterministicAndDependOnTheSeed(NoiseType type)
    {
        Assert.Equal(Render(type, 7, 1), Render(type, 7, 1));
        Assert.NotEqual(Render(type, 7, 1), Render(type, 8, 1));
    }
}

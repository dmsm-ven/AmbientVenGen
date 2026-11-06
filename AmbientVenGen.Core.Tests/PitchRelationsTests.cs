using AmbientVenGen.Core.Composition;
using Xunit;

namespace AmbientVenGen.Core.Tests;

public class PitchRelationsTests
{
    private static ChordPreset Chord(string name) => PitchRelations.Chords.Single(c => c.Name == name);

    [Theory]
    [InlineData(1200, 600)]
    [InlineData(2000, 1000)] // 1000 Hz is the top of the range, so it stays
    [InlineData(3000, 750)]
    [InlineData(10, 20)]
    [InlineData(5, 20)]
    [InlineData(440, 440)]
    public void FoldKeepsThePitchInRangeByOctaves(double input, double expected) =>
        Assert.Equal(expected, PitchRelations.Fold(input), 6);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-50)]
    public void FoldNeverReturnsGarbage(double input) =>
        Assert.InRange(PitchRelations.Fold(input), Layer.MinPitchHz, Layer.MaxPitchHz);

    [Fact]
    public void MajorTriadOnA220() =>
        Assert.Equal((275.0, 330.0), PitchRelations.ChordPitches(220, Chord("Major triad")));

    [Fact]
    public void MinorTriadOnA220() =>
        Assert.Equal((264.0, 330.0), PitchRelations.ChordPitches(220, Chord("Minor triad")));

    [Fact]
    public void OpenFifthOnA220() =>
        Assert.Equal((330.0, 440.0), PitchRelations.ChordPitches(220, Chord("Open fifth")));

    [Fact]
    public void Sus4OnA220()
    {
        (double low, double high) = PitchRelations.ChordPitches(220, Chord("Sus4"));

        Assert.Equal(293.3, low, 1);
        Assert.Equal(330.0, high, 1);
    }

    [Fact]
    public void BeatingClusterIsAFewCentsAroundTheRoot()
    {
        (double low, double high) = PitchRelations.ChordPitches(220, Chord("Beating cluster"));

        Assert.InRange(low, 218.8, 219.2);
        Assert.InRange(high, 220.8, 221.2);
    }

    [Fact]
    public void OctaveSpreadFoldsTheTopVoiceWhenItWouldLeaveTheRange()
    {
        (double low, double high) = PitchRelations.ChordPitches(600, Chord("Octave spread"));

        Assert.Equal(300.0, low, 6);
        Assert.Equal(600.0, high, 6); // 1200 Hz folded by an octave
    }

    [Fact]
    public void LowVoiceIsNeverAboveTheHighOne()
    {
        foreach (ChordPreset chord in PitchRelations.Chords)
        {
            foreach (double root in new[] { 20.0, 55.0, 220.0, 700.0, 1000.0 })
            {
                (double low, double high) = PitchRelations.ChordPitches(root, chord);

                Assert.InRange(low, Layer.MinPitchHz, Layer.MaxPitchHz);
                Assert.InRange(high, Layer.MinPitchHz, Layer.MaxPitchHz);
            }
        }
    }

    [Fact]
    public void ThereAreSevenRelationsWithUniqueNames()
    {
        Assert.Equal(7, PitchRelations.Chords.Count);
        Assert.Equal(7, PitchRelations.Chords.Select(c => c.Name).Distinct().Count());
    }
}

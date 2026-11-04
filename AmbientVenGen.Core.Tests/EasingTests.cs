using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Tests;

public class EasingTests
{
    public static IEnumerable<object[]> AllEasings() => Enum.GetValues<Easing>().Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(AllEasings))]
    public void StartsAtZeroAndEndsAtOne(Easing easing)
    {
        Assert.Equal(0.0, EasingFunctions.Apply(easing, 0.0), 6);
        Assert.Equal(1.0, EasingFunctions.Apply(easing, 1.0), 6);
    }

    [Theory]
    [MemberData(nameof(AllEasings))]
    public void NeverLeavesTheUnitRangeAndNeverGoesBack(Easing easing)
    {
        double previous = 0;

        for (int i = 0; i <= 100; i++)
        {
            double value = EasingFunctions.Apply(easing, i / 100.0);

            Assert.InRange(value, -1e-9, 1 + 1e-9);
            Assert.True(value >= previous - 1e-9, $"{easing} goes back at {i}%");
            previous = value;
        }
    }

    [Fact]
    public void LinearIsTheIdentity() =>
        Assert.Equal(0.25, EasingFunctions.Apply(Easing.Linear, 0.25), 9);
}

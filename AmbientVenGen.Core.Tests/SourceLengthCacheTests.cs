using AmbientVenGen.Core.Audio;

namespace AmbientVenGen.Core.Tests;

public class SourceLengthCacheTests
{
    [Fact]
    public async Task AMissingFileHasNoLengthAndDoesNotThrow()
    {
        string path = Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".wav");

        double seconds = await SourceLengthCache.ReadAsync(path);

        Assert.Equal(0.0, seconds);
        Assert.False(SourceLengthCache.TryGet(path, out _));
    }
}

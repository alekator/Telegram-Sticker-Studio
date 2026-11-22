using StickerForge.Media;

namespace StickerForge.Tests;

public sealed class FfmpegServiceTests
{
    [Fact]
    public async Task MissingExecutablesAreReported()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "ffmpeg");
        var result = await new FfmpegService(missing, missing).CheckAsync();
        Assert.False(result.Available);
        Assert.Contains("unavailable", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FfmpegService().CheckAsync(source.Token));
    }

    [Fact]
    public void InvalidTimestampIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new FfmpegService().ReadFrameAsync("file.mp4", -1); });
    }
}

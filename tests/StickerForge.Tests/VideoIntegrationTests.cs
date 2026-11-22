using System.Diagnostics;
using StickerForge.Media;

namespace StickerForge.Tests;

public sealed class FfmpegIntegrationFactAttribute : FactAttribute
{
    public FfmpegIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("STICKERFORGE_FFMPEG_INTEGRATION") != "1")
            Skip = "Set STICKERFORGE_FFMPEG_INTEGRATION=1 when FFmpeg and FFprobe are installed.";
    }
}

public sealed class VideoIntegrationTests
{
    [FfmpegIntegrationFact]
    public async Task RealMp4AndWebmDecodeProbeSeekAndCancel()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stickerforge-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var (name, codec) in new[] { ("sample.mp4", "libx264"), ("sample.webm", "libvpx-vp9") })
            {
                var path = Path.Combine(dir, name);
                var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
                foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc=size=64x48:rate=10", "-t", "1",
                    "-c:v", codec, "-pix_fmt", "yuv420p", "-y", path }) start.ArgumentList.Add(arg);
                using var generator = Process.Start(start)!;
                var errors = await generator.StandardError.ReadToEndAsync();
                await generator.WaitForExitAsync();
                Assert.True(generator.ExitCode == 0, errors);
                var service = new FfmpegService();
                Assert.True((await service.CheckAsync()).Available);
                var metadata = await service.ProbeAsync(path);
                Assert.Equal(64, metadata.Width);
                Assert.Equal(48, metadata.Height);
                Assert.InRange(metadata.DurationSeconds, 0.9, 1.1);
                Assert.InRange(metadata.FramesPerSecond!.Value, 9.9, 10.1);
                var imported = (await new ImageImporter(new SkiaImageDecoder(), service).ImportAsync([path]))[0];
                Assert.NotNull(imported.Item);
                Assert.Equal(metadata.Codec, imported.Info!.Video!.Codec);
                Assert.NotEmpty(await service.ReadFrameAsync(path, 0));
                var frames = new List<VideoFrame>();
                await foreach (var frame in service.StreamFramesAsync(path, metadata, 0))
                    frames.Add(frame);
                Assert.True(frames.Count >= 9);
                Assert.Equal(64 * 48 * 4, frames[0].Rgba.Length);
                Assert.True(frames[^1].Seconds > frames[0].Seconds);
                Assert.False(frames[0].Rgba.SequenceEqual(frames[^1].Rgba));
                await foreach (var frame in service.StreamFramesAsync(path, metadata, 0.5))
                {
                    Assert.InRange(frame.Seconds, 0.5, 0.6);
                    break;
                }
                using var cancel = new CancellationTokenSource();
                await using var enumerator = service.StreamFramesAsync(path, metadata, 0, cancel.Token).GetAsyncEnumerator();
                Assert.True(await enumerator.MoveNextAsync());
                cancel.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
            }
            var corrupt = Path.Combine(dir, "bad.mp4");
            await File.WriteAllTextAsync(corrupt, "invalid");
            await Assert.ThrowsAsync<InvalidDataException>(() => new FfmpegService().ProbeAsync(corrupt));
        }
        finally { Directory.Delete(dir, true); }
    }
}

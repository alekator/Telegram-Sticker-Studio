using System.Diagnostics;
using StickerForge.Media;
using SkiaSharp;
using StickerForge.Core;

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
                Assert.Contains(name.EndsWith(".mp4", StringComparison.Ordinal) ? "mov" : "matroska", metadata.Container);
                Assert.Equal(64, metadata.Width);
                Assert.Equal(48, metadata.Height);
                Assert.InRange(metadata.DurationSeconds, 0.9, 1.1);
                Assert.InRange(metadata.FramesPerSecond!.Value, 9.9, 10.1);
                var imported = (await new ImageImporter(new SkiaImageDecoder(), service).ImportAsync([path]))[0];
                Assert.NotNull(imported.Item);
                var exported = await new StickerPackExporter(service).ExportAsync([imported.Item!], dir, "video-" + name);
                Assert.True(exported.Errors.Count == 0, string.Join("; ", exported.Errors));
                Assert.Equal(1, exported.Exported);
                var sticker = Path.Combine(exported.Directory, "001.webm");
                Assert.InRange(new FileInfo(sticker).Length, 1, 256 * 1024);
                var stickerInfo = await service.ProbeAsync(sticker);
                Assert.Equal("vp9", stickerInfo.Codec); Assert.Equal(512, stickerInfo.Width); Assert.Equal(512, stickerInfo.Height);
                var decode = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                foreach (var arg in new[] { "-v", "error", "-c:v", "libvpx-vp9", "-i", sticker, "-frames:v", "1", "-f", "image2pipe", "-vcodec", "png", "pipe:1" }) decode.ArgumentList.Add(arg);
                using var decoder = Process.Start(decode)!; using var png = new MemoryStream();
                var decodeError = decoder.StandardError.ReadToEndAsync(); await decoder.StandardOutput.BaseStream.CopyToAsync(png); await decoder.WaitForExitAsync();
                Assert.True(decoder.ExitCode == 0, await decodeError);
                using var image = SKBitmap.Decode(png.ToArray()); Assert.NotNull(image); Assert.Equal(0, image.GetPixel(0, 0).Alpha); Assert.True(image.GetPixel(256, 256).Alpha > 0);
                Assert.Equal(metadata.Codec, imported.Info!.Video!.Codec);
                Assert.NotEmpty(await service.ReadFrameAsync(path, 0));
                var settings = new TransformSettings { Fit = FitMode.Fill, Scale = .8, Rotation = 25, OffsetX = 30 };
                using (var transformed = SKBitmap.Decode(await service.ReadTransformedFrameAsync(path, metadata, 0, settings)))
                { Assert.NotNull(transformed); Assert.Equal(512, transformed.Width); Assert.Equal(512, transformed.Height); }
                await foreach (var frame in service.StreamFramesAsync(path, metadata, 0, CancellationToken.None, settings))
                { Assert.Equal(512, frame.Width); Assert.Equal(512, frame.Height); break; }
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
            var gif = Path.Combine(dir, "sample.gif");
            var makeGif = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc=size=64x48:rate=10", "-t", "1", "-y", gif }) makeGif.ArgumentList.Add(arg);
            using var gifGenerator = Process.Start(makeGif)!; var gifErrors = await gifGenerator.StandardError.ReadToEndAsync(); await gifGenerator.WaitForExitAsync(); Assert.True(gifGenerator.ExitCode == 0, gifErrors);
            var gifReport = await new StickerPackExporter().ExportAsync([gif], dir, "gif-pack");
            Assert.True(gifReport.Errors.Count == 0, string.Join("; ", gifReport.Errors)); Assert.Equal(1, gifReport.Exported);
            var gifService = new FfmpegService(); var gifMetadata = await gifService.ProbeAsync(gif);
            using (var gifPreview = SKBitmap.Decode(await gifService.ReadTransformedFrameAsync(gif, gifMetadata, 0, new TransformSettings { Fit = FitMode.Manual, Scale = 1.2, Rotation = 20 })))
            { Assert.NotNull(gifPreview); Assert.Equal(512, gifPreview.Width); Assert.Equal(512, gifPreview.Height); }
            var tiff = Path.Combine(dir, "sample.tiff");
            var makeTiff = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "color=red:s=64x48", "-frames:v", "1", "-y", tiff }) makeTiff.ArgumentList.Add(arg);
            using var tiffGenerator = Process.Start(makeTiff)!; var tiffErrors = await tiffGenerator.StandardError.ReadToEndAsync(); await tiffGenerator.WaitForExitAsync(); Assert.True(tiffGenerator.ExitCode == 0, tiffErrors);
            var tiffImport = (await new ImageImporter(new SkiaImageDecoder(), new FfmpegService()).ImportAsync([tiff]))[0];
            Assert.NotNull(tiffImport.Item); Assert.True(tiffImport.Info!.FfmpegStill);
            var tiffReport = await new StickerPackExporter().ExportAsync([tiffImport.Item!], dir, "tiff-pack");
            Assert.True(tiffReport.Errors.Count == 0, string.Join("; ", tiffReport.Errors)); Assert.Equal(1, tiffReport.Exported); Assert.True(File.Exists(Path.Combine(tiffReport.Directory, "001.webp")));
            var apng = Path.Combine(dir, "animated.png");
            var makeApng = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc=size=64x48:rate=10", "-t", "1", "-plays", "0", "-f", "apng", "-y", apng }) makeApng.ArgumentList.Add(arg);
            using var apngGenerator = Process.Start(makeApng)!; var apngErrors = await apngGenerator.StandardError.ReadToEndAsync(); await apngGenerator.WaitForExitAsync(); Assert.True(apngGenerator.ExitCode == 0, apngErrors);
            var apngImport = (await new ImageImporter(new SkiaImageDecoder(), new FfmpegService()).ImportAsync([apng]))[0];
            Assert.True(apngImport.Item is not null, apngImport.Error);
            Assert.True(apngImport.Info!.Video is not null || apngImport.Info.FrameDurations is { Length: > 1 }, "APNG was treated as a still image.");
            var apngReport = await new StickerPackExporter().ExportAsync([apngImport.Item!], dir, "apng-pack");
            Assert.True(apngReport.Errors.Count == 0, string.Join("; ", apngReport.Errors)); Assert.True(File.Exists(Path.Combine(apngReport.Directory, "001.webm")));
            var webp = Path.Combine(dir, "animated.webp");
            var makeWebp = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc=size=64x48:rate=10", "-t", "1", "-loop", "0", "-y", webp }) makeWebp.ArgumentList.Add(arg);
            using var webpGenerator = Process.Start(makeWebp)!; var webpErrors = await webpGenerator.StandardError.ReadToEndAsync(); await webpGenerator.WaitForExitAsync(); Assert.True(webpGenerator.ExitCode == 0, webpErrors);
            var webpImport = (await new ImageImporter(new SkiaImageDecoder(), new FfmpegService()).ImportAsync([webp]))[0];
            Assert.True(webpImport.Item is not null, webpImport.Error);
            Assert.True(webpImport.Info!.FrameDurations is { Length: > 1 }, "Animated WebP was treated as a still image.");
            var webpReport = await new StickerPackExporter().ExportAsync([webpImport.Item!], dir, "webp-pack");
            Assert.True(webpReport.Errors.Count == 0, string.Join("; ", webpReport.Errors)); Assert.True(File.Exists(Path.Combine(webpReport.Directory, "001.webm")));
            var mixed = await new StickerPackExporter().ExportAsync([tiff, gif, Path.Combine(dir, "sample.mp4")], dir, "mixed");
            Assert.True(mixed.Errors.Count == 0, string.Join("; ", mixed.Errors)); Assert.Equal(3, mixed.Exported);
            Assert.True(File.Exists(Path.Combine(mixed.Directory, "001.webp"))); Assert.True(File.Exists(Path.Combine(mixed.Directory, "002.webm"))); Assert.True(File.Exists(Path.Combine(mixed.Directory, "003.webm")));
        }
        finally { Directory.Delete(dir, true); }
    }
}

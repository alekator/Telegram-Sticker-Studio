using System.Diagnostics;
using SkiaSharp;
using StickerForge.Core;
using StickerForge.Media;

namespace StickerForge.Tests;

public sealed class BundledToolFactAttribute : FactAttribute
{
    public BundledToolFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STICKERFORGE_PACKAGE_DIR"))) Skip = "Requires the published Windows package."; }
}

public sealed class BundledToolTests
{
    [BundledToolFact]
    public async Task PackageToolsWorkWithoutPathAndExportMixedMedia()
    {
        var package = Path.GetFullPath(Environment.GetEnvironmentVariable("STICKERFORGE_PACKAGE_DIR")!);
        var location = FfmpegLocator.FindVerified(package, Path.Combine(package, "unused"), FfmpegLocator.ReleaseHashes);
        Assert.NotNull(location); Assert.Equal(package, location.Source);
        var service = FfmpegLocator.CreateForDirectory(package, Path.Combine(package, "unused"), FfmpegLocator.ReleaseHashes);
        Assert.True((await service.CheckAsync()).Available);
        var dir = Path.Combine(Path.GetTempPath(), "sf-package-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var png = Path.Combine(dir, "a.png"); var jpg = Path.Combine(dir, "b.jpg");
            using (var bitmap = new SKBitmap(64, 48)) using (var image = SKImage.FromBitmap(bitmap))
            using (var pngData = image.Encode(SKEncodedImageFormat.Png, 100)) using (var jpgData = image.Encode(SKEncodedImageFormat.Jpeg, 90))
            { File.WriteAllBytes(png, pngData.ToArray()); File.WriteAllBytes(jpg, jpgData.ToArray()); }
            var gif = Path.Combine(dir, "c.gif"); var mp4 = Path.Combine(dir, "d.mp4");
            foreach (var (path, extra) in new[] { (gif, new[] { "-f", "gif" }), (mp4, new[] { "-c:v", "mpeg4", "-pix_fmt", "yuv420p" }) })
            {
                var start = new ProcessStartInfo(location.Ffmpeg) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
                foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc=size=64x48:rate=10", "-t", "1" }.Concat(extra).Concat(["-y", path])) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!; var error = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, error);
            }
            var transform = new TransformSettings { Scale = .8, Rotation = 25, FlipHorizontal = true };
            var items = new[] { png, jpg, gif, mp4 }.Select(path => new StickerItem { SourcePath = path, Transform = transform }).ToArray();
            var result = await new StickerPackExporter(service).ExportAsync(items, dir, "pack");
            Assert.True(result.Errors.Count == 0, string.Join("; ", result.Errors)); Assert.Equal(4, result.Exported);
            var validator = new TelegramStickerValidator(service);
            foreach (var name in new[] { "001.webp", "002.webp" }) validator.ValidateStatic(Path.Combine(result.Directory, name));
            foreach (var name in new[] { "003.webm", "004.webm" })
            {
                var path = Path.Combine(result.Directory, name); await validator.ValidateVideoAsync(path, true);
                var start = new ProcessStartInfo(location.Ffmpeg) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                foreach (var arg in new[] { "-v", "error", "-c:v", "libvpx-vp9", "-i", path, "-frames:v", "1", "-f", "image2pipe", "-vcodec", "png", "pipe:1" }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!; using var output = new MemoryStream(); var error = process.StandardError.ReadToEndAsync();
                await process.StandardOutput.BaseStream.CopyToAsync(output); await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, await error);
                using var frame = SKBitmap.Decode(output.ToArray()); Assert.NotNull(frame); Assert.Equal(0, frame.GetPixel(0, 0).Alpha);
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}

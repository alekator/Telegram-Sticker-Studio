using System.Diagnostics;
using SkiaSharp;
using StickerForge.Media;

namespace StickerForge.Tests;

public sealed class TelegramStickerValidatorTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "sf-validation-" + Guid.NewGuid().ToString("N"));
    public TelegramStickerValidatorTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    private string Image(string name, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height); bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Webp, 80);
        var path = Path.Combine(dir, name); File.WriteAllBytes(path, data.ToArray()); return path;
    }

    [Fact]
    public void ValidatesDecodedStaticWebpDimensionsAndSize()
    {
        var validator = new TelegramStickerValidator(new FfmpegService());
        validator.ValidateStatic(Image("ok.webp", 512, 256));
        Assert.Throws<InvalidDataException>(() => validator.ValidateStatic(Image("wrong.webp", 256, 256)));
        var corrupt = Path.Combine(dir, "bad.webp"); File.WriteAllText(corrupt, "bad");
        Assert.Throws<InvalidDataException>(() => validator.ValidateStatic(corrupt));
        var huge = Path.Combine(dir, "huge.webp"); File.WriteAllBytes(huge, new byte[512 * 1024 + 1]);
        Assert.Throws<InvalidDataException>(() => validator.ValidateStatic(huge));
    }

    private async Task<string> MakeVideo(string name, int size = 512, int fps = 10, double duration = 1, string codec = "libvpx-vp9", bool audio = false)
    {
        var path = Path.Combine(dir, name); var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        var args = new List<string> { "-v", "error", "-f", "lavfi", "-i", $"color=red:s={size}x{size}:r={fps}" };
        if (audio) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=440", "-c:a", "libopus"]);
        args.AddRange(["-t", duration.ToString(System.Globalization.CultureInfo.InvariantCulture), "-c:v", codec, "-pix_fmt", "yuv420p", "-y", path]);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var errors = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, errors); return path;
    }

    [FfmpegIntegrationFact]
    public async Task ValidatesVideoCodecDimensionsDurationFpsAudioAndAlpha()
    {
        var validator = new TelegramStickerValidator(new FfmpegService());
        await validator.ValidateVideoAsync(await MakeVideo("ok.webm"), false);
        await Assert.ThrowsAsync<InvalidDataException>(() => validator.ValidateVideoAsync(Path.Combine(dir, "ok.webm"), true));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await validator.ValidateVideoAsync(await MakeVideo("size.webm", size: 256), false));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await validator.ValidateVideoAsync(await MakeVideo("duration.webm", duration: 3.2), false));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await validator.ValidateVideoAsync(await MakeVideo("fps.webm", fps: 31), false));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await validator.ValidateVideoAsync(await MakeVideo("audio.webm", audio: true), false));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await validator.ValidateVideoAsync(await MakeVideo("vp8.webm", codec: "libvpx"), false));
        var oversized = Path.Combine(dir, "huge.webm"); File.WriteAllBytes(oversized, new byte[256 * 1024 + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => validator.ValidateVideoAsync(oversized, false));
        var damaged = Path.Combine(dir, "damaged.webm"); File.WriteAllText(damaged, "bad");
        await Assert.ThrowsAsync<InvalidDataException>(() => validator.ValidateVideoAsync(damaged, false));
        await Assert.ThrowsAnyAsync<Exception>(() => new TelegramStickerValidator(new FfmpegService("missing-ffmpeg", "missing-ffprobe")).ValidateVideoAsync(Path.Combine(dir, "ok.webm"), false));
    }
}

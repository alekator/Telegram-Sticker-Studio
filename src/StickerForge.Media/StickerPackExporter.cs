using SkiaSharp;
using StickerForge.Core;
using System.Globalization;
using System.Text.Json;

namespace StickerForge.Media;

public sealed record ExportProgress(int Completed, int Total, string FileName);
public sealed record ExportReport(string Directory, int Exported, IReadOnlyList<string> Errors, bool Cancelled, IReadOnlyList<string> Successes);

/// <summary>Creates a new folder of Telegram static WebP stickers without modifying source files.</summary>
public sealed class StickerPackExporter
{
    private readonly FfmpegService ffmpeg;
    public StickerPackExporter(FfmpegService? ffmpeg = null) => this.ffmpeg = ffmpeg ?? FfmpegLocator.CreateDefault();
    public const int MaxFileBytes = 512 * 1024;
    private const int Size = 512;
    private const long MaxPixels = 40_000_000;

    public Task<ExportReport> ExportAsync(IReadOnlyList<string> sources, string parentDirectory, string packName,
        IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default) =>
        ExportAsync(sources.Select(s => new StickerItem { SourcePath = s }).ToArray(), parentDirectory, packName, progress, cancellationToken);

    public Task<ExportReport> ExportAsync(IReadOnlyList<StickerItem> items, string parentDirectory, string packName,
        IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Export(items, parentDirectory, packName, progress, cancellationToken), CancellationToken.None);

    private ExportReport Export(IReadOnlyList<StickerItem> sources, string parentDirectory, string packName,
        IProgress<ExportProgress>? progress, CancellationToken token)
    {
        ExportDiagnostics.Write($"export start count={sources.Count} output={ExportDiagnostics.PathId(parentDirectory)}");
        if (sources.Count == 0) throw new ArgumentException("Add at least one image before exporting.");
        if (!System.IO.Directory.Exists(parentDirectory)) throw new DirectoryNotFoundException("The selected output folder does not exist.");
        token.ThrowIfCancellationRequested();
        var safeName = SafeFolderName(packName);
        string target = Path.Combine(parentDirectory, safeName);
        for (var suffix = 2; System.IO.Directory.Exists(target) || File.Exists(target); suffix++) target = Path.Combine(parentDirectory, $"{safeName} ({suffix})");
        System.IO.Directory.CreateDirectory(target);
        var errors = new List<string>(); var successes = new List<string>(); var exported = 0; var cancelled = false;
        for (var i = 0; i < sources.Count; i++)
        {
            var item = sources[i]; var source = item.SourcePath;
            try
            {
                ExportDiagnostics.Write($"item {i + 1} input={ExportDiagnostics.PathId(source)} fit={item.Transform.Fit} scale={item.Transform.Scale} rotation={item.Transform.Rotation} x={item.Transform.OffsetX} y={item.Transform.OffsetY} flipX={item.Transform.FlipHorizontal} flipY={item.Transform.FlipVertical} start={item.Transform.StartSeconds} duration={item.Transform.DurationSeconds}");
                token.ThrowIfCancellationRequested();
                if (new FileInfo(source).Length > 100 * 1024 * 1024) throw new InvalidDataException("File exceeds 100 MB.");
                var tiff = ImageImporter.IsTiff(source);
                var animated = !tiff && IsAnimated(source);
                var bytes = animated ? ConvertAnimation(source, item.Transform, target, token) : tiff
                    ? ConvertImage(ffmpeg.ReadFrameAsync(source, 0, token).GetAwaiter().GetResult(), item.Transform, token)
                    : ConvertImage(File.ReadAllBytes(source), item.Transform, token);
                if (!animated)
                {
                    using var data = new SKMemoryStream(bytes);
                    using var encoded = SKCodec.Create(data);
                    if (encoded is null || encoded.EncodedFormat != SKEncodedImageFormat.Webp || encoded.Info.Width != Size || encoded.Info.Height != Size || bytes.Length > MaxFileBytes)
                        throw new InvalidDataException("Output verification failed.");
                }
                token.ThrowIfCancellationRequested();
                var destination = Path.Combine(target, $"{i + 1:000}.{(animated ? "webm" : "webp")}");
                var temporary = Path.Combine(target, $".{Guid.NewGuid():N}.tmp");
                try
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) output.Write(bytes);
                    token.ThrowIfCancellationRequested();
                    if (bytes.Length == 0 || !System.Security.Cryptography.SHA256.HashData(bytes).SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(temporary))))
                        throw new InvalidDataException("Output verification failed.");
                    File.Move(temporary, destination, false); exported++;
                    successes.Add($"{Path.GetFileName(source)} → {Path.GetFileName(destination)}");
                    ExportDiagnostics.Write($"item {i + 1} complete output={ExportDiagnostics.PathId(destination)} bytes={bytes.Length}");
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (OperationCanceledException) { cancelled = true; break; }
            catch (Exception ex)
            { ExportDiagnostics.Write($"item {i + 1} failed: {ex.ToString().Replace(source, ExportDiagnostics.PathId(source), StringComparison.OrdinalIgnoreCase).Replace(target, ExportDiagnostics.PathId(target), StringComparison.OrdinalIgnoreCase)}"); errors.Add($"{Path.GetFileName(source)}: {ex.Message}"); }
            progress?.Report(new ExportProgress(i + 1, sources.Count, Path.GetFileName(source)));
        }
        ExportDiagnostics.Write($"export complete success={exported} errors={errors.Count} cancelled={cancelled}");
        return new ExportReport(target, exported, errors, cancelled, successes);
    }

    private byte[] ConvertAnimation(string path, TransformSettings transform, string directory, CancellationToken token)
    {
        using var input = File.OpenRead(path);
        using var codec = SKCodec.Create(input);
        if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Webp || codec.FrameCount <= 1)
            return ConvertVideo(path, transform, directory, token);
        if (codec.FrameCount > 1000) throw new InvalidDataException("Animation exceeds the frame limit.");
        var work = Path.Combine(directory, ".frames-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var manifest = Path.Combine(work, "frames.txt");
            var lines = new List<string> { "ffconcat version 1.0" };
            var durations = codec.FrameInfo.Select(f => Math.Max(20, f.Duration)).ToArray();
            var startMs = Math.Clamp(transform.StartSeconds * 1000, 0, Math.Max(0, durations.Sum() - 20));
            var startIndex = 0; var frameStart = 0;
            while (startIndex < durations.Length - 1 && frameStart + durations[startIndex] <= startMs) frameStart += durations[startIndex++];
            var stopMs = startMs + Math.Clamp(transform.DurationSeconds, .05, 3) * 1000;
            var endIndex = startIndex; var endTime = frameStart;
            while (endIndex < durations.Length && endTime < stopMs) endTime += durations[endIndex++];
            for (var i = startIndex; i < endIndex; i++)
            {
                token.ThrowIfCancellationRequested();
                var frameName = $"{i:0000}.png";
                File.WriteAllBytes(Path.Combine(work, frameName), GifFrameReader.ReadFrame(path, i, token));
                lines.Add($"file '{frameName}'");
                lines.Add("duration " + (durations[i] / 1000d).ToString("0.########", CultureInfo.InvariantCulture));
            }
            lines.Add($"file '{endIndex - 1:0000}.png'");
            File.WriteAllLines(manifest, lines);
            var intermediate = Path.Combine(work, "animation.png");
            ffmpeg.RunEncoderAsync(["-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", manifest,
                "-t", ((endTime - frameStart) / 1000d).ToString("0.########", CultureInfo.InvariantCulture), "-plays", "1", "-f", "apng", intermediate], token).GetAwaiter().GetResult();
            var settings = new TransformSettings { Fit = transform.Fit, Scale = transform.Scale, OffsetX = transform.OffsetX, OffsetY = transform.OffsetY,
                Rotation = transform.Rotation, FlipHorizontal = transform.FlipHorizontal, FlipVertical = transform.FlipVertical,
                StartSeconds = (startMs - frameStart) / 1000, DurationSeconds = transform.DurationSeconds };
            return ConvertVideo(intermediate, settings, directory, token);
        }
        finally { Directory.Delete(work, true); }
    }

    private static bool IsAnimated(string path)
    {
        if (AnimatedPng.Duration(path) is not null) return true;
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        return codec is null || codec.FrameCount > 1;
    }

    private byte[] ConvertVideo(string path, TransformSettings transform, string directory, CancellationToken token)
    {
        var metadata = ffmpeg.ProbeAsync(path, token, exportDiagnostics: true).GetAwaiter().GetResult();
        var start = Math.Clamp(transform.StartSeconds, 0, Math.Max(0, metadata.DurationSeconds - .05));
        var duration = Math.Min(Math.Clamp(transform.DurationSeconds, .05, 3), metadata.DurationSeconds - start);
        var filter = BuildVideoFilter(metadata, transform, duration);
        static string N(double n) => n.ToString("0.########", CultureInfo.InvariantCulture);
        var temp = Path.Combine(directory, $".{Guid.NewGuid():N}.webm");
        try
        {
            foreach (var crf in new[] { 20, 28, 36, 44, 52, 60, 63 })
            {
                token.ThrowIfCancellationRequested();
                ffmpeg.RunEncoderAsync(["-v", "error", "-y", "-protocol_whitelist", "file,pipe", "-ss", N(start), "-i", Path.GetFullPath(path), "-t", N(duration), "-filter_complex", filter, "-an", "-c:v", "libvpx-vp9", "-pix_fmt", "yuva420p", "-auto-alt-ref", "0", "-b:v", "0", "-crf", crf.ToString(CultureInfo.InvariantCulture), temp], token).GetAwaiter().GetResult();
                var fileSize = new FileInfo(temp).Length;
                ExportDiagnostics.Write($"video encoded sourceDuration={N(metadata.DurationSeconds)} requestedStart={N(transform.StartSeconds)} requestedDuration={N(transform.DurationSeconds)} start={N(start)} duration={N(duration)} sourceFps={N(metadata.FramesPerSecond ?? 30)} outputFps={N(OutputFrameRate(metadata))} crf={crf} bytes={fileSize} maxBytes={256 * 1024} maxDuration=3");
                if (fileSize > 256 * 1024) continue;
                var probe = ffmpeg.RunProbeAsync(["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,codec_name,width,height,avg_frame_rate,r_frame_rate,nb_read_frames,start_time:format=duration,start_time,size", "-of", "json", temp], token).GetAwaiter().GetResult();
                using var json = JsonDocument.Parse(probe);
                var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                var video = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
                var actualRate = video.GetProperty("avg_frame_rate").GetString() ?? "0/0";
                var rateParts = actualRate.Split('/');
                var fpsValid = rateParts.Length == 2 && double.TryParse(rateParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
                    && double.TryParse(rateParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) && denominator > 0
                    && double.IsFinite(numerator / denominator) && numerator / denominator is > 0 and <= 30;
                var frameCount = video.GetProperty("nb_read_frames").GetString();
                var format = json.RootElement.GetProperty("format");
                ExportDiagnostics.Write($"video validation duration={format.GetProperty("duration").GetString()} startTime={(format.TryGetProperty("start_time", out var time) ? time.GetString() : "unknown")} frames={frameCount} fps={actualRate.Replace('/', ':')} bytes={fileSize} maxDuration=3 maxBytes={256 * 1024} maxFps=30 fpsValid={fpsValid}");
                if (streams.Any(s => s.GetProperty("codec_type").GetString() == "audio") || video.GetProperty("codec_name").GetString() != "vp9" || video.GetProperty("width").GetInt32() != 512 || video.GetProperty("height").GetInt32() != 512) throw new InvalidDataException("Encoded video failed format validation.");
                if (!fpsValid || !long.TryParse(frameCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames) || frames <= 0) throw new InvalidDataException("Encoded video frame rate or frame count is invalid.");
                var seconds = double.Parse(format.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
                if (!double.IsFinite(seconds) || seconds > 3 || seconds <= 0) throw new InvalidDataException($"Encoded video duration is invalid: {N(seconds)} seconds (maximum 3).");
                return File.ReadAllBytes(temp);
            }
            throw new InvalidDataException("Cannot fit this animation into 256 KiB. Choose a shorter or simpler fragment.");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static string BuildVideoFilter(VideoMetadata metadata, TransformSettings transform, double duration)
    {
        var fps = OutputFrameRate(metadata);
        var sx = 512d / metadata.Width; var sy = 512d / metadata.Height;
        if (transform.Fit != FitMode.Stretch) sx = sy = transform.Fit == FitMode.Fill ? Math.Max(sx, sy) : Math.Min(sx, sy);
        var scale = Math.Clamp(transform.Scale, .1, 10);
        var width = Math.Clamp((int)Math.Round(metadata.Width * sx * scale), 1, 5120);
        var height = Math.Clamp((int)Math.Round(metadata.Height * sy * scale), 1, 5120);
        static string N(double n) => n.ToString("0.########", CultureInfo.InvariantCulture);
        var flips = (transform.FlipHorizontal ? "hflip," : "") + (transform.FlipVertical ? "vflip," : "");
        return $"[0:v]fps={N(fps)},format=rgba,{flips}scale={width}:{height},rotate={N(transform.Rotation * Math.PI / 180)}:ow=rotw(iw):oh=roth(ih):c=none[fg];color=c=black@0.0:s=512x512:r={N(fps)}:d={N(duration)},format=rgba[bg];[bg][fg]overlay=(W-w)/2+{N(transform.OffsetX)}:(H-h)/2+{N(transform.OffsetY)}:shortest=1:format=auto,format=yuva420p";
    }

    internal static double OutputFrameRate(VideoMetadata metadata) => Math.Ceiling(Math.Clamp(metadata.FramesPerSecond ?? 30, 1, 30));

    private static byte[] ConvertImage(byte[] bytes, TransformSettings transform, CancellationToken token)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        if (stream.Length > 100 * 1024 * 1024) throw new InvalidDataException("File exceeds 100 MB.");
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Unsupported or damaged image.");
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp or SKEncodedImageFormat.Bmp or SKEncodedImageFormat.Gif) || codec.FrameCount > 1)
            throw new InvalidDataException("Unsupported or animated image.");
        var info = codec.Info;
        if (info.Width < 1 || info.Height < 1 || (long)info.Width * info.Height > MaxPixels) throw new InvalidDataException("Image dimensions exceed the limit.");
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException("Image decoding failed.");
        token.ThrowIfCancellationRequested();
        using var image = SKImage.FromBitmap(bitmap);
        using var result = Render(image, transform);
        foreach (var quality in new[] { 100, 90, 80, 70, 60, 50, 40, 30, 20, 10, 0 })
        {
            token.ThrowIfCancellationRequested();
            using var encoded = result.Encode(SKEncodedImageFormat.Webp, quality) ?? throw new InvalidDataException("WebP encoding is unavailable.");
            if (encoded.Size <= MaxFileBytes) return encoded.ToArray();
        }
        throw new InvalidDataException("Cannot fit this image into Telegram's 512 KiB limit without exceeding the format constraints.");
    }

    public static SKImage Render(SKImage image, TransformSettings transform)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul)) ?? throw new InvalidDataException("Unable to create image canvas.");
        var canvas = surface.Canvas; canvas.Clear(SKColors.Transparent);
        var sx = (float)Size / image.Width; var sy = (float)Size / image.Height;
        if (transform.Fit != FitMode.Stretch) sx = sy = transform.Fit == FitMode.Fill ? Math.Max(sx, sy) : Math.Min(sx, sy);
        var scale = (float)Math.Clamp(double.IsFinite(transform.Scale) ? transform.Scale : 1, 0.1, 10);
        canvas.Translate(Size / 2f + (float)transform.OffsetX, Size / 2f + (float)transform.OffsetY);
        canvas.RotateDegrees((float)transform.Rotation);
        canvas.Scale(sx * scale * (transform.FlipHorizontal ? -1 : 1), sy * scale * (transform.FlipVertical ? -1 : 1));
        canvas.DrawImage(image, SKRect.Create(-image.Width / 2f, -image.Height / 2f, image.Width, image.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return surface.Snapshot();
    }

    private static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((name ?? "").Select(c => invalid.Contains(c) || c is '/' or '\\' || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (safe is "" or "." or "..") safe = "Stickers";
        if (safe.Length > 80) safe = safe[..80].TrimEnd(' ', '.');
        var stem = safe.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase)) safe = "_" + safe;
        return safe;
    }
}

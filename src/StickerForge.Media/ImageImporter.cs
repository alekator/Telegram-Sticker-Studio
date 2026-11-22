using SkiaSharp;
using StickerForge.Core;

namespace StickerForge.Media;

public sealed record ImageInfo(string Format, int Width, int Height, long FileSize, byte[] Thumbnail, int[]? FrameDurations = null, VideoMetadata? Video = null);
public sealed record ImportResult(string Path, StickerItem? Item, ImageInfo? Info, string? Error);
public interface IImageDecoder
{
    ImageInfo Decode(string path, CancellationToken cancellationToken);
}

public sealed class SkiaImageDecoder : IImageDecoder
{
    public ImageInfo Decode(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        if (file.Length > 100 * 1024 * 1024) throw new InvalidDataException("File exceeds 100 MB.");
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Unsupported or corrupt image.");
        var format = codec.EncodedFormat;
        if (format is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp or SKEncodedImageFormat.Bmp or SKEncodedImageFormat.Gif))
            throw new InvalidDataException("Unsupported image. Video support is not yet available.");
        if (format != SKEncodedImageFormat.Gif && codec.FrameCount > 1) throw new InvalidDataException("Animated images other than GIF are not yet supported.");
        if (codec.FrameCount > 1000) throw new InvalidDataException("GIF exceeds the frame limit.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > 40_000_000)
            throw new InvalidDataException("Image dimensions exceed the limit.");
        using var bitmap = new SKBitmap(info.Width, info.Height);
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result != SKCodecResult.Success) throw new InvalidDataException($"Image decoding failed: {result}.");
        cancellationToken.ThrowIfCancellationRequested();
        var scale = Math.Min(1d, 192d / Math.Max(info.Width, info.Height));
        using var resized = bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(info.Width * scale)), Math.Max(1, (int)(info.Height * scale))), SKSamplingOptions.Default)
            ?? throw new InvalidDataException("Unable to create thumbnail.");
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var durations = format == SKEncodedImageFormat.Gif ? codec.FrameInfo.Select(f => f.Duration).ToArray() : null;
        return new ImageInfo(format.ToString(), info.Width, info.Height, file.Length, data.ToArray(), durations);
    }
}

public sealed class ImageImporter(IImageDecoder decoder, FfmpegService? ffmpeg = null)
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".webm", ".avi" };
    public async Task<IReadOnlyList<ImportResult>> ImportAsync(IEnumerable<string> paths, IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var files = paths.ToArray();
        var results = new List<ImportResult>();
        for (var i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[i];
            try
            {
                ImageInfo info;
                if (VideoExtensions.Contains(Path.GetExtension(path)))
                {
                    if (ffmpeg is null) throw new InvalidDataException("FFmpeg and FFprobe are unavailable; configure them to import video.");
                    var availability = await ffmpeg.CheckAsync(cancellationToken);
                    if (!availability.Available) throw new InvalidDataException("FFmpeg and FFprobe are unavailable; install or configure them to import video. " + availability.Message);
                    var metadata = await ffmpeg.ProbeAsync(path, cancellationToken);
                    var frame = await ffmpeg.ReadFrameAsync(path, 0, cancellationToken);
                    if (frame.Length == 0) throw new InvalidDataException("No video frame could be decoded.");
                    info = new ImageInfo("Video", metadata.Width, metadata.Height, metadata.FileSize, frame, Video: metadata);
                }
                else info = await Task.Run(() => decoder.Decode(path, cancellationToken), cancellationToken);
                results.Add(new ImportResult(path, new StickerItem { SourcePath = Path.GetFullPath(path) }, info, null));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or KeyNotFoundException)
            {
                results.Add(new ImportResult(path, null, null, e.Message));
            }
            progress?.Report((i + 1, files.Length));
        }
        return results;
    }
}

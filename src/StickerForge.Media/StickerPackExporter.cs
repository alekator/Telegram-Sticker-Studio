using SkiaSharp;

namespace StickerForge.Media;

public sealed record ExportProgress(int Completed, int Total, string FileName);
public sealed record ExportReport(string Directory, int Exported, IReadOnlyList<string> Errors, bool Cancelled);

/// <summary>Creates a new folder of Telegram static WebP stickers without modifying source files.</summary>
public sealed class StickerPackExporter
{
    public const int MaxFileBytes = 512 * 1024;
    private const int Size = 512;
    private const long MaxPixels = 40_000_000;

    public Task<ExportReport> ExportAsync(IReadOnlyList<string> sources, string parentDirectory, string packName,
        IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Export(sources, parentDirectory, packName, progress, cancellationToken), CancellationToken.None);

    private static ExportReport Export(IReadOnlyList<string> sources, string parentDirectory, string packName,
        IProgress<ExportProgress>? progress, CancellationToken token)
    {
        if (sources.Count == 0) throw new ArgumentException("Add at least one image before exporting.");
        if (!System.IO.Directory.Exists(parentDirectory)) throw new DirectoryNotFoundException("The selected output folder does not exist.");
        token.ThrowIfCancellationRequested();
        var safeName = SafeFolderName(packName);
        string target = Path.Combine(parentDirectory, safeName);
        for (var suffix = 2; System.IO.Directory.Exists(target) || File.Exists(target); suffix++) target = Path.Combine(parentDirectory, $"{safeName} ({suffix})");
        System.IO.Directory.CreateDirectory(target);
        var errors = new List<string>(); var exported = 0; var cancelled = false;
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            try
            {
                token.ThrowIfCancellationRequested();
                var bytes = ConvertImage(source, token);
                token.ThrowIfCancellationRequested();
                var destination = Path.Combine(target, $"{i + 1:000}.webp");
                var temporary = Path.Combine(target, $".{Guid.NewGuid():N}.tmp");
                try
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) output.Write(bytes);
                    token.ThrowIfCancellationRequested();
                    File.Move(temporary, destination, false); exported++;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (OperationCanceledException) { cancelled = true; break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            { errors.Add($"{Path.GetFileName(source)}: {ex.Message}"); }
            progress?.Report(new ExportProgress(i + 1, sources.Count, Path.GetFileName(source)));
        }
        return new ExportReport(target, exported, errors, cancelled);
    }

    private static byte[] ConvertImage(string path, CancellationToken token)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 100 * 1024 * 1024) throw new InvalidDataException("File exceeds 100 MB.");
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Unsupported or damaged image.");
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp) || codec.FrameCount > 1)
            throw new InvalidDataException("Only static PNG, JPEG and WebP images can be exported.");
        var info = codec.Info;
        if (info.Width < 1 || info.Height < 1 || (long)info.Width * info.Height > MaxPixels) throw new InvalidDataException("Image dimensions exceed the limit.");
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException("Image decoding failed.");
        token.ThrowIfCancellationRequested();
        using var surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul)) ?? throw new InvalidDataException("Unable to create image canvas.");
        var canvas = surface.Canvas; canvas.Clear(SKColors.Transparent);
        var scale = Math.Min((float)Size / info.Width, (float)Size / info.Height);
        var w = info.Width * scale; var h = info.Height * scale;
        var destination = SKRect.Create((Size - w) / 2, (Size - h) / 2, w, h);
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, destination, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var result = surface.Snapshot();
        foreach (var quality in new[] { 100, 90, 80, 70, 60, 50, 40, 30, 20, 10, 0 })
        {
            token.ThrowIfCancellationRequested();
            using var encoded = result.Encode(SKEncodedImageFormat.Webp, quality) ?? throw new InvalidDataException("WebP encoding is unavailable.");
            if (encoded.Size <= MaxFileBytes) return encoded.ToArray();
        }
        throw new InvalidDataException("Cannot fit this image into Telegram's 512 KiB limit without exceeding the format constraints.");
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

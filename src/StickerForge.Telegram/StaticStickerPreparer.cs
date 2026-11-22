using SkiaSharp;

namespace StickerForge.Telegram;

/// <summary>Produces a separate PNG, leaving the source untouched.</summary>
public sealed class StaticStickerPreparer
{
    public const int MaximumBytes = 512 * 1024;
    private const long MaximumPixels = 64_000_000;

    public Task<byte[]> PrepareAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Prepare(path, cancellationToken), cancellationToken);

    private static byte[] Prepare(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("The image cannot be decoded.");
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            throw new InvalidDataException("Only static PNG and WebP images are supported.");
        if (codec.FrameCount > 1) throw new InvalidDataException("Animated images require the video sticker pipeline.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaximumPixels)
            throw new InvalidDataException("Image dimensions exceed the supported limit.");
        using var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("The image is damaged.");
        cancellationToken.ThrowIfCancellationRequested();
        double scale = 512.0 / Math.Max(bitmap.Width, bitmap.Height);
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        using var resized = bitmap.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), SKSamplingOptions.Default)
            ?? throw new InvalidDataException("Unable to resize the image.");
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidDataException("Unable to encode PNG.");
        if (encoded.Size > MaximumBytes) throw new InvalidDataException("The prepared PNG exceeds Telegram's 512 KB limit.");
        cancellationToken.ThrowIfCancellationRequested();
        return encoded.ToArray();
    }
}

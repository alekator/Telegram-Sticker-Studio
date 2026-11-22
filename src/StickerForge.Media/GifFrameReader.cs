using SkiaSharp;

namespace StickerForge.Media;

public static class GifFrameReader
{
    public static byte[] ReadFrame(string path, int index, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Invalid GIF.");
        if (codec.EncodedFormat != SKEncodedImageFormat.Gif || index < 0 || index >= codec.FrameCount || codec.FrameCount > 1000 ||
            codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 40_000_000)
            throw new InvalidDataException("Invalid GIF frame or dimensions.");
        using var bitmap = new SKBitmap(codec.Info.Width, codec.Info.Height);
        bitmap.Erase(SKColors.Transparent);
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels(), new SKCodecOptions(index, -1));
        if (result != SKCodecResult.Success) throw new InvalidDataException($"GIF frame decoding failed: {result}.");
        cancellationToken.ThrowIfCancellationRequested();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

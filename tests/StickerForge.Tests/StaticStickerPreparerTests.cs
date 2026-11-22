using SkiaSharp;
using StickerForge.Telegram;

namespace StickerForge.Tests;

public sealed class StaticStickerPreparerTests
{
    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task PreservesSourceProportionsAndTransparency(SKEncodedImageFormat format)
    {
        var path = Path.GetTempFileName();
        try
        {
            using var bitmap = new SKBitmap(20, 10); bitmap.Erase(SKColors.Transparent); bitmap.SetPixel(10, 5, SKColors.Red);
            using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(format, 100);
            var source = encoded.ToArray(); await File.WriteAllBytesAsync(path, source);
            var result = await new StaticStickerPreparer().PrepareAsync(path);
            using var output = SKBitmap.Decode(result);
            Assert.Equal(512, output.Width); Assert.Equal(256, output.Height);
            Assert.Equal(0, output.GetPixel(0, 0).Alpha);
            Assert.Equal(source, await File.ReadAllBytesAsync(path));
            Assert.InRange(result.Length, 1, StaticStickerPreparer.MaximumBytes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RejectsDamagedAndCancelledInput()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "broken");
            await Assert.ThrowsAsync<InvalidDataException>(() => new StaticStickerPreparer().PrepareAsync(path));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new StaticStickerPreparer().PrepareAsync(path, cancelled.Token));
        }
        finally { File.Delete(path); }
    }
}

using SkiaSharp;
using StickerForge.Media;

namespace StickerForge.Tests;

public sealed class GifTests
{
    // Synthetic 2x2 red/blue GIF, generated for this test, 100 ms then 250 ms.
    private const string Gif = "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQBGQABACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7";

    [Fact]
    public async Task ImportsGifAndDecodesFramesWithTheirTiming()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        try
        {
            await File.WriteAllBytesAsync(path, Convert.FromBase64String(Gif));
            var result = (await new ImageImporter(new SkiaImageDecoder()).ImportAsync([path]))[0];
            Assert.NotNull(result.Info);
            Assert.Equal(new[] { 100, 250 }, result.Info!.FrameDurations);
            using var first = SKBitmap.Decode(GifFrameReader.ReadFrame(path, 0));
            using var second = SKBitmap.Decode(GifFrameReader.ReadFrame(path, 1));
            Assert.Equal(SKColors.Red, first.GetPixel(0, 0));
            Assert.Equal(SKColors.Blue, second.GetPixel(0, 0));
        }
        finally { File.Delete(path); }
    }
}

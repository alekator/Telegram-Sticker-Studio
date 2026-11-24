using SkiaSharp;
using StickerForge.Media;
using StickerForge.Core;

namespace StickerForge.Tests;

public sealed class StickerPackExporterTests : IDisposable
{
    [Theory]
    [InlineData("with-text.png", SKEncodedImageFormat.Png)]
    [InlineData("with-text.jpg", SKEncodedImageFormat.Jpeg)]
    public async Task StaticTextLayersUseTheSameCompositionForPreviewAndExport(string filename, SKEncodedImageFormat format)
    {
        var path = Image(filename, format);
        var transform = new TransformSettings { Rotation = -35, FlipHorizontal = true, Scale = .8 };
        var layers = new List<TextLayer> { new() { Text = "Привет\nWorld", X = 210, Y = 80, FontSize = 50, Color = "#FFFFFFFF" }, new() { Text = "hidden", IsVisible = false } };
        using var image = SKImage.FromEncodedData(path); using var preview = StickerPackExporter.Render(image, transform, layers); using var expected = SKBitmap.FromImage(preview);
        var report = await new StickerPackExporter().ExportAsync([new StickerItem { SourcePath = path, Transform = transform, TextLayers = layers }], directory, "text");
        Assert.True(report.Errors.Count == 0, string.Join("; ", report.Errors));
        using var actual = SKBitmap.Decode(Path.Combine(report.Directory, "001.webp"));
        var opaque = 0;
        for (var y = 0; y < 150; y++) for (var x = 0; x < 512; x++)
        {
            var a = actual.GetPixel(x, y); var e = expected.GetPixel(x, y);
            Assert.InRange(Math.Abs(a.Alpha - e.Alpha), 0, 2);
            if (e.Alpha > 128) opaque++;
        }
        Assert.True(opaque > 50);
    }
    [Fact]
    public void TextLayerOrderVisibilityAndTransformAffectTheSharedOverlay()
    {
        var first = new TextLayer { Text = "Мир\nHi", X = 180, Y = 180, FontSize = 64, Color = "#FFFF0000", Scale = 1.2, Rotation = 22 };
        var second = new TextLayer { Text = first.Text, X = first.X, Y = first.Y, FontSize = first.FontSize, Color = "#FF0000FF", Scale = first.Scale, Rotation = first.Rotation };
        using var overlay = TextLayerRenderer.RenderOverlay([first, second]); using var bitmap = SKBitmap.FromImage(overlay);
        var bluePixels = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++) { var pixel = bitmap.GetPixel(x, y); if (pixel.Blue > 200 && pixel.Red < 30 && pixel.Alpha > 200) bluePixels++; }
        Assert.True(bluePixels > 20);
        second.IsVisible = false;
        using var hidden = TextLayerRenderer.RenderOverlay([first, second]); using var hiddenBitmap = SKBitmap.FromImage(hidden);
        var redPixels = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++) { var pixel = hiddenBitmap.GetPixel(x, y); if (pixel.Red > 200 && pixel.Blue < 30 && pixel.Alpha > 200) redPixels++; }
        Assert.True(redPixels > 20);
    }
    private sealed class CallbackProgress(Action<ExportProgress> action) : IProgress<ExportProgress> { public void Report(ExportProgress value) => action(value); }
    [Theory]
    [InlineData(false, false, 255, 0)]
    [InlineData(true, false, 0, 255)]
    [InlineData(false, true, 255, 0)]
    [InlineData(true, true, 0, 255)]
    public async Task ReflectionsAreIndependentInPreviewAndExport(bool horizontal, bool vertical, byte expectedRed, byte expectedBlue)
    {
        using var bitmap = new SKBitmap(64, 64);
        for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
            bitmap.SetPixel(x, y, x < 32 ? (y < 32 ? SKColors.Red : SKColors.Yellow) : (y < 32 ? SKColors.Blue : SKColors.Cyan));
        using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var path = Path.Combine(directory, "quadrants.png"); File.WriteAllBytes(path, png.ToArray());
        var settings = new TransformSettings { FlipHorizontal = horizontal, FlipVertical = vertical };
        using var rendered = StickerPackExporter.Render(image, settings); using var preview = SKBitmap.FromImage(rendered);
        var expected = preview.GetPixel(128, 128);
        Assert.Equal(expectedRed, expected.Red); Assert.Equal(expectedBlue, expected.Blue);
        Assert.Equal(vertical ? 255 : 0, expected.Green);
        var report = await new StickerPackExporter().ExportAsync([new StickerItem { SourcePath = path, Transform = settings }], directory, "flips");
        Assert.Empty(report.Errors); using var exported = SKBitmap.Decode(Path.Combine(report.Directory, "001.webp"));
        var actual = exported.GetPixel(128, 128);
        Assert.InRange(Math.Abs(actual.Red - expected.Red), 0, 15); Assert.InRange(Math.Abs(actual.Green - expected.Green), 0, 15); Assert.InRange(Math.Abs(actual.Blue - expected.Blue), 0, 15);
    }
    [Theory]
    [InlineData(FitMode.Fit, 0)]
    [InlineData(FitMode.Manual, 0)]
    [InlineData(FitMode.Fill, 255)]
    [InlineData(FitMode.Stretch, 255)]
    public void FitModesProduceExpectedCanvas(FitMode mode, byte topAlpha)
    {
        using var source = SKImage.FromEncodedData(Image("modes.png", SKEncodedImageFormat.Png));
        using var output = StickerPackExporter.Render(source, new TransformSettings { Fit = mode });
        using var bitmap = SKBitmap.FromImage(output);
        Assert.Equal(topAlpha, bitmap.GetPixel(256, 10).Alpha);
        Assert.Equal(255, bitmap.GetPixel(256, 256).Alpha);
    }
    [Fact]
    public async Task TransformChangesExportAndMatchesSharedRenderer()
    {
        var path = Image("transform.png", SKEncodedImageFormat.Png);
        var transform = new TransformSettings { Fit = FitMode.Fill, Scale = .75, OffsetX = 70, OffsetY = -30, Rotation = 30, FlipHorizontal = true, FlipVertical = true };
        var report = await new StickerPackExporter().ExportAsync([new StickerItem { SourcePath = path, Transform = transform }], directory, "transform");
        Assert.Empty(report.Errors);
        using var original = SKImage.FromEncodedData(path); using var rendered = StickerPackExporter.Render(original, transform);
        using var expected = SKBitmap.FromImage(rendered); using var actual = SKBitmap.Decode(Path.Combine(report.Directory, "001.webp"));
        Assert.Equal(expected.GetPixel(0, 0).Alpha, actual.GetPixel(0, 0).Alpha);
        Assert.Equal(expected.GetPixel(256, 256).Alpha, actual.GetPixel(256, 256).Alpha);
    }
    private readonly string directory = Path.Combine(Path.GetTempPath(), "stickerforge-export-" + Guid.NewGuid().ToString("N"));
    public StickerPackExporterTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private string Image(string name, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(40, 20); bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(format, 100);
        var path = Path.Combine(directory, name); File.WriteAllBytes(path, encoded.ToArray()); return path;
    }

    [Fact]
    public async Task ExportsOrderedImagesWithTransparentPaddingWithoutChangingOriginals()
    {
        var sources = new[] { Image("one.png", SKEncodedImageFormat.Png), Image("two.jpg", SKEncodedImageFormat.Jpeg), Image("three.webp", SKEncodedImageFormat.Webp) };
        var originals = sources.Select(File.ReadAllBytes).ToArray();
        var report = await new StickerPackExporter().ExportAsync(sources, directory, "My pack");
        Assert.Equal(3, report.Exported); Assert.Empty(report.Errors); Assert.False(report.Cancelled);
        for (var i = 0; i < sources.Length; i++)
        {
            var file = Path.Combine(report.Directory, $"{i + 1:000}.webp"); var bytes = await File.ReadAllBytesAsync(file);
            Assert.InRange(bytes.Length, 1, StickerPackExporter.MaxFileBytes);
            using var data = new SKMemoryStream(bytes); using var codec = SKCodec.Create(data);
            Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
            using var output = SKBitmap.Decode(bytes); Assert.Equal(512, output.Width); Assert.Equal(512, output.Height);
            Console.WriteLine($"{Path.GetExtension(sources[i])}: WEBP {output.Width}x{output.Height}, {bytes.Length} bytes");
            Assert.Equal(0, output.GetPixel(256, 0).Alpha); Assert.True(output.GetPixel(256, 256).Red > 200);
            Assert.Equal(originals[i], await File.ReadAllBytesAsync(sources[i]));
        }
        var second = await new StickerPackExporter().ExportAsync(sources, directory, "My pack");
        Assert.NotEqual(report.Directory, second.Directory); Assert.True(File.Exists(Path.Combine(report.Directory, "001.webp")));
    }

    [Fact]
    public async Task IsolatesDamagedFilesAndHonoursCancellation()
    {
        var source = Image("one.png", SKEncodedImageFormat.Png); var missing = Path.Combine(directory, "missing.png");
        var report = await new StickerPackExporter().ExportAsync([missing, source], directory, "..\\pack");
        Assert.Single(report.Errors); Assert.Equal(0, report.Exported); Assert.Equal("", report.Directory);
        Assert.Empty(Directory.GetDirectories(directory));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new StickerPackExporter().ExportAsync([source], directory, "cancelled", cancellationToken: cancelled.Token));
        Assert.False(Directory.Exists(Path.Combine(directory, "cancelled")));
    }

    [Fact]
    public async Task CorruptInputMustNotAbortBatch()
    {
        var bad = Path.Combine(directory, "corrupt.png"); await File.WriteAllTextAsync(bad, "not an image");
        var good = Image("good.png", SKEncodedImageFormat.Png);
        var report = await new StickerPackExporter().ExportAsync([bad, good], directory, "partial");
        Assert.Single(report.Errors); Assert.Equal(0, report.Exported);
        Assert.Equal("", report.Directory); Assert.Empty(report.Successes); Assert.Empty(Directory.GetDirectories(directory));
        var retry = await new StickerPackExporter().ExportAsync([good], directory, "partial");
        Assert.Empty(retry.Errors); Assert.Equal(1, retry.Exported);
    }

    [Fact]
    public async Task UnavailableOutputFolderIsReported()
    {
        var source = Image("folder.png", SKEncodedImageFormat.Png);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => new StickerPackExporter().ExportAsync([source], Path.Combine(directory, "absent"), "pack"));
    }

    [Fact]
    public async Task AtomicBatchPreservesPreviousExportOnErrorAndCancellation()
    {
        var good = Image("first.png", SKEncodedImageFormat.Png); var other = Image("last.jpg", SKEncodedImageFormat.Jpeg);
        var exporter = new StickerPackExporter();
        var previous = await exporter.ExportAsync([good], directory, "saved");
        Assert.Equal(1, previous.Exported); var previousFile = Path.Combine(previous.Directory, "001.webp"); var bytes = await File.ReadAllBytesAsync(previousFile);
        var missing = Path.Combine(directory, "missing.png");
        var failed = await exporter.ExportAsync([good, missing, other], directory, "saved");
        Assert.Equal(0, failed.Exported); Assert.Single(failed.Errors); Assert.Equal("", failed.Directory);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(previousFile)); Assert.Single(Directory.GetDirectories(directory));
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.ExportAsync([good, other], directory, "cancel", new CallbackProgress(_ => cancel.Cancel()), cancel.Token));
        Assert.Single(Directory.GetDirectories(directory)); Assert.Equal(bytes, await File.ReadAllBytesAsync(previousFile));
        var retry = await exporter.ExportAsync([good, other], directory, "saved");
        Assert.Equal(2, retry.Exported); Assert.Empty(retry.Errors); Assert.Equal(bytes, await File.ReadAllBytesAsync(previousFile));
    }

    [Fact]
    public async Task HighEntropyImageIsReducedBelowTelegramLimit()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var random = new Random(9876);
        for (var y = 0; y < 512; y++)
            for (var x = 0; x < 512; x++) bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        using var image = SKImage.FromBitmap(bitmap);
        using var high = image.Encode(SKEncodedImageFormat.Webp, 100);
        Assert.True(high.Size > StickerPackExporter.MaxFileBytes, "The fixture must exercise quality reduction.");
        using var png = image.Encode(SKEncodedImageFormat.Png, 100); var path = Path.Combine(directory, "noise.png"); File.WriteAllBytes(path, png.ToArray());
        var report = await new StickerPackExporter().ExportAsync([path], directory, "Noise");
        Assert.Empty(report.Errors); Assert.Equal(1, report.Exported);
        Assert.InRange(new FileInfo(Path.Combine(report.Directory, "001.webp")).Length, 1, StickerPackExporter.MaxFileBytes);
    }

    [Fact]
    public async Task PreservesTransparentPixelsWithinImage()
    {
        using var bitmap = new SKBitmap(32, 32); bitmap.Erase(SKColors.Transparent);
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++) bitmap.SetPixel(x, y, SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var source = Path.Combine(directory, "alpha.png"); File.WriteAllBytes(source, png.ToArray());
        var report = await new StickerPackExporter().ExportAsync([source], directory, "Alpha");
        Assert.Empty(report.Errors); using var output = SKBitmap.Decode(Path.Combine(report.Directory, "001.webp"));
        Assert.Equal(0, output.GetPixel(400, 400).Alpha); Assert.True(output.GetPixel(100, 100).Blue > 200);
    }
}

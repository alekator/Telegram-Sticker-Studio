using SkiaSharp;
using StickerForge.Media;
using StickerForge.Core;

namespace StickerForge.Tests;

public sealed class StickerPackExporterTests : IDisposable
{
    [Fact]
    public void MetallicGlossAndReliefRespectPixelatedGlyphMask()
    {
        var layer = new TextLayer { Text = "PIXEL", FontSize = 100, Color = "#FF346C89", RasterEffects = new() { PixelationEnabled = true, PixelSize = 10 } };
        using var beforeImage = TextLayerRenderer.RenderOverlay([layer]); using var before = SKBitmap.FromImage(beforeImage);
        layer.RasterEffects.MetallicEnabled = true; layer.RasterEffects.GlossEnabled = true;
        layer.RasterEffects.Emboss = new() { Enabled = true, Depth = 3, Strength = .5 };
        using var afterImage = TextLayerRenderer.RenderOverlay([layer]); using var after = SKBitmap.FromImage(afterImage);
        var changed = 0; var escaped = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var a = before.GetPixel(x, y); var b = after.GetPixel(x, y);
            if (a.Alpha > 200 && Math.Abs(a.Red - b.Red) > 10) changed++;
            if (a.Alpha == 0 && b.Alpha > 0) escaped++;
        }
        Assert.True(changed > 20); Assert.Equal(0, escaped);
    }
    [Fact]
    public void RgbShiftKeepsSemiTransparentTextPremultipliedCorrectly()
    {
        var layer = new TextLayer { Text = "RGB", FontSize = 90, Color = "#8080FF80", RasterEffects = new() { ChromaticEnabled = true, ChromaticOffset = 4 } };
        using var image = TextLayerRenderer.RenderOverlay([layer]); using var bitmap = SKBitmap.FromImage(image);
        var visible = 0; var maxAlpha = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var pixel = bitmap.GetPixel(x, y); maxAlpha = Math.Max(maxAlpha, pixel.Alpha);
            if (pixel.Alpha > 80 && pixel.Green > 150) visible++;
        }
        Assert.InRange(maxAlpha, 120, 128); Assert.True(visible > 20); Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
    }
    [Fact]
    public void CombinedInnerGlowAndReliefReuseMaskWithoutEscapingGlyphs()
    {
        var layer = new TextLayer { Text = "MASK", FontSize = 90, Color = "#FF808080" };
        using var beforeImage = TextLayerRenderer.RenderOverlay([layer]); using var before = SKBitmap.FromImage(beforeImage);
        layer.Glow = new() { InnerEnabled = true, InnerColor = "#FFFF0000", InnerRadius = 4, InnerOpacity = .8 };
        layer.RasterEffects.Emboss = new() { Enabled = true, Strength = .5, Depth = 3 };
        layer.RasterEffects.Inset = new() { Enabled = true, Strength = .3, Depth = 2 };
        using var afterImage = TextLayerRenderer.RenderOverlay([layer]); using var after = SKBitmap.FromImage(afterImage);
        var different = 0; var escaped = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var a = before.GetPixel(x, y); var b = after.GetPixel(x, y);
            if (a.Alpha > 200 && Math.Abs(a.Red - b.Red) > 10) different++;
            if (a.Alpha == 0 && b.Alpha > 0) escaped++;
        }
        Assert.True(different > 20); Assert.Equal(0, escaped);
    }
    [Fact]
    public void BackgroundUsesShapedCurvedGlyphBounds()
    {
        var layer = new TextLayer { Text = "ffi Ж שלום", FontSize = 55, Color = "#FFFF0000", Curvature = 100 };
        using var plainImage = TextLayerRenderer.RenderOverlay([layer]); using var plain = SKBitmap.FromImage(plainImage);
        layer.Background = new() { Enabled = true, Color = "#FF00FF00", PaddingX = 15, PaddingY = 15, Radius = 0 };
        using var backgroundImage = TextLayerRenderer.RenderOverlay([layer]); using var background = SKBitmap.FromImage(backgroundImage);
        var textLeft = 512; var textRight = 0; var textTop = 512; var textBottom = 0;
        var bgLeft = 512; var bgRight = 0; var bgTop = 512; var bgBottom = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            if (plain.GetPixel(x, y).Alpha > 128) { textLeft = Math.Min(textLeft, x); textRight = Math.Max(textRight, x); textTop = Math.Min(textTop, y); textBottom = Math.Max(textBottom, y); }
            var p = background.GetPixel(x, y);
            if (p.Alpha > 200 && p.Green > 200 && p.Red < 30) { bgLeft = Math.Min(bgLeft, x); bgRight = Math.Max(bgRight, x); bgTop = Math.Min(bgTop, y); bgBottom = Math.Max(bgBottom, y); }
        }
        Assert.True(bgLeft <= textLeft - 5); Assert.True(bgRight >= textRight + 5);
        Assert.True(bgTop <= textTop - 5); Assert.True(bgBottom >= textBottom + 5);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReliefChangesTheInteriorOfGlyphs(bool inset)
    {
        var layer = new TextLayer { Text = "RELIEF", FontSize = 80, Color = "#FF808080" };
        using var beforeImage = TextLayerRenderer.RenderOverlay([layer]); using var before = SKBitmap.FromImage(beforeImage);
        var relief = new TextRelief { Enabled = true, Depth = 4, Strength = 1, Angle = 45 };
        if (inset) layer.RasterEffects.Inset = relief; else layer.RasterEffects.Emboss = relief;
        using var afterImage = TextLayerRenderer.RenderOverlay([layer]); using var after = SKBitmap.FromImage(afterImage);
        var changed = 0; var escaped = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var a = before.GetPixel(x, y); var b = after.GetPixel(x, y);
            if (a.Alpha > 200 && Math.Abs(a.Red - b.Red) > 15) changed++;
            if (a.Alpha == 0 && b.Alpha > 0) escaped++;
        }
        Assert.True(changed > 20); Assert.Equal(0, escaped);
    }
    [Fact]
    public void MetallicAndGlossChangeGlyphColorsWithoutFillingTheCanvas()
    {
        var layer = new TextLayer { Text = "METAL", FontSize = 80, Color = "#FF346C89" };
        using var originalImage = TextLayerRenderer.RenderOverlay([layer]); using var original = SKBitmap.FromImage(originalImage);
        layer.RasterEffects = new() { MetallicEnabled = true, MetallicStrength = 1, GlossEnabled = true, GlossStrength = .5 };
        using var styledImage = TextLayerRenderer.RenderOverlay([layer]); using var styled = SKBitmap.FromImage(styledImage);
        var different = 0;
        for (var y = 100; y < 400; y++) for (var x = 0; x < 512; x++)
        {
            var a = original.GetPixel(x, y); var b = styled.GetPixel(x, y);
            if (a.Alpha > 200 && Math.Abs(a.Red - b.Red) > 20) different++;
        }
        Assert.True(different > 50); Assert.Equal(0, styled.GetPixel(0, 0).Alpha);
    }
    [Fact]
    public void RgbShiftCreatesColorFringesAndKeepsTheCanvasTransparent()
    {
        var layer = new TextLayer { Text = "RGB", FontSize = 100, Color = "#FFFFFFFF", RasterEffects = new() { ChromaticEnabled = true, ChromaticOffset = 8 } };
        using var image = TextLayerRenderer.RenderOverlay([layer]); using var bitmap = SKBitmap.FromImage(image);
        var redFringe = 0; var cyanFringe = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var p = bitmap.GetPixel(x, y);
            if (p.Alpha > 100 && p.Red > 180 && p.Green < 80 && p.Blue < 80) redFringe++;
            if (p.Alpha > 100 && p.Red < 80 && p.Green > 180 && p.Blue > 180) cyanFringe++;
        }
        Assert.True(redFringe > 10); Assert.True(cyanFringe > 10); Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
    }
    [Theory]
    [InlineData(-100)]
    [InlineData(100)]
    public void CurvedTextChangesBaselineWithoutChangingOriginalText(double curvature)
    {
        var layer = new TextLayer { Text = "CURVED TEXT", FontSize = 42, Underline = true, Outlines = [new() { Enabled = true, Width = 2, Placement = TextOutlinePlacement.Inside }] };
        using var straightImage = TextLayerRenderer.RenderOverlay([layer]); using var straight = SKBitmap.FromImage(straightImage);
        layer.Curvature = curvature;
        using var curvedImage = TextLayerRenderer.RenderOverlay([layer]); using var curved = SKBitmap.FromImage(curvedImage);
        var different = 0; var visible = 0;
        for (var y = 100; y < 420; y++) for (var x = 0; x < 512; x++)
        {
            var a = straight.GetPixel(x, y); var b = curved.GetPixel(x, y);
            if (b.Alpha > 100) visible++;
            if (Math.Abs(a.Alpha - b.Alpha) > 50) different++;
        }
        Assert.True(visible > 100); Assert.True(different > 100); Assert.Equal("CURVED TEXT", layer.Text);
    }
    [Fact]
    public void CachedLayersRefreshWhenTheirEffectsChange()
    {
        var layer = new TextLayer { Text = "CACHE", Color = "#FFFFFFFF", FontSize = 70 };
        using var beforeImage = TextLayerRenderer.RenderOverlay([layer]); using var before = SKBitmap.FromImage(beforeImage);
        using var cachedImage = TextLayerRenderer.RenderOverlay([layer]); using var cached = SKBitmap.FromImage(cachedImage);
        layer.Color = "#FFFF0000";
        using var changedImage = TextLayerRenderer.RenderOverlay([layer]); using var changed = SKBitmap.FromImage(changedImage);
        var visible = 0; var recolored = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var a = before.GetPixel(x, y); var b = cached.GetPixel(x, y); var c = changed.GetPixel(x, y);
            Assert.Equal(a, b);
            if (a.Alpha > 200) { visible++; if (c.Red > 200 && c.Green < 30 && c.Blue < 30) recolored++; }
        }
        Assert.True(visible > 20); Assert.True(recolored > 20);
    }
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TextBlurAndPixelationAffectGlyphsInTheSharedOverlay(bool blur, bool pixelation)
    {
        var layer = new TextLayer { Text = "PIXELS", FontSize = 80, Color = "#FFFFFFFF", RasterEffects = new() { BlurEnabled = blur, BlurRadius = 3, PixelationEnabled = pixelation, PixelSize = 8 } };
        using var effectedImage = TextLayerRenderer.RenderOverlay([layer]); using var effected = SKBitmap.FromImage(effectedImage);
        layer.RasterEffects = new();
        using var plainImage = TextLayerRenderer.RenderOverlay([layer]); using var plain = SKBitmap.FromImage(plainImage);
        var different = 0; var opaque = 0;
        for (var y = 150; y < 360; y++) for (var x = 0; x < 512; x++)
        {
            var a = effected.GetPixel(x, y); var b = plain.GetPixel(x, y);
            if (a.Alpha > 0) opaque++;
            if (Math.Abs(a.Alpha - b.Alpha) > 20) different++;
        }
        Assert.True(opaque > 50); Assert.True(different > 50);
    }
    [Fact]
    public void InnerGlowColorsGlyphEdgesWithoutAddingAnOuterGlow()
    {
        var layer = new TextLayer { Text = "TEXT", FontSize = 100, Color = "#FFFFFFFF" };
        using var originalImage = TextLayerRenderer.RenderOverlay([layer]); using var original = SKBitmap.FromImage(originalImage);
        layer.Glow = new() { InnerEnabled = true, InnerColor = "#FFFF0000", InnerRadius = 4, InnerOpacity = 1 };
        using var effectImage = TextLayerRenderer.RenderOverlay([layer]); using var effect = SKBitmap.FromImage(effectImage);
        var colored = 0; var escaped = 0; byte maxEscapedAlpha = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var before = original.GetPixel(x, y); var after = effect.GetPixel(x, y);
            if (before.Alpha > 200 && after.Red > 200 && after.Green < 150) colored++;
            if (before.Alpha == 0 && after.Alpha > 10) { escaped++; maxEscapedAlpha = Math.Max(maxEscapedAlpha, after.Alpha); }
        }
        Assert.True(colored > 10); Assert.True(escaped == 0, $"Escaped pixels: {escaped}; maximum alpha: {maxEscapedAlpha}.");
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackgroundAndExtrusionAreRenderedInTheSharedOverlay(bool perLine)
    {
        var layer = new TextLayer { Text = "HI\nWORLD", FontSize = 60, Background = new() { Enabled = true, PerLine = perLine, Color = "#FF00FF00", PaddingX = 12, PaddingY = 5 }, Extrusion = new() { Enabled = true, Color = "#FFFF0000", Depth = 12, Angle = 0, Shading = 0 } };
        using var image = TextLayerRenderer.RenderOverlay([layer]); using var bitmap = SKBitmap.FromImage(image);
        var green = 0; var red = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var p = bitmap.GetPixel(x, y);
            if (p.Alpha > 200 && p.Green > 200 && p.Red < 40) green++;
            if (p.Alpha > 200 && p.Red > 200 && p.Green < 40) red++;
        }
        Assert.True(green > 100); Assert.True(red > 20);
    }
    [Fact]
    public void GradientAndGlowChangeTheSharedTextOverlay()
    {
        var layer = new TextLayer { Text = "STICKER", FontSize = 72, Color = "#FFFFFFFF" };
        using var plainImage = TextLayerRenderer.RenderOverlay([layer]);
        using var plain = SKBitmap.FromImage(plainImage);
        layer.Gradient.Enabled = true;
        layer.Gradient.Stops = [new() { Color = "#FFFF0000", Position = 0 }, new() { Color = "#FF0000FF", Position = 1 }];
        layer.Glow = new() { Enabled = true, Color = "#FF00FF00", Radius = 6, Opacity = 1 };
        using var effectImage = TextLayerRenderer.RenderOverlay([layer]);
        using var effected = SKBitmap.FromImage(effectImage);
        var colored = 0; var glow = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var before = plain.GetPixel(x, y); var after = effected.GetPixel(x, y);
            if (before.Alpha > 200 && after.Alpha > 200 && (after.Red < 200 || after.Blue < 200)) colored++;
            if (before.Alpha == 0 && after.Alpha > 0 && after.Green > after.Red && after.Green > after.Blue) glow++;
        }
        Assert.True(colored > 20); Assert.True(glow > 20);
        Assert.Equal("STICKER", layer.Text);
    }
    [Theory]
    [InlineData(TextOutlinePlacement.Outside)]
    [InlineData(TextOutlinePlacement.Center)]
    [InlineData(TextOutlinePlacement.Inside)]
    public void OutlinePlacementsRenderWithAlpha(TextOutlinePlacement placement)
    {
        var layer = new TextLayer { Text = "TEXT", FontSize = 90, Outlines = [new() { Enabled = true, Color = "#FFFF0000", Width = 6, Placement = placement }] };
        using var image = TextLayerRenderer.RenderOverlay([layer]); using var bitmap = SKBitmap.FromImage(image);
        var red = 0; var transparent = 0;
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.Alpha > 128 && pixel.Red > 200 && pixel.Green < 80) red++;
            if (pixel.Alpha == 0) transparent++;
        }
        Assert.True(red > 10); Assert.True(transparent > 1000);
    }
    [Theory]
    [InlineData("with-text.png", SKEncodedImageFormat.Png)]
    [InlineData("with-text.jpg", SKEncodedImageFormat.Jpeg)]
    public async Task StaticTextLayersUseTheSameCompositionForPreviewAndExport(string filename, SKEncodedImageFormat format)
    {
        var path = Image(filename, format);
        var transform = new TransformSettings { Rotation = -35, FlipHorizontal = true, Scale = .8 };
        var layers = new List<TextLayer> { new() { Text = "Привет\nWorld שלום 👩‍💻", X = 210, Y = 80, FontSize = 50, Color = "#FFFFFFFF", Underline = true, Case = TextCase.Upper, Curvature = 35, Outlines = [new() { Enabled = true, Color = "#FF0044AA", Width = 3 }], Shadow = new() { Enabled = true, OffsetX = 5, Blur = 2 }, RasterEffects = new() { MetallicEnabled = true, GlossEnabled = true, ChromaticEnabled = true } }, new() { Text = "hidden", IsVisible = false } };
        var bundled = BundledFontCatalog.Load()?.Families.FirstOrDefault(family => family.Id == "roboto");
        if (bundled is not null) { layers[0].FontFamily = bundled.Name; layers[0].FontAsset = bundled.Id + "/" + bundled.Fonts[0].File; }
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

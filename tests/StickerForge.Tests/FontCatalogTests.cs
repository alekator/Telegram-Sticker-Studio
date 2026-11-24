using System.Diagnostics;
using SkiaSharp;
using StickerForge.Core;
using StickerForge.Media;

namespace StickerForge.Tests;

public sealed class FontBundleFactAttribute : FactAttribute
{
    public FontBundleFactAttribute()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "fonts", "manifest.json"))) Skip = "Prepare the bundled fonts next to the test assembly.";
    }
}

public sealed class FontCatalogTests
{
    [Fact]
    public void MixedDirectionRunsAreShapedInUnicodeVisualOrder()
    {
        var method = typeof(TextLayerRenderer).GetMethod("GetVisualRuns", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var runs = ((string Text, bool RightToLeft)[])method.Invoke(null, ["abc שלום 123 xyz"])!;
        Assert.Equal(new[] { ("abc ", false), ("123", false), ("שלום ", true), (" xyz", false) }, runs);
    }

    [FontBundleFact]
    public void CatalogLoadsMetadataAndUsesTheExactBundledFile()
    {
        var memory = GC.GetTotalMemory(true); var timer = Stopwatch.StartNew();
        var catalog = BundledFontCatalog.Load(); timer.Stop();
        Assert.NotNull(catalog); Assert.True(catalog.Families.Length >= 200);
        var family = Assert.Single(catalog.Families, f => f.Id == "roboto");
        Assert.Contains("cyrillic", family.Subsets);
        var asset = family.Id + "/" + family.Fonts[0].File;
        using var face = BundledFontCatalog.Typeface(new TextLayer { FontFamily = "deliberately absent", FontAsset = asset });
        Assert.Contains("Roboto", face.FamilyName, StringComparison.OrdinalIgnoreCase);
        using var font = new SKFont(face, 36); Assert.All(font.GetGlyphs("Привет Latin"), glyph => Assert.NotEqual((ushort)0, glyph));
        Console.WriteLine($"Catalog: {catalog.Families.Length} families, {catalog.Families.Sum(f => f.Fonts.Length)} files, load {timer.ElapsedMilliseconds} ms, managed delta {GC.GetTotalMemory(false) - memory} bytes");
    }

    [FontBundleFact]
    public void BundledFontChangesRenderedTextWithoutSystemInstallation()
    {
        var catalog = BundledFontCatalog.Load()!;
        var family = catalog.Families.First(f => f.Id == "roboto");
        var layer = new TextLayer { FontAsset = family.Id + "/" + family.Fonts[0].File, Text = "Привет, world!", FontSize = 48 };
        using var rendered = TextLayerRenderer.RenderOverlay([layer]); using var pixels = SKBitmap.FromImage(rendered);
        var occupied = 0;
        for (var y = 180; y < 330; y++) for (var x = 0; x < 512; x++) if (pixels.GetPixel(x, y).Alpha > 0) occupied++;
        Assert.True(occupied > 100);
    }
}

using StickerForge.Core;
using StickerForge.Infrastructure;
using StickerForge.Media;
using SkiaSharp;

namespace StickerForge.Tests;

public sealed class ProjectTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "stickerforge-tests-" + Guid.NewGuid().ToString("N"));
    private static byte[] CreatePng()
    {
        using var bitmap = new SKBitmap(1, 1);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    public ProjectTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Theory]
    [InlineData(FitMode.Fill)]
    [InlineData(FitMode.Manual)]
    public async Task TransformSettingsRoundTrip(FitMode mode)
    {
        var path = Path.Combine(directory, "pack.json");
        var item = new StickerItem { SourcePath = Path.Combine(directory, "source.png"), Transform = new TransformSettings { Fit = mode, Scale = 1.5, OffsetX = 20, OffsetY = -30, Rotation = 45, FlipHorizontal = true, FlipVertical = true, StartSeconds = 1, DurationSeconds = 2 } };
        var store = new ProjectStore(); await store.SaveAsync(new StickerProject { Stickers = [item] }, path);
        var saved = Assert.Single((await store.LoadAsync(path)).Stickers).Transform;
        Assert.Equal(mode, saved.Fit); Assert.Equal(1.5, saved.Scale); Assert.Equal(20, saved.OffsetX); Assert.Equal(-30, saved.OffsetY); Assert.Equal(45, saved.Rotation); Assert.True(saved.FlipHorizontal); Assert.True(saved.FlipVertical); Assert.Equal(1, saved.StartSeconds); Assert.Equal(2, saved.DurationSeconds);
    }

    [Fact]
    public async Task AnimatedItemsKeepIndependentTransformsAndFragments()
    {
        var path = Path.Combine(directory, "animated.json");
        var first = new TransformSettings { Fit = FitMode.Fill, Scale = 1.5, OffsetX = 20, OffsetY = -30, Rotation = 45, FlipHorizontal = true, StartSeconds = .4, DurationSeconds = 1.2 };
        var second = new TransformSettings { Fit = FitMode.Manual, Scale = .7, OffsetX = -15, OffsetY = 35, Rotation = -90, FlipVertical = true, StartSeconds = 2.1, DurationSeconds = .8 };
        var store = new ProjectStore();
        await store.SaveAsync(new StickerProject { Stickers = [new() { SourcePath = Path.Combine(directory, "a.gif"), Transform = first }, new() { SourcePath = Path.Combine(directory, "b.mp4"), Transform = second }] }, path);
        var loaded = await store.LoadAsync(path);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first), System.Text.Json.JsonSerializer.Serialize(loaded.Stickers[0].Transform));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(second), System.Text.Json.JsonSerializer.Serialize(loaded.Stickers[1].Transform));
    }

    [Fact]
    public async Task ExistingProjectWithoutReflectionSettingsKeepsOriginalOrientation()
    {
        var path = Path.Combine(directory, "old.json");
        await File.WriteAllTextAsync(path, """
            {"schemaVersion":1,"name":"old","stickers":[{"id":"11111111-1111-1111-1111-111111111111","sourcePath":"image.png","transform":{"fit":1,"scale":1.5,"offsetX":12,"rotation":90}}]}
            """);
        var item = Assert.Single((await new ProjectStore().LoadAsync(path)).Stickers);
        Assert.Empty(item.TextLayers);
        var transform = item.Transform;
        Assert.Equal(FitMode.Fill, transform.Fit); Assert.Equal(1.5, transform.Scale); Assert.Equal(12, transform.OffsetX); Assert.Equal(90, transform.Rotation);
        Assert.False(transform.FlipHorizontal); Assert.False(transform.FlipVertical);
    }

    [Fact]
    public void NewProjectIsEmpty() => Assert.Empty(new StickerProject().Stickers);

    [Fact]
    public async Task TextLayersRoundTripIndependentlyAndInOrder()
    {
        var path = Path.Combine(directory, "text.json");
        var first = new TextLayer { Name = "Первый", Text = "Привет\nworld", FontAsset = "roboto/Roboto[wdth,wght].ttf", X = 42, Y = 180, Rotation = -25, IsLocked = true, Case = TextCase.Upper, Underline = true, StretchX = 1.4, Outlines = [new() { Enabled = true, Width = 7, Color = "#FF123456" }], Shadow = new() { Enabled = true, OffsetX = 9, Blur = 5 }, Glow = new() { Enabled = true, Radius = 12 }, Gradient = new() { Enabled = true, Kind = TextGradientKind.Angular, Angle = 30, Stops = [new() { Color = "#80FF0000", Position = .25 }, new() { Color = "#FF00FFFF", Position = .8 }] } };
        var second = new TextLayer { Text = "Second", IsVisible = false, LetterSpacing = 2 };
        first.Background = new() { Enabled = true, PerLine = true, PaddingX = 12, BorderWidth = 3, Gradient = new() { Enabled = true } };
        first.Extrusion = new() { Enabled = true, Depth = 15, Angle = 75, Shading = .5 };
        var project = new StickerProject { Stickers = [new() { SourcePath = Path.Combine(directory, "one.png"), TextLayers = [first, second] }, new() { SourcePath = Path.Combine(directory, "two.gif") }] };
        var store = new ProjectStore();
        await store.SaveAsync(project, path);
        var loaded = await store.LoadAsync(path);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(project.Stickers[0].TextLayers), System.Text.Json.JsonSerializer.Serialize(loaded.Stickers[0].TextLayers));
        Assert.Empty(loaded.Stickers[1].TextLayers);
        loaded.Stickers[1].TextLayers.Add(new TextLayer());
        Assert.Equal(2, loaded.Stickers[0].TextLayers.Count);
    }

    [Fact]
    public void TextStylesPersistAndDoNotChangeContentOrGeometry()
    {
        var stylePath = Path.Combine(directory, "styles.json");
        var source = new TextLayer { Text = "source", Color = "#FF00AAFF", Outlines = [new() { Enabled = true, Width = 9 }] };
        var style = TextStyle.Capture(source, "Custom");
        var store = new TextStyleStore(stylePath); store.Save([style]);
        var loaded = Assert.Single(store.Load());
        var target = new TextLayer { Text = "target", X = 17, Y = 29, Scale = 2, Rotation = -45 };
        loaded.Apply(target);
        Assert.Equal("target", target.Text); Assert.Equal(17, target.X); Assert.Equal(29, target.Y);
        Assert.Equal(2, target.Scale); Assert.Equal(-45, target.Rotation); Assert.Equal("#FF00AAFF", target.Color);
        Assert.Equal(9, Assert.Single(target.Outlines).Width);
        target.Outlines[0].Width = 2;
        Assert.Equal(9, source.Outlines[0].Width); Assert.Equal(9, loaded.Outlines[0].Width);
        Assert.Contains(TextStylePresets.Create(), preset => preset.Name == "Neon Blue" && preset.Glow.Enabled);
    }

    [Fact]
    public void FontFavoritesAndRecentSurviveRestartAndDamagedSettings()
    {
        var path = Path.Combine(directory, "font-preferences.json");
        new FontPreferencesStore(path).Save(new FontPreferences(["roboto", "system:Arial"], ["roboto"]));
        var loaded = new FontPreferencesStore(path).Load();
        Assert.Equal(new[] { "roboto", "system:Arial" }, loaded.Favorites); Assert.Equal(new[] { "roboto" }, loaded.Recent);
        File.WriteAllText(path, "{"); Assert.Empty(new FontPreferencesStore(path).Load().Favorites);
    }

    [Fact]
    public void BundledFontPathsRejectTraversalAndMissingFiles()
    {
        Assert.Throws<InvalidDataException>(() => BundledFontCatalog.Resolve("../Arial.ttf", directory));
        Assert.Throws<InvalidDataException>(() => BundledFontCatalog.Resolve("roboto/../../Arial.ttf", directory));
        Assert.Throws<InvalidDataException>(() => BundledFontCatalog.Resolve("roboto/missing.ttf", directory));
        Assert.Throws<InvalidDataException>(() => BundledFontCatalog.Typeface(new TextLayer { FontAsset = "missing/missing.ttf" }));
    }

    [Fact]
    public void PanelLayoutRestoresWidthsAndVisibilityAndToleratesDamagedSettings()
    {
        var path = Path.Combine(directory, "settings", "panels.json");
        var store = new PanelLayoutStore(path);
        store.Save(new PanelLayout(410, 260, false, true));
        Assert.Equal(new PanelLayout(410, 260, false, true), new PanelLayoutStore(path).Load());
        File.WriteAllText(path, "{ повреждённый файл");
        Assert.Equal(new PanelLayout(), store.Load());
    }

    [Fact]
    public async Task SaveLoadPreservesOrderAndMissingSources()
    {
        var store = new ProjectStore();
        var first = Path.Combine(directory, "missing.png");
        var second = Path.Combine(directory, "second.png");
        var project = new StickerProject { Name = "Pack", Stickers = [new() { SourcePath = first }, new() { SourcePath = second }] };
        var path = Path.Combine(directory, "project.json");
        await store.SaveAsync(project, path);
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain(directory.Replace("\\", "\\\\"), json);
        var loaded = await store.LoadAsync(path);
        Assert.False(loaded.Stickers[0].Transform.FlipHorizontal); Assert.False(loaded.Stickers[0].Transform.FlipVertical);
        Assert.Equal("Pack", loaded.Name);
        Assert.Equal(new[] { first, second }, loaded.Stickers.Select(x => x.SourcePath));
        Assert.False(File.Exists(loaded.Stickers[0].SourcePath));
        loaded.Stickers.RemoveAt(0);
        await store.SaveAsync(loaded, path);
        Assert.Single((await store.LoadAsync(path)).Stickers);
    }

    [Fact]
    public async Task RejectsUnsupportedSchema()
    {
        var path = Path.Combine(directory, "future.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":999,\"name\":\"x\",\"stickers\":[]}");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProjectStore().LoadAsync(path));
    }

    [Fact]
    public async Task ImportsImageAndIsolatesBadFiles()
    {
        var image = Path.Combine(directory, "one.png");
        var broken = Path.Combine(directory, "bad.png");
        var png = CreatePng();
        await File.WriteAllBytesAsync(image, png);
        await File.WriteAllTextAsync(broken, "not an image");
        var results = await new ImageImporter(new SkiaImageDecoder()).ImportAsync([image, broken]);
        Assert.NotNull(results[0].Item);
        Assert.Equal(1, results[0].Info!.Width);
        Assert.NotEmpty(results[0].Info!.Thumbnail);
        Assert.Null(results[1].Item);
        Assert.NotNull(results[1].Error);
        Assert.Equal(png, await File.ReadAllBytesAsync(image));
    }

    [Fact]
    public async Task CancellationStopsImport()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ImageImporter(new SkiaImageDecoder()).ImportAsync(["missing"], cancellationToken: source.Token));
    }

    [Fact]
    public async Task ImportsTwentyImagesInOrder()
    {
        var png = CreatePng();
        var paths = Enumerable.Range(0, 20).Select(i => Path.Combine(directory, $"image{i:D2}.png")).ToArray();
        foreach (var path in paths) await File.WriteAllBytesAsync(path, png);
        var results = await new ImageImporter(new SkiaImageDecoder()).ImportAsync(paths);
        Assert.Equal(paths, results.Select(r => r.Path));
        Assert.All(results, result => Assert.NotNull(result.Info));
    }
}

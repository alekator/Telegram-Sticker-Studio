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

    [Fact]
    public async Task TransformSettingsRoundTrip()
    {
        var path = Path.Combine(directory, "pack.json");
        var item = new StickerItem { SourcePath = Path.Combine(directory, "source.png"), Transform = new TransformSettings { Fit = FitMode.Fill, Scale = 1.5, OffsetX = 20, OffsetY = -30, Rotation = 45, StartSeconds = 1, DurationSeconds = 2 } };
        var store = new ProjectStore(); await store.SaveAsync(new StickerProject { Stickers = [item] }, path);
        var saved = Assert.Single((await store.LoadAsync(path)).Stickers).Transform;
        Assert.Equal(FitMode.Fill, saved.Fit); Assert.Equal(1.5, saved.Scale); Assert.Equal(20, saved.OffsetX); Assert.Equal(-30, saved.OffsetY); Assert.Equal(45, saved.Rotation); Assert.Equal(1, saved.StartSeconds); Assert.Equal(2, saved.DurationSeconds);
    }

    [Fact]
    public void NewProjectIsEmpty() => Assert.Empty(new StickerProject().Stickers);

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

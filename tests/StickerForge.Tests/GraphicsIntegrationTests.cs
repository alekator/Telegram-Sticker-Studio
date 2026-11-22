using System.Numerics;
using SkiaSharp;
using StickerForge.Core;
using StickerForge.Infrastructure;
using StickerForge.Rendering;

namespace StickerForge.Tests;

public sealed class GraphicsIntegrationTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "sf-graphics-" + Guid.NewGuid().ToString("N"));
    public GraphicsIntegrationTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);
    private string Original()
    {
        var path = Path.Combine(dir, "original.png");
        using var bitmap = new SKBitmap(64, 64); bitmap.Erase(SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray()); return path;
    }

    [Fact]
    public async Task EditRoundtripPreservesOriginalLayersThumbnailAndHistory()
    {
        var source = Original(); var original = await File.ReadAllBytesAsync(source);
        byte[] archive; byte[] thumbnail;
        using (var editor = GraphicsSession.Open(source, null))
        {
            Assert.Single(editor.Session.Document.Layers);
            editor.Session.AddLayer("Paint");
            editor.Begin(new Vector2(20, 20)); editor.Continue(new Vector2(40, 20)); editor.End();
            using (var png = SKBitmap.Decode(editor.RenderPng())) Assert.True(png.GetPixel(30, 20).Alpha > 0);
            editor.Session.Undo();
            using (var png = SKBitmap.Decode(editor.RenderPng())) Assert.Equal(0, png.GetPixel(30, 20).Alpha);
            editor.Session.Redo();
            archive = editor.Save(); thumbnail = editor.Thumbnail();
        }
        using (var thumb = SKBitmap.Decode(thumbnail)) { Assert.True(thumb.GetPixel(30, 20).Alpha > 0); Assert.Equal(0, thumb.GetPixel(0, 0).Alpha); }
        var project = new StickerProject { Stickers = [new() { SourcePath = source, EditableDocument = archive }] };
        var path = Path.Combine(dir, "project.json"); var store = new ProjectStore();
        await store.SaveAsync(project, path);
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        var loaded = await store.LoadAsync(path);
        Assert.Equal(archive, loaded.Stickers[0].EditableDocument);
        using var reopened = GraphicsSession.Open(source, loaded.Stickers[0].EditableDocument);
        Assert.Equal(new[] { "Original", "Paint" }, reopened.Session.Document.Layers.Select(x => x.Name));
        using var result = SKBitmap.Decode(reopened.RenderPng()); Assert.True(result.GetPixel(30, 20).Alpha > 0);
        Assert.Equal(thumbnail, reopened.Thumbnail());
        var copyPath = Path.Combine(dir, "copy.json"); await store.SaveAsync(loaded, copyPath);
        Assert.Equal(archive, (await store.LoadAsync(copyPath)).Stickers[0].EditableDocument);
    }

    [Fact]
    public async Task LoadsVersionOneAndRejectedSaveKeepsOldProject()
    {
        var source = Original(); var path = Path.Combine(dir, "project.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"name\":\"Old\",\"stickers\":[{\"id\":\"" + Guid.NewGuid() + "\",\"sourcePath\":\"original.png\"}]}");
        var store = new ProjectStore(); var old = await store.LoadAsync(path);
        Assert.Null(old.Stickers[0].EditableDocument); Assert.Equal(source, old.Stickers[0].SourcePath);
        var bytes = await File.ReadAllBytesAsync(path);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(old, path, cancelled.Token));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }
}

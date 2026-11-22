using System.Numerics;
using System.Security.Cryptography;
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

    [Fact]
    public async Task ReeditSaveAsAndMovePreserveBothProjectsAndOriginal()
    {
        var source = Original(); var original = await File.ReadAllBytesAsync(source);
        using var first = GraphicsSession.Open(source, null);
        first.Session.AddLayer("Paint"); first.Begin(new(16, 16)); first.End();
        var store = new ProjectStore(); var path = Path.Combine(dir, "pack.json");
        await store.SaveAsync(new StickerProject { Stickers = [new() { SourcePath = source, EditableDocument = first.Save() }] }, path);
        var loaded = await store.LoadAsync(path);
        using (var edit = GraphicsSession.Open(source, loaded.Stickers[0].EditableDocument))
        {
            edit.Session.AddLayer("Second pass"); edit.Begin(new(48, 48)); edit.End();
            loaded.Stickers[0].EditableDocument = edit.Save();
        }
        var copy = Path.Combine(dir, "copy.json"); await store.SaveAsync(loaded, copy);
        using (var old = GraphicsSession.Open(source, (await store.LoadAsync(path)).Stickers[0].EditableDocument)) Assert.Equal(2, old.Session.Document.Layers.Count);
        var moved = Path.Combine(dir, "moved"); Directory.CreateDirectory(moved);
        File.Copy(source, Path.Combine(moved, "original.png")); File.Copy(copy, Path.Combine(moved, "copy.json"));
        var assets = copy + ".assets"; var movedAssets = Path.Combine(moved, "copy.json.assets"); Directory.CreateDirectory(movedAssets);
        foreach (var file in Directory.GetFiles(assets)) File.Copy(file, Path.Combine(movedAssets, Path.GetFileName(file)));
        var restored = await store.LoadAsync(Path.Combine(moved, "copy.json"));
        using var reopened = GraphicsSession.Open(restored.Stickers[0].SourcePath, restored.Stickers[0].EditableDocument);
        Assert.Equal(new[] { "Original", "Paint", "Second pass" }, reopened.Session.Document.Layers.Select(x => x.Name));
        using var image = SKBitmap.Decode(reopened.RenderPng()); Assert.True(image.GetPixel(16, 16).Alpha > 0); Assert.True(image.GetPixel(48, 48).Alpha > 0);
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task FailedAssetWriteKeepsPreviousProjectAndInMemoryEdits()
    {
        var source = Original(); var path = Path.Combine(dir, "pack.json"); var store = new ProjectStore();
        using var edit = GraphicsSession.Open(source, null); edit.Session.AddLayer("Paint");
        var project = new StickerProject { Stickers = [new() { SourcePath = source, EditableDocument = edit.Save() }] };
        await store.SaveAsync(project, path); var previous = await File.ReadAllBytesAsync(path);
        edit.Begin(new(32, 32)); edit.End(); var next = edit.Save(); project.Stickers[0].EditableDocument = next;
        var collision = Path.Combine(path + ".assets", Convert.ToHexString(SHA256.HashData(next)).ToLowerInvariant() + ".imagespace");
        await File.WriteAllBytesAsync(collision, [1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(project, path));
        Assert.Equal(previous, await File.ReadAllBytesAsync(path));
        Assert.Equal(next, project.Stickers[0].EditableDocument);
        using var intact = GraphicsSession.Open(source, (await store.LoadAsync(path)).Stickers[0].EditableDocument);
        using var png = SKBitmap.Decode(intact.RenderPng()); Assert.Equal(0, png.GetPixel(32, 32).Alpha);
    }

    [Fact]
    public void DamagedEditableDocumentFailsWithoutChangingOriginal()
    {
        var source = Original(); var before = File.ReadAllBytes(source);
        var error = Assert.Throws<InvalidDataException>(() => GraphicsSession.Open(source, [1, 2, 3, 4]));
        Assert.Contains("damaged", error.Message); Assert.Equal(before, File.ReadAllBytes(source));
    }

    [Fact]
    public async Task MissingAssetReportsRecoveryInstructionsWithoutModifyingProject()
    {
        var source = Original(); var path = Path.Combine(dir, "project.json");
        using var editor = GraphicsSession.Open(source, null);
        var store = new ProjectStore();
        await store.SaveAsync(new StickerProject { Stickers = [new() { SourcePath = source, EditableDocument = editor.Save() }] }, path);
        var json = await File.ReadAllBytesAsync(path);
        foreach (var file in Directory.GetFiles(path + ".assets")) File.Delete(file);
        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => store.LoadAsync(path));
        Assert.Contains(".assets", error.Message); Assert.Equal(json, await File.ReadAllBytesAsync(path));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task DamagedAssetAndMissingOriginalDoNotModifySavedProject()
    {
        var source = Original(); var path = Path.Combine(dir, "project.json"); var store = new ProjectStore();
        using var edit = GraphicsSession.Open(source, null);
        await store.SaveAsync(new StickerProject { Stickers = [new() { SourcePath = source, EditableDocument = edit.Save() }] }, path);
        var json = await File.ReadAllBytesAsync(path);
        var asset = Assert.Single(Directory.GetFiles(path + ".assets"));
        await File.WriteAllBytesAsync(asset, [1, 2, 3, 4]);
        var project = await store.LoadAsync(path);
        Assert.Throws<InvalidDataException>(() => GraphicsSession.Open(source, project.Stickers[0].EditableDocument));
        File.Delete(source);
        var missing = await store.LoadAsync(path);
        Assert.False(File.Exists(missing.Stickers[0].SourcePath));
        Assert.Throws<FileNotFoundException>(() => GraphicsSession.Open(source, null));
        Assert.Equal(json, await File.ReadAllBytesAsync(path));
    }
}

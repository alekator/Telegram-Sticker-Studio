using System.Numerics;
using SkiaSharp;
using StickerForge.Core;
using StickerForge.Infrastructure;
using StickerForge.Rendering;

namespace StickerForge.Tests;

public sealed class WorkspaceEditSessionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sf-workspace-" + Guid.NewGuid().ToString("N"));
    private readonly string source;
    public WorkspaceEditSessionTests()
    {
        Directory.CreateDirectory(directory); source = Path.Combine(directory, "image.png");
        using var bitmap = new SKBitmap(64, 64); bitmap.Erase(SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(source, png.ToArray());
    }
    public void Dispose() => Directory.Delete(directory, true);
    private static void Stroke(WorkspaceEditSession workspace, int x, int y)
    {
        workspace.Graphics.Begin(new Vector2(x, y)); workspace.Graphics.End(); workspace.Changed();
    }

    [Fact]
    public void ApplyThenDiscardKeepsAppliedDocumentAndThumbnail()
    {
        byte[]? document = null; byte[]? thumbnail = null; int applies = 0;
        using var workspace = new WorkspaceEditSession(GraphicsSession.Open(source, null), (doc, thumb) => { document = doc; thumbnail = thumb; applies++; });
        Stroke(workspace, 15, 15);
        Assert.False(workspace.PrepareLeave(LeaveEditorChoice.Continue)); Assert.True(workspace.HasUnappliedChanges); Assert.Null(document);
        workspace.Apply(); Assert.False(workspace.HasUnappliedChanges); Assert.Equal(1, applies);
        using (var bmp = SKBitmap.Decode(thumbnail!)) Assert.True(bmp.GetPixel(15, 15).Alpha > 0);
        Stroke(workspace, 48, 48);
        Assert.True(workspace.PrepareLeave(LeaveEditorChoice.Discard)); workspace.Dispose();
        Assert.True(workspace.IsDisposed); Assert.Equal(1, applies);
        using var reopened = GraphicsSession.Open(source, document);
        using var png = SKBitmap.Decode(reopened.RenderPng());
        Assert.True(png.GetPixel(15, 15).Alpha > 0); Assert.Equal(0, png.GetPixel(48, 48).Alpha);
    }

    [Fact]
    public void FailedApplyPreservesOpenSessionAndUnappliedEdits()
    {
        using var workspace = new WorkspaceEditSession(GraphicsSession.Open(source, null), (_, _) => throw new IOException("failure"));
        Stroke(workspace, 20, 20);
        Assert.Throws<IOException>(() => workspace.PrepareLeave(LeaveEditorChoice.Apply));
        Assert.False(workspace.IsDisposed); Assert.True(workspace.HasUnappliedChanges);
        using var png = SKBitmap.Decode(workspace.Graphics.RenderPng()); Assert.True(png.GetPixel(20, 20).Alpha > 0);
    }

    [Fact]
    public async Task AppliedEditsSurviveProjectSaveAndReopen()
    {
        var item = new StickerItem { SourcePath = source };
        using var workspace = new WorkspaceEditSession(GraphicsSession.Open(source, null), (doc, _) => item.EditableDocument = doc);
        workspace.Graphics.Session.AddLayer("Paint"); workspace.Changed(); Stroke(workspace, 30, 30);
        Assert.True(workspace.PrepareLeave(LeaveEditorChoice.Apply));
        var project = new StickerProject { Stickers = [item] }; var path = Path.Combine(directory, "project.json");
        var store = new ProjectStore(); await store.SaveAsync(project, path);
        var restored = (await store.LoadAsync(path)).Stickers.Single();
        using var reopened = GraphicsSession.Open(restored.SourcePath, restored.EditableDocument);
        Assert.Equal(2, reopened.Session.Document.Layers.Count);
        using var png = SKBitmap.Decode(reopened.RenderPng()); Assert.True(png.GetPixel(30, 30).Alpha > 0);
    }

    [Fact]
    public void DisposeIsIdempotentAndFinishesActiveStrokeBeforeLeaving()
    {
        using var workspace = new WorkspaceEditSession(GraphicsSession.Open(source, null), (_, _) => { });
        workspace.Graphics.Begin(new Vector2(25, 25));
        Assert.False(workspace.PrepareLeave(LeaveEditorChoice.Continue));
        Assert.False(workspace.Graphics.Session.IsInTransaction); Assert.True(workspace.HasUnappliedChanges);
        workspace.Dispose(); workspace.Dispose();
        Assert.Throws<ObjectDisposedException>(() => workspace.Apply());
    }
}

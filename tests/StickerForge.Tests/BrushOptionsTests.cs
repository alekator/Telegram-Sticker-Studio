using System.Numerics;
using ImageSpace.Core;
using SkiaSharp;
using StickerForge.Rendering;

namespace StickerForge.Tests;

public sealed class BrushOptionsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sf-brush-" + Guid.NewGuid().ToString("N"));
    private readonly string source;
    public BrushOptionsTests()
    {
        Directory.CreateDirectory(directory); source = Path.Combine(directory, "original.png");
        using var bitmap = new SKBitmap(128, 128); bitmap.Erase(SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(source, data.ToArray());
    }
    public void Dispose() => Directory.Delete(directory, true);
    private static void Dab(GraphicsSession session, int x, int y) { session.Begin(new Vector2(x, y)); session.End(); }

    [Fact]
    public void BrushColorSizeOpacityAndHardnessAffectRealPixels()
    {
        using var session = GraphicsSession.Open(source, null); session.Session.AddLayer("Paint");
        session.BrushColor = new Rgba32(12, 120, 240, 255);
        session.BrushOptions = new ToolOptions(6, .5f, 1); Dab(session, 24, 24);
        session.BrushOptions = new ToolOptions(26, 1, 1); Dab(session, 80, 24);
        session.BrushOptions = new ToolOptions(26, 1, 0); Dab(session, 80, 80);
        using var png = SKBitmap.Decode(session.RenderPng());
        var small = png.GetPixel(24, 24);
        Assert.InRange(small.Alpha, 126, 129); Assert.InRange(small.Blue, 238, 242); Assert.InRange(small.Green, 118, 122);
        Assert.Equal(0, png.GetPixel(29, 24).Alpha); Assert.True(png.GetPixel(85, 24).Alpha > 240);
        Assert.True(png.GetPixel(90, 80).Alpha < png.GetPixel(90, 24).Alpha);
        Assert.Equal(0, png.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void EraserStrengthTargetsOnlyCurrentLayerAndUndoRedoWorks()
    {
        using var session = GraphicsSession.Open(source, null); session.Session.AddLayer("Paint");
        session.BrushColor = new Rgba32(255, 0, 0, 255); session.BrushOptions = new ToolOptions(24, 1, 1); Dab(session, 32, 32);
        session.Eraser = true; session.EraserOptions = new ToolOptions(12, .5f, 1); Dab(session, 32, 32);
        using (var erased = SKBitmap.Decode(session.RenderPng())) Assert.InRange(erased.GetPixel(32, 32).Alpha, 126, 129);
        session.Session.Undo(); using (var undo = SKBitmap.Decode(session.RenderPng())) Assert.Equal(255, undo.GetPixel(32, 32).Alpha);
        session.Session.Redo(); using (var redo = SKBitmap.Decode(session.RenderPng())) Assert.InRange(redo.GetPixel(32, 32).Alpha, 126, 129);
        Assert.Equal(24, session.BrushOptions.Size); Assert.Equal(12, session.EraserOptions.Size);
        session.EraserOptions = new ToolOptions(12, 1, 1); Dab(session, 32, 32);
        using var transparent = SKBitmap.Decode(session.RenderPng()); Assert.Equal(0, transparent.GetPixel(32, 32).Alpha);
    }

    [Fact]
    public void SettingsAreValidatedAndFrozenForAnActiveStroke()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolOptions(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolOptions(201, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolOptions(10, float.NaN, 1));
        using var session = GraphicsSession.Open(source, null); session.Session.AddLayer("Paint");
        session.BrushColor = new Rgba32(255, 0, 0, 255); session.BrushOptions = new ToolOptions(10, 1, 1);
        session.Begin(new(20, 20));
        session.BrushColor = new Rgba32(0, 0, 255, 255); session.Eraser = true; session.EraserOptions = new ToolOptions(40, 1, 1);
        session.Continue(new(60, 20)); session.End();
        using var png = SKBitmap.Decode(session.RenderPng()); Assert.True(png.GetPixel(50, 20).Red > 240); Assert.True(png.GetPixel(50, 20).Alpha > 240);
    }

    [Fact]
    public void EraserDoesNotPaintBackgroundOrChangeOtherLayers()
    {
        using var session = GraphicsSession.Open(source, null);
        var background = session.Session.Document.ActiveLayer!;
        background.Pixels!.Set(32, 32, new Rgba32(0, 0, 255, 255));
        session.Session.AddLayer("Paint"); session.BrushColor = new Rgba32(255, 0, 0, 255);
        session.BrushOptions = new ToolOptions(20, 1, 1); Dab(session, 32, 32);
        session.Eraser = true; session.EraserOptions = new ToolOptions(20, 1, 1); Dab(session, 32, 32);
        Assert.Equal(0, session.Session.Document.ActiveLayer!.Pixels!.Get(32, 32).A);
        Assert.Equal(255, background.Pixels.Get(32, 32).A);
        using var png = SKBitmap.Decode(session.RenderPng()); Assert.Equal(SKColors.Blue, png.GetPixel(32, 32));
    }

    [Fact]
    public void EditedDocumentRoundtripsAndOriginalIsUnchanged()
    {
        var original = File.ReadAllBytes(source);
        using var session = GraphicsSession.Open(source, null); session.Session.AddLayer("Paint");
        session.BrushColor = new Rgba32(10, 210, 40, 255); session.BrushOptions = new ToolOptions(18, .5f, 1); Dab(session, 50, 50);
        using var reopened = GraphicsSession.Open(source, session.Save());
        Assert.Equal(2, reopened.Session.Document.Layers.Count);
        using var png = SKBitmap.Decode(reopened.RenderPng()); Assert.InRange(png.GetPixel(50, 50).Alpha, 126, 129); Assert.True(png.GetPixel(50, 50).Green > 200);
        Assert.Equal(original, File.ReadAllBytes(source));
    }
}

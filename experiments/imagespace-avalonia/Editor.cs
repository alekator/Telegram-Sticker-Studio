using System.Numerics;
using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Editing;
using ImageSpace.Imaging;
using ImageSpace.Skia;
using SkiaSharp;

namespace ImageSpaceAvalonia;

internal sealed class Editor : IDisposable
{
    private readonly BrushEngine brush = new();
    private readonly ImageRenderer renderer = new();
    private bool stroking;
    public EditorSession Session { get; private set; }
    public bool Eraser { get; set; }
    public Editor() { Session = NewSession(); }
    private static EditorSession NewSession()
    {
        var session = new EditorSession(new ImageDocument(512, 512, "Sticker"));
        session.AddLayer("Layer 1"); session.AddLayer("Layer 2");
        session.MarkSaved();
        return session;
    }
    public void Begin(Vector2 position)
    {
        var layer = Session.Document.ActiveLayer;
        if (layer?.Pixels is null || !layer.Visible || layer.Locked) return;
        Session.Begin(Eraser ? "Eraser" : "Brush");
        brush.Begin(layer.Pixels, Vector2.Zero);
        stroking = true;
        Continue(position);
    }
    public void Continue(Vector2 position)
    {
        if (!stroking || Session.Document.ActiveLayer is not { } layer) return;
        brush.Paint(Session.Document, layer, position, 1, new BrushSettings(Size: 24, Hardness: .8f, Flow: 1), new Rgba32(232, 75, 76, 255), Eraser ? PaintMode.Eraser : PaintMode.Brush);
    }
    public void End(bool cancel = false)
    {
        if (!stroking) return;
        brush.End(); stroking = false;
        if (cancel) Session.Cancel(); else Session.Commit();
    }
    public byte[] RenderPng() => renderer.Export(Session.Document, SKEncodedImageFormat.Png);
    public byte[] Save() => DocumentArchive.Save(Session.Document);
    public void Open(byte[] bytes) { End(true); var document = DocumentArchive.Load(bytes); Session = new EditorSession(document); renderer.Prune(document); }
    public void Dispose() { End(true); renderer.Dispose(); }
}

internal readonly record struct ViewTransform(double Scale, double X, double Y)
{
    public Vector2 ToDocument(double x, double y) => new((float)((x - X) / Scale), (float)((y - Y) / Scale));
    public ViewTransform ZoomAt(double factor, double x, double y)
    {
        var scale = Math.Clamp(Scale * factor, .1, 8);
        var p = ToDocument(x, y);
        return new(scale, x - p.X * scale, y - p.Y * scale);
    }
}

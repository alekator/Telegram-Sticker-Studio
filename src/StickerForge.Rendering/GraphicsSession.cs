using System.Numerics;
using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Editing;
using ImageSpace.Imaging;
using ImageSpace.Skia;
using SkiaSharp;

namespace StickerForge.Rendering;

public sealed class GraphicsSession : IDisposable
{
    private readonly BrushEngine brush = new();
    private readonly ImageRenderer renderer = new();
    private bool stroking;
    public EditorSession Session { get; }
    public bool Eraser { get; set; }

    private GraphicsSession(ImageDocument document) => Session = new EditorSession(document);
    public static GraphicsSession Open(string source, byte[]? saved)
    {
        if (saved is not null) return new GraphicsSession(DocumentArchive.Load(saved));
        var pixels = ImageRenderer.Decode(File.ReadAllBytes(source));
        var document = new ImageDocument(pixels.Width, pixels.Height, Path.GetFileNameWithoutExtension(source));
        var layer = Layer.Raster("Original", pixels.Width, pixels.Height); layer.Pixels = pixels;
        document.Layers.Add(layer); document.ActiveLayerId = layer.Id;
        return new GraphicsSession(document);
    }
    public void Begin(Vector2 position)
    {
        var layer = Session.Document.ActiveLayer;
        if (layer?.Pixels is null || !layer.Visible || layer.Locked) return;
        Session.Begin(Eraser ? "Eraser" : "Brush");
        brush.Begin(layer.Pixels, Vector2.Zero); stroking = true; Continue(position);
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
    public byte[] Save() => DocumentArchive.Save(Session.Document);
    public byte[] RenderPng() => renderer.Export(Session.Document, SKEncodedImageFormat.Png);
    public byte[] Thumbnail()
    {
        using var image = SKBitmap.Decode(RenderPng());
        var scale = Math.Min(1d, 192d / Math.Max(image.Width, image.Height));
        using var small = image.Resize(new SKImageInfo(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))), SKSamplingOptions.Default) ?? throw new InvalidDataException("Unable to render thumbnail.");
        using var skImage = SKImage.FromBitmap(small);
        using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    public void Dispose() { End(true); renderer.Dispose(); }
}

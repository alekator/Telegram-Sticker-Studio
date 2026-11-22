using System.Numerics;
using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Editing;
using ImageSpace.Imaging;
using ImageSpace.Skia;
using SkiaSharp;

namespace StickerForge.Rendering;

public sealed record ToolOptions
{
    public float Size { get; }
    public float Opacity { get; }
    public float Hardness { get; }
    public ToolOptions(float size = 24, float opacity = 1, float hardness = .8f)
    {
        if (!float.IsFinite(size) || size is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(size));
        if (!float.IsFinite(opacity) || opacity is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
        if (!float.IsFinite(hardness) || hardness is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(hardness));
        Size = size; Opacity = opacity; Hardness = hardness;
    }
    internal BrushSettings ToBrushSettings() => new(Size: Size, Hardness: Hardness, Opacity: Opacity, Flow: 1);
}

public sealed class GraphicsSession : IDisposable
{
    private readonly BrushEngine brush = new();
    private readonly ImageRenderer renderer = new();
    private bool stroking;
    private BrushSettings? activeSettings;
    private Rgba32 activeColor;
    private PaintMode activeMode;
    public EditorSession Session { get; }
    public bool Eraser { get; set; }
    public ToolOptions BrushOptions { get; set; } = new();
    public ToolOptions EraserOptions { get; set; } = new();
    public Rgba32 BrushColor { get; set; } = new(232, 75, 76, 255);

    private GraphicsSession(ImageDocument document) => Session = new EditorSession(document);
    public static GraphicsSession Open(string source, byte[]? saved)
    {
        if (saved is not null)
        {
            try { return new GraphicsSession(DocumentArchive.Load(saved)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or NotSupportedException)
            { throw new InvalidDataException("The editable ImageSpace document is damaged or unsupported. The project and original have not been changed.", ex); }
        }
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
        activeMode = Eraser ? PaintMode.Eraser : PaintMode.Brush;
        activeSettings = (Eraser ? EraserOptions : BrushOptions).ToBrushSettings();
        activeColor = BrushColor;
        Session.Begin(Eraser ? "Eraser" : "Brush");
        brush.Begin(layer.Pixels, Vector2.Zero); stroking = true; Continue(position);
    }
    public void Continue(Vector2 position)
    {
        if (!stroking || Session.Document.ActiveLayer is not { } layer) return;
        brush.Paint(Session.Document, layer, position, 1, activeSettings!, activeColor, activeMode);
    }
    public void End(bool cancel = false)
    {
        if (!stroking) return;
        brush.End(); stroking = false; activeSettings = null;
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

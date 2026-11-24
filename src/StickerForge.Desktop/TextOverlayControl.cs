using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using StickerForge.Core;
using StickerForge.Media;

namespace StickerForge.Desktop;

/// <summary>Draws the shared text overlay and handles its independent sticker-space manipulation.</summary>
internal sealed class TextOverlayControl : Control
{
    private StickerItem? item;
    private TextLayer? selected;
    private Bitmap? bitmap;
    private Point start;
    private double startX, startY, startScale, startRotation, startDistance, startAngle;
    private string? gesture;
    public Action? Changed { get; set; }
    public Action<TextLayer?>? SelectionChanged { get; set; }
    public Action? EditRequested { get; set; }
    public Action<string>? RenderingFailed { get; set; }
    public bool Editing { get; set; }

    public void SetItem(StickerItem? value) { item = value; selected = null; Refresh(); }
    public void SetSelected(TextLayer? value) { selected = value; InvalidateVisual(); }
    public void Refresh()
    {
        bitmap?.Dispose(); bitmap = null;
        if (item is { TextLayers.Count: > 0 })
        {
            try
            {
                using var image = TextLayerRenderer.RenderOverlay(item.TextLayers);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = new MemoryStream(data.ToArray()); bitmap = new Bitmap(stream);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { RenderingFailed?.Invoke(ex.Message); }
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (bitmap is not null) context.DrawImage(bitmap, new Rect(0, 0, 512, 512));
        if (!Editing || selected is not { IsVisible: true } layer) return;
        Rect bounds;
        try { bounds = LocalBounds(layer); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { RenderingFailed?.Invoke(ex.Message); return; }
        var transform = Matrix.CreateRotation(layer.Rotation * Math.PI / 180) * Matrix.CreateTranslation(layer.X, layer.Y);
        using (context.PushTransform(transform))
        {
            var pen = new Pen(Brushes.DeepSkyBlue, 2);
            context.DrawRectangle(null, pen, bounds);
            foreach (var corner in Corners(bounds)) context.DrawRectangle(Brushes.White, pen, new Rect(corner.X - 4, corner.Y - 4, 8, 8));
            var rotationPoint = new Point(bounds.Center.X, bounds.Top - 18);
            context.DrawLine(pen, new Point(bounds.Center.X, bounds.Top), rotationPoint);
            context.DrawEllipse(Brushes.White, pen, rotationPoint, 5, 5);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!Editing || item is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        TextLayer? hit = null; string? mode = null;
        try
        {
        if (selected is { IsVisible: true, IsLocked: false } active)
        {
            var local = ToLocal(point, active); var bounds = LocalBounds(active);
            if (Distance(local, new Point(bounds.Center.X, bounds.Top - 18)) <= 12) { hit = active; mode = "rotate"; }
            else if (Corners(bounds).Any(corner => Distance(local, corner) <= 12)) { hit = active; mode = "scale"; }
        }
        if (hit is null)
        {
            hit = item.TextLayers.AsEnumerable().Reverse().FirstOrDefault(layer => layer.IsVisible && LocalBounds(layer).Inflate(5).Contains(ToLocal(point, layer)));
            mode = hit is null || hit.IsLocked ? null : "move";
        }
        selected = hit; SelectionChanged?.Invoke(hit); InvalidateVisual();
        if (hit is null) { e.Handled = true; return; }
        if (e.ClickCount >= 2 && !hit.IsLocked) { EditRequested?.Invoke(); e.Handled = true; return; }
        if (mode is null) { e.Handled = true; return; }
        gesture = mode; start = point; startX = hit.X; startY = hit.Y; startScale = hit.Scale; startRotation = hit.Rotation;
        startDistance = Math.Max(1, Distance(point, new Point(hit.X, hit.Y)));
        startAngle = Math.Atan2(point.Y - hit.Y, point.X - hit.X);
        e.Pointer.Capture(this); e.Handled = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { RenderingFailed?.Invoke(ex.Message); e.Handled = true; }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (gesture is null || selected is not { IsLocked: false } layer || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        if (gesture == "move") { layer.X = startX + point.X - start.X; layer.Y = startY + point.Y - start.Y; }
        else if (gesture == "scale") layer.Scale = Math.Clamp(startScale * Distance(point, new Point(startX, startY)) / startDistance, .01, 20);
        else layer.Rotation = startRotation + (Math.Atan2(point.Y - startY, point.X - startX) - startAngle) * 180 / Math.PI;
        Refresh(); Changed?.Invoke(); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (gesture is null) return; gesture = null; e.Pointer.Capture(null); e.Handled = true; }

    private static Point ToLocal(Point point, TextLayer layer)
    {
        var angle = -layer.Rotation * Math.PI / 180; var x = point.X - layer.X; var y = point.Y - layer.Y;
        return new Point(x * Math.Cos(angle) - y * Math.Sin(angle), x * Math.Sin(angle) + y * Math.Cos(angle));
    }
    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private static Point[] Corners(Rect r) => [r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight];
    internal static Rect LocalBounds(TextLayer layer)
    {
        using var typeface = BundledFontCatalog.Typeface(layer);
        using var font = new SKFont(typeface, (float)Math.Clamp(layer.FontSize, 1, 512));
        var lines = (layer.Text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var width = Math.Max(24, lines.Max(line => font.MeasureText(line) + Math.Max(0, line.Length - 1) * layer.LetterSpacing)) * layer.Scale;
        var height = Math.Max(24, (font.Metrics.Descent - font.Metrics.Ascent) * (1 + (lines.Length - 1) * layer.LineSpacing)) * layer.Scale;
        var x = layer.Alignment switch { TextLayerAlignment.Left => 0, TextLayerAlignment.Right => -width, _ => -width / 2 };
        return new Rect(x, -height / 2, width, height);
    }
}

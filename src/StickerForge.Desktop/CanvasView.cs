using StickerForge.Rendering;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using ImageSpace.Core;

namespace StickerForge.Desktop;

internal sealed class CanvasView : Control, IDisposable
{
    private readonly GraphicsSession editor;
    private readonly DirectRenderer renderer = new();
    private ViewTransform view = new(1, 20, 20);
    private bool drawing, panning;
    private Point last;
    public event Action? Changed;
    public CanvasView(GraphicsSession editor) { this.editor = editor; ClipToBounds = true; }
    public void Refresh() => InvalidateVisual();
    public void Fit()
    {
        var doc = editor.Session.Document;
        var scale = Math.Clamp(Math.Min((Bounds.Width - 40) / doc.Width, (Bounds.Height - 40) / doc.Height), .1, 8);
        view = new(scale, (Bounds.Width - doc.Width * scale) / 2, (Bounds.Height - doc.Height * scale) / 2);
        InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.DimGray, new Rect(Bounds.Size));
        var doc = editor.Session.Document;
        var bounds = new Rect(view.X, view.Y, doc.Width * view.Scale, doc.Height * view.Scale);
        using (context.PushClip(bounds))
        {
            const int cell = 16;
            var a = new SolidColorBrush(Color.Parse("#eeeeee"));
            var b = new SolidColorBrush(Color.Parse("#aaaaaa"));
            for (int y = 0; y < doc.Height; y += cell)
                for (int x = 0; x < doc.Width; x += cell)
                    context.FillRectangle(((x / cell + y / cell) & 1) == 0 ? a : b,
                        new Rect(view.X + x * view.Scale, view.Y + y * view.Scale, cell * view.Scale, cell * view.Scale));
            context.Custom(new DrawOperation(new Rect(Bounds.Size), editor.Session.Document.Snapshot(), view, renderer));
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); var p = e.GetPosition(this); var props = e.GetCurrentPoint(this).Properties;
        if (props.IsMiddleButtonPressed) { panning = true; last = p; e.Pointer.Capture(this); }
        else if (props.IsLeftButtonPressed)
        {
            var doc = view.ToDocument(p.X, p.Y);
            if (doc.X < 0 || doc.Y < 0 || doc.X >= editor.Session.Document.Width || doc.Y >= editor.Session.Document.Height) return;
            editor.Begin(doc); drawing = editor.Session.IsInTransaction;
            if (drawing) { e.Pointer.Capture(this); Refresh(); }
        }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); var p = e.GetPosition(this);
        if (panning) { view = view with { X = view.X + p.X - last.X, Y = view.Y + p.Y - last.Y }; last = p; InvalidateVisual(); }
        else if (drawing) { editor.Continue(view.ToDocument(p.X, p.Y)); Refresh(); }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (drawing) { editor.Continue(view.ToDocument(e.GetPosition(this).X, e.GetPosition(this).Y)); editor.End(); drawing = false; Refresh(); Changed?.Invoke(); }
        panning = false; e.Pointer.Capture(null);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (drawing) { editor.End(); drawing = false; Refresh(); Changed?.Invoke(); }
        panning = false;
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e); if (drawing || panning) return;
        var p = e.GetPosition(this); view = view.ZoomAt(e.Delta.Y > 0 ? 1.2 : 1 / 1.2, p.X, p.Y); InvalidateVisual(); e.Handled = true;
    }
    public void Dispose() => renderer.Dispose();

    private sealed class DrawOperation(Rect bounds, ImageDocument snapshot, ViewTransform view, DirectRenderer renderer) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => bounds.Contains(p);
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            renderer.Draw(lease.SkCanvas, snapshot, view);
        }
    }
}


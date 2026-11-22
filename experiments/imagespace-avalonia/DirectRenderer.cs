using ImageSpace.Core;
using ImageSpace.Skia;
using SkiaSharp;

namespace ImageSpaceAvalonia;

// Avalonia may invoke custom draw operations on a rendering thread. Only immutable
// document snapshots cross that boundary; the renderer and its native cache are
// serialized with disposal and never shared with the UI/export renderer.
internal sealed class DirectRenderer : IDisposable
{
    private readonly object gate = new();
    private ImageRenderer? renderer = new();

    public void Draw(SKCanvas canvas, ImageDocument snapshot, ViewTransform view)
    {
        lock (gate)
        {
            if (renderer is null) return;
            var saved = canvas.Save();
            try
            {
                canvas.Translate((float)view.X, (float)view.Y);
                canvas.Scale((float)view.Scale);
                renderer.Draw(canvas, snapshot);
                // Drop SKImages for tiles replaced since the previous snapshot.
                renderer.Prune(snapshot);
            }
            finally { canvas.RestoreToCount(saved); }
        }
    }

    public void Dispose()
    {
        lock (gate) { renderer?.Dispose(); renderer = null; }
    }
}

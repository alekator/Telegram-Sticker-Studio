using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StickerForge.Desktop;

public sealed class Checkerboard : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        const double size = 16;
        context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        for (var y = 0; y * size < Bounds.Height; y++)
            for (var x = 0; x * size < Bounds.Width; x++)
                if ((x + y) % 2 == 0)
                    context.FillRectangle(Brushes.LightGray, new Rect(x * size, y * size, size, size));
    }
}

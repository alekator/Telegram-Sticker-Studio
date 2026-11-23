using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StickerForge.Desktop;

public sealed class Checkerboard : Control
{
    private static readonly IBrush Dark = new SolidColorBrush(Color.Parse("#0D1C30"));
    private static readonly IBrush Light = new SolidColorBrush(Color.Parse("#1B3049"));
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        const double size = 16;
        context.FillRectangle(Dark, new Rect(Bounds.Size));
        for (var y = 0; y * size < Bounds.Height; y++)
            for (var x = 0; x * size < Bounds.Width; x++)
                if ((x + y) % 2 == 0)
                    context.FillRectangle(Light, new Rect(x * size, y * size, size, size));
    }
}

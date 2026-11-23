using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace StickerForge.Desktop;

public sealed class AnimationTimeline : Control
{
    private static readonly IBrush Track = StudioTheme.TimelineTrack;
    private static readonly IBrush Selection = StudioTheme.TimelineSelection;
    private static readonly IBrush Handle = StudioTheme.TimelineHandle;
    private static readonly IPen Playhead = new Pen(Brushes.White, 2);
    private int dragging;
    public double Duration { get; private set; }
    public double Start { get; private set; }
    public double Length { get; private set; }
    public double Current { get; private set; }
    public event Action<double, double>? RangeChanged;
    public event Action<double>? SeekRequested;

    public AnimationTimeline() { Height = 32; MinWidth = 120; Focusable = true; }

    public void SetState(double duration, double start, double length, double current)
    {
        Duration = Math.Max(0, duration);
        var minimum = Math.Min(.05, Duration);
        Start = Math.Clamp(start, 0, Math.Max(0, Duration - minimum));
        Length = Math.Clamp(length, minimum, Math.Min(3, Duration - Start));
        Current = Math.Clamp(current, 0, Duration);
        InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Math.Max(0, Bounds.Width - 20); var y = Bounds.Height / 2;
        context.DrawRectangle(Track, null, new Rect(10, y - 4, width, 8), 4, 4);
        if (Duration <= 0 || width <= 0) return;
        var left = 10 + width * Start / Duration; var right = 10 + width * (Start + Length) / Duration;
        context.DrawRectangle(Selection, null, new Rect(left, y - 5, Math.Max(0, right - left), 10), 3, 3);
        context.DrawRectangle(Handle, null, new Rect(left - 5, y - 12, 10, 24), 3, 3);
        context.DrawRectangle(Handle, null, new Rect(right - 5, y - 12, 10, 24), 3, 3);
        var current = 10 + width * Current / Duration;
        context.DrawLine(Playhead, new Point(current, y - 13), new Point(current, y + 13));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Duration <= 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var x = e.GetPosition(this).X; var width = Math.Max(1, Bounds.Width - 20);
        var left = 10 + width * Start / Duration; var right = 10 + width * (Start + Length) / Duration;
        dragging = Math.Min(Math.Abs(x - left), Math.Abs(x - right)) <= 12 ? (Math.Abs(x - left) <= Math.Abs(x - right) ? 1 : 2) : 3;
        e.Pointer.Capture(this); MoveTo(x); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragging == 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        MoveTo(e.GetPosition(this).X); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragging == 0) return;
        dragging = 0; e.Pointer.Capture(null); e.Handled = true;
    }
    private void MoveTo(double x)
    {
        var time = Math.Clamp((x - 10) / Math.Max(1, Bounds.Width - 20), 0, 1) * Duration;
        var minimum = Math.Min(.05, Duration);
        if (dragging == 1)
        {
            var end = Start + Length; var start = Math.Clamp(time, Math.Max(0, end - 3), Math.Max(0, end - minimum));
            SetState(Duration, start, end - start, Current); RangeChanged?.Invoke(Start, Length);
        }
        else if (dragging == 2)
        {
            var end = Math.Clamp(time, Start + minimum, Math.Min(Duration, Start + 3));
            SetState(Duration, Start, end - Start, Current); RangeChanged?.Invoke(Start, Length);
        }
        else { Current = time; InvalidateVisual(); SeekRequested?.Invoke(time); }
    }
}

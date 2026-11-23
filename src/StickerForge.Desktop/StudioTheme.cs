using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace StickerForge.Desktop;

internal sealed class StudioTheme
{
    public static readonly IBrush TimelineTrack = new ImmutableSolidColorBrush(Color.Parse("#304254"));
    public static readonly IBrush TimelineSelection = new ImmutableSolidColorBrush(Color.Parse("#3390EC"));
    public static readonly IBrush TimelineHandle = new ImmutableSolidColorBrush(Color.Parse("#F5F7FA"));
    public static readonly IBrush StickerBoundaryOuter = new ImmutableSolidColorBrush(Color.Parse("#E6FFFFFF"));
    public static readonly IBrush StickerBoundaryInner = new ImmutableSolidColorBrush(Color.Parse("#A6000000"));
    public static readonly IBrush BadgeBackground = new ImmutableSolidColorBrush(Color.Parse("#D917212B"));
    public readonly SolidColorBrush Background = new();
    public readonly SolidColorBrush Panel = new();
    public readonly SolidColorBrush Surface = new();
    public readonly SolidColorBrush Border = new();
    public readonly SolidColorBrush Accent = new();
    public readonly SolidColorBrush Success = new();
    public readonly SolidColorBrush Selected = new();
    public readonly SolidColorBrush Text = new();
    public readonly SolidColorBrush Secondary = new();
    public void Apply(bool light)
    {
        Background.Color = Color.Parse(light ? "#F2F5F8" : "#0E1621");
        Panel.Color = Color.Parse(light ? "#FFFFFF" : "#17212B");
        Surface.Color = Color.Parse(light ? "#E8EFF5" : "#1F2C3A");
        Border.Color = Color.Parse(light ? "#C8D6E3" : "#304254");
        Accent.Color = Color.Parse("#3390EC");
        Success.Color = Color.Parse("#36C98A");
        Selected.Color = Color.Parse(light ? "#D5EAFE" : "#293B4D");
        Text.Color = Color.Parse(light ? "#17212B" : "#F5F7FA");
        Secondary.Color = Color.Parse(light ? "#536A80" : "#91A3B5");
    }
}

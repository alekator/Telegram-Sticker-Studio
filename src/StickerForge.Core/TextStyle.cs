using System.Text.Json;

namespace StickerForge.Core;

/// <summary>Appearance only: applying a style leaves content and geometry untouched.</summary>
public sealed class TextStyle
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#FFFFFFFF";
    public double Opacity { get; set; } = 1;
    public List<TextOutline> Outlines { get; set; } = [];
    public TextShadow Shadow { get; set; } = new();
    public TextGlow Glow { get; set; } = new();
    public TextGradient Gradient { get; set; } = new();
    public TextBackground Background { get; set; } = new();
    public TextExtrusion Extrusion { get; set; } = new();

    public static TextStyle Capture(TextLayer layer, string name) => Clone(new TextStyle
    {
        Name = name, Color = layer.Color, Opacity = layer.Opacity, Outlines = layer.Outlines,
        Shadow = layer.Shadow, Glow = layer.Glow, Gradient = layer.Gradient,
        Background = layer.Background, Extrusion = layer.Extrusion
    });

    public void Apply(TextLayer layer)
    {
        var copy = Clone(this);
        layer.Color = copy.Color; layer.Opacity = copy.Opacity; layer.Outlines = copy.Outlines;
        layer.Shadow = copy.Shadow; layer.Glow = copy.Glow; layer.Gradient = copy.Gradient;
        layer.Background = copy.Background; layer.Extrusion = copy.Extrusion;
    }

    private static TextStyle Clone(TextStyle value) => JsonSerializer.Deserialize<TextStyle>(JsonSerializer.Serialize(value))!;
}

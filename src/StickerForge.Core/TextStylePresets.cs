namespace StickerForge.Core;

public static class TextStylePresets
{
    public static IReadOnlyList<TextStyle> Create() =>
    [
        Outline("Classic Meme", "#FFFFFFFF", "#FF000000", 5),
        Outline("Black Outline", "#FFFFFFFF", "#FF000000", 3),
        Outline("White Outline", "#FF111111", "#FFFFFFFF", 3),
        new() { Name = "Double Outline", Outlines = [new() { Enabled = true, Color = "#FF000000", Width = 3 }, new() { Enabled = true, Color = "#FFFFFFFF", Width = 7 }] },
        Neon("Neon Blue", "#FF36D7FF"), Neon("Neon Pink", "#FFFF54D5"), Neon("Neon Green", "#FF4DFF91"),
        Gradient("Fire", "#FFFFF057", "#FFFF7900", "#FFC71D08"),
        Gradient("Ice", "#FFFFFFFF", "#FF82DFFF", "#FF0873C6"),
        Gradient("Gold", "#FFFFF1A6", "#FFDA9E24", "#FFFFE77A"),
        Gradient("Silver", "#FFFFFFFF", "#FF8795A7", "#FFF4F8FF"),
        Gradient("Rainbow", "#FFFF3C3C", "#FFFFDE38", "#FF37E98C", "#FF268DFF", "#FFBF55FF"),
        new() { Name = "Soft Shadow", Shadow = new() { Enabled = true, Blur = 6, OffsetX = 3, OffsetY = 4 } },
        new() { Name = "Subtitle", Background = new() { Enabled = true, PerLine = true, Color = "#CC000000", PaddingX = 9, PaddingY = 3, Radius = 3 } },
        new() { Name = "Sticker Label", Background = new() { Enabled = true, Color = "#FF087FE8", BorderColor = "#FFFFFFFF", BorderWidth = 3, PaddingX = 14, PaddingY = 7, Radius = 12 } },
        new() { Name = "3D Text", Extrusion = new() { Enabled = true, Color = "#FF173558", Depth = 10, Angle = 45, Shading = .4 } }
    ];

    private static TextStyle Outline(string name, string fill, string stroke, double width) => new()
    {
        Name = name, Color = fill, Outlines = [new() { Enabled = true, Color = stroke, Width = width }]
    };
    private static TextStyle Neon(string name, string color) => new()
    {
        Name = name, Color = "#FFFFFFFF", Glow = new() { Enabled = true, Color = color, Radius = 12, Opacity = 1 },
        Outlines = [new() { Enabled = true, Color = color, Width = 2 }]
    };
    private static TextStyle Gradient(string name, params string[] colors) => new()
    {
        Name = name, Gradient = new() { Enabled = true, Angle = 90, Stops = colors.Select((color, i) => new TextGradientStop { Color = color, Position = (double)i / (colors.Length - 1) }).ToList() }
    };
}

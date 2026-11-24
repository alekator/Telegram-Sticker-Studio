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
        new() { Name = "3D Text", Extrusion = new() { Enabled = true, Color = "#FF173558", Depth = 10, Angle = 45, Shading = .4 } },
        new() { Name = "Comic", Color = "#FFFFE34A", Outlines = [new() { Enabled = true, Color = "#FF101010", Width = 4 }], Shadow = new() { Enabled = true, Color = "#FF000000", Blur = 0, OffsetX = 5, OffsetY = 5 } },
        new() { Name = "Cartoon", Color = "#FFFFF3A0", Outlines = [new() { Enabled = true, Color = "#FF6B2713", Width = 5 }], Extrusion = new() { Enabled = true, Color = "#FFDF633C", Depth = 5 } },
        new() { Name = "Retro", Color = "#FFFFD77A", Outlines = [new() { Enabled = true, Color = "#FF5B284B", Width = 3 }], Extrusion = new() { Enabled = true, Color = "#FFDF664F", Depth = 7 } },
        new() { Name = "Pixel", Color = "#FF56FF9B", RasterEffects = new() { PixelationEnabled = true, PixelSize = 5 }, Shadow = new() { Enabled = true, Color = "#FF063722", Blur = 0, OffsetX = 4, OffsetY = 4 } },
        new() { Name = "Cyberpunk", Color = "#FFFFF34C", Outlines = [new() { Enabled = true, Color = "#FFFC31BD", Width = 3 }], Glow = new() { Enabled = true, Color = "#FF09CDFF", Radius = 10 } },
        new() { Name = "Horror", Color = "#FFFF4545", Shadow = new() { Enabled = true, Color = "#FF4C0000", Blur = 6, OffsetX = 4, OffsetY = 5 }, Glow = new() { InnerEnabled = true, InnerColor = "#FF390000", InnerRadius = 5 } },
        new() { Name = "Gaming", Color = "#FFFFFFFF", Outlines = [new() { Enabled = true, Color = "#FF067AC7", Width = 4 }, new() { Enabled = true, Color = "#FF0A1C38", Width = 7 }], Extrusion = new() { Enabled = true, Color = "#FF0E2A57", Depth = 6 } },
        new() { Name = "Speech Bubble", Color = "#FF111111", Background = new() { Enabled = true, Color = "#FFFFFFFF", BorderColor = "#FF111111", BorderWidth = 3, PaddingX = 16, PaddingY = 10, Radius = 20 } },
        new() { Name = "Candy", Gradient = new() { Enabled = true, Angle = 90, Stops = [new() { Color = "#FFFFF5FE" }, new() { Color = "#FFFF56BA", Position = 1 }] }, Outlines = [new() { Enabled = true, Color = "#FFB22785", Width = 3 }], Glow = new() { InnerEnabled = true, InnerColor = "#FFFFFFFF", InnerRadius = 2 } },
        new() { Name = "Glitch", Color = "#FFFFFFFF", RasterEffects = new() { ChromaticEnabled = true, ChromaticOffset = 5 }, Shadow = new() { Enabled = true, Color = "#FF101020", OffsetX = 3, OffsetY = 2, Blur = 0 } },
        new() { Name = "Chrome", Color = "#FF8AA3BD", RasterEffects = new() { MetallicEnabled = true, MetallicColor = "#FFD1E3F4", MetallicStrength = 1, GlossEnabled = true, GlossStrength = .5 }, Outlines = [new() { Enabled = true, Color = "#FF1B2E49", Width = 2 }] }
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

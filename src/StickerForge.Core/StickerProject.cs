namespace StickerForge.Core;

public sealed class StickerProject
{
    public const int CurrentSchemaVersion = 1;
    public string Name { get; set; } = "Новый набор";
    public List<StickerItem> Stickers { get; set; } = [];
}

public sealed class StickerItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string SourcePath { get; set; }
    public TransformSettings Transform { get; set; } = new();
    public List<TextLayer> TextLayers { get; set; } = [];
}

public enum TextLayerAlignment { Left, Center, Right }
public enum TextCase { Original, Upper, Lower }
public enum TextGradientKind { Linear, Radial, Angular }
public enum TextOutlinePlacement { Outside, Center, Inside }

public sealed class TextGradientStop
{
    public string Color { get; set; } = "#FFFFFFFF";
    public double Position { get; set; }
}

public sealed class TextGradient
{
    public bool Enabled { get; set; }
    public TextGradientKind Kind { get; set; }
    public double Angle { get; set; }
    public List<TextGradientStop> Stops { get; set; } = [new(), new() { Color = "#FF078BFA", Position = 1 }];
}

public sealed class TextGlow
{
    public bool Enabled { get; set; }
    public string Color { get; set; } = "#FF078BFA";
    public double Opacity { get; set; } = .8;
    public double Radius { get; set; } = 8;
    public double Intensity { get; set; } = 1;
    public bool InnerEnabled { get; set; }
    public string InnerColor { get; set; } = "#FFFFFFFF";
    public double InnerOpacity { get; set; } = .8;
    public double InnerRadius { get; set; } = 4;
    public double InnerIntensity { get; set; } = 1;
}

public sealed class TextExtrusion
{
    public bool Enabled { get; set; }
    public string Color { get; set; } = "#FF173558";
    public double Opacity { get; set; } = 1;
    public double Depth { get; set; } = 8;
    public double Angle { get; set; } = 45;
    public double Shading { get; set; } = .35;
}

public sealed class TextBackground
{
    public bool Enabled { get; set; }
    public bool PerLine { get; set; }
    public string Color { get; set; } = "#CC000000";
    public double Opacity { get; set; } = 1;
    public double PaddingX { get; set; } = 10;
    public double PaddingY { get; set; } = 6;
    public double Radius { get; set; } = 8;
    public string BorderColor { get; set; } = "#FFFFFFFF";
    public double BorderWidth { get; set; }
    public TextGradient Gradient { get; set; } = new();
}

public sealed class TextRelief
{
    public bool Enabled { get; set; }
    public double Strength { get; set; } = .65;
    public double Depth { get; set; } = 3;
    public double Angle { get; set; } = 45;
}

public sealed class TextRasterEffects
{
    public bool BlurEnabled { get; set; }
    public double BlurRadius { get; set; } = 4;
    public bool PixelationEnabled { get; set; }
    public int PixelSize { get; set; } = 6;
    public bool ChromaticEnabled { get; set; }
    public int ChromaticOffset { get; set; } = 4;
    public bool MetallicEnabled { get; set; }
    public string MetallicColor { get; set; } = "#FFB7C8DA";
    public double MetallicStrength { get; set; } = .8;
    public bool GlossEnabled { get; set; }
    public double GlossStrength { get; set; } = .6;
    public TextRelief Emboss { get; set; } = new();
    public TextRelief Inset { get; set; } = new();
}

public sealed class TextOutline
{
    public bool Enabled { get; set; }
    public string Color { get; set; } = "#FF000000";
    public double Width { get; set; } = 4;
    public double Opacity { get; set; } = 1;
    public TextOutlinePlacement Placement { get; set; }
}

public sealed class TextShadow
{
    public bool Enabled { get; set; }
    public string Color { get; set; } = "#FF000000";
    public double Opacity { get; set; } = .75;
    public double OffsetX { get; set; } = 4;
    public double OffsetY { get; set; } = 4;
    public double Blur { get; set; } = 4;
}

/// <summary>A text overlay in the coordinate space of the final 512-pixel sticker.</summary>
public sealed class TextLayer : System.ComponentModel.INotifyPropertyChanged
{
    private string name = "Text";
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => name; set { if (name == value) return; name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    public string Text { get; set; } = "";
    public string FontFamily { get; set; } = "Arial";
    // Relative to the bundled font catalog, never an arbitrary project file path.
    public string? FontAsset { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public double FontSize { get; set; } = 48;
    public string Color { get; set; } = "#FFFFFFFF";
    public double Opacity { get; set; } = 1;
    public double X { get; set; } = 256;
    public double Y { get; set; } = 256;
    public double Scale { get; set; } = 1;
    public double Rotation { get; set; }
    public TextLayerAlignment Alignment { get; set; } = TextLayerAlignment.Center;
    public double LineSpacing { get; set; } = 1;
    public double LetterSpacing { get; set; }
    public bool Underline { get; set; }
    public bool Strikethrough { get; set; }
    public TextCase Case { get; set; }
    public double StretchX { get; set; } = 1;
    public double StretchY { get; set; } = 1;
    public double Skew { get; set; }
    // Total angle of the baseline arc, in degrees; zero is a straight line.
    public double Curvature { get; set; }
    public List<TextOutline> Outlines { get; set; } = [];
    public TextShadow Shadow { get; set; } = new();
    public TextGlow Glow { get; set; } = new();
    public TextGradient Gradient { get; set; } = new();
    public TextExtrusion Extrusion { get; set; } = new();
    public TextBackground Background { get; set; } = new();
    public TextRasterEffects RasterEffects { get; set; } = new();
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
}

public enum FitMode { Fit, Fill, Stretch, Manual }

public sealed class TransformSettings
{
    public FitMode Fit { get; set; } = FitMode.Fit;
    public double Scale { get; set; } = 1;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Rotation { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public double StartSeconds { get; set; }
    public double DurationSeconds { get; set; } = 3;
}

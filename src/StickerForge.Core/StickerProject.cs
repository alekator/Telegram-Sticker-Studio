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

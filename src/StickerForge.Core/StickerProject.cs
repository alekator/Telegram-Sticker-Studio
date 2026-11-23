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
}

public enum FitMode { Fit, Fill, Stretch }

public sealed class TransformSettings
{
    public FitMode Fit { get; set; } = FitMode.Fit;
    public double Scale { get; set; } = 1;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Rotation { get; set; }
    public double StartSeconds { get; set; }
    public double DurationSeconds { get; set; } = 3;
}

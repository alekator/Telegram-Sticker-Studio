namespace StickerForge.Core;

public sealed class StickerProject
{
    public const int CurrentSchemaVersion = 2;
    public string Name { get; set; } = "Untitled pack";
    public List<StickerItem> Stickers { get; set; } = [];
}

public sealed class StickerItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string SourcePath { get; set; }
    public byte[]? EditableDocument { get; set; }
}

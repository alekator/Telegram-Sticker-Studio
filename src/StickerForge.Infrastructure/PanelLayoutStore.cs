using System.Text.Json;

namespace StickerForge.Infrastructure;

public sealed record PanelLayout(double GalleryWidth = 270, double PropertiesWidth = 300, bool GalleryVisible = true, bool PropertiesVisible = true);

public sealed class PanelLayoutStore
{
    private readonly string path;
    public PanelLayoutStore(string? path = null) => this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickerForge", "panel-layout.json");

    public PanelLayout Load()
    {
        try
        {
            if (!File.Exists(path)) return new();
            var layout = JsonSerializer.Deserialize<PanelLayout>(File.ReadAllText(path));
            if (layout is null || !double.IsFinite(layout.GalleryWidth) || !double.IsFinite(layout.PropertiesWidth)) return new();
            return layout with { GalleryWidth = Math.Clamp(layout.GalleryWidth, 180, 600), PropertiesWidth = Math.Clamp(layout.PropertiesWidth, 220, 500) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }

    public void Save(PanelLayout layout)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(layout));
            File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }
}

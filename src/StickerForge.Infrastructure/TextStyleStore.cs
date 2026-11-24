using System.Text.Json;
using StickerForge.Core;

namespace StickerForge.Infrastructure;

public sealed class TextStyleStore(string? path = null)
{
    private readonly string path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickerForge", "text-styles.json");
    public List<TextStyle> Load()
    {
        try { return JsonSerializer.Deserialize<List<TextStyle>>(File.ReadAllText(path)) ?? []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
    public void Save(IReadOnlyList<TextStyle> styles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(styles));
        File.Move(temp, path, true);
    }
}

using System.Text.Json;

namespace StickerForge.Infrastructure;

public sealed record FontPreferences(List<string> Favorites, List<string> Recent);

public sealed class FontPreferencesStore(string? path = null)
{
    private readonly string path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickerForge", "font-preferences.json");
    public FontPreferences Load()
    {
        try
        {
            var state = JsonSerializer.Deserialize<FontPreferences>(File.ReadAllText(path));
            return state is null ? new([], []) : new(state.Favorites ?? [], state.Recent ?? []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new([], []); }
    }
    public void Save(FontPreferences state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(state)); File.Move(tmp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

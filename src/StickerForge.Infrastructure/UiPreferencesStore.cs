using System.Text.Json;

namespace StickerForge.Infrastructure;

public sealed record UiPreferences(string Language = "ru", string Theme = "dark");

public sealed class UiPreferencesStore
{
    private readonly string path;
    public UiPreferencesStore(string? path = null) => this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickerForge", "ui-preferences.json");
    public UiPreferences Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(path));
            return value is null ? new() : new(value.Language is "ru" or "en" ? value.Language : "ru", value.Theme is "dark" or "light" or "system" ? value.Theme : "dark");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }
    public void Save(UiPreferences value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(value)); File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }
}

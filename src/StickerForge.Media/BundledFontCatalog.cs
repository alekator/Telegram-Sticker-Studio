using System.Text.Json;
using System.Text;
using SkiaSharp;
using StickerForge.Core;

namespace StickerForge.Media;

public sealed record BundledFontFile(string File, string Style, int Weight, string Sha256, long Bytes);
public sealed record BundledFontFamily(string Id, string Name, string Category, string[] Subsets, string LicenseSha256, BundledFontFile[] Fonts);
public sealed record BundledFontManifest(string Revision, string Source, BundledFontFamily[] Families);

/// <summary>Reads catalog metadata without loading any font faces into memory.</summary>
public static class BundledFontCatalog
{
    private static readonly Lazy<BundledFontManifest?> DefaultManifest = new(() => Read(DirectoryPath));
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> FallbackPaths = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Length, DateTime Modified)> VerifiedFiles = new();
    public static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "fonts");
    public static BundledFontManifest? Load(string? directory = null) => directory is null ? DefaultManifest.Value : Read(directory);
    private static BundledFontManifest? Read(string directory)
    {
        var path = Path.Combine(directory, "manifest.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<BundledFontManifest>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Font catalog is invalid.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Font catalog is damaged.", ex); }
    }

    public static string Resolve(string asset, string? directory = null)
    {
        var parts = asset.Split('/');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[0].Any(c => !(char.IsAsciiLetterOrDigit(c))) ||
            string.IsNullOrWhiteSpace(parts[1]) || Path.GetFileName(parts[1]) != parts[1] || parts[1].Contains('\\'))
            throw new InvalidDataException("The selected font identifier is invalid.");
        var path = Path.Combine(directory ?? DirectoryPath, parts[0], parts[1]);
        if (!File.Exists(path)) throw new InvalidDataException($"The selected bundled font is missing: {parts[0]}/{parts[1]}.");
        if (directory is null)
        {
            var entry = Load()?.Families.FirstOrDefault(f => f.Id == parts[0])?.Fonts.FirstOrDefault(f => f.File == parts[1])
                ?? throw new InvalidDataException("The selected font is not in the bundled catalog.");
            var info = new FileInfo(path);
            if (!VerifiedFiles.TryGetValue(path, out var verified) || verified.Length != info.Length || verified.Modified != info.LastWriteTimeUtc)
            {
                using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The selected bundled font failed its integrity check.");
                VerifiedFiles[path] = (info.Length, info.LastWriteTimeUtc);
            }
        }
        return path;
    }

    public static SKTypeface Typeface(TextLayer layer)
    {
        if (!string.IsNullOrEmpty(layer.FontAsset))
            return SKTypeface.FromFile(Resolve(layer.FontAsset)) ?? throw new InvalidDataException("The selected bundled font could not be opened.");
        var style = new SKFontStyle(layer.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, layer.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        return SKFontManager.Default.MatchFamily(layer.FontFamily, style) ?? SKTypeface.Default;
    }

    public static SKTypeface? Fallback(string element)
    {
        var path = FallbackPaths.GetOrAdd(element, FindFallbackPath);
        return path.Length == 0 ? null : SKTypeface.FromFile(path);
    }

    private static string FindFallbackPath(string element)
    {
        var meaningful = element.EnumerateRunes().Where(r => System.Text.Rune.GetUnicodeCategory(r) != System.Globalization.UnicodeCategory.Format).Select(r => r.Value).ToArray();
        if (meaningful.Length == 0) return "";
        var rune = meaningful[0];
        var subset = rune switch { >= 0x1f000 => "emoji", >= 0x400 and <= 0x52f => "cyrillic", >= 0x370 and <= 0x3ff => "greek", >= 0x590 and <= 0x5ff => "hebrew", >= 0x600 and <= 0x8ff => "arabic", >= 0x900 and <= 0x97f => "devanagari", _ => "latin" };
        var catalog = Load(); if (catalog is null) return "";
        foreach (var family in catalog.Families.Where(f => f.Subsets.Contains(subset) || f.Id == "notoemoji"))
        {
            if (family.Fonts.Length == 0) continue;
            var path = Path.Combine(DirectoryPath, family.Id, family.Fonts[0].File);
            if (!File.Exists(path)) continue;
            using var typeface = SKTypeface.FromFile(path); if (typeface is null) continue;
            using var font = new SKFont(typeface, 20);
            if (font.GetGlyphs(meaningful).All(glyph => glyph != 0)) return Resolve(family.Id + "/" + family.Fonts[0].File);
        }
        return "";
    }
}

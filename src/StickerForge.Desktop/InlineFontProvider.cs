using Avalonia.Media;
using Avalonia.Media.Fonts;
using StickerForge.Core;
using StickerForge.Media;

namespace StickerForge.Desktop;

/// <summary>Registers only the currently edited bundled face with Avalonia.</summary>
internal sealed class InlineFontProvider : IDisposable
{
    private sealed class RuntimeCollection(Uri key) : FontCollectionBase
    {
        public override Uri Key { get; } = key;
    }

    private Uri? currentKey;
    private readonly Uri collectionKey = new($"fonts:StickerForgeInline{Guid.NewGuid():N}", UriKind.Absolute);
    private string? currentAsset;
    private FontFamily? currentFamily;

    public FontFamily Resolve(TextLayer layer)
    {
        if (string.IsNullOrEmpty(layer.FontAsset)) { Clear(); return new FontFamily(layer.FontFamily); }
        if (currentAsset == layer.FontAsset && currentFamily is not null) return currentFamily;
        Clear();
        var path = BundledFontCatalog.Resolve(layer.FontAsset);
        var key = collectionKey;
        var collection = new RuntimeCollection(key);
        using var stream = File.OpenRead(path);
        if (!collection.TryAddGlyphTypeface(stream, out var glyph) || glyph is null) throw new InvalidDataException("The selected font could not be loaded for editing.");
        FontManager.Current.AddFontCollection(collection);
        currentKey = key; currentAsset = layer.FontAsset;
        return currentFamily = new FontFamily($"{key}#{glyph.FamilyName}");
    }

    private void Clear()
    {
        if (currentKey is not null) FontManager.Current.RemoveFontCollection(currentKey);
        currentKey = null; currentAsset = null; currentFamily = null;
    }
    public void Dispose() => Clear();
}

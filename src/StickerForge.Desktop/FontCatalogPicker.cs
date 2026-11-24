using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using StickerForge.Core;
using StickerForge.Media;
using StickerForge.Infrastructure;

namespace StickerForge.Desktop;

/// <summary>A virtualized catalog; preview bitmaps and typefaces exist only for realized rows.</summary>
internal sealed class FontCatalogPicker : StackPanel
{
    private sealed record Choice(string Id, string Name, BundledFontFamily? Family);
    private readonly FontPreferencesStore stateStore;
    private readonly List<string> favorites = [];
    private readonly List<string> recent = [];
    private readonly List<Choice> choices = [];
    private readonly ListBox list = new() { Height = 200 };
    private readonly Dictionary<string, bool> systemCyrillic = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox search = new() { PlaceholderText = "Поиск шрифта" };
    private readonly ComboBox categories = new();
    private readonly CheckBox cyrillic = new() { Content = "Кириллица" };
    private readonly ComboBox scripts = new();
    private string[] scriptValues = [];
    private readonly TextLayer layer;
    private readonly Action changed;
    private readonly string language;

    public FontCatalogPicker(TextLayer layer, StudioTheme theme, Action changed, string language, FontPreferencesStore? stateStore = null)
    {
        this.layer = layer; this.changed = changed; this.language = language; this.stateStore = stateStore ?? new(); Spacing = 5;
        var state = this.stateStore.Load(); favorites.AddRange(state.Favorites); recent.AddRange(state.Recent);
        BundledFontManifest? catalog;
        try { catalog = BundledFontCatalog.Load(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { catalog = null; }
        if (catalog is not null) choices.AddRange(catalog.Families.Select(family => new Choice(family.Id, family.Name, family)));
        choices.AddRange(SKFontManager.Default.FontFamilies.OrderBy(x => x).Select(name => new Choice("system:" + name, name, null)));
        choices.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        search.PlaceholderText = Tr("Поиск шрифта", "Search fonts"); Children.Add(search);
        categories.ItemsSource = new[] { Tr("Все", "All"), Tr("Избранные", "Favorites"), Tr("Недавние", "Recent"), Tr("С засечками", "Serif"), Tr("Без засечек", "Sans serif"), Tr("Декоративные", "Display"), Tr("Рукописные", "Handwriting"), Tr("Моноширинные", "Monospace"), Tr("Системные", "System"), Tr("Пиксельные", "Pixel"), Tr("Готические", "Gothic"), Tr("Комиксы", "Comics"), Tr("Ретро", "Retro"), Tr("Каллиграфические", "Calligraphy"), Tr("Мемы", "Memes"), Tr("Современные", "Modern") };
        categories.SelectedIndex = 0; categories.HorizontalAlignment = HorizontalAlignment.Stretch; Children.Add(categories);
        cyrillic.Content = Tr("Кириллица", "Cyrillic"); Children.Add(cyrillic);
        if (catalog is null) Children.Add(new TextBlock { Text = Tr("Встроенная библиотека шрифтов отсутствует.", "Bundled font library is unavailable."), TextWrapping = TextWrapping.Wrap, Foreground = theme.Secondary });
        scriptValues = catalog?.Families.SelectMany(f => f.Subsets).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray() ?? [];
        scripts.ItemsSource = new[] { Tr("Все письменности", "All scripts") }.Concat(scriptValues).ToArray(); scripts.SelectedIndex = 0; Children.Add(scripts);
        list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        list.ItemTemplate = new FuncDataTemplate<Choice>((choice, _) => choice is null ? new TextBlock() : MakeRow(choice, theme));
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not Choice choice) return;
            layer.FontFamily = choice.Name;
            if (choice.Family is { Fonts.Length: > 0 } family)
            {
                var face = family.Fonts[0]; layer.FontAsset = family.Id + "/" + face.File;
                layer.Bold = face.Weight >= 600; layer.Italic = face.Style == "italic";
            }
            else { layer.FontAsset = null; layer.Bold = false; layer.Italic = false; }
            recent.Remove(choice.Id); recent.Insert(0, choice.Id); if (recent.Count > 20) recent.RemoveRange(20, recent.Count - 20);
            Save(); changed();
        };
        Children.Add(list);
        search.TextChanged += (_, _) => Filter(); categories.SelectionChanged += (_, _) => Filter(); cyrillic.IsCheckedChanged += (_, _) => Filter(); scripts.SelectionChanged += (_, _) => Filter();
        Filter();
    }
    public void RefreshPreview() => Filter();

    private Control MakeRow(Choice choice, StudioTheme theme)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4, Margin = new Thickness(0, 2) };
        var info = new StackPanel { Spacing = 1 };
        info.Children.Add(new TextBlock { Text = choice.Name, FontSize = 12, Foreground = theme.Secondary, TextTrimming = TextTrimming.CharacterEllipsis });
        var sample = string.IsNullOrWhiteSpace(layer.Text) ? "Привет! Hello!" : layer.Text.Replace('\n', ' ');
        if (sample.Length > 60) sample = sample[..60];
        if (choice.Family is { Fonts.Length: > 0 } family)
        {
            try
            {
                var previewLayer = new TextLayer { Text = sample, FontFamily = choice.Name, FontAsset = family.Id + "/" + family.Fonts[0].File, FontSize = 22, X = 2, Y = 18, Alignment = TextLayerAlignment.Left, Color = "#FF" + theme.Text.Color.R.ToString("X2") + theme.Text.Color.G.ToString("X2") + theme.Text.Color.B.ToString("X2") };
                using var image = TextLayerRenderer.RenderOverlay([previewLayer]); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = new MemoryStream(encoded.ToArray()); var bitmap = new Bitmap(stream);
                var view = new Image { Source = new CroppedBitmap(bitmap, new PixelRect(0, 0, 512, 40)), Height = 30, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                view.DetachedFromVisualTree += (_, _) => bitmap.Dispose(); info.Children.Add(view);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { info.Children.Add(new TextBlock { Text = sample }); }
        }
        else info.Children.Add(new TextBlock { Text = sample, FontFamily = new FontFamily(choice.Name), FontSize = 18, TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(info);
        var star = new Button { Content = favorites.Contains(choice.Id) ? "★" : "☆", MinHeight = 30, Padding = new Thickness(6), Background = theme.Panel, Foreground = theme.Accent };
        ToolTip.SetTip(star, Tr("Избранные", "Favorites"));
        star.PointerPressed += (_, e) => e.Handled = true;
        star.Click += (_, e) => { if (!favorites.Remove(choice.Id)) favorites.Add(choice.Id); Save(); star.Content = favorites.Contains(choice.Id) ? "★" : "☆"; if (categories.SelectedIndex == 1) Filter(); e.Handled = true; };
        Grid.SetColumn(star, 1); row.Children.Add(star); return row;
    }
    private void Filter()
    {
        var query = search.Text?.Trim() ?? "";
        var category = categories.SelectedIndex;
        var filtered = choices.Where(choice => choice.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Where(choice => cyrillic.IsChecked != true || (choice.Family?.Subsets.Contains("cyrillic") ?? SupportsCyrillic(choice.Name)))
            .Where(choice => scripts.SelectedIndex <= 0 || (choice.Family?.Subsets.Contains(scriptValues[scripts.SelectedIndex - 1]) ?? false))
            .Where(choice => category switch { 1 => favorites.Contains(choice.Id), 2 => recent.Contains(choice.Id), 3 => choice.Family?.Category == "SERIF", 4 => choice.Family?.Category == "SANS_SERIF", 5 => choice.Family?.Category == "DISPLAY", 6 => choice.Family?.Category == "HANDWRITING", 7 => choice.Family?.Category == "MONOSPACE", 8 => choice.Family is null, 9 => ContainsAny(choice, "pixel", "pressstart", "vt323"), 10 => ContainsAny(choice, "fraktur", "gothic"), 11 => ContainsAny(choice, "comic", "bangers"), 12 => ContainsAny(choice, "retro", "specialelite", "vt323", "orbitron"), 13 => ContainsAny(choice, "script", "callig", "greatvibes", "allura", "dancing"), 14 => ContainsAny(choice, "anton", "bangers", "bebas", "russoone", "luckiest"), 15 => ContainsAny(choice, "inter", "roboto", "montserrat", "dmsans"), _ => true });
        if (category == 2) filtered = filtered.OrderBy(choice => recent.IndexOf(choice.Id));
        list.ItemsSource = filtered.ToArray();
    }
    private static bool ContainsAny(Choice choice, params string[] keywords) => keywords.Any(keyword => choice.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) || choice.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    private bool SupportsCyrillic(string name)
    {
        if (systemCyrillic.TryGetValue(name, out var result)) return result;
        using var typeface = SKFontManager.Default.MatchFamily(name); if (typeface is null) return systemCyrillic[name] = false;
        using var font = new SKFont(typeface, 16);
        return systemCyrillic[name] = font.GetGlyphs("Привет").All(glyph => glyph != 0);
    }
    private void Save() => stateStore.Save(new FontPreferences(favorites, recent));
    private string Tr(string ru, string en) => language == "en" ? en : ru;
}

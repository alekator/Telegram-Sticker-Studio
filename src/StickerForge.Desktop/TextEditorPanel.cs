using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Data;
using SkiaSharp;
using StickerForge.Core;
using StickerForge.Media;
using StickerForge.Infrastructure;

namespace StickerForge.Desktop;

/// <summary>Controls text-layer state without owning the project or the canvas.</summary>
internal sealed class TextEditorPanel : StackPanel
{
    private readonly Action changed;
    private readonly Action selectionChanged;
    private readonly StudioTheme theme;
    private readonly FontPreferencesStore fontPreferences;
    private StickerItem? item;
    private TextLayer? selected;
    private bool updating;
    private TextBox? textField;
    private string language = "ru";

    public TextLayer? Selected => selected;
    public Action? LayerAdded { get; set; }
    public Action<Action<string>>? SampleCanvasColor { get; set; }
    public TextEditorPanel(StudioTheme theme, Action changed, Action selectionChanged, FontPreferencesStore? fontPreferences = null)
    {
        this.theme = theme;
        this.fontPreferences = fontPreferences ?? new();
        this.changed = changed;
        this.selectionChanged = selectionChanged;
        Spacing = 8;
    }

    public void SetItem(StickerItem? value)
    {
        if (ReferenceEquals(item, value)) return;
        item = value; selected = value?.TextLayers.LastOrDefault();
        Rebuild();
    }

    public void SetLanguage(string value) { if (language != value) { language = value; Rebuild(); } else StudioLocalization.Apply(this, language); }

    public void Select(TextLayer? layer)
    {
        selected = layer;
        Rebuild();
        selectionChanged();
    }

    public void RefreshFields() => Rebuild();
    public void FocusText() { textField?.Focus(); textField?.SelectAll(); }

    private void Rebuild()
    {
        updating = true;
        Children.Clear();
        if (item is null) { updating = false; return; }
        var layerControls = new StackPanel { Spacing = 4 };
        Children.Add(Section("Слои текста", layerControls));
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Добавить текст", () => { var layer = new TextLayer { Name = $"{StudioLocalization.Text("Текст", language)} {item.TextLayers.Count + 1}", Text = StudioLocalization.Text("Текст", language) }; item.TextLayers.Add(layer); selected = layer; ChangedAndRebuild(); LayerAdded?.Invoke(); }));
        actions.Children.Add(ActionButton("Дублировать", () => { if (selected is null) return; var copy = System.Text.Json.JsonSerializer.Deserialize<TextLayer>(System.Text.Json.JsonSerializer.Serialize(selected))!; copy.Id = Guid.NewGuid(); copy.X += 12; copy.Y += 12; item.TextLayers.Insert(item.TextLayers.IndexOf(selected) + 1, copy); selected = copy; ChangedAndRebuild(); }));
        var delete = ActionButton("Удалить", () => { if (selected is null || selected.IsLocked) return; item.TextLayers.Remove(selected); selected = item.TextLayers.LastOrDefault(); ChangedAndRebuild(); });
        delete.IsEnabled = selected is { IsLocked: false }; actions.Children.Add(delete);
        layerControls.Children.Add(actions);
        var list = new ListBox { MaxHeight = 150, ItemsSource = item.TextLayers.ToArray(), SelectedItem = selected };
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TextLayer>((layer, _) => { var label = new TextBlock { Margin = new Thickness(4) }; label.Bind(TextBlock.TextProperty, new Binding(nameof(TextLayer.Name)) { Source = layer }); return label; });
        list.SelectionChanged += (_, _) => { if (!updating) Select(list.SelectedItem as TextLayer); };
        layerControls.Children.Add(list);
        if (selected is not { } current) { StudioLocalization.Capture(this); StudioLocalization.Apply(this, language); updating = false; return; }
        var order = new WrapPanel();
        order.Children.Add(ActionButton("Вверх", () => Move(1)));
        order.Children.Add(ActionButton("Вниз", () => Move(-1)));
        var visible = new CheckBox { Content = "Видимый", IsChecked = current.IsVisible, Margin = new Thickness(4) };
        visible.IsCheckedChanged += (_, _) => { current.IsVisible = visible.IsChecked == true; changed(); };
        var locked = new CheckBox { Content = "Заблокирован", IsChecked = current.IsLocked, Margin = new Thickness(4) };
        locked.IsCheckedChanged += (_, _) => { current.IsLocked = locked.IsChecked == true; ChangedAndRebuild(); };
        order.Children.Add(visible); order.Children.Add(locked); layerControls.Children.Add(order);
        var fields = new StackPanel { Spacing = 6, IsEnabled = !current.IsLocked };
        fields.Children.Add(new TextBlock { Text = "Название слоя" });
        var name = new TextBox { Text = current.Name };
        name.TextChanged += (_, _) => { if (!updating) { current.Name = name.Text ?? ""; changed(); } }; fields.Children.Add(name);
        fields.Children.Add(new TextBlock { Text = "Текст" });
        FontCatalogPicker? activePicker = null;
        var text = new TextBox { Text = current.Text, AcceptsReturn = true, MinHeight = 56, TextWrapping = TextWrapping.Wrap }; textField = text;
        text.TextChanged += (_, _) => { if (!updating) { current.Text = text.Text ?? ""; changed(); activePicker?.RefreshPreview(); } }; fields.Children.Add(text);
        fields.Children.Add(new TextBlock { Text = "Шрифт" });
        var family = ActionButton(current.FontFamily, () => { });
        family.HorizontalAlignment = HorizontalAlignment.Stretch; family.HorizontalContentAlignment = HorizontalAlignment.Left;
        var fontPickerHost = new Flyout { Placement = PlacementMode.Top };
        family.Click += (_, _) =>
        {
            activePicker = new FontCatalogPicker(current, theme, () => { fontPickerHost.Hide(); changed(); Rebuild(); selectionChanged(); }, language, fontPreferences);
            fontPickerHost.Content = new Border { Width = 330, Padding = new Thickness(10), Background = theme.Panel, BorderBrush = theme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = activePicker };
            fontPickerHost.ShowAt(family);
        };
        fields.Children.Add(family);
        var bundledFamily = string.IsNullOrEmpty(current.FontAsset) ? null : BundledFontCatalog.Load()?.Families.FirstOrDefault(f => current.FontAsset.StartsWith(f.Id + "/", StringComparison.Ordinal));
        if (bundledFamily is not null)
        {
            fields.Children.Add(new TextBlock { Text = "Начертание" });
            var face = new ComboBox { ItemsSource = bundledFamily.Fonts.Select(f => $"{f.Style} · {f.Weight}").ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
            face.SelectedIndex = Array.FindIndex(bundledFamily.Fonts, f => current.FontAsset == bundledFamily.Id + "/" + f.File);
            face.SelectionChanged += (_, _) =>
            {
                if (updating || face.SelectedIndex < 0) return;
                var selectedFace = bundledFamily.Fonts[face.SelectedIndex]; current.FontAsset = bundledFamily.Id + "/" + selectedFace.File;
                current.Bold = selectedFace.Weight >= 600; current.Italic = selectedFace.Style == "italic"; changed(); Rebuild(); selectionChanged();
            };
            fields.Children.Add(face);
        }
        var fontActions = new WrapPanel();
        var bold = new CheckBox { Content = "Жирный", IsChecked = current.Bold, Margin = new Thickness(4) };
        bold.IsCheckedChanged += (_, _) => { current.Bold = bold.IsChecked == true; changed(); };
        var italic = new CheckBox { Content = "Курсив", IsChecked = current.Italic, Margin = new Thickness(4) };
        italic.IsCheckedChanged += (_, _) => { current.Italic = italic.IsChecked == true; changed(); };
        bold.IsEnabled = italic.IsEnabled = bundledFamily is null;
        fontActions.Children.Add(bold); fontActions.Children.Add(italic); fields.Children.Add(fontActions);
        var decorations = new WrapPanel();
        var underline = new CheckBox { Content = StudioLocalization.Text("Подчёркивание", language), IsChecked = current.Underline, Margin = new Thickness(4) };
        underline.IsCheckedChanged += (_, _) => { current.Underline = underline.IsChecked == true; changed(); };
        var strike = new CheckBox { Content = StudioLocalization.Text("Зачёркивание", language), IsChecked = current.Strikethrough, Margin = new Thickness(4) };
        strike.IsCheckedChanged += (_, _) => { current.Strikethrough = strike.IsChecked == true; changed(); };
        decorations.Children.Add(underline); decorations.Children.Add(strike); fields.Children.Add(decorations);
        fields.Children.Add(new TextBlock { Text = "Регистр" });
        var textCase = new ComboBox { ItemsSource = new[] { StudioLocalization.Text("Без изменений", language), StudioLocalization.Text("Верхний", language), StudioLocalization.Text("Нижний", language) }, SelectedIndex = (int)current.Case, HorizontalAlignment = HorizontalAlignment.Stretch };
        textCase.SelectionChanged += (_, _) => { if (!updating && textCase.SelectedIndex >= 0) { current.Case = (TextCase)textCase.SelectedIndex; changed(); } }; fields.Children.Add(textCase);
        fields.Children.Add(Number("Размер", current.FontSize, 1, 512, value => current.FontSize = value));
        fields.Children.Add(new TextBlock { Text = "Цвет (#AARRGGBB)" });
        var color = new TextBox { Text = current.Color };
        color.LostFocus += (_, _) => { if (SKColor.TryParse(color.Text, out _)) { current.Color = color.Text!; changed(); } else color.Text = current.Color; }; fields.Children.Add(color);
        fields.Children.Add(PaletteButton(() => current.Color, value => { current.Color = value; color.Text = value; changed(); }));
        var palette = new WrapPanel();
        foreach (var shade in new[] { "#FFFFFFFF", "#FF000000", "#FFFF3B30", "#FFFFCC00", "#FF34C759", "#FF078BFA", "#FFAF52DE" })
        {
            var swatch = new Button { Width = 28, Height = 28, MinHeight = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 5, 0), Background = new SolidColorBrush(Avalonia.Media.Color.Parse(shade)), BorderBrush = theme.Border, BorderThickness = new Thickness(1) };
            ToolTip.SetTip(swatch, shade);
            swatch.Click += (_, _) => { current.Color = shade; color.Text = shade; changed(); };
            palette.Children.Add(swatch);
        }
        fields.Children.Add(palette);
        fields.Children.Add(Number("Прозрачность, %", current.Opacity * 100, 0, 100, value => current.Opacity = value / 100));
        var placement = new StackPanel { Spacing = 6, IsEnabled = !current.IsLocked };
        placement.Children.Add(Number("Положение X", current.X, -1024, 1536, value => current.X = value));
        placement.Children.Add(Number("Положение Y", current.Y, -1024, 1536, value => current.Y = value));
        placement.Children.Add(Number("Масштаб, %", current.Scale * 100, 1, 2000, value => current.Scale = value / 100));
        placement.Children.Add(Number("Поворот, °", current.Rotation, -360, 360, value => current.Rotation = value));
        fields.Children.Add(new TextBlock { Text = "Выравнивание" });
        var alignment = new ComboBox { ItemsSource = new[] { StudioLocalization.Text("Слева", language), StudioLocalization.Text("По центру", language), StudioLocalization.Text("Справа", language) }, SelectedIndex = (int)current.Alignment, HorizontalAlignment = HorizontalAlignment.Stretch };
        alignment.SelectionChanged += (_, _) => { if (!updating && alignment.SelectedIndex >= 0) { current.Alignment = (TextLayerAlignment)alignment.SelectedIndex; changed(); } }; fields.Children.Add(alignment);
        fields.Children.Add(Number("Межстрочный интервал", current.LineSpacing * 100, 10, 1000, value => current.LineSpacing = value / 100));
        fields.Children.Add(Number("Межбуквенный интервал", current.LetterSpacing, -100, 100, value => current.LetterSpacing = value));
        fields.Children.Add(Number("Растяжение X, %", current.StretchX * 100, 10, 1000, value => current.StretchX = value / 100));
        fields.Children.Add(Number("Растяжение Y, %", current.StretchY * 100, 10, 1000, value => current.StretchY = value / 100));
        fields.Children.Add(Number("Наклон, %", current.Skew * 100, -200, 200, value => current.Skew = value / 100));
        Children.Add(Section("Текст", fields));
        var outlines = new StackPanel { Spacing = 6, IsEnabled = !current.IsLocked };
        current.Outlines ??= [];
        var outlineList = new StackPanel { Spacing = 6 };
        void RefreshOutlines()
        {
            outlineList.Children.Clear();
            foreach (var outline in current.Outlines.ToArray())
            {
                var row = new StackPanel { Spacing = 6 };
                var enabled = new CheckBox { Content = StudioLocalization.Text("Включить обводку", language), IsChecked = outline.Enabled };
                var values = new StackPanel { Spacing = 6, IsVisible = outline.Enabled };
                values.Children.Add(ColorField("Цвет обводки (#AARRGGBB)", outline.Color, value => outline.Color = value));
                values.Children.Add(Number("Толщина", outline.Width, 0, 100, value => outline.Width = value));
                values.Children.Add(Number("Прозрачность, %", outline.Opacity * 100, 0, 100, value => outline.Opacity = value / 100));
                var placementMode = new ComboBox { ItemsSource = new[] { StudioLocalization.Text("Снаружи", language), StudioLocalization.Text("По центру обводки", language), StudioLocalization.Text("Внутри", language) }, SelectedIndex = (int)outline.Placement, HorizontalAlignment = HorizontalAlignment.Stretch };
                placementMode.SelectionChanged += (_, _) => { if (placementMode.SelectedIndex >= 0) { outline.Placement = (TextOutlinePlacement)placementMode.SelectedIndex; changed(); } };
                values.Children.Add(placementMode);
                enabled.IsCheckedChanged += (_, _) => { outline.Enabled = enabled.IsChecked == true; values.IsVisible = outline.Enabled; changed(); };
                row.Children.Add(enabled); row.Children.Add(values);
                row.Children.Add(ActionButton(StudioLocalization.Text("Удалить обводку", language), () => { current.Outlines.Remove(outline); RefreshOutlines(); changed(); }));
                outlineList.Children.Add(row);
            }
        }
        RefreshOutlines(); outlines.Children.Add(outlineList);
        outlines.Children.Add(ActionButton(StudioLocalization.Text("Добавить обводку", language), () => { current.Outlines.Add(new() { Enabled = true }); RefreshOutlines(); changed(); }));
        Children.Add(Section("Обводка", outlines));
        var shadow = current.Shadow ??= new();
        var shadowPanel = new StackPanel { Spacing = 6, IsEnabled = !current.IsLocked };
        var shadowEnabled = new CheckBox { Content = StudioLocalization.Text("Включить тень", language), IsChecked = shadow.Enabled };
        var shadowFields = new StackPanel { Spacing = 6, IsVisible = shadow.Enabled };
        shadowFields.Children.Add(ColorField("Цвет тени (#AARRGGBB)", shadow.Color, value => shadow.Color = value));
        shadowFields.Children.Add(Number("Прозрачность, %", shadow.Opacity * 100, 0, 100, value => shadow.Opacity = value / 100));
        shadowFields.Children.Add(Number("Смещение X", shadow.OffsetX, -512, 512, value => shadow.OffsetX = value));
        shadowFields.Children.Add(Number("Смещение Y", shadow.OffsetY, -512, 512, value => shadow.OffsetY = value));
        shadowFields.Children.Add(Number("Размытие", shadow.Blur, 0, 100, value => shadow.Blur = value));
        shadowEnabled.IsCheckedChanged += (_, _) => { shadow.Enabled = shadowEnabled.IsChecked == true; shadowFields.IsVisible = shadow.Enabled; changed(); };
        shadowPanel.Children.Add(shadowEnabled); shadowPanel.Children.Add(shadowFields); Children.Add(Section("Тень", shadowPanel));
        var glow = current.Glow ??= new();
        var glowPanel = new StackPanel { Spacing = 6, IsEnabled = !current.IsLocked };
        var glowEnabled = new CheckBox { Content = StudioLocalization.Text("Внешнее свечение", language), IsChecked = glow.Enabled };
        var glowFields = new StackPanel { Spacing = 6, IsVisible = glow.Enabled };
        glowFields.Children.Add(ColorField("Цвет свечения (#AARRGGBB)", glow.Color, value => glow.Color = value));
        glowFields.Children.Add(Number("Прозрачность, %", glow.Opacity * 100, 0, 100, value => glow.Opacity = value / 100));
        glowFields.Children.Add(Number("Радиус", glow.Radius, 0, 100, value => glow.Radius = value));
        glowEnabled.IsCheckedChanged += (_, _) => { glow.Enabled = glowEnabled.IsChecked == true; glowFields.IsVisible = glow.Enabled; changed(); };
        glowPanel.Children.Add(glowEnabled); glowPanel.Children.Add(glowFields); Children.Add(Section("Свечение", glowPanel));
        var gradient = current.Gradient ??= new();
        gradient.Stops ??= [];
        var gradientPanel = new StackPanel { Spacing = 6, IsEnabled = !current.IsLocked };
        var gradientEnabled = new CheckBox { Content = StudioLocalization.Text("Градиентная заливка", language), IsChecked = gradient.Enabled };
        var gradientFields = new StackPanel { Spacing = 6, IsVisible = gradient.Enabled };
        var kind = new ComboBox { ItemsSource = new[] { StudioLocalization.Text("Линейный", language), StudioLocalization.Text("Радиальный", language), StudioLocalization.Text("Угловой", language) }, SelectedIndex = (int)gradient.Kind, HorizontalAlignment = HorizontalAlignment.Stretch };
        kind.SelectionChanged += (_, _) => { if (!updating && kind.SelectedIndex >= 0) { gradient.Kind = (TextGradientKind)kind.SelectedIndex; changed(); } };
        gradientFields.Children.Add(kind);
        gradientFields.Children.Add(Number("Угол, °", gradient.Angle, -360, 360, value => gradient.Angle = value));
        var stopsPanel = new StackPanel { Spacing = 6 };
        void RefreshStops()
        {
            stopsPanel.Children.Clear();
            foreach (var stop in gradient.Stops.ToArray())
            {
                var row = new StackPanel { Spacing = 4 };
                row.Children.Add(ColorField("Цвет точки (#AARRGGBB)", stop.Color, value => stop.Color = value));
                row.Children.Add(Number("Положение точки, %", stop.Position * 100, 0, 100, value => stop.Position = value / 100));
                var remove = ActionButton(StudioLocalization.Text("Удалить точку", language), () => { gradient.Stops.Remove(stop); RefreshStops(); changed(); });
                remove.IsEnabled = gradient.Stops.Count > 2; row.Children.Add(remove); stopsPanel.Children.Add(row);
            }
        }
        RefreshStops(); gradientFields.Children.Add(stopsPanel);
        gradientFields.Children.Add(ActionButton(StudioLocalization.Text("Добавить точку", language), () => { gradient.Stops.Add(new() { Position = .5 }); RefreshStops(); changed(); }));
        gradientEnabled.IsCheckedChanged += (_, _) => { gradient.Enabled = gradientEnabled.IsChecked == true; gradientFields.IsVisible = gradient.Enabled; changed(); };
        gradientPanel.Children.Add(gradientEnabled); gradientPanel.Children.Add(gradientFields); Children.Add(Section("Заливка", gradientPanel));
        Children.Add(Section("Положение и поворот", placement));
        StudioLocalization.Capture(this);
        StudioLocalization.Apply(this, language);
        updating = false;
    }

    private Border Section(string title, Control content)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }); stack.Children.Add(content);
        return new Border { Background = theme.Surface, BorderBrush = theme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Child = stack };
    }

    private Control Number(string label, double value, double min, double max, Action<double> setter)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,100"), ColumnSpacing = 8 };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var input = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)Math.Clamp(value, min, max), Increment = 1, ShowButtonSpinner = false, FormatString = "0.##", Height = 34 };
        input.ValueChanged += (_, _) => { if (!updating && input.Value is { } next) { setter((double)next); changed(); } };
        Grid.SetColumn(input, 1); grid.Children.Add(input); return grid;
    }

    private Control ColorField(string label, string value, Action<string> setter)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        var input = new TextBox { Text = value };
        input.LostFocus += (_, _) => { if (SKColor.TryParse(input.Text, out _)) { value = input.Text!; setter(value); changed(); } else input.Text = value; };
        stack.Children.Add(input);
        stack.Children.Add(PaletteButton(() => value, next => { value = next; input.Text = next; setter(next); changed(); }));
        return stack;
    }

    private Button PaletteButton(Func<string> value, Action<string> set)
    {
        var button = ActionButton(StudioLocalization.Text("Выбрать цвет", language), () => { });
        var popup = new Flyout { Placement = PlacementMode.Top };
        button.Click += (_, _) =>
        {
            var picker = new ColorPalettePicker(value(), theme, language, set, SampleCanvasColor is null ? null : callback => { popup.Hide(); SampleCanvasColor(callback); });
            popup.Content = new ScrollViewer { MaxHeight = 500, Content = picker, Background = theme.Panel };
            popup.ShowAt(button);
        };
        return button;
    }

    private Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 4), MinHeight = 30, Background = theme.Panel, BorderBrush = theme.Border, BorderThickness = new Thickness(1) };
        button.Click += (_, _) => action(); return button;
    }

    private void Move(int delta)
    {
        if (item is null || selected is null || selected.IsLocked) return;
        var index = item.TextLayers.IndexOf(selected); var target = index + delta;
        if (target < 0 || target >= item.TextLayers.Count) return;
        item.TextLayers.RemoveAt(index); item.TextLayers.Insert(target, selected); ChangedAndRebuild();
    }
    private void ChangedAndRebuild() { changed(); Rebuild(); selectionChanged(); }
}

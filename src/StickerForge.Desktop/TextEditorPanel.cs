using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Data;
using SkiaSharp;
using StickerForge.Core;
using StickerForge.Media;
using StickerForge.Infrastructure;

namespace StickerForge.Desktop;

/// <summary>Controls text-layer state without owning the project or the canvas.</summary>
internal sealed class TextEditorPanel : StackPanel
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> StylePreviewCache = new();
    private readonly Action changed;
    private readonly Action selectionChanged;
    private readonly StudioTheme theme;
    private readonly FontPreferencesStore fontPreferences;
    private readonly TextStyleStore styleStore = new();
    private StickerItem? item;
    private TextLayer? selected;
    private bool updating;
    private TextBox? textField;
    private string language = "ru";
    private readonly Dictionary<string, bool> expandedSections = new(StringComparer.Ordinal);

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
        fields.Children.Add(Number("Кривизна, °", current.Curvature, -300, 300, value => current.Curvature = value));
        Children.Add(Section("Текст", fields));
        var stylesPanel = new StackPanel { Spacing = 6, IsEnabled = !current.IsLocked };
        var styleError = new TextBlock { Foreground = theme.Secondary, TextWrapping = TextWrapping.Wrap };
        var presetsPanel = new WrapPanel();
        foreach (var preset in TextStylePresets.Create()) presetsPanel.Children.Add(StyleButton(preset, current));
        stylesPanel.Children.Add(presetsPanel);
        var custom = styleStore.Load();
        var customPanel = new StackPanel { Spacing = 4 };
        void SaveStyles()
        {
            try { styleStore.Save(custom); styleError.Text = ""; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { styleError.Text = language == "en" ? "Could not save styles." : "Не удалось сохранить стили."; }
        }
        void RefreshCustomStyles()
        {
            customPanel.Children.Clear();
            foreach (var style in custom.ToArray())
            {
                var row = new StackPanel { Spacing = 4 };
                var styleName = new TextBox { Text = style.Name, PlaceholderText = language == "en" ? "Style name" : "Название стиля" };
                styleName.LostFocus += (_, _) => { var nameValue = styleName.Text?.Trim(); if (!string.IsNullOrEmpty(nameValue)) { style.Name = nameValue; SaveStyles(); } else styleName.Text = style.Name; };
                row.Children.Add(styleName); var actionsRow = new WrapPanel(); actionsRow.Children.Add(StyleButton(style, current));
                actionsRow.Children.Add(ActionButton(StudioLocalization.Text("Удалить стиль", language), () => { custom.Remove(style); SaveStyles(); RefreshCustomStyles(); }));
                row.Children.Add(actionsRow); customPanel.Children.Add(row);
            }
        }
        RefreshCustomStyles(); stylesPanel.Children.Add(customPanel);
        var newStyleName = new TextBox { PlaceholderText = language == "en" ? "New style name" : "Название нового стиля" }; stylesPanel.Children.Add(newStyleName);
        stylesPanel.Children.Add(ActionButton(StudioLocalization.Text("Сохранить стиль", language), () =>
        {
            var styleName = newStyleName.Text?.Trim();
            if (string.IsNullOrEmpty(styleName)) { styleError.Text = language == "en" ? "Enter a style name." : "Введите название стиля."; return; }
            custom.Add(TextStyle.Capture(current, styleName)); SaveStyles(); RefreshCustomStyles(); newStyleName.Text = "";
        }));
        stylesPanel.Children.Add(styleError); Children.Add(Section("Готовые стили", stylesPanel));
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
        outlines.Children.Add(ActionButton(StudioLocalization.Text("Сбросить обводку", language), () => { current.Outlines.Clear(); ChangedAndRebuild(); }));
        Children.Add(Section("Обводка", outlines));
        var shadow = current.Shadow ??= new();
        var shadowPanel = new StackPanel { Spacing = 6, IsEnabled = !current.IsLocked };
        var shadowEnabled = new CheckBox { Content = StudioLocalization.Text("Включить тень", language), IsChecked = shadow.Enabled };
        var shadowFields = new StackPanel { Spacing = 6, IsVisible = shadow.Enabled };
        shadowFields.Children.Add(ColorField("Цвет тени (#AARRGGBB)", shadow.Color, value => shadow.Color = value));
        shadowFields.Children.Add(Number("Прозрачность, %", shadow.Opacity * 100, 0, 100, value => shadow.Opacity = value / 100));
        Control? shadowX = null, shadowY = null, shadowAngle = null, shadowDistance = null;
        var shadowDirection = Math.Atan2(shadow.OffsetY, shadow.OffsetX) * 180 / Math.PI;
        void SyncNumber(Control? control, double value)
        {
            if (control is not Grid grid) return;
            var oldUpdating = updating; updating = true;
            try
            {
                var input = grid.Children.OfType<NumericUpDown>().Single();
                var slider = grid.Children.OfType<Slider>().Single();
                var next = Math.Clamp(value, (double)input.Minimum, (double)input.Maximum);
                input.Value = (decimal)next; slider.Value = next;
            }
            finally { updating = oldUpdating; }
        }
        void UpdatePolar()
        {
            var distance = Math.Sqrt(shadow.OffsetX * shadow.OffsetX + shadow.OffsetY * shadow.OffsetY);
            if (distance > .000001) shadowDirection = Math.Atan2(shadow.OffsetY, shadow.OffsetX) * 180 / Math.PI;
            SyncNumber(shadowAngle, shadowDirection); SyncNumber(shadowDistance, distance);
        }
        void UpdateCartesian(double angle, double distance)
        {
            var radians = angle * Math.PI / 180;
            shadow.OffsetX = Math.Clamp(Math.Cos(radians) * distance, -512, 512);
            shadow.OffsetY = Math.Clamp(Math.Sin(radians) * distance, -512, 512);
            SyncNumber(shadowX, shadow.OffsetX); SyncNumber(shadowY, shadow.OffsetY); UpdatePolar();
        }
        shadowX = Number("Смещение X", shadow.OffsetX, -512, 512, value => { shadow.OffsetX = value; UpdatePolar(); });
        shadowY = Number("Смещение Y", shadow.OffsetY, -512, 512, value => { shadow.OffsetY = value; UpdatePolar(); });
        shadowAngle = Number("Угол, °", shadowDirection, -180, 180, value => { shadowDirection = value; UpdateCartesian(value, Math.Sqrt(shadow.OffsetX * shadow.OffsetX + shadow.OffsetY * shadow.OffsetY)); });
        shadowDistance = Number("Расстояние", Math.Sqrt(shadow.OffsetX * shadow.OffsetX + shadow.OffsetY * shadow.OffsetY), 0, 724, value => UpdateCartesian(shadowDirection, value));
        shadowFields.Children.Add(shadowX); shadowFields.Children.Add(shadowY); shadowFields.Children.Add(shadowAngle); shadowFields.Children.Add(shadowDistance);
        shadowFields.Children.Add(Number("Размытие", shadow.Blur, 0, 100, value => shadow.Blur = value));
        shadowEnabled.IsCheckedChanged += (_, _) => { shadow.Enabled = shadowEnabled.IsChecked == true; shadowFields.IsVisible = shadow.Enabled; changed(); };
        shadowPanel.Children.Add(shadowEnabled); shadowPanel.Children.Add(shadowFields); shadowPanel.Children.Add(ActionButton(StudioLocalization.Text("Сбросить тень", language), () => { current.Shadow = new(); ChangedAndRebuild(); })); Children.Add(Section("Тень", shadowPanel));
        var glow = current.Glow ??= new();
        var glowPanel = new StackPanel { Spacing = 6, IsEnabled = !current.IsLocked };
        var glowEnabled = new CheckBox { Content = StudioLocalization.Text("Внешнее свечение", language), IsChecked = glow.Enabled };
        var glowFields = new StackPanel { Spacing = 6, IsVisible = glow.Enabled };
        glowFields.Children.Add(ColorField("Цвет свечения (#AARRGGBB)", glow.Color, value => glow.Color = value));
        glowFields.Children.Add(Number("Прозрачность, %", glow.Opacity * 100, 0, 100, value => glow.Opacity = value / 100));
        glowFields.Children.Add(Number("Радиус", glow.Radius, 0, 100, value => glow.Radius = value));
        glowFields.Children.Add(Number("Интенсивность, %", glow.Intensity * 100, 0, 500, value => glow.Intensity = value / 100));
        glowEnabled.IsCheckedChanged += (_, _) => { glow.Enabled = glowEnabled.IsChecked == true; glowFields.IsVisible = glow.Enabled; changed(); };
        var innerGlowEnabled = new CheckBox { Content = StudioLocalization.Text("Внутреннее свечение", language), IsChecked = glow.InnerEnabled };
        var innerGlowFields = new StackPanel { Spacing = 6, IsVisible = glow.InnerEnabled };
        innerGlowFields.Children.Add(ColorField("Цвет свечения (#AARRGGBB)", glow.InnerColor, value => glow.InnerColor = value));
        innerGlowFields.Children.Add(Number("Прозрачность, %", glow.InnerOpacity * 100, 0, 100, value => glow.InnerOpacity = value / 100));
        innerGlowFields.Children.Add(Number("Радиус", glow.InnerRadius, 0, 100, value => glow.InnerRadius = value));
        innerGlowFields.Children.Add(Number("Интенсивность, %", glow.InnerIntensity * 100, 0, 500, value => glow.InnerIntensity = value / 100));
        innerGlowEnabled.IsCheckedChanged += (_, _) => { glow.InnerEnabled = innerGlowEnabled.IsChecked == true; innerGlowFields.IsVisible = glow.InnerEnabled; changed(); };
        glowPanel.Children.Add(glowEnabled); glowPanel.Children.Add(glowFields); glowPanel.Children.Add(innerGlowEnabled); glowPanel.Children.Add(innerGlowFields); glowPanel.Children.Add(ActionButton(StudioLocalization.Text("Сбросить свечение", language), () => { current.Glow = new(); ChangedAndRebuild(); })); Children.Add(Section("Свечение", glowPanel));
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
        gradientPanel.Children.Add(gradientEnabled); gradientPanel.Children.Add(gradientFields); gradientPanel.Children.Add(ActionButton(StudioLocalization.Text("Сбросить градиент", language), () => { current.Gradient = new(); ChangedAndRebuild(); })); Children.Add(Section("Заливка", gradientPanel));
        var extrusion = current.Extrusion ??= new();
        var extrusionPanel = new StackPanel { Spacing = 6, IsEnabled = !current.IsLocked };
        var extrusionEnabled = new CheckBox { Content = StudioLocalization.Text("Включить объём", language), IsChecked = extrusion.Enabled };
        var extrusionFields = new StackPanel { Spacing = 6, IsVisible = extrusion.Enabled };
        extrusionFields.Children.Add(ColorField("Цвет объёма (#AARRGGBB)", extrusion.Color, value => extrusion.Color = value));
        extrusionFields.Children.Add(Number("Прозрачность, %", extrusion.Opacity * 100, 0, 100, value => extrusion.Opacity = value / 100));
        extrusionFields.Children.Add(Number("Глубина", extrusion.Depth, 0, 100, value => extrusion.Depth = value));
        extrusionFields.Children.Add(Number("Угол, °", extrusion.Angle, -360, 360, value => extrusion.Angle = value));
        extrusionFields.Children.Add(Number("Затенение, %", extrusion.Shading * 100, 0, 100, value => extrusion.Shading = value / 100));
        extrusionEnabled.IsCheckedChanged += (_, _) => { extrusion.Enabled = extrusionEnabled.IsChecked == true; extrusionFields.IsVisible = extrusion.Enabled; changed(); };
        extrusionPanel.Children.Add(extrusionEnabled); extrusionPanel.Children.Add(extrusionFields); extrusionPanel.Children.Add(ActionButton(StudioLocalization.Text("Сбросить объём", language), () => { current.Extrusion = new(); ChangedAndRebuild(); })); Children.Add(Section("Объём", extrusionPanel));
        var background = current.Background ??= new();
        var backgroundPanel = new StackPanel { Spacing = 6, IsEnabled = !current.IsLocked };
        var backgroundEnabled = new CheckBox { Content = StudioLocalization.Text("Включить подложку", language), IsChecked = background.Enabled };
        var backgroundFields = new StackPanel { Spacing = 6, IsVisible = background.Enabled };
        var perLine = new CheckBox { Content = StudioLocalization.Text("Отдельно для каждой строки", language), IsChecked = background.PerLine };
        perLine.IsCheckedChanged += (_, _) => { background.PerLine = perLine.IsChecked == true; changed(); }; backgroundFields.Children.Add(perLine);
        backgroundFields.Children.Add(ColorField("Цвет подложки (#AARRGGBB)", background.Color, value => background.Color = value));
        backgroundFields.Children.Add(Number("Прозрачность, %", background.Opacity * 100, 0, 100, value => background.Opacity = value / 100));
        backgroundFields.Children.Add(Number("Отступ X", background.PaddingX, 0, 256, value => background.PaddingX = value));
        backgroundFields.Children.Add(Number("Отступ Y", background.PaddingY, 0, 256, value => background.PaddingY = value));
        backgroundFields.Children.Add(Number("Скругление", background.Radius, 0, 256, value => background.Radius = value));
        backgroundFields.Children.Add(ColorField("Цвет границы (#AARRGGBB)", background.BorderColor, value => background.BorderColor = value));
        backgroundFields.Children.Add(Number("Толщина границы", background.BorderWidth, 0, 100, value => background.BorderWidth = value));
        var bgGradient = background.Gradient ??= new();
        bgGradient.Stops ??= [];
        var bgGradientEnabled = new CheckBox { Content = StudioLocalization.Text("Градиентная заливка", language), IsChecked = bgGradient.Enabled };
        var bgGradientFields = new StackPanel { Spacing = 6, IsVisible = bgGradient.Enabled };
        var bgKind = new ComboBox { ItemsSource = new[] { StudioLocalization.Text("Линейный", language), StudioLocalization.Text("Радиальный", language), StudioLocalization.Text("Угловой", language) }, SelectedIndex = (int)bgGradient.Kind, HorizontalAlignment = HorizontalAlignment.Stretch };
        bgKind.SelectionChanged += (_, _) => { if (bgKind.SelectedIndex >= 0) { bgGradient.Kind = (TextGradientKind)bgKind.SelectedIndex; changed(); } };
        bgGradientFields.Children.Add(bgKind);
        bgGradientFields.Children.Add(Number("Угол, °", bgGradient.Angle, -360, 360, value => bgGradient.Angle = value));
        var bgStops = new StackPanel { Spacing = 6 };
        void RefreshBackgroundStops()
        {
            bgStops.Children.Clear();
            foreach (var stop in bgGradient.Stops.ToArray())
            {
                var row = new StackPanel { Spacing = 4 };
                row.Children.Add(ColorField("Цвет точки (#AARRGGBB)", stop.Color, value => stop.Color = value));
                row.Children.Add(Number("Положение точки, %", stop.Position * 100, 0, 100, value => stop.Position = value / 100));
                var remove = ActionButton(StudioLocalization.Text("Удалить точку", language), () => { bgGradient.Stops.Remove(stop); RefreshBackgroundStops(); changed(); });
                remove.IsEnabled = bgGradient.Stops.Count > 2; row.Children.Add(remove); bgStops.Children.Add(row);
            }
        }
        RefreshBackgroundStops(); bgGradientFields.Children.Add(bgStops);
        bgGradientFields.Children.Add(ActionButton(StudioLocalization.Text("Добавить точку", language), () => { bgGradient.Stops.Add(new() { Position = .5 }); RefreshBackgroundStops(); changed(); }));
        bgGradientEnabled.IsCheckedChanged += (_, _) => { bgGradient.Enabled = bgGradientEnabled.IsChecked == true; bgGradientFields.IsVisible = bgGradient.Enabled; changed(); };
        backgroundFields.Children.Add(bgGradientEnabled); backgroundFields.Children.Add(bgGradientFields);
        backgroundEnabled.IsCheckedChanged += (_, _) => { background.Enabled = backgroundEnabled.IsChecked == true; backgroundFields.IsVisible = background.Enabled; changed(); };
        backgroundPanel.Children.Add(backgroundEnabled); backgroundPanel.Children.Add(backgroundFields); backgroundPanel.Children.Add(ActionButton(StudioLocalization.Text("Сбросить подложку", language), () => { current.Background = new(); ChangedAndRebuild(); })); Children.Add(Section("Подложка", backgroundPanel));
        var raster = current.RasterEffects ??= new();
        var rasterPanel = new StackPanel { Spacing = 6, IsEnabled = !current.IsLocked };
        var blurEnabled = new CheckBox { Content = StudioLocalization.Text("Размытие текста", language), IsChecked = raster.BlurEnabled };
        var blurFields = new StackPanel { Spacing = 6, IsVisible = raster.BlurEnabled };
        blurFields.Children.Add(Number("Радиус размытия", raster.BlurRadius, 0, 100, value => raster.BlurRadius = value));
        blurEnabled.IsCheckedChanged += (_, _) => { raster.BlurEnabled = blurEnabled.IsChecked == true; blurFields.IsVisible = raster.BlurEnabled; changed(); };
        var pixelEnabled = new CheckBox { Content = StudioLocalization.Text("Пикселизация текста", language), IsChecked = raster.PixelationEnabled };
        var pixelFields = new StackPanel { Spacing = 6, IsVisible = raster.PixelationEnabled };
        pixelFields.Children.Add(Number("Размер пикселя", raster.PixelSize, 2, 64, value => raster.PixelSize = (int)Math.Round(value)));
        pixelEnabled.IsCheckedChanged += (_, _) => { raster.PixelationEnabled = pixelEnabled.IsChecked == true; pixelFields.IsVisible = raster.PixelationEnabled; changed(); };
        var chromaticEnabled = new CheckBox { Content = StudioLocalization.Text("Смещение RGB", language), IsChecked = raster.ChromaticEnabled };
        var chromaticFields = new StackPanel { Spacing = 6, IsVisible = raster.ChromaticEnabled };
        chromaticFields.Children.Add(Number("Смещение каналов", raster.ChromaticOffset, 0, 64, value => raster.ChromaticOffset = (int)Math.Round(value)));
        chromaticEnabled.IsCheckedChanged += (_, _) => { raster.ChromaticEnabled = chromaticEnabled.IsChecked == true; chromaticFields.IsVisible = raster.ChromaticEnabled; changed(); };
        var metallicEnabled = new CheckBox { Content = StudioLocalization.Text("Металлический эффект", language), IsChecked = raster.MetallicEnabled };
        var metallicFields = new StackPanel { Spacing = 6, IsVisible = raster.MetallicEnabled };
        metallicFields.Children.Add(ColorField("Цвет металла (#AARRGGBB)", raster.MetallicColor, value => raster.MetallicColor = value));
        metallicFields.Children.Add(Number("Интенсивность, %", raster.MetallicStrength * 100, 0, 100, value => raster.MetallicStrength = value / 100));
        metallicEnabled.IsCheckedChanged += (_, _) => { raster.MetallicEnabled = metallicEnabled.IsChecked == true; metallicFields.IsVisible = raster.MetallicEnabled; changed(); };
        var glossEnabled = new CheckBox { Content = StudioLocalization.Text("Глянец", language), IsChecked = raster.GlossEnabled };
        var glossFields = new StackPanel { Spacing = 6, IsVisible = raster.GlossEnabled };
        glossFields.Children.Add(Number("Интенсивность, %", raster.GlossStrength * 100, 0, 100, value => raster.GlossStrength = value / 100));
        glossEnabled.IsCheckedChanged += (_, _) => { raster.GlossEnabled = glossEnabled.IsChecked == true; glossFields.IsVisible = raster.GlossEnabled; changed(); };
        Control ReliefFields(string label, TextRelief relief)
        {
            var group = new StackPanel { Spacing = 6 };
            var enabled = new CheckBox { Content = StudioLocalization.Text(label, language), IsChecked = relief.Enabled };
            var values = new StackPanel { Spacing = 6, IsVisible = relief.Enabled };
            values.Children.Add(Number("Интенсивность, %", relief.Strength * 100, 0, 100, value => relief.Strength = value / 100));
            values.Children.Add(Number("Глубина", relief.Depth, 0, 30, value => relief.Depth = value));
            values.Children.Add(Number("Угол, °", relief.Angle, -360, 360, value => relief.Angle = value));
            enabled.IsCheckedChanged += (_, _) => { relief.Enabled = enabled.IsChecked == true; values.IsVisible = relief.Enabled; changed(); };
            group.Children.Add(enabled); group.Children.Add(values); return group;
        }
        rasterPanel.Children.Add(blurEnabled); rasterPanel.Children.Add(blurFields); rasterPanel.Children.Add(pixelEnabled); rasterPanel.Children.Add(pixelFields); rasterPanel.Children.Add(chromaticEnabled); rasterPanel.Children.Add(chromaticFields);
        rasterPanel.Children.Add(metallicEnabled); rasterPanel.Children.Add(metallicFields); rasterPanel.Children.Add(glossEnabled); rasterPanel.Children.Add(glossFields);
        rasterPanel.Children.Add(ReliefFields("Тиснение", raster.Emboss ??= new())); rasterPanel.Children.Add(ReliefFields("Вдавленный текст", raster.Inset ??= new()));
        rasterPanel.Children.Add(ActionButton(StudioLocalization.Text("Сбросить дополнительные эффекты", language), () => { current.RasterEffects = new(); ChangedAndRebuild(); }));
        Children.Add(Section("Дополнительные эффекты", rasterPanel));
        Children.Add(Section("Положение и поворот", placement));
        var resetEffects = ActionButton(StudioLocalization.Text("Сбросить все эффекты", language), () => { current.Outlines.Clear(); current.Shadow = new(); current.Glow = new(); current.Gradient = new(); current.Background = new(); current.Extrusion = new(); current.RasterEffects = new(); ChangedAndRebuild(); });
        resetEffects.IsEnabled = !current.IsLocked; Children.Add(resetEffects);
        StudioLocalization.Capture(this);
        StudioLocalization.Apply(this, language);
        updating = false;
    }

    private Border Section(string title, Control content)
    {
        var expander = new Expander { Header = StudioLocalization.Text(title, language), Content = content, IsExpanded = expandedSections.TryGetValue(title, out var expanded) ? expanded : title is "Слои текста" or "Текст" };
        expander.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) expandedSections[title] = expander.IsExpanded; };
        return new Border { Background = theme.Surface, BorderBrush = theme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Child = expander };
    }

    private Control Number(string label, double value, double min, double max, Action<double> setter)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,100"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var input = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)Math.Clamp(value, min, max), Increment = 1, ShowButtonSpinner = false, FormatString = "0.##", Height = 34 };
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), HorizontalAlignment = HorizontalAlignment.Stretch };
        slider.Classes.Add("edit-slider");
        var synchronizing = false;
        input.ValueChanged += (_, _) =>
        {
            if (updating || synchronizing || input.Value is not { } next) return;
            synchronizing = true; slider.Value = (double)next; synchronizing = false; setter((double)next); changed();
        };
        slider.ValueChanged += (_, _) =>
        {
            if (updating || synchronizing) return;
            synchronizing = true; input.Value = (decimal)slider.Value; synchronizing = false; setter(slider.Value); changed();
        };
        Grid.SetColumn(input, 1); grid.Children.Add(input);
        Grid.SetRow(slider, 1); Grid.SetColumnSpan(slider, 2); grid.Children.Add(slider); return grid;
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

    private Button StyleButton(TextStyle style, TextLayer target)
    {
        var contents = new StackPanel { Spacing = 2 };
        if (StylePreviewCache.Count > 128) StylePreviewCache.Clear();
        var key = System.Text.Json.JsonSerializer.Serialize(style);
        var bytes = StylePreviewCache.GetOrAdd(key, _ =>
        {
            var preview = new TextLayer { Text = "Aa", FontSize = 50, X = 256, Y = 256 };
            style.Apply(preview);
            using var image = TextLayerRenderer.RenderOverlay([preview]);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        });
        using (var stream = new MemoryStream(bytes))
        {
            var bitmap = new Bitmap(stream);
            var view = new Image { Source = new CroppedBitmap(bitmap, new PixelRect(128, 206, 256, 100)), Width = 100, Height = 40, Stretch = Stretch.Uniform };
            view.DetachedFromVisualTree += (_, _) => bitmap.Dispose(); contents.Children.Add(view);
        }
        contents.Children.Add(new TextBlock { Text = style.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 112, FontSize = 11 });
        var button = new Button { Content = contents, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(5), MinHeight = 30, Background = theme.Panel, BorderBrush = theme.Border, BorderThickness = new Thickness(1) };
        button.Click += (_, _) => { style.Apply(target); changed(); Rebuild(); selectionChanged(); };
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

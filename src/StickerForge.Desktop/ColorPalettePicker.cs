using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;

namespace StickerForge.Desktop;

/// <summary>Compact, non-modal color editor shared by text fill and effects.</summary>
internal sealed class ColorPalettePicker : StackPanel
{
    private sealed class SavedColors { public List<string> Recent { get; set; } = []; public List<string> Favorites { get; set; } = []; }
    private static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickerForge", "colors.json");
    private readonly StudioTheme theme;
    private readonly Action<string> changed;
    private readonly TextBox hex = new();
    private readonly NumericUpDown[] channels = new NumericUpDown[4];
    private readonly Slider hue = new() { Minimum = 0, Maximum = 360 };
    private readonly Slider alpha = new() { Minimum = 0, Maximum = 255 };
    private readonly ColorSquare square;
    private readonly Border sample = new() { Width = 40, Height = 30, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
    private readonly WrapPanel favorites = new();
    private readonly WrapPanel recent = new();
    private readonly SavedColors saved;
    private SKColor color;
    private float h, s, v;
    private bool updating;
    private readonly string language;

    public ColorPalettePicker(string value, StudioTheme theme, string language, Action<string> changed, Action<Action<string>>? sampleCanvas = null)
    {
        this.theme = theme; this.language = language; this.changed = changed;
        Spacing = 6; Width = 270;
        saved = Load();
        if (!SKColor.TryParse(value, out color)) color = SKColors.White;
        color.ToHsv(out h, out s, out v);
        square = new ColorSquare { Height = 140, Hue = h, Saturation = s, Value = v };
        square.Changed = (sat, val) => { s = sat; v = val; FromHsv(); };
        Children.Add(square);
        Children.Add(Label("Цветовой тон", "Hue")); Children.Add(hue);
        Children.Add(Label("Прозрачность", "Opacity")); Children.Add(alpha);
        var hexRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        hexRow.Children.Add(hex); sample.BorderBrush = theme.Border; Grid.SetColumn(sample, 1); hexRow.Children.Add(sample); Children.Add(hexRow);
        var channelGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 4 };
        for (var i = 0; i < channels.Length; i++)
        {
            var index = i;
            var column = new StackPanel { Spacing = 2 };
            column.Children.Add(new TextBlock { Text = "RGBA"[i].ToString(), Foreground = theme.Secondary, FontSize = 12 });
            var field = channels[i] = new NumericUpDown { Minimum = 0, Maximum = 255, Increment = 1, ShowButtonSpinner = false, FormatString = "0", MinWidth = 0 };
            field.ValueChanged += (_, _) => { if (!updating && channels.All(c => c?.Value is not null)) { color = new SKColor((byte)channels[0].Value!.Value, (byte)channels[1].Value!.Value, (byte)channels[2].Value!.Value, (byte)channels[3].Value!.Value); color.ToHsv(out h, out s, out v); UpdateControls(); Notify(); } };
            column.Children.Add(field); Grid.SetColumn(column, index); channelGrid.Children.Add(column);
        }
        Children.Add(channelGrid);
        if (sampleCanvas is not null)
        {
            var eyedropper = new Button { Content = language == "en" ? "Pick from canvas" : "Взять цвет с холста", MinHeight = 30, Padding = new Thickness(8, 3), Background = theme.Surface, Foreground = theme.Text };
            eyedropper.Click += (_, _) => sampleCanvas(value => { if (SKColor.TryParse(value, out var picked)) Select(picked); });
            Children.Add(eyedropper);
        }
        Children.Add(Label("Быстрые цвета", "Presets"));
        var presets = new WrapPanel();
        foreach (var preset in new[] { "#FFFFFFFF", "#FF000000", "#FFFF3B30", "#FFFFCC00", "#FF34C759", "#FF078BFA", "#FFAF52DE", "#00000000" }) AddSwatch(presets, preset);
        Children.Add(presets);
        var favoriteButton = new Button { Content = language == "en" ? "★ Add to favorites" : "★ В избранное", MinHeight = 30, Padding = new Thickness(8, 3), Background = theme.Surface, Foreground = theme.Text };
        favoriteButton.Click += (_, _) => { var text = AsText(); if (!saved.Favorites.Remove(text)) saved.Favorites.Insert(0, text); Save(); RefreshSwatches(); };
        Children.Add(favoriteButton); Children.Add(Label("Избранные", "Favorites")); Children.Add(favorites); Children.Add(Label("Недавние", "Recent")); Children.Add(recent);
        hue.Classes.Add("edit-slider"); alpha.Classes.Add("edit-slider");
        hue.ValueChanged += (_, _) => { if (!updating) { h = (float)hue.Value; FromHsv(); } };
        alpha.ValueChanged += (_, _) => { if (!updating) { color = color.WithAlpha((byte)alpha.Value); UpdateControls(); Notify(); } };
        hex.LostFocus += (_, _) => { if (SKColor.TryParse(hex.Text, out var next)) Select(next); else UpdateControls(); };
        DetachedFromVisualTree += (_, _) => { var text = AsText(); saved.Recent.Remove(text); saved.Recent.Insert(0, text); if (saved.Recent.Count > 12) saved.Recent.RemoveRange(12, saved.Recent.Count - 12); Save(); };
        RefreshSwatches(); UpdateControls();
    }

    private TextBlock Label(string ru, string en) => new() { Text = language == "en" ? en : ru, Foreground = theme.Text, FontSize = 12 };
    private string AsText() => $"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    private void FromHsv() { color = SKColor.FromHsv(h, s, v, color.Alpha); UpdateControls(); Notify(); }
    private void Select(SKColor value) { color = value; color.ToHsv(out h, out s, out v); UpdateControls(); Notify(); }
    private void Notify() => changed(AsText());
    private void UpdateControls()
    {
        updating = true;
        hue.Value = h; alpha.Value = color.Alpha; hex.Text = AsText();
        channels[0].Value = color.Red; channels[1].Value = color.Green; channels[2].Value = color.Blue; channels[3].Value = color.Alpha;
        sample.Background = new SolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
        square.Hue = h; square.Saturation = s; square.Value = v; square.InvalidateVisual();
        updating = false;
    }
    private void RefreshSwatches()
    {
        favorites.Children.Clear(); recent.Children.Clear();
        foreach (var entry in saved.Favorites) AddSwatch(favorites, entry);
        foreach (var entry in saved.Recent) AddSwatch(recent, entry);
    }
    private void AddSwatch(WrapPanel panel, string entry)
    {
        if (!SKColor.TryParse(entry, out var c)) return;
        var button = new Button { Width = 26, Height = 26, MinHeight = 26, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 4), Background = new SolidColorBrush(Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue)), BorderBrush = theme.Border, BorderThickness = new Thickness(1) };
        ToolTip.SetTip(button, entry); button.Click += (_, _) => Select(c); panel.Children.Add(button);
    }
    private static SavedColors Load()
    {
        try { return JsonSerializer.Deserialize<SavedColors>(File.ReadAllText(PathName)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    private void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(PathName)!); File.WriteAllText(PathName, JsonSerializer.Serialize(saved)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed class ColorSquare : Control
    {
        public float Hue { get; set; }
        public float Saturation { get; set; }
        public float Value { get; set; }
        public Action<float, float>? Changed { get; set; }
        public override void Render(DrawingContext context)
        {
            var c = SKColor.FromHsv(Hue, 100, 100);
            var rect = new Rect(Bounds.Size);
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue)), rect);
            context.FillRectangle(new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = new GradientStops { new(Colors.White, 0), new(Colors.Transparent, 1) } }, rect);
            context.FillRectangle(new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = new GradientStops { new(Colors.Transparent, 0), new(Colors.Black, 1) } }, rect);
            context.DrawEllipse(null, new Pen(Brushes.White, 2), new Point(rect.Width * Saturation / 100, rect.Height * (1 - Value / 100)), 5, 5);
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; e.Pointer.Capture(this); Set(e.GetPosition(this)); e.Handled = true; }
        protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (e.Pointer.Captured != this) return; Set(e.GetPosition(this)); e.Handled = true; }
        protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (e.Pointer.Captured == this) { e.Pointer.Capture(null); e.Handled = true; } }
        private void Set(Point p) => Changed?.Invoke((float)(Math.Clamp(p.X / Math.Max(1, Bounds.Width), 0, 1) * 100), (float)((1 - Math.Clamp(p.Y / Math.Max(1, Bounds.Height), 0, 1)) * 100));
    }
}

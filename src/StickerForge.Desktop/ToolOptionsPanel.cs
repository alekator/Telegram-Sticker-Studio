using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ImageSpace.Core;
using StickerForge.Rendering;

namespace StickerForge.Desktop;

internal sealed class ToolOptionsPanel : StackPanel
{
    private readonly GraphicsSession editor;
    private Ellipse preview = null!;
    private readonly TextBlock previewLabel = new();
    private readonly TextBlock error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private static readonly string[] Palette = ["#E84B4C", "#000000", "#FFFFFF", "#F8C23A", "#50B978", "#3989ED", "#9D63D8", "#F16CA4"];

    public ToolOptionsPanel(GraphicsSession editor)
    {
        this.editor = editor; Spacing = 6; Rebuild();
    }
    public void Rebuild()
    {
        Children.Clear();
        error.Text = "";
        preview = new Ellipse { Stroke = Brushes.White, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Children.Add(new TextBlock { Text = editor.Eraser ? "ERASER" : "BRUSH", FontWeight = FontWeight.Bold });
        var options = editor.Eraser ? editor.EraserOptions : editor.BrushOptions;
        if (!editor.Eraser)
        {
            Children.Add(new TextBlock { Text = "Color (#RRGGBB)" });
            var hex = new TextBox { Text = ToHex(editor.BrushColor), PlaceholderText = "#RRGGBB" };
            hex.TextChanged += (_, _) =>
            {
                if (TryColor(hex.Text, out var color)) { editor.BrushColor = color; error.Text = ""; UpdatePreview(); }
                else error.Text = "Enter a color as #RRGGBB.";
            };
            Children.Add(hex); Children.Add(error);
            var swatches = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var value in Palette)
            {
                var button = new Button { Width = 25, Height = 25, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(2), Background = new SolidColorBrush(Color.Parse(value)) };
                ToolTip.SetTip(button, value);
                button.Click += (_, _) => hex.Text = value;
                swatches.Children.Add(button);
            }
            Children.Add(swatches);
        }
        AddSlider("Size", 1, 200, options.Size, "px", v => Update(size: (float)v));
        AddSlider(editor.Eraser ? "Strength" : "Opacity", 0, 100, options.Opacity * 100, "%", v => Update(opacity: (float)v / 100));
        AddSlider("Hardness", 0, 100, options.Hardness * 100, "%", v => Update(hardness: (float)v / 100));
        Children.Add(previewLabel);
        Children.Add(new Border { Height = 80, Background = Brushes.Gray, Child = preview, ClipToBounds = true });
        UpdatePreview();
    }
    private void AddSlider(string name, double min, double max, double value, string unit, Action<double> changed)
    {
        var label = new TextBlock { Text = $"{name}: {value:0} {unit}" }; Children.Add(label);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true };
        slider.PropertyChanged += (_, e) => { if (e.Property != RangeBase.ValueProperty) return; label.Text = $"{name}: {slider.Value:0} {unit}"; changed(slider.Value); };
        Children.Add(slider);
    }
    private void Update(float? size = null, float? opacity = null, float? hardness = null)
    {
        var current = editor.Eraser ? editor.EraserOptions : editor.BrushOptions;
        var next = new ToolOptions(size ?? current.Size, opacity ?? current.Opacity, hardness ?? current.Hardness);
        if (editor.Eraser) editor.EraserOptions = next; else editor.BrushOptions = next;
        UpdatePreview();
    }
    private void UpdatePreview()
    {
        var options = editor.Eraser ? editor.EraserOptions : editor.BrushOptions;
        var color = editor.Eraser ? new Rgba32(255, 255, 255, 255) : editor.BrushColor;
        preview.Width = preview.Height = Math.Clamp(options.Size * .35, 4, 70);
        preview.Fill = new SolidColorBrush(Color.FromArgb((byte)Math.Round(options.Opacity * 255), color.R, color.G, color.B));
        previewLabel.Text = $"{(editor.Eraser ? "Eraser" : "Brush")} preview · {options.Size:0} px (scaled)";
    }
    private static string ToHex(Rgba32 color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    private static bool TryColor(string? text, out Rgba32 color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#' || !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
        color = new Rgba32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255); return true;
    }
}

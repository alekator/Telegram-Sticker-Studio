using System.Globalization;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using StickerForge.Core;

namespace StickerForge.Media;

/// <summary>Renders sticker-space text independently of the source image transform.</summary>
public static class TextLayerRenderer
{
    public static SKImage RenderOverlay(IReadOnlyList<TextLayer> layers)
    {
        using var surface = SKSurface.Create(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidDataException("Unable to create text canvas.");
        surface.Canvas.Clear(SKColors.Transparent);
        foreach (var layer in layers)
            if (layer.IsVisible && !string.IsNullOrEmpty(layer.Text)) Draw(surface.Canvas, layer);
        return surface.Snapshot();
    }

    private static void Draw(SKCanvas canvas, TextLayer layer)
    {
        var style = new SKFontStyle(layer.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, layer.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        using var typeface = SKFontManager.Default.MatchFamily(layer.FontFamily, style) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, (float)Math.Clamp(layer.FontSize, 1, 512));
        var color = SKColor.TryParse(layer.Color, out var parsed) ? parsed : SKColors.White;
        using var paint = new SKPaint { IsAntialias = true, Color = color.WithAlpha((byte)Math.Clamp(Math.Round(color.Alpha * Math.Clamp(layer.Opacity, 0, 1)), 0, 255)) };
        var lines = layer.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var spacing = (float)Math.Clamp(layer.LetterSpacing, -100, 100);
        var metrics = font.Metrics;
        var lineHeight = Math.Max(1, metrics.Descent - metrics.Ascent + metrics.Leading) * (float)Math.Clamp(layer.LineSpacing, .1, 10);
        canvas.Save();
        canvas.Translate((float)layer.X, (float)layer.Y);
        canvas.RotateDegrees((float)layer.Rotation);
        canvas.Scale((float)Math.Clamp(layer.Scale, .01, 20));
        var firstBaseline = -(lines.Length - 1) * lineHeight / 2 - (metrics.Ascent + metrics.Descent) / 2;
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var elements = new List<string>();
            var iterator = StringInfo.GetTextElementEnumerator(lines[lineIndex]);
            while (iterator.MoveNext()) elements.Add((string)iterator.Current!);
            var runs = new List<(string Text, SKFont Font, SKTypeface? Fallback)>();
            try
            {
                var common = new StringBuilder();
                void FlushCommon() { if (common.Length > 0) { runs.Add((common.ToString(), font, null)); common.Clear(); } }
                foreach (var element in elements)
                {
                    SKTypeface? fallback = null;
                    if (font.GetGlyphs(element).Any(glyph => glyph == 0))
                    {
                        var rune = element.EnumerateRunes().First();
                        fallback = SKFontManager.Default.MatchCharacter(rune.Value);
                    }
                    if (fallback is null) common.Append(element);
                    else { FlushCommon(); runs.Add((element, new SKFont(fallback, font.Size), fallback)); }
                }
                FlushCommon();
                var shaped = runs.Select(run => { using var shaper = new SKShaper(run.Font.Typeface); return shaper.Shape(run.Text, run.Font); }).ToArray();
                var clusterCount = shaped.Sum(run => run.Clusters.Distinct().Count());
                var width = shaped.Sum(run => run.Width) + Math.Max(0, clusterCount - 1) * spacing;
                var x = layer.Alignment switch { TextLayerAlignment.Left => 0, TextLayerAlignment.Right => -width, _ => -width / 2 };
                for (var runIndex = 0; runIndex < runs.Count; runIndex++)
                {
                    var run = runs[runIndex]; var result = shaped[runIndex]; var points = result.Points.ToArray();
                    var clusters = 0;
                    for (var i = 0; i < points.Length; i++)
                    {
                        if (i > 0 && result.Clusters[i] != result.Clusters[i - 1]) clusters++;
                        points[i].X += clusters * spacing;
                    }
                    using var builder = new SKTextBlobBuilder();
                    builder.AddPositionedRun(result.Codepoints.Select(glyph => (ushort)glyph).ToArray(), run.Font, points);
                    using var blob = builder.Build();
                    if (blob is not null) canvas.DrawText(blob, x, firstBaseline + lineIndex * lineHeight, paint);
                    x += result.Width + (clusters + 1) * spacing;
                }
            }
            finally { foreach (var run in runs) if (run.Fallback is not null) { run.Font.Dispose(); run.Fallback.Dispose(); } }
        }
        canvas.Restore();
    }
}

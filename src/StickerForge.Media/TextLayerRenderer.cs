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
        using var typeface = BundledFontCatalog.Typeface(layer);
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
            var line = lines[lineIndex];
            if (line.Length == 0) continue;
            var visualRuns = GetVisualRuns(line);
            var runs = new List<(string Text, SKFont Font, SKTypeface? Fallback, bool RightToLeft)>();
            try
            {
                foreach (var visualRun in visualRuns)
                {
                    var rightToLeft = visualRun.RightToLeft;
                    var segments = new List<(string Text, SKFont Font, SKTypeface? Fallback, bool RightToLeft)>();
                    var common = new StringBuilder();
                    void FlushCommon() { if (common.Length > 0) { segments.Add((common.ToString(), font, null, rightToLeft)); common.Clear(); } }
                    var iterator = StringInfo.GetTextElementEnumerator(visualRun.Text);
                    while (iterator.MoveNext())
                    {
                        var element = (string)iterator.Current!;
                        SKTypeface? fallback = null;
                        if (font.GetGlyphs(element).Any(glyph => glyph == 0))
                        {
                            var rune = element.EnumerateRunes().First();
                            fallback = BundledFontCatalog.Fallback(element) ?? SKFontManager.Default.MatchCharacter(rune.Value);
                        }
                        if (fallback is null) common.Append(element);
                        else
                        {
                            FlushCommon();
                            if (segments.Count > 0 && segments[^1].Fallback is { } previous && previous.FamilyName == fallback.FamilyName)
                            {
                                var last = segments[^1]; segments[^1] = (last.Text + element, last.Font, last.Fallback, last.RightToLeft); fallback.Dispose();
                            }
                            else segments.Add((element, new SKFont(fallback, font.Size), fallback, rightToLeft));
                        }
                    }
                    FlushCommon();
                    if (rightToLeft) segments.Reverse();
                    runs.AddRange(segments);
                }
                var shaped = runs.Select(run =>
                {
                    using var shaper = new SKShaper(run.Font.Typeface);
                    using var buffer = new HarfBuzzSharp.Buffer(); buffer.AddUtf16(run.Text); buffer.GuessSegmentProperties();
                    buffer.Direction = run.RightToLeft ? HarfBuzzSharp.Direction.RightToLeft : HarfBuzzSharp.Direction.LeftToRight;
                    return shaper.Shape(buffer, run.Font);
                }).ToArray();
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

    internal static (string Text, bool RightToLeft)[] GetVisualRuns(string line)
    {
        if (line.Length == 0) return [];
        var bidi = Unicode.Bidi.BidiInfo.Create(line, null);
        var result = new List<(string, bool)>();
        foreach (var paragraph in bidi.Paragraphs)
        {
            var (levels, ranges) = bidi.VisualRuns(paragraph, paragraph.Range);
            result.AddRange(ranges.Select(range => (line.Substring(range.Start, range.Length), levels[range.Start].IsRtl())));
        }
        return result.ToArray();
    }
}

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
        var displayText = layer.Case switch { TextCase.Upper => layer.Text.ToUpperInvariant(), TextCase.Lower => layer.Text.ToLowerInvariant(), _ => layer.Text };
        var lines = displayText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var spacing = (float)Math.Clamp(layer.LetterSpacing, -100, 100);
        var metrics = font.Metrics;
        var lineHeight = Math.Max(1, metrics.Descent - metrics.Ascent + metrics.Leading) * (float)Math.Clamp(layer.LineSpacing, .1, 10);
        canvas.Save();
        canvas.Translate((float)layer.X, (float)layer.Y);
        canvas.RotateDegrees((float)layer.Rotation);
        canvas.Scale((float)Math.Clamp(layer.Scale, .01, 20));
        canvas.Skew((float)Math.Clamp(layer.Skew, -2, 2), 0);
        canvas.Scale((float)Math.Clamp(layer.StretchX, .1, 10), (float)Math.Clamp(layer.StretchY, .1, 10));
        var firstBaseline = -(lines.Length - 1) * lineHeight / 2 - (metrics.Ascent + metrics.Descent) / 2;
        if (layer.Background is { Enabled: true, PerLine: false } background)
        {
            var estimatedWidth = lines.Max(line => font.MeasureText(line) + Math.Max(0, StringInfo.ParseCombiningCharacters(line).Length - 1) * spacing);
            var left = layer.Alignment switch { TextLayerAlignment.Left => 0, TextLayerAlignment.Right => -estimatedWidth, _ => -estimatedWidth / 2 };
            DrawBackground(canvas, background, layer.Opacity, left, firstBaseline + metrics.Ascent, estimatedWidth, (lines.Length - 1) * lineHeight + metrics.Descent - metrics.Ascent);
        }
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
                var lineX = x;
                var lineBaseline = firstBaseline + lineIndex * lineHeight;
                if (layer.Background is { Enabled: true, PerLine: true } lineBackground)
                    DrawBackground(canvas, lineBackground, layer.Opacity, lineX, lineBaseline + metrics.Ascent, width, metrics.Descent - metrics.Ascent);
                using var gradient = CreateGradient(layer, lineX, lineBaseline + metrics.Ascent, width, metrics.Descent - metrics.Ascent);
                paint.Shader = gradient;
                paint.Color = gradient is null ? ApplyOpacity(color, layer.Opacity) : SKColors.White;
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
                    if (blob is not null)
                    {
                        var baseline = firstBaseline + lineIndex * lineHeight;
                        if (layer.Shadow is { Enabled: true } shadow && SKColor.TryParse(shadow.Color, out var shadowColor))
                        {
                            using var shadowPaint = new SKPaint { IsAntialias = true, Color = ApplyOpacity(shadowColor, shadow.Opacity * layer.Opacity) };
                            using var blur = shadow.Blur > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)Math.Clamp(shadow.Blur, .01, 100)) : null;
                            shadowPaint.MaskFilter = blur;
                            canvas.DrawText(blob, x + (float)shadow.OffsetX, baseline + (float)shadow.OffsetY, shadowPaint);
                        }
                        if (layer.Glow is { Enabled: true, Radius: > 0 } glow && SKColor.TryParse(glow.Color, out var glowColor))
                        {
                            using var glowPaint = new SKPaint { IsAntialias = true, Color = ApplyOpacity(glowColor, GlowOpacity(glow.Opacity, glow.Intensity) * layer.Opacity) };
                            using var glowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)Math.Clamp(glow.Radius, .01, 100));
                            glowPaint.MaskFilter = glowBlur;
                            canvas.DrawText(blob, x, baseline, glowPaint);
                        }
                        if (layer.Extrusion is { Enabled: true, Depth: > 0 } extrusion && SKColor.TryParse(extrusion.Color, out var extrusionColor))
                        {
                            var depth = (float)Math.Clamp(extrusion.Depth, 0, 100);
                            var angle = extrusion.Angle * Math.PI / 180;
                            var dx = (float)Math.Cos(angle); var dy = (float)Math.Sin(angle);
                            var steps = Math.Max(1, (int)Math.Ceiling(depth));
                            using var side = new SKPaint { IsAntialias = true };
                            for (var step = steps; step >= 1; step--)
                            {
                                var distance = depth * step / steps;
                                var light = 1 - Math.Clamp(extrusion.Shading, 0, 1) * step / steps;
                                side.Color = ApplyOpacity(new SKColor((byte)(extrusionColor.Red * light), (byte)(extrusionColor.Green * light), (byte)(extrusionColor.Blue * light), extrusionColor.Alpha), extrusion.Opacity * layer.Opacity);
                                canvas.DrawText(blob, x + dx * distance, baseline + dy * distance, side);
                            }
                        }
                        foreach (var outline in (layer.Outlines ?? []).Where(o => o.Enabled && o.Placement == TextOutlinePlacement.Outside).Reverse())
                        {
                            if (!SKColor.TryParse(outline.Color, out var outlineColor)) continue;
                            using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, StrokeWidth = (float)Math.Clamp(outline.Width, 0, 100) * 2, Color = ApplyOpacity(outlineColor, outline.Opacity * layer.Opacity) };
                            canvas.DrawText(blob, x, baseline, stroke);
                        }
                        canvas.DrawText(blob, x, baseline, paint);
                        if (layer.Glow is { InnerEnabled: true, InnerRadius: > 0 } innerGlow && SKColor.TryParse(innerGlow.InnerColor, out var innerColor))
                        {
                            using var maskSurface = SKSurface.Create(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul)) ?? throw new InvalidDataException("Unable to create text mask.");
                            maskSurface.Canvas.Clear(SKColors.Transparent); maskSurface.Canvas.SetMatrix(canvas.TotalMatrix);
                            using var glyphPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
                            maskSurface.Canvas.DrawText(blob, x, baseline, glyphPaint);
                            using var maskImage = maskSurface.Snapshot();
                            canvas.SaveLayer();
                            using var innerPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, StrokeWidth = (float)Math.Clamp(innerGlow.InnerRadius, .01, 100) * 2, Color = ApplyOpacity(innerColor, GlowOpacity(innerGlow.InnerOpacity, innerGlow.InnerIntensity) * layer.Opacity) };
                            using var innerBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)Math.Clamp(innerGlow.InnerRadius / 2, .01, 50));
                            innerPaint.MaskFilter = innerBlur;
                            canvas.DrawText(blob, x, baseline, innerPaint);
                            canvas.Save(); canvas.ResetMatrix();
                            using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
                            canvas.DrawImage(maskImage, 0, 0, maskPaint);
                            canvas.Restore(); canvas.Restore();
                        }
                        foreach (var outline in (layer.Outlines ?? []).Where(o => o.Enabled && o.Placement != TextOutlinePlacement.Outside).Reverse())
                        {
                            if (!SKColor.TryParse(outline.Color, out var outlineColor)) continue;
                            var widthFactor = outline.Placement == TextOutlinePlacement.Inside ? 2 : 1;
                            using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, StrokeWidth = (float)Math.Clamp(outline.Width, 0, 100) * widthFactor, Color = ApplyOpacity(outlineColor, outline.Opacity * layer.Opacity) };
                            if (outline.Placement == TextOutlinePlacement.Inside)
                            {
                                using var glyphs = new SKPath();
                                for (var i = 0; i < result.Codepoints.Length; i++)
                                {
                                    using var glyph = run.Font.GetGlyphPath((ushort)result.Codepoints[i]);
                                    if (glyph is not null) glyphs.AddPath(glyph, x + points[i].X, baseline + points[i].Y);
                                }
                                canvas.Save();
                                canvas.ClipPath(glyphs, SKClipOperation.Intersect, true);
                                canvas.DrawText(blob, x, baseline, stroke);
                                canvas.Restore();
                            }
                            else canvas.DrawText(blob, x, baseline, stroke);
                        }
                        if (layer.Underline || layer.Strikethrough)
                        {
                            using var linePaint = new SKPaint { IsAntialias = true, Color = paint.Color, StrokeWidth = Math.Max(1, font.Size / 18) };
                            if (layer.Underline) canvas.DrawLine(x, baseline + font.Size * .12f, x + result.Width, baseline + font.Size * .12f, linePaint);
                            if (layer.Strikethrough) canvas.DrawLine(x, baseline - font.Size * .3f, x + result.Width, baseline - font.Size * .3f, linePaint);
                        }
                    }
                    x += result.Width + (clusters + 1) * spacing;
                }
            }
            finally { foreach (var run in runs) if (run.Fallback is not null) { run.Font.Dispose(); run.Fallback.Dispose(); } }
        }
        canvas.Restore();
    }

    private static SKColor ApplyOpacity(SKColor color, double opacity) => color.WithAlpha((byte)Math.Clamp(Math.Round(color.Alpha * Math.Clamp(opacity, 0, 1)), 0, 255));
    private static double GlowOpacity(double opacity, double intensity) => 1 - Math.Pow(1 - Math.Clamp(opacity, 0, 1), Math.Clamp(intensity, 0, 5));

    private static SKShader? CreateGradient(TextLayer layer, float x, float y, float width, float height)
    {
        return CreateGradient(layer.Gradient, layer.Opacity, x, y, width, height);
    }

    private static SKShader? CreateGradient(TextGradient? gradient, double opacity, float x, float y, float width, float height)
    {
        if (gradient is not { Enabled: true }) return null;
        var stops = (gradient.Stops ?? []).Where(s => SKColor.TryParse(s.Color, out _)).OrderBy(s => s.Position).ToArray();
        if (stops.Length < 2) return null;
        var colors = stops.Select(s => { SKColor.TryParse(s.Color, out var c); return ApplyOpacity(c, opacity); }).ToArray();
        var positions = stops.Select(s => (float)Math.Clamp(s.Position, 0, 1)).ToArray();
        var center = new SKPoint(x + width / 2, y + height / 2);
        var angle = (float)(gradient.Angle * Math.PI / 180);
        var radius = Math.Max(1, Math.Max(width, height) / 2);
        return gradient.Kind switch
        {
            TextGradientKind.Radial => SKShader.CreateRadialGradient(center, radius, colors, positions, SKShaderTileMode.Clamp),
            TextGradientKind.Angular => SKShader.CreateSweepGradient(center, colors, positions, SKMatrix.CreateRotation(angle, center.X, center.Y)),
            _ => SKShader.CreateLinearGradient(new SKPoint(center.X - MathF.Cos(angle) * radius, center.Y - MathF.Sin(angle) * radius), new SKPoint(center.X + MathF.Cos(angle) * radius, center.Y + MathF.Sin(angle) * radius), colors, positions, SKShaderTileMode.Clamp)
        };
    }

    private static void DrawBackground(SKCanvas canvas, TextBackground background, double layerOpacity, float x, float y, float width, float height)
    {
        var px = (float)Math.Clamp(background.PaddingX, 0, 256); var py = (float)Math.Clamp(background.PaddingY, 0, 256);
        var rect = new SKRect(x - px, y - py, x + width + px, y + height + py);
        var radius = (float)Math.Clamp(background.Radius, 0, Math.Min(rect.Width, rect.Height) / 2);
        var opacity = Math.Clamp(background.Opacity, 0, 1) * layerOpacity;
        using var shader = CreateGradient(background.Gradient, opacity, rect.Left, rect.Top, rect.Width, rect.Height);
        var color = SKColor.TryParse(background.Color, out var parsed) ? parsed : SKColors.Black;
        using var fill = new SKPaint { IsAntialias = true, Color = shader is null ? ApplyOpacity(color, opacity) : SKColors.White, Shader = shader };
        canvas.DrawRoundRect(rect, radius, radius, fill);
        if (background.BorderWidth > 0 && SKColor.TryParse(background.BorderColor, out var borderColor))
        {
            using var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Clamp(background.BorderWidth, 0, 100), Color = ApplyOpacity(borderColor, opacity) };
            canvas.DrawRoundRect(rect, radius, radius, border);
        }
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

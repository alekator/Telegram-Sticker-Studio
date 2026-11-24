using System.Globalization;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using StickerForge.Core;

namespace StickerForge.Media;

/// <summary>Renders sticker-space text independently of the source image transform.</summary>
public static class TextLayerRenderer
{
    private const int LayerCacheLimit = 12;
    private static readonly object LayerCacheLock = new();
    private static readonly Dictionary<string, SKImage> LayerCache = new(StringComparer.Ordinal);
    private static readonly Queue<string> LayerCacheOrder = new();

    public static SKImage RenderOverlay(IReadOnlyList<TextLayer> layers)
    {
        using var surface = SKSurface.Create(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidDataException("Unable to create text canvas.");
        surface.Canvas.Clear(SKColors.Transparent);
        foreach (var layer in layers)
            if (layer.IsVisible && !string.IsNullOrEmpty(layer.Text)) DrawCachedLayer(surface.Canvas, layer);
        return surface.Snapshot();
    }

    private static void DrawCachedLayer(SKCanvas destination, TextLayer layer)
    {
        var key = System.Text.Json.JsonSerializer.Serialize(layer);
        if (!string.IsNullOrEmpty(layer.FontAsset))
        {
            // Resolve also checks the font against the manifest before a cached
            // rendering can be reused after a font file was changed or removed.
            var path = BundledFontCatalog.Resolve(layer.FontAsset);
            var info = new FileInfo(path);
            key += $"|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        lock (LayerCacheLock)
        {
            if (LayerCache.TryGetValue(key, out var cached)) { destination.DrawImage(cached, 0, 0); return; }
        }

        using var layerSurface = SKSurface.Create(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidDataException("Unable to create a text layer canvas.");
        layerSurface.Canvas.Clear(SKColors.Transparent);
        Draw(layerSurface.Canvas, layer);
        var image = layerSurface.Snapshot();
        lock (LayerCacheLock)
        {
            if (LayerCache.TryGetValue(key, out var existing)) { image.Dispose(); destination.DrawImage(existing, 0, 0); return; }
            while (LayerCache.Count >= LayerCacheLimit && LayerCacheOrder.TryDequeue(out var oldest))
                if (LayerCache.Remove(oldest, out var removed)) removed.Dispose();
            LayerCache.Add(key, image); LayerCacheOrder.Enqueue(key);
            destination.DrawImage(image, 0, 0);
        }
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
        var backgroundRects = new List<SKRect>();
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
                SKRect? lineBounds = null;
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
                    var glyphIds = result.Codepoints.Select(glyph => (ushort)glyph).ToArray();
                    SKRotationScaleMatrix[]? curvedTransforms = null;
                    if (Math.Abs(layer.Curvature) > .001 && Math.Abs(width) > .001)
                    {
                        curvedTransforms = new SKRotationScaleMatrix[points.Length];
                        for (var i = 0; i < points.Length; i++)
                        {
                            var (point, angle) = ArcPosition(x + points[i].X, lineX, width, layer.Curvature);
                            curvedTransforms[i] = new SKRotationScaleMatrix((float)Math.Cos(angle), (float)Math.Sin(angle), point.X - x, point.Y + points[i].Y);
                        }
                        builder.AddRotationScaleRun(glyphIds, run.Font, curvedTransforms);
                    }
                    else builder.AddPositionedRun(glyphIds, run.Font, points);
                    using var blob = builder.Build();
                    if (blob is not null)
                    {
                        var baseline = firstBaseline + lineIndex * lineHeight;
                        var needsMask = layer.Glow is { InnerEnabled: true, InnerRadius: > 0 }
                            || layer.RasterEffects?.Emboss is { Enabled: true, Depth: > 0, Strength: > 0 }
                            || layer.RasterEffects?.Inset is { Enabled: true, Depth: > 0, Strength: > 0 };
                        using var glyphMask = needsMask ? CreateGlyphMask(canvas, blob, x, baseline, layer.RasterEffects) : null;
                        var bounds = blob.Bounds; bounds.Offset(x, baseline);
                        if (!bounds.IsEmpty) lineBounds = lineBounds is { } previous ? SKRect.Union(previous, bounds) : bounds;
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
                        var rasterEffects = layer.RasterEffects;
                        DrawGlyphWithRasterEffects(canvas, blob, x, baseline, paint, rasterEffects);
                        if (rasterEffects is { MetallicEnabled: true, MetallicStrength: > 0 } && SKColor.TryParse(rasterEffects.MetallicColor, out var metalColor))
                        {
                            var opacity = Math.Clamp(rasterEffects.MetallicStrength, 0, 1) * layer.Opacity;
                            var bright = ApplyOpacity(metalColor, opacity);
                            var dark = ApplyOpacity(new SKColor((byte)(metalColor.Red * .22), (byte)(metalColor.Green * .22), (byte)(metalColor.Blue * .22), metalColor.Alpha), opacity);
                            using var metalShader = SKShader.CreateLinearGradient(new SKPoint(0, lineBaseline + metrics.Ascent), new SKPoint(0, lineBaseline + metrics.Descent), [bright, dark, bright, dark, bright], [0f, .28f, .48f, .67f, 1f], SKShaderTileMode.Clamp);
                            using var metalPaint = new SKPaint { IsAntialias = true, Shader = metalShader, Color = SKColors.White };
                            DrawGlyphWithRasterEffects(canvas, blob, x, baseline, metalPaint, rasterEffects);
                        }
                        if (rasterEffects is { GlossEnabled: true, GlossStrength: > 0 })
                        {
                            var highlight = ApplyOpacity(SKColors.White, Math.Clamp(rasterEffects.GlossStrength, 0, 1) * layer.Opacity);
                            using var glossShader = SKShader.CreateLinearGradient(new SKPoint(0, lineBaseline + metrics.Ascent), new SKPoint(0, lineBaseline + metrics.Descent), [highlight, SKColors.Transparent, SKColors.Transparent], [0f, .5f, 1f], SKShaderTileMode.Clamp);
                            using var glossPaint = new SKPaint { IsAntialias = true, Shader = glossShader, Color = SKColors.White };
                            DrawGlyphWithRasterEffects(canvas, blob, x, baseline, glossPaint, rasterEffects);
                        }
                        if (rasterEffects?.Emboss is { Enabled: true } emboss && glyphMask is not null) DrawRelief(canvas, blob, x, baseline, emboss, layer.Opacity, false, glyphMask);
                        if (rasterEffects?.Inset is { Enabled: true } inset && glyphMask is not null) DrawRelief(canvas, blob, x, baseline, inset, layer.Opacity, true, glyphMask);
                        if (layer.Glow is { InnerEnabled: true, InnerRadius: > 0 } innerGlow && SKColor.TryParse(innerGlow.InnerColor, out var innerColor))
                        {
                            canvas.SaveLayer();
                            using var innerPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, StrokeWidth = (float)Math.Clamp(innerGlow.InnerRadius, .01, 100) * 2, Color = ApplyOpacity(innerColor, GlowOpacity(innerGlow.InnerOpacity, innerGlow.InnerIntensity) * layer.Opacity) };
                            using var innerBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)Math.Clamp(innerGlow.InnerRadius / 2, .01, 50));
                            innerPaint.MaskFilter = innerBlur;
                            canvas.DrawText(blob, x, baseline, innerPaint);
                            canvas.Save(); canvas.ResetMatrix();
                            using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
                            canvas.DrawImage(glyphMask!, 0, 0, maskPaint);
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
                                    if (glyph is not null)
                                    {
                                        if (curvedTransforms is null) glyphs.AddPath(glyph, x + points[i].X, baseline + points[i].Y);
                                        else
                                        {
                                            var matrix = curvedTransforms[i].ToMatrix(); matrix.TransX += x; matrix.TransY += baseline;
                                            glyphs.AddPath(glyph, matrix);
                                        }
                                    }
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
                            using var linePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = paint.Color, StrokeWidth = Math.Max(1, font.Size / 18) };
                            if (layer.Underline) DrawDecoration(canvas, linePaint, x, result.Width, baseline, font.Size * .12f, lineX, width, layer.Curvature);
                            if (layer.Strikethrough) DrawDecoration(canvas, linePaint, x, result.Width, baseline, -font.Size * .3f, lineX, width, layer.Curvature);
                        }
                    }
                    x += result.Width + (clusters + 1) * spacing;
                }
                if (lineBounds is { } completeLine) backgroundRects.Add(completeLine);
            }
            finally { foreach (var run in runs) if (run.Fallback is not null) { run.Font.Dispose(); run.Fallback.Dispose(); } }
        }
        if (layer.Background is { Enabled: true } background && backgroundRects.Count > 0)
        {
            if (background.PerLine)
            {
                foreach (var rect in backgroundRects) DrawBackground(canvas, background, layer.Opacity, rect.Left, rect.Top, rect.Width, rect.Height);
            }
            else
            {
                var rect = backgroundRects.Aggregate(SKRect.Union);
                DrawBackground(canvas, background, layer.Opacity, rect.Left, rect.Top, rect.Width, rect.Height);
            }
        }
        canvas.Restore();
    }

    private static SKColor ApplyOpacity(SKColor color, double opacity) => color.WithAlpha((byte)Math.Clamp(Math.Round(color.Alpha * Math.Clamp(opacity, 0, 1)), 0, 255));
    private static double GlowOpacity(double opacity, double intensity) => 1 - Math.Pow(1 - Math.Clamp(opacity, 0, 1), Math.Clamp(intensity, 0, 5));

    private static SKImage CreateGlyphMask(SKCanvas canvas, SKTextBlob blob, float x, float baseline, TextRasterEffects? effects)
    {
        using var surface = SKSurface.Create(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidDataException("Unable to create a text mask.");
        surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.SetMatrix(canvas.TotalMatrix);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        DrawGlyphWithRasterEffects(surface.Canvas, blob, x, baseline, paint, effects);
        return surface.Snapshot();
    }

    private static void DrawGlyphWithRasterEffects(SKCanvas canvas, SKTextBlob blob, float x, float baseline, SKPaint paint, TextRasterEffects? effects)
    {
        using var blur = effects is { BlurEnabled: true, BlurRadius: > 0 } ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)Math.Clamp(effects.BlurRadius, .01, 100)) : null;
        paint.MaskFilter = blur;
        if (effects is { PixelationEnabled: true, PixelSize: > 1 } or { ChromaticEnabled: true, ChromaticOffset: > 0 }) DrawProcessedGlyphs(canvas, blob, x, baseline, paint, effects);
        else canvas.DrawText(blob, x, baseline, paint);
        paint.MaskFilter = null;
    }

    private static void DrawRelief(SKCanvas canvas, SKTextBlob blob, float x, float baseline, TextRelief settings, double layerOpacity, bool inset, SKImage maskImage)
    {
        var depth = (float)Math.Clamp(settings.Depth, 0, 30);
        var opacity = Math.Clamp(settings.Strength, 0, 1) * layerOpacity;
        if (depth <= 0 || opacity <= 0) return;
        var angle = settings.Angle * Math.PI / 180;
        var dx = (float)Math.Cos(angle) * depth; var dy = (float)Math.Sin(angle) * depth;
        if (inset) { dx = -dx; dy = -dy; }
        canvas.SaveLayer();
        using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(.1f, depth / 2));
        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = depth * 2, StrokeJoin = SKStrokeJoin.Round, MaskFilter = blur };
        edge.Color = ApplyOpacity(SKColors.Black, opacity); canvas.DrawText(blob, x + dx, baseline + dy, edge);
        edge.Color = ApplyOpacity(SKColors.White, opacity); canvas.DrawText(blob, x - dx, baseline - dy, edge);
        canvas.Save(); canvas.ResetMatrix();
        using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
        canvas.DrawImage(maskImage, 0, 0, maskPaint);
        canvas.Restore(); canvas.Restore();
    }

    private static (SKPoint Point, double Angle) ArcPosition(float x, float lineX, float width, double degrees)
    {
        var totalAngle = Math.Clamp(degrees, -300, 300) * Math.PI / 180;
        if (Math.Abs(totalAngle) < .00001 || Math.Abs(width) < .001) return (new SKPoint(x, 0), 0);
        var center = lineX + width / 2;
        var angle = (x - center) / width * totalAngle;
        var radius = width / totalAngle;
        return (new SKPoint((float)(center + radius * Math.Sin(angle)), (float)(radius * (1 - Math.Cos(angle)))), angle);
    }

    private static void DrawDecoration(SKCanvas canvas, SKPaint paint, float x, float width, float baseline, float offset, float lineX, float lineWidth, double curvature)
    {
        if (Math.Abs(curvature) <= .001) { canvas.DrawLine(x, baseline + offset, x + width, baseline + offset, paint); return; }
        using var path = new SKPath();
        for (var i = 0; i <= 24; i++)
        {
            var (point, angle) = ArcPosition(x + width * i / 24, lineX, lineWidth, curvature);
            var p = new SKPoint(point.X - (float)Math.Sin(angle) * offset, baseline + point.Y + (float)Math.Cos(angle) * offset);
            if (i == 0) path.MoveTo(p); else path.LineTo(p);
        }
        canvas.DrawPath(path, paint);
    }

    private static void DrawProcessedGlyphs(SKCanvas canvas, SKTextBlob blob, float x, float y, SKPaint paint, TextRasterEffects effects)
    {
        using var surface = SKSurface.Create(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidDataException("Unable to create the text effects canvas.");
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.SetMatrix(canvas.TotalMatrix);
        surface.Canvas.DrawText(blob, x, y, paint);
        using var source = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(source);
        var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
        var size = Math.Max(1, (int)Math.Ceiling(512d / Math.Clamp(effects.PixelSize, 2, 64)));
        using var small = effects.PixelationEnabled ? bitmap.Resize(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul), sampling) : null;
        using var large = effects.PixelationEnabled ? small?.Resize(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul), sampling) : null;
        if (effects.PixelationEnabled && large is null) throw new InvalidDataException("Unable to scale the pixelation canvas.");
        var raster = large ?? bitmap;
        using var chromatic = effects.ChromaticEnabled && effects.ChromaticOffset > 0 ? ShiftColorChannels(raster, Math.Clamp(effects.ChromaticOffset, 1, 64)) : null;
        using var image = SKImage.FromBitmap(chromatic ?? raster);
        canvas.Save(); canvas.ResetMatrix(); canvas.DrawImage(image, 0, 0); canvas.Restore();
    }

    private static SKBitmap ShiftColorChannels(SKBitmap source, int offset)
    {
        var result = new SKBitmap(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (source.ColorType == SKColorType.Rgba8888 && source.AlphaType == SKAlphaType.Premul)
        {
            var input = new byte[source.RowBytes * 512];
            var output = new byte[result.RowBytes * 512];
            System.Runtime.InteropServices.Marshal.Copy(source.GetPixels(), input, 0, input.Length);
            for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
            {
                var middle = y * source.RowBytes + x * 4; var target = y * result.RowBytes + x * 4;
                var red = x >= offset ? middle - offset * 4 : -1;
                var blue = x + offset < 512 ? middle + offset * 4 : -1;
                output[target] = red >= 0 ? input[red] : (byte)0;
                output[target + 1] = input[middle + 1];
                output[target + 2] = blue >= 0 ? input[blue + 2] : (byte)0;
                output[target + 3] = Math.Max(input[middle + 3], Math.Max(red >= 0 ? input[red + 3] : (byte)0, blue >= 0 ? input[blue + 3] : (byte)0));
            }
            System.Runtime.InteropServices.Marshal.Copy(output, 0, result.GetPixels(), output.Length);
            return result;
        }
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var red = x >= offset ? source.GetPixel(x - offset, y) : SKColors.Transparent;
            var green = source.GetPixel(x, y);
            var blue = x + offset < 512 ? source.GetPixel(x + offset, y) : SKColors.Transparent;
            var alpha = Math.Max(red.Alpha, Math.Max(green.Alpha, blue.Alpha));
            if (alpha == 0) continue;
            result.SetPixel(x, y, new SKColor((byte)(red.Red * red.Alpha / alpha), (byte)(green.Green * green.Alpha / alpha), (byte)(blue.Blue * blue.Alpha / alpha), alpha));
        }
        return result;
    }

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
        if (background.BorderWidth > 0 && SKColor.TryParse(background.BorderColor, out var borderColor))
        {
            using var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Clamp(background.BorderWidth, 0, 100), Color = ApplyOpacity(borderColor, opacity), BlendMode = SKBlendMode.DstOver };
            canvas.DrawRoundRect(rect, radius, radius, border);
        }
        using var fill = new SKPaint { IsAntialias = true, Color = shader is null ? ApplyOpacity(color, opacity) : SKColors.White, Shader = shader, BlendMode = SKBlendMode.DstOver };
        canvas.DrawRoundRect(rect, radius, radius, fill);
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

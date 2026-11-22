using System.Diagnostics;
using ImageSpace.Core;
using ImageSpace.Skia;
using SkiaSharp;
using Xunit.Abstractions;

namespace StickerForge.Tests;

public sealed class GraphicsPerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(512, 2)]
    [InlineData(2048, 4)]
    public void MeasuresCpuRenderer(int size, int layerCount)
    {
        var document = new ImageDocument(size, size);
        for (int i = 0; i < layerCount; i++)
        {
            var layer = Layer.Raster($"Layer {i}", size, size);
            layer.Pixels!.Fill(new Rgba32(100, 80, 180, (byte)(i == 0 ? 255 : 70)));
            document.Layers.Add(layer);
        }
        using var renderer = new ImageRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        void Draw() { surface.Canvas.Clear(SKColors.Transparent); var snapshot = document.Snapshot(); renderer.Draw(surface.Canvas, snapshot); renderer.Prune(snapshot); surface.Canvas.Flush(); }
        Draw(); Draw();
        var samples = new double[20]; var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var process = Process.GetCurrentProcess(); process.Refresh(); var memory = process.PrivateMemorySize64;
        for (int i = 0; i < samples.Length; i++)
        {
            document.Layers[^1].Pixels!.Set(i, i, new Rgba32(255, 0, 0, 255));
            var start = Stopwatch.GetTimestamp(); Draw(); samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        var allocations = (GC.GetAllocatedBytesForCurrentThread() - allocated) / samples.Length;
        process.Refresh(); var delta = process.PrivateMemorySize64 - memory;
        Array.Sort(samples);
        output.WriteLine($"{size}x{size}/{layerCount} layers: median {samples[10]:F2} ms; p95 {samples[19]:F2} ms; managed/frame {allocations} bytes; private delta {delta} bytes");
        using var result = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(result);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(19, 19));
    }
}

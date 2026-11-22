using System.Diagnostics;
using ImageSpace.Core;
using SkiaSharp;

namespace ImageSpaceAvalonia;

internal static class Benchmark
{
    public static void Run()
    {
        Measure(512, 2, 200);
        Measure(2048, 8, 80);
        Stress();
    }

    private static void Measure(int size, int layers, int frames)
    {
        var document = new ImageDocument(size, size, "Benchmark");
        for (var i = 0; i < layers; i++)
        {
            var layer = Layer.Raster($"Layer {i}", size, size);
            layer.Pixels!.Fill(new Rgba32((byte)(30 * i), 100, 180, (byte)(i == 0 ? 255 : 60)));
            document.Layers.Add(layer);
        }
        document.ActiveLayerId = document.Layers[^1].Id;
        using var renderer = new DirectRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        void Draw() { surface.Canvas.Clear(SKColors.Transparent); renderer.Draw(surface.Canvas, document.Snapshot(), new ViewTransform(1, 0, 0)); surface.Canvas.Flush(); }
        for (var i = 0; i < 10; i++) Draw();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess(); process.Refresh(); var before = process.PrivateMemorySize64;
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var times = new double[frames];
        for (var i = 0; i < frames; i++)
        {
            // One changed pixel exercises tile cache invalidation and copy-on-write.
            document.Layers[^1].Pixels!.Set(i % size, i % size, new Rgba32(240, 20, 20, 255));
            var start = Stopwatch.GetTimestamp(); Draw(); times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        using (var image = surface.Snapshot()) using (var bitmap = SKBitmap.FromImage(image))
        {
            var pixel = bitmap.GetPixel((frames - 1) % size, (frames - 1) % size);
            if (pixel.Red < 200 || pixel.Alpha != 255) throw new InvalidOperationException("Changed pixel missing from benchmark render");
        }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); process.Refresh(); var after = process.PrivateMemorySize64;
        Array.Sort(times);
        Console.WriteLine($"{size}x{size}, {layers} layers, {frames} frames: median={times[frames / 2]:F2}ms p95={times[(int)(frames * .95)]:F2}ms managed/frame={bytes / frames}B private-delta={after - before}B");
    }

    private static void Stress()
    {
        using var process = Process.GetCurrentProcess();
        for (int cycle = 0; cycle < 5; cycle++)
        {
            RunCycle();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); process.Refresh();
            Console.WriteLine($"close cycle {cycle + 1}: private={process.PrivateMemorySize64}B managed-live={GC.GetTotalMemory(false)}B");
        }
        static void RunCycle()
        {
            var doc = new ImageDocument(512, 512, "Stress");
            var layer = Layer.Raster("Paint", 512, 512); doc.Layers.Add(layer); doc.ActiveLayerId = layer.Id;
            using var renderer = new DirectRenderer();
            using var surface = SKSurface.Create(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul));
            for (int i = 0; i < 200; i++)
            {
                layer.Pixels!.Set(i, i, new Rgba32(255, 0, 0, 255));
                renderer.Draw(surface.Canvas, doc.Snapshot(), new ViewTransform(1, 0, 0));
            }
        }
    }
}

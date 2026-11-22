using System.Numerics;
using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Editing;
using ImageSpace.Imaging;
using ImageSpace.Skia;
using SkiaSharp;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine($"PASS: {message}");
}

var session = new EditorSession(new ImageDocument(64, 64, "Experiment"));
session.AddLayer("Background");
session.AddLayer("Paint");
Check(session.Document.Layers.Count == 2, "two layers");
session.Execute("Brush", document =>
{
    var layer = document.Layers.Single(l => l.Name == "Paint");
    var brush = new BrushEngine();
    brush.Begin(layer.Pixels!, Vector2.Zero);
    brush.Paint(document, layer, new Vector2(32, 32), 1, new BrushSettings(Size: 12, Hardness: 1, Flow: 1), new Rgba32(255, 0, 0, 255), PaintMode.Brush);
    brush.End();
});
Check(session.Document.Layers.Single(l => l.Name == "Paint").Pixels!.Get(32, 32).A > 0, "brush paints");
session.Undo();
Check(session.Document.Layers.Single(l => l.Name == "Paint").Pixels!.Get(32, 32).A == 0, "undo removes brush");
session.Redo();
Check(session.Document.Layers.Single(l => l.Name == "Paint").Pixels!.Get(32, 32).A > 0, "redo restores brush");
byte[] bytes = DocumentArchive.Save(session.Document);
var path = Path.Combine(Path.GetTempPath(), $"imagespace-experiment-{Guid.NewGuid():N}.imagespace");
File.WriteAllBytes(path, bytes);
ImageDocument reopened;
try { reopened = DocumentArchive.Load(File.ReadAllBytes(path)); }
finally { File.Delete(path); }
Check(reopened.Layers.Count == 2 && reopened.Layers.Single(l => l.Name == "Paint").Pixels!.Get(32, 32).A > 0, "archive restores layers and brush");
using var renderer = new ImageRenderer();
byte[] png = renderer.Export(reopened, SKEncodedImageFormat.Png);
using var decoded = SKBitmap.Decode(png);
Check(decoded != null && decoded.GetPixel(0, 0).Alpha == 0 && decoded.GetPixel(32, 32).Alpha > 0, "PNG retains transparent and painted pixels");

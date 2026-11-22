using Composa.Editing;
using Composa.IO;
using Composa.Painting;
using Composa.Rendering;
using SkiaSharp;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine($"PASS: {message}");
}

var session = EditorSession.NewCanvas(64, 64);
session.AddBlankLayer();
Check(session.Document.Layers.Count == 2, "two layers");
var problem = session.PaintStroke([new SKPoint(32, 32)], new BrushSettings(), SKColors.Red);
Check(problem == null, $"brush accepted: {problem}");
using (var painted = DocumentRenderer.Flatten(session.Document))
    Check(painted.GetPixel(32, 32).Alpha > 0, "brush paints");
session.Undo();
using (var undone = DocumentRenderer.Flatten(session.Document))
    Check(undone.GetPixel(32, 32).Alpha == 0, "undo removes brush");
session.Redo();
using (var redone = DocumentRenderer.Flatten(session.Document))
    Check(redone.GetPixel(32, 32).Alpha > 0, "redo restores brush");
var path = Path.Combine(Path.GetTempPath(), $"composa-experiment-{Guid.NewGuid():N}.composa");
try
{
    ProjectFile.Save(session.Document, path);
    var reopened = ProjectFile.Load(path);
    Check(reopened.Layers.Count == 2, "project restores two layers");
    using var flattened = DocumentRenderer.Flatten(reopened);
    using var image = SKImage.FromBitmap(flattened);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var decoded = SKBitmap.Decode(data.ToArray());
    Check(decoded.GetPixel(0, 0).Alpha == 0 && decoded.GetPixel(32, 32).Alpha > 0, "PNG retains transparent and painted pixels after reopening");
}
finally { if (File.Exists(path)) File.Delete(path); }

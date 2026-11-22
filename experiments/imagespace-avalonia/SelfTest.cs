using System.Numerics;
using SkiaSharp;

namespace ImageSpaceAvalonia;

internal static class SelfTest
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine($"PASS {message}"); }
    public static void Run()
    {
        var view = new ViewTransform(2, 40, 60);
        Check(Vector2.Distance(view.ToDocument(140, 160), new Vector2(50, 50)) < .001, "pointer maps to document at zoom");
        var zoom = view.ZoomAt(1.5, 140, 160);
        Check(Vector2.Distance(zoom.ToDocument(140, 160), new Vector2(50, 50)) < .001, "zoom preserves pointer anchor");
        using var editor = new Editor();
        Check(editor.Session.Document.Layers.Count == 2, "two editable layers");
        var topId = editor.Session.Document.ActiveLayerId;
        editor.Session.MoveLayer(-1);
        Check(editor.Session.Document.Layers[0].Id == topId, "reorder layer");
        editor.Session.Undo();
        Check(editor.Session.Document.Layers[1].Id == topId, "undo layer order");
        editor.Begin(view.ToDocument(140, 160)); editor.Continue(view.ToDocument(540, 160)); editor.End();
        var layer = editor.Session.Document.ActiveLayer!;
        for (int x = 50; x <= 245; x += 5) Check(layer.Pixels!.Get(x, 50).A > 0, $"fast stroke continuity x={x}");
        editor.Session.Undo(); Check(editor.Session.Document.ActiveLayer!.Pixels!.Get(100, 50).A == 0, "stroke undo");
        editor.Session.Redo(); Check(editor.Session.Document.ActiveLayer!.Pixels!.Get(100, 50).A > 0, "stroke redo");
        editor.Eraser = true; editor.Begin(new(100, 50)); editor.End();
        Check(editor.Session.Document.ActiveLayer!.Pixels!.Get(100, 50).A == 0, "eraser");
        editor.Session.Undo(); Check(editor.Session.Document.ActiveLayer!.Pixels!.Get(100, 50).A > 0, "eraser undo");
        var active = editor.Session.Document.ActiveLayer!;
        editor.Session.Execute("hide", _ => active.Visible = false);
        using (var hidden = SKBitmap.Decode(editor.RenderPng())) Check(hidden.GetPixel(100, 50).Alpha == 0, "layer visibility");
        editor.Session.Undo();
        var before = editor.RenderPng();
        var file = Path.Combine(Path.GetTempPath(), $"imagespace-{Guid.NewGuid():N}.imagespace");
        try { File.WriteAllBytes(file, editor.Save()); editor.Open(File.ReadAllBytes(file)); }
        finally { if (File.Exists(file)) File.Delete(file); }
        Check(editor.Session.Document.Layers.Count == 2 && editor.Session.Document.ActiveLayer!.Pixels!.Get(100, 50).A > 0, "save/open editable layers");
        using var display = SKBitmap.Decode(before); using var exported = SKBitmap.Decode(editor.RenderPng());
        Check(exported.GetPixel(0, 0).Alpha == 0 && exported.GetPixel(100, 50).Alpha > 0, "PNG alpha");
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
            if (display.GetPixel(x, y) != exported.GetPixel(x, y)) throw new Exception($"render differs at {x},{y}");
        Console.WriteLine("PASS display image equals exported PNG after roundtrip");
    }
}

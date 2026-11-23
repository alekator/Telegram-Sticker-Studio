using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Themes.Fluent;
using Avalonia.Input;
using System.Text.Json;
using SkiaSharp;
using StickerForge.Desktop;
using StickerForge.Infrastructure;

namespace StickerForge.Tests;

public static class WindowLayoutTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<WindowLayoutTestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class WindowLayoutTestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public sealed class WindowLayoutTests
{
    [Fact]
    public async Task PanelsFitSmallAndLargeWindowsAndRestoreState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-panels-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(async () =>
            {
                var path = Path.Combine(directory, "panels.json");
                var window = new MainWindow(new PanelLayoutStore(path)); window.Show();
                try
                {
                    var grid = (Grid)typeof(MainWindow).GetField("workspaceGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var canvas = (Border)typeof(MainWindow).GetField("previewCanvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var viewbox = (Viewbox)typeof(MainWindow).GetField("previewViewbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    void CheckCanvasFits()
                    {
                        var child = (Control)viewbox.Child!;
                        var first = child.TranslatePoint(new Point(0, 0), canvas)!.Value;
                        var last = child.TranslatePoint(new Point(512, 512), canvas)!.Value;
                        Assert.InRange(first.X, -1, canvas.Bounds.Width); Assert.InRange(first.Y, -1, canvas.Bounds.Height);
                        Assert.InRange(last.X, 0, canvas.Bounds.Width + 1); Assert.InRange(last.Y, 0, canvas.Bounds.Height + 1);
                    }
                    window.Width = 820; window.Height = 520; await Task.Delay(100);
                    Assert.True(grid.ColumnDefinitions[2].ActualWidth >= 220);
                    Assert.True(grid.Bounds.Width <= 820);
                    typeof(MainWindow).GetMethod("ActualPreviewSize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); await Task.Delay(100); CheckCanvasFits();
                    window.Width = 1280; window.Height = 720; await Task.Delay(100);
                    Assert.True(grid.ColumnDefinitions[2].ActualWidth >= 220); CheckCanvasFits();
                    window.Width = 1920; window.Height = 1080; await Task.Delay(100);
                    Assert.True(grid.ColumnDefinitions[2].ActualWidth > 600); CheckCanvasFits();
                    var imagePath = Path.Combine(directory, "sample.png");
                    using (var bitmap = new SKBitmap(32, 24)) using (var image = SKImage.FromBitmap(bitmap)) using (var png = image.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(imagePath, png.ToArray());
                    await (Task)typeof(MainWindow).GetMethod("ImportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { imagePath }])!;
                    var gallery = (ListBox)typeof(MainWindow).GetField("gallery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    gallery.SelectedIndex = 0; await Task.Delay(100);
                    var project = (StickerForge.Core.StickerProject)typeof(MainWindow).GetField("project", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var transformBefore = JsonSerializer.Serialize(project.Stickers[0].Transform);
                    typeof(MainWindow).GetMethod("ActualPreviewSize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); await Task.Delay(100); CheckCanvasFits();
                    typeof(MainWindow).GetMethod("FitPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    Assert.Equal(transformBefore, JsonSerializer.Serialize(project.Stickers[0].Transform));
                    var card = Assert.IsType<ListBoxItem>(gallery.SelectedItem);
                    var presenter = card.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
                    Assert.Equal(Color.Parse("#302841"), Assert.IsType<SolidColorBrush>(presenter.Background).Color);
                    Assert.False(((Control)typeof(MainWindow).GetField("animationBar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).IsVisible);
                    grid.ColumnDefinitions[0].Width = new GridLength(360); await Task.Delay(100);
                    typeof(MainWindow).GetMethod("CapturePanelWidth", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true]);
                    typeof(MainWindow).GetMethod("ToggleProperties", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    typeof(MainWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false); window.Close();
                    var saved = new PanelLayoutStore(path).Load(); Assert.InRange(saved.GalleryWidth, 359, 361); Assert.False(saved.PropertiesVisible);
                    var reopened = new MainWindow(new PanelLayoutStore(path)); reopened.Show(); await Task.Delay(100);
                    try
                    {
                        var restored = (Grid)typeof(MainWindow).GetField("workspaceGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reopened)!;
                        Assert.Equal(0, restored.ColumnDefinitions[4].ActualWidth);
                        Assert.InRange(restored.ColumnDefinitions[0].ActualWidth, 359, 361);
                        typeof(MainWindow).GetMethod("ToggleGallery", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reopened, null);
                        await Task.Delay(100); Assert.Equal(0, restored.ColumnDefinitions[0].ActualWidth);
                    }
                    finally { reopened.Close(); }
                    Assert.False(new PanelLayoutStore(path).Load().GalleryVisible);
                }
                finally { if (window.IsVisible) { typeof(MainWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false); window.Close(); } }
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task TimelineHandlesAndFieldsStaySynchronized()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-timeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(async () =>
            {
                var window = new MainWindow(new PanelLayoutStore(Path.Combine(directory, "layout.json"))); window.Show();
                try
                {
                    window.Width = 1280; window.Height = 720;
                    var timeline = (AnimationTimeline)typeof(MainWindow).GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var startInput = (NumericUpDown)typeof(MainWindow).GetField("fragmentStart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var durationInput = (NumericUpDown)typeof(MainWindow).GetField("fragmentDuration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var bar = (Control)typeof(MainWindow).GetField("animationBar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    typeof(MainWindow).GetField("animationDuration", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 4d);
                    bar.IsVisible = true; timeline.SetState(4, 0, 3, 0); await Task.Delay(100);
                    Assert.True(startInput.Bounds.Width >= 110); Assert.True(durationInput.Bounds.Width >= 110);
                    var from = timeline.TranslatePoint(new Point(10, timeline.Bounds.Height / 2), window)!.Value;
                    var to = timeline.TranslatePoint(new Point(10 + (timeline.Bounds.Width - 20) / 4, timeline.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(from, MouseButton.Left, RawInputModifiers.None); window.MouseMove(to, RawInputModifiers.LeftMouseButton); window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);
                    Assert.InRange(timeline.Start, .8, 1.2); Assert.InRange((double)startInput.Value!, .8, 1.2);
                    durationInput.Value = 1.2m; await Task.Delay(100); Assert.InRange(timeline.Length, 1.19, 1.21);
                    var endX = 10 + (timeline.Bounds.Width - 20) * (timeline.Start + timeline.Length) / timeline.Duration;
                    var endFrom = timeline.TranslatePoint(new Point(endX, timeline.Bounds.Height / 2), window)!.Value;
                    var endTo = timeline.TranslatePoint(new Point(timeline.Bounds.Width - 10, timeline.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(endFrom, MouseButton.Left, RawInputModifiers.None); window.MouseMove(endTo, RawInputModifiers.LeftMouseButton); window.MouseUp(endTo, MouseButton.Left, RawInputModifiers.None);
                    Assert.InRange(timeline.Length, 2.8, 3.0); Assert.InRange((double)durationInput.Value!, 2.8, 3.0);
                }
                finally { window.Close(); }
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }
}

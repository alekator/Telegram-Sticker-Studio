using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Themes.Fluent;
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
                    window.Width = 820; window.Height = 520; await Task.Delay(100);
                    Assert.True(grid.ColumnDefinitions[2].ActualWidth >= 220);
                    Assert.True(grid.Bounds.Width <= 820);
                    window.Width = 1600; window.Height = 1000; await Task.Delay(100);
                    Assert.True(grid.ColumnDefinitions[2].ActualWidth > 600);
                    var imagePath = Path.Combine(directory, "sample.png");
                    using (var bitmap = new SKBitmap(32, 24)) using (var image = SKImage.FromBitmap(bitmap)) using (var png = image.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(imagePath, png.ToArray());
                    await (Task)typeof(MainWindow).GetMethod("ImportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { imagePath }])!;
                    var gallery = (ListBox)typeof(MainWindow).GetField("gallery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    gallery.SelectedIndex = 0; await Task.Delay(100);
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
}

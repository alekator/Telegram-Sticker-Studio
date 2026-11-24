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
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<WindowLayoutTestApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class WindowLayoutTestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

[Collection("Avalonia Headless")]
public sealed class WindowLayoutTests
{
    [FontBundleFact]
    public async Task CatalogSearchSelectsBundledFontAndPersistsFavorites()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-font-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(async () =>
            {
                var path = Path.Combine(directory, "image.png");
                using (var image = new SKBitmap(64, 64)) using (var frame = SKImage.FromBitmap(image)) using (var png = frame.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(path, png.ToArray());
                var fontPrefs = new FontPreferencesStore(Path.Combine(directory, "fonts.json"));
                var window = new MainWindow(new PanelLayoutStore(Path.Combine(directory, "layout.json")), new UiPreferencesStore(Path.Combine(directory, "ui.json")), fontPrefs) { Width = 1280, Height = 800 }; window.Show();
                try
                {
                    await (Task)typeof(MainWindow).GetMethod("ImportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { path }])!;
                    var gallery = (ListBox)typeof(MainWindow).GetField("gallery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!; gallery.SelectedIndex = 0; await Task.Delay(100);
                    window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Текст").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(30);
                    window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Добавить текст").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); await Task.Delay(30);
                    window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Arial").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                    var picker = window.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "FontCatalogPicker");
                    var search = picker.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == "Поиск шрифта");
                    search.Text = "Roboto"; await Task.Delay(100);
                    var list = picker.GetVisualDescendants().OfType<ListBox>().Single();
                    var choice = list.Items.Cast<object>().First(item => (string)item.GetType().GetProperty("Id")!.GetValue(item)! == "roboto");
                    var row = list.GetVisualDescendants().OfType<ListBoxItem>().First(item => ReferenceEquals(item.DataContext, choice));
                    var star = row.GetVisualDescendants().OfType<Button>().Single();
                    var starPoint = star.TranslatePoint(new Point(star.Bounds.Width / 2, star.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(starPoint, MouseButton.Left, RawInputModifiers.None); window.MouseUp(starPoint, MouseButton.Left, RawInputModifiers.None); await Task.Delay(50);
                    Assert.Contains("roboto", fontPrefs.Load().Favorites);
                    list.SelectedItem = choice; await Task.Delay(100);
                    var project = (StickerForge.Core.StickerProject)typeof(MainWindow).GetField("project", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var layer = Assert.Single(project.Stickers[0].TextLayers); Assert.StartsWith("roboto/", layer.FontAsset); Assert.Contains("roboto", fontPrefs.Load().Recent);
                    var overlay = (Control)typeof(MainWindow).GetField("textOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var point = overlay.TranslatePoint(new Point(layer.X, layer.Y), window)!.Value;
                    window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                    window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); window.MouseUp(point, MouseButton.Left, RawInputModifiers.None); await Task.Delay(100);
                    var inline = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Classes.Contains("inline-text"));
                    Assert.True(inline.IsVisible); Assert.Contains("StickerForgeInline", inline.FontFamily.ToString());
                }
                finally { typeof(MainWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false); window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task TextEditorCanRenderInBothThemes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-text-render-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(async () =>
            {
                var path = Path.Combine(directory, "sample.png");
                using (var image = new SKBitmap(512, 400))
                {
                    using var canvas = new SKCanvas(image); canvas.Clear(new SKColor(34, 85, 131));
                    using var paint = new SKPaint { Color = new SKColor(41, 157, 200), IsAntialias = true };
                    canvas.DrawCircle(256, 200, 150, paint);
                    using var frame = SKImage.FromBitmap(image); using var png = frame.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(path, png.ToArray());
                }
                foreach (var theme in new[] { "dark", "light" })
                {
                    var preferences = new UiPreferencesStore(Path.Combine(directory, theme + ".json")); preferences.Save(new UiPreferences("ru", theme));
                    var window = new MainWindow(new PanelLayoutStore(Path.Combine(directory, theme + "-panels.json")), preferences) { Width = 1280, Height = 800 }; window.Show();
                    try
                    {
                        await (Task)typeof(MainWindow).GetMethod("ImportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { path }])!;
                        var gallery = (ListBox)typeof(MainWindow).GetField("gallery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!; gallery.SelectedIndex = 0; await Task.Delay(100);
                        var tab = window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Текст"); tab.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(30);
                        var add = window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Добавить текст"); add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                        using var screenshot = window.CaptureRenderedFrame(); Assert.NotNull(screenshot);
                        var output = Environment.GetEnvironmentVariable("STICKERFORGE_SCREENSHOT_DIR") ?? AppContext.BaseDirectory;
                        Directory.CreateDirectory(output); screenshot.Save(Path.Combine(output, "text-editor-" + theme + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); await Task.Delay(30);
                        var fontButton = window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Arial");
                        fontButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(200);
                        using var catalogImage = window.CaptureRenderedFrame(); Assert.NotNull(catalogImage);
                        catalogImage.Save(Path.Combine(output, "font-catalog-" + theme + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
                    finally { typeof(MainWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false); window.Close(); }
                }
                return true;
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task TextPanelAndCanvasManipulateTheSelectedSticker()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-text-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(async () =>
            {
                var path = Path.Combine(directory, "a.png");
                using (var image = new SKBitmap(32, 24)) using (var frame = SKImage.FromBitmap(image)) using (var png = frame.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(path, png.ToArray());
                var window = new MainWindow(new PanelLayoutStore(Path.Combine(directory, "layout.json")), new UiPreferencesStore(Path.Combine(directory, "ui.json"))) { Width = 1280, Height = 800 };
                window.Show();
                try
                {
                    await (Task)typeof(MainWindow).GetMethod("ImportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { path }])!;
                    var gallery = (ListBox)typeof(MainWindow).GetField("gallery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    gallery.SelectedIndex = 0; await Task.Delay(100);
                    var tab = window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Текст");
                    tab.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
                    var add = window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Добавить текст");
                    add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
                    var project = (StickerForge.Core.StickerProject)typeof(MainWindow).GetField("project", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var layer = Assert.Single(project.Stickers[0].TextLayers);
                    var inline = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Classes.Contains("inline-text"));
                    Assert.True(inline.IsVisible); Assert.Equal(layer.Text, inline.Text); Assert.Equal(inline.Text!.Length, Math.Abs(inline.SelectionEnd - inline.SelectionStart));
                    inline.Text = "Строка 1\nLine 2"; await Task.Delay(30); Assert.Equal("Строка 1\nLine 2", layer.Text);
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); await Task.Delay(30); Assert.False(inline.IsVisible);
                    var overlay = (Control)typeof(MainWindow).GetField("textOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var from = overlay.TranslatePoint(new Point(layer.X, layer.Y), window)!.Value;
                    var to = overlay.TranslatePoint(new Point(layer.X + 30, layer.Y + 20), window)!.Value;
                    window.MouseDown(from, MouseButton.Left, RawInputModifiers.None); window.MouseMove(to, RawInputModifiers.LeftMouseButton); window.MouseUp(to, MouseButton.Left, RawInputModifiers.None); await Task.Delay(50);
                    Assert.InRange(layer.X, 285, 287); Assert.InRange(layer.Y, 275, 277);
                    Assert.True((bool)typeof(MainWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!);
                    var panel = (Control)typeof(MainWindow).GetField("textEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var xRow = panel.GetVisualDescendants().OfType<Grid>().Single(row => row.Children.OfType<TextBlock>().Any(label => label.Text == "Положение X"));
                    var xInput = xRow.Children.OfType<NumericUpDown>().Single();
                    Assert.InRange(xInput.Value!.Value, 285, 287);
                    xInput.Value = 100; Assert.Equal(100, layer.X);
                    var boundsMethod = overlay.GetType().GetMethod("LocalBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
                    var bounds = (Rect)boundsMethod.Invoke(null, [layer])!;
                    from = overlay.TranslatePoint(new Point(layer.X + bounds.Right, layer.Y + bounds.Bottom), window)!.Value;
                    to = overlay.TranslatePoint(new Point(layer.X + bounds.Right * 1.4, layer.Y + bounds.Bottom * 1.4), window)!.Value;
                    window.MouseDown(from, MouseButton.Left, RawInputModifiers.None); window.MouseMove(to, RawInputModifiers.LeftMouseButton); window.MouseUp(to, MouseButton.Left, RawInputModifiers.None); await Task.Delay(30);
                    Assert.InRange(layer.Scale, 1.3, 1.5);
                    bounds = (Rect)boundsMethod.Invoke(null, [layer])!;
                    from = overlay.TranslatePoint(new Point(layer.X + bounds.Center.X, layer.Y + bounds.Top - 18), window)!.Value;
                    to = overlay.TranslatePoint(new Point(layer.X + 60, layer.Y), window)!.Value;
                    window.MouseDown(from, MouseButton.Left, RawInputModifiers.None); window.MouseMove(to, RawInputModifiers.LeftMouseButton); window.MouseUp(to, MouseButton.Left, RawInputModifiers.None); await Task.Delay(30);
                    Assert.InRange(layer.Rotation, 85, 95);
                    var content = window.GetVisualDescendants().OfType<TextBox>().First(box => box.AcceptsReturn && box.IsVisible && box.Text == "Строка 1\nLine 2");
                    content.Text = "Привет\nWorld"; await Task.Delay(50); Assert.Equal("Привет\nWorld", layer.Text);
                    var duplicate = window.GetVisualDescendants().OfType<Button>().First(button => (button.Content as string) == "Дублировать");
                    duplicate.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
                    Assert.Equal(2, project.Stickers[0].TextLayers.Count);
                    var copy = project.Stickers[0].TextLayers[1]; Assert.NotEqual(layer.Id, copy.Id); Assert.Equal(layer.Text, copy.Text); Assert.Equal(layer.X + 12, copy.X);
                }
                finally { typeof(MainWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false); window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }

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
                    var emptyCanvas = (TextBlock)typeof(MainWindow).GetField("emptyCanvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var emptyProperties = (TextBlock)typeof(MainWindow).GetField("emptyProperties", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var editing = (StackPanel)typeof(MainWindow).GetField("editingProperties", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var export = (Button)typeof(MainWindow).GetField("exportButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    Assert.Equal(14, window.FontSize); Assert.True(window.RenderScaling > 0);
                    Assert.Equal(14, window.GetVisualDescendants().OfType<MenuItem>().First().FontSize);
                    Assert.True(emptyCanvas.IsVisible); Assert.True(emptyProperties.IsVisible); Assert.False(editing.IsVisible); Assert.False(export.IsEnabled);
                    void CheckCanvasFits()
                    {
                        var child = (Control)viewbox.Child!;
                        var first = child.TranslatePoint(new Point(0, 0), canvas)!.Value;
                        var last = child.TranslatePoint(new Point(512, 512), canvas)!.Value;
                        Assert.InRange(first.X, -1, canvas.Bounds.Width); Assert.InRange(first.Y, -1, canvas.Bounds.Height);
                        Assert.InRange(last.X, 0, canvas.Bounds.Width + 1); Assert.InRange(last.Y, 0, canvas.Bounds.Height + 1);
                    }
                    window.Width = 820; window.Height = 520; await Task.Delay(100);
                    Assert.True(grid.ColumnDefinitions[2].ActualWidth >= 400);
                    Assert.Equal(0, grid.ColumnDefinitions[0].ActualWidth);
                    Assert.True(grid.Bounds.Width <= 820);
                    window.Width = 1280; window.Height = 800; await Task.Delay(100);
                    Assert.True(grid.ColumnDefinitions[2].ActualWidth >= 400); Assert.True(grid.ColumnDefinitions[0].ActualWidth >= 180);
                    window.Width = 1920; window.Height = 1080; await Task.Delay(100);
                    Assert.True(grid.ColumnDefinitions[2].ActualWidth > 600);
                    var imagePath = Path.Combine(directory, "sample.png");
                    using (var bitmap = new SKBitmap(32, 24)) using (var image = SKImage.FromBitmap(bitmap)) using (var png = image.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(imagePath, png.ToArray());
                    await (Task)typeof(MainWindow).GetMethod("ImportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { imagePath }])!;
                    var gallery = (ListBox)typeof(MainWindow).GetField("gallery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    gallery.SelectedIndex = 0; await Task.Delay(100);
                    Assert.False(emptyCanvas.IsVisible); Assert.False(emptyProperties.IsVisible); Assert.True(editing.IsVisible); Assert.True(export.IsEnabled);
                    var inputs = editing.GetVisualDescendants().OfType<TextBox>().ToArray();
                    Assert.Equal(4, inputs.Length);
                    foreach (var input in inputs)
                    { Assert.InRange(input.Bounds.Width, 67, 69); Assert.InRange(input.Bounds.Height, 33, 35); Assert.Equal(14, input.FontSize); }
                    var project = (StickerForge.Core.StickerProject)typeof(MainWindow).GetField("project", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var transformBefore = JsonSerializer.Serialize(project.Stickers[0].Transform);
                    typeof(MainWindow).GetMethod("ActualPreviewSize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); await Task.Delay(100); CheckCanvasFits();
                    typeof(MainWindow).GetMethod("FitPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    Assert.Equal(transformBefore, JsonSerializer.Serialize(project.Stickers[0].Transform));
                    foreach (var (width, height) in new[] { (820, 520), (1280, 800), (1920, 1080) })
                    { window.Width = width; window.Height = height; await Task.Delay(100); Assert.True(grid.ColumnDefinitions[2].ActualWidth >= 400); CheckCanvasFits(); }
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
    public async Task SwitchingStickerLoadsItsOwnSettingsEvenWithFocusedInput()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(async () =>
            {
                var paths = new[] { Path.Combine(directory, "a.png"), Path.Combine(directory, "b.png") };
                using (var bitmap = new SKBitmap(32, 24)) using (var image = SKImage.FromBitmap(bitmap)) using (var png = image.Encode(SKEncodedImageFormat.Png, 100))
                    foreach (var path in paths) File.WriteAllBytes(path, png.ToArray());
                var window = new MainWindow(new PanelLayoutStore(Path.Combine(directory, "layout.json"))); window.Show();
                try
                {
                    await (Task)typeof(MainWindow).GetMethod("ImportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [paths])!;
                    var project = (StickerForge.Core.StickerProject)typeof(MainWindow).GetField("project", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    project.Stickers[0].Transform.Scale = 1.5; project.Stickers[0].Transform.Rotation = 45;
                    project.Stickers[1].Transform.Scale = .7; project.Stickers[1].Transform.Rotation = -90;
                    var gallery = (ListBox)typeof(MainWindow).GetField("gallery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    gallery.SelectedIndex = 0; await Task.Delay(100);
                    var editing = (StackPanel)typeof(MainWindow).GetField("editingProperties", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var input = editing.GetVisualDescendants().OfType<TextBox>().First();
                    Assert.Equal("150", input.Text); input.Focus();
                    gallery.SelectedIndex = 1; await Task.Delay(100);
                    Assert.Equal("70", input.Text);
                    Assert.Equal(1.5, project.Stickers[0].Transform.Scale); Assert.Equal(45, project.Stickers[0].Transform.Rotation);
                    Assert.Equal(.7, project.Stickers[1].Transform.Scale); Assert.Equal(-90, project.Stickers[1].Transform.Rotation);
                }
                finally { typeof(MainWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false); window.Close(); }
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

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using SkiaSharp;
using StickerForge.Core;
using StickerForge.Desktop;
using StickerForge.Media;
using StickerForge.Rendering;

namespace StickerForge.Tests;

public static class HeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HeadlessApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class HeadlessApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public sealed class WorkspaceNavigationTests
{
    private static object? Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static async Task Choose(Window owner, string text)
    {
        Window? dialog = null;
        for (int i = 0; i < 100 && dialog is null; i++)
        {
            dialog = owner.OwnedWindows.FirstOrDefault(w => w.IsVisible);
            if (dialog is null) await Task.Delay(10);
        }
        Assert.NotNull(dialog);
        var button = dialog.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, text));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [Fact]
    public async Task GalleryToEditorAndBackUsesSameWindowAndCloses()
    {
        using var testSession = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppBuilder));
        await testSession.Dispatch(RunScenario, CancellationToken.None);
    }

    private static async Task RunScenario()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sf-ui-{Guid.NewGuid():N}.png");
        using (var bitmap = new SKBitmap(64, 64))
        using (var image = SKImage.FromBitmap(bitmap))
        using (var bytes = image.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(path, bytes.ToArray());
        var window = new MainWindow(); window.Show();
        try
        {
            var galleryView = window.Content;
            await (Task)Invoke(window, "ImportAsync", new[] { path })!;
            var gallery = Field<ListBox>(window, "gallery"); Assert.Single(gallery.Items);
            gallery.SelectedIndex = 0;
            await (Task)Invoke(window, "EditSelectedAsync")!;
            Assert.NotSame(galleryView, window.Content);
            Assert.Equal("EditorWorkspace", window.Content!.GetType().Name);
            Assert.Same(window, TopLevel.GetTopLevel((Control)window.Content));
            var state = (WorkspaceEditSession)window.Content.GetType().GetProperty("State")!.GetValue(window.Content)!;
            var panel = (Control)Field<object>(window.Content, "options");
            var hex = panel.GetLogicalDescendants().OfType<TextBox>().Single();
            hex.Text = "#1278EF"; await Task.Delay(20);
            Assert.Equal(0x12, state.Graphics.BrushColor.R); Assert.Equal(0x78, state.Graphics.BrushColor.G); Assert.Equal(0xEF, state.Graphics.BrushColor.B);
            hex.Text = "#12ZZEF"; await Task.Delay(20); Assert.Equal(0x78, state.Graphics.BrushColor.G);
            var sliders = panel.GetLogicalDescendants().OfType<Slider>().ToArray();
            sliders[0].Value = 42; sliders[1].Value = 50; sliders[2].Value = 30;
            Assert.Equal(42, state.Graphics.BrushOptions.Size);
            Assert.Equal(.5f, state.Graphics.BrushOptions.Opacity); Assert.Equal(.3f, state.Graphics.BrushOptions.Hardness);
            var canvas = (Control)Field<object>(window.Content, "canvas"); canvas.Focus();
            window.KeyPress(Key.E, RawInputModifiers.None, PhysicalKey.E, "e"); Assert.True(state.Graphics.Eraser);
            panel.GetLogicalDescendants().OfType<Slider>().First().Value = 17;
            Assert.Equal(17, state.Graphics.EraserOptions.Size); Assert.Equal(42, state.Graphics.BrushOptions.Size);
            window.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.B, "b"); Assert.False(state.Graphics.Eraser);
            Assert.Equal(.5f, state.Graphics.BrushOptions.Opacity); Assert.Equal(.3f, state.Graphics.BrushOptions.Hardness);
            hex = panel.GetLogicalDescendants().OfType<TextBox>().Single(); hex.Focus();
            window.KeyPress(Key.E, RawInputModifiers.None, PhysicalKey.E, "e"); Assert.False(state.Graphics.Eraser);
            canvas.Focus();
            state.Graphics.Begin(new(15, 15)); state.Graphics.End(); state.Changed(); state.Apply();
            var project = Field<StickerProject>(window, "project"); var item = Assert.Single(project.Stickers);
            var cache = Field<Dictionary<Guid, ImageInfo>>(window, "cache");
            using (var thumb = SKBitmap.Decode(cache[item.Id].Thumbnail)) Assert.True(thumb.GetPixel(15, 15).Alpha > 0);
            state.Graphics.Begin(new(48, 48)); state.Graphics.End(); state.Changed();
            window.Close(); await Choose(window, "Continue editing"); await Task.Delay(20);
            Assert.True(window.IsVisible); Assert.Same(state, window.Content!.GetType().GetProperty("State")!.GetValue(window.Content));
            var cancelled = (Task<bool>)Invoke(window, "LeaveEditorAsync")!;
            await Choose(window, "Continue editing"); Assert.False(await cancelled.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(state, window.Content!.GetType().GetProperty("State")!.GetValue(window.Content));
            var leaving = (Task<bool>)Invoke(window, "LeaveEditorAsync")!;
            await Choose(window, "Discard changes"); Assert.True(await leaving.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(galleryView, window.Content);
            using (var reopened = GraphicsSession.Open(path, item.EditableDocument))
            using (var png = SKBitmap.Decode(reopened.RenderPng())) { Assert.True(png.GetPixel(15, 15).Alpha > 0); Assert.Equal(0, png.GetPixel(48, 48).Alpha); }
            Assert.True(state.IsDisposed);
            window.Close(); await Choose(window, "Cancel"); await Task.Delay(20); Assert.True(window.IsVisible);
            window.Close(); await Choose(window, "Discard"); await Task.Delay(20); Assert.False(window.IsVisible);
        }
        finally { if (window.IsVisible) { window.GetType().GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false); window.Close(); } File.Delete(path); }
    }
}

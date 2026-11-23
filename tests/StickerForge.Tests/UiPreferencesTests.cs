using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia;
using Avalonia.VisualTree;
using StickerForge.Desktop;
using StickerForge.Infrastructure;

namespace StickerForge.Tests;

[Collection("Avalonia Headless")]
public sealed class UiPreferencesTests
{
    [Fact]
    public void PreferencesRoundTripAndRejectInvalidValues()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "preferences.json"); var store = new UiPreferencesStore(path);
            Assert.Equal(new UiPreferences(), store.Load());
            store.Save(new UiPreferences("en", "light")); Assert.Equal(new UiPreferences("en", "light"), store.Load());
            store.Save(new UiPreferences("ru", "system")); Assert.Equal(new UiPreferences("ru", "system"), store.Load());
            File.WriteAllText(path, "{\"Language\":\"invalid\",\"Theme\":\"invalid\"}"); Assert.Equal(new UiPreferences(), store.Load());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task EmptyProjectAndPreferencesApplyToWindow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-ui-window-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var settings = new UiPreferencesStore(Path.Combine(directory, "preferences.json")); settings.Save(new UiPreferences("en", "light"));
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(() =>
            {
                var window = new MainWindow(new PanelLayoutStore(Path.Combine(directory, "panels.json")), settings); window.Show();
                try
                {
                    Assert.Equal(ThemeVariant.Light, window.RequestedThemeVariant);
                    var canvas = (TextBlock)typeof(MainWindow).GetField("emptyCanvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var export = (Button)typeof(MainWindow).GetField("exportButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var left = (Control)typeof(MainWindow).GetField("galleryPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var right = (Control)typeof(MainWindow).GetField("propertiesSide", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    Assert.Contains("Drag", canvas.Text); Assert.Contains("Export", (string)export.Content!); Assert.False(export.IsEnabled);
                    Assert.False(left.IsVisible); Assert.False(right.IsVisible);
                    var untranslated = window.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible && (x.Text ?? "").Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё')).Select(x => x.Text).ToArray();
                    Assert.Empty(untranslated);
                    typeof(MainWindow).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, new UiPreferences("ru", "dark"));
                    typeof(MainWindow).GetMethod("ApplyPreferences", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant); Assert.Contains("Перетащите", canvas.Text); Assert.Contains("Экспортировать", (string)export.Content!);
                    typeof(MainWindow).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, new UiPreferences("ru", "system"));
                    typeof(MainWindow).GetMethod("ApplyPreferences", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    Assert.Equal(ThemeVariant.Default, window.RequestedThemeVariant);
                }
                finally { window.Close(); }
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task WindowResizesWithoutOverlappingWorkspace()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-ui-size-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(() =>
            {
                foreach (var (width, height) in new[] { (900, 700), (1280, 720), (1920, 1080), (2560, 1440), (3440, 1440) })
                {
                    var settings = new UiPreferencesStore(Path.Combine(directory, "ui.json")); settings.Save(new UiPreferences("en", "dark"));
                    var window = new MainWindow(new PanelLayoutStore(Path.Combine(directory, "panels.json")), settings) { Width = width, Height = height };
                    try
                    {
                        var project = (StickerForge.Core.StickerProject)typeof(MainWindow).GetField("project", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        project.Stickers.Add(new StickerForge.Core.StickerItem { SourcePath = Path.Combine(directory, "missing.png") });
                        typeof(MainWindow).GetMethod("RefreshGallery", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                        window.Show(); window.UpdateLayout();
                        var grid = (Grid)typeof(MainWindow).GetField("workspaceGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        Assert.True(grid.ColumnDefinitions[2].ActualWidth >= 400, $"{width}: central width {grid.ColumnDefinitions[2].ActualWidth}");
                        Assert.True(grid.Bounds.Width <= width, $"{width}: workspace width {grid.Bounds.Width}");
                        var untranslated = window.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible && (x.Text ?? "").Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё')).Select(x => x.Text).ToArray();
                        Assert.Empty(untranslated);
                    }
                    finally { window.Close(); }
                }
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }
}

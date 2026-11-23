using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using StickerForge.Desktop;
using StickerForge.Infrastructure;

namespace StickerForge.Tests;

[Collection("Avalonia Headless")]
public sealed class UiPreferencesTests
{
    [Fact]
    public async Task ToolbarAndSettingsTogglePreserveProjectInBothLanguagesAndCompactWindows()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-toolbar-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(() =>
            {
                foreach (var language in new[] { "ru", "en" })
                foreach (var populated in new[] { false, true })
                foreach (var width in new[] { 700, 1280, 2560 })
                {
                    var preferences = new UiPreferencesStore(Path.Combine(directory, $"{language}-{populated}-{width}.json"));
                    preferences.Save(new UiPreferences(language, "dark"));
                    var window = new MainWindow(new PanelLayoutStore(Path.Combine(directory, "panels.json")), preferences) { Width = width, Height = 720 };
                    try
                    {
                        var project = (StickerForge.Core.StickerProject)typeof(MainWindow).GetField("project", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        if (populated) project.Stickers.Add(new StickerForge.Core.StickerItem { SourcePath = Path.Combine(directory, "missing.png"), Transform = new StickerForge.Core.TransformSettings { Rotation = 37, FlipHorizontal = true } });
                        typeof(MainWindow).GetMethod("RefreshGallery", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                        window.Show(); window.UpdateLayout();
                        var gallery = (ListBox)typeof(MainWindow).GetField("gallery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        if (populated) gallery.SelectedIndex = 0;
                        typeof(MainWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                        var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
                        var add = buttons.Single(x => x.Name == "AddFilesAction");
                        var open = buttons.Single(x => x.Name == "OpenProjectAction");
                        var save = buttons.Single(x => x.Name == "SaveProjectAction");
                        var more = buttons.Single(x => x.Name == "MoreActions");
                        var settings = buttons.Single(x => x.Name == "SettingsAction");
                        Assert.Single(buttons, x => ToolTip.GetTip(x) is string tip && tip == (language == "en" ? "Settings" : "Настройки"));
                        Assert.Equal(language == "en" ? "Add" : "Добавить", ((StackPanel)add.Content!).Children.OfType<TextBlock>().Single().Text);
                        Assert.Equal(language == "en" ? "Open" : "Открыть", ((StackPanel)open.Content!).Children.OfType<TextBlock>().Single().Text);
                        Assert.Equal(language == "en" ? "Save" : "Сохранить", ((StackPanel)save.Content!).Children.OfType<TextBlock>().Single().Text);
                        foreach (var button in new[] { add, open, save, more, settings })
                        {
                            var position = button.TranslatePoint(new Point(0, 0), window)!.Value;
                            Assert.True(position.X >= 0 && position.X + button.Bounds.Width <= width, $"{width}: {button.Name} is clipped");
                        }
                        var menu = Assert.IsType<ContextMenu>(more.ContextMenu);
                        var items = menu.Items.OfType<MenuItem>().ToArray();
                        Assert.All(items, item => Assert.Empty(item.Items));
                        Assert.Contains(items, x => (string?)x.Header == (language == "en" ? "New project" : "Новый проект"));
                        Assert.Contains(items, x => (string?)x.Header == (language == "en" ? "Save as…" : "Сохранить как…"));
                        Assert.Contains(items, x => (string?)x.Header == (language == "en" ? "About" : "О программе"));
                        var editor = (Control)typeof(MainWindow).GetField("editorPage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        var page = (Control)typeof(MainWindow).GetField("settingsPage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        var editorPosition = editor.TranslatePoint(new Point(0, 0), window)!.Value;
                        Assert.InRange(editorPosition.X, 7, 9);
                        Assert.InRange(editor.Bounds.Width, width - 17, width - 15);
                        Assert.DoesNotContain(buttons, x => ToolTip.GetTip(x) is string tip && (tip == "Стикеры" || tip == "Stickers"));
                        if (populated && width >= 1280)
                        {
                            var galleryPanel = (Control)typeof(MainWindow).GetField("galleryPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                            var galleryPosition = galleryPanel.TranslatePoint(new Point(0, 0), window)!.Value;
                            Assert.True(galleryPanel.IsVisible);
                            Assert.InRange(galleryPosition.X, 11, 13);
                        }
                        var selected = gallery.SelectedItem;
                        var inactive = settings.Background;
                        settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.True(page.IsVisible); Assert.False(editor.IsVisible); Assert.NotSame(inactive, settings.Background);
                        window.UpdateLayout();
                        var combos = page.GetVisualDescendants().OfType<ComboBox>().ToArray();
                        combos[0].SelectedIndex = language == "en" ? 0 : 1;
                        combos[1].SelectedIndex = 1;
                        Assert.Equal(language == "en" ? "ru" : "en", preferences.Load().Language);
                        Assert.Equal("light", preferences.Load().Theme);
                        Assert.Equal(language == "en" ? "Добавить" : "Add", ((StackPanel)add.Content!).Children.OfType<TextBlock>().Single().Text);
                        settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.False(page.IsVisible); Assert.True(editor.IsVisible); Assert.Same(inactive, settings.Background);
                        Assert.Same(selected, gallery.SelectedItem); Assert.Equal(populated ? 1 : 0, project.Stickers.Count);
                        if (populated) { Assert.Equal(37, project.Stickers[0].Transform.Rotation); Assert.True(project.Stickers[0].Transform.FlipHorizontal); }
                        Assert.True((bool)typeof(MainWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!);
                    }
                    finally { typeof(MainWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false); window.Close(); }
                }
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }
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

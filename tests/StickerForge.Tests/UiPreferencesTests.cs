using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using StickerForge.Desktop;
using StickerForge.Infrastructure;

namespace StickerForge.Tests;

[Collection("Avalonia Headless")]
public sealed class UiPreferencesTests
{
    [Fact]
    public async Task EditingSlidersUseThemeColorsAndRemainReadableWhenDisabled()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-slider-theme-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(() =>
            {
                var window = new MainWindow(new PanelLayoutStore(Path.Combine(directory, "panels.json")), new UiPreferencesStore(Path.Combine(directory, "ui.json"))) { Width = 1280, Height = 800 };
                try
                {
                    var project = (StickerForge.Core.StickerProject)typeof(MainWindow).GetField("project", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    project.Stickers.Add(new StickerForge.Core.StickerItem { SourcePath = Path.Combine(directory, "missing.png") });
                    typeof(MainWindow).GetMethod("RefreshGallery", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    window.Show(); var gallery = (ListBox)typeof(MainWindow).GetField("gallery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!; gallery.SelectedIndex = 0; window.UpdateLayout();
                    var slider = (Slider)typeof(MainWindow).GetField("scale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    foreach (var theme in new[] { "dark", "light", "system" })
                    {
                        typeof(MainWindow).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, new UiPreferences("ru", theme));
                        typeof(MainWindow).GetMethod("ApplyPreferences", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                        window.MouseMove(new Point(1, 1), RawInputModifiers.None); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        var light = theme == "light" || (theme == "system" && window.ActualThemeVariant == ThemeVariant.Light);
                        var track = slider.GetVisualDescendants().OfType<Border>().Where(x => x.Name == "TrackBackground").ToArray();
                        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single(x => x.Name == "thumb");
                        var thumbBorder = thumb.GetVisualDescendants().OfType<Border>().First();
                        Assert.Equal(Color.Parse(light ? "#006DCC" : "#078BFA"), Assert.IsAssignableFrom<ISolidColorBrush>(track[0].Background).Color);
                        Assert.Equal(Color.Parse(light ? "#AFC6D8" : "#365571"), Assert.IsAssignableFrom<ISolidColorBrush>(track[1].Background).Color);
                        Assert.Equal(Color.Parse(light ? "#006DCC" : "#EAF6FF"), Assert.IsAssignableFrom<ISolidColorBrush>(thumbBorder.Background).Color);
                        slider.Focus(NavigationMethod.Tab); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        Assert.Equal(new Thickness(2), thumbBorder.BorderThickness);
                        var origin = thumb.TranslatePoint(new Point(0, 0), window)!.Value; var point = new Point(origin.X + thumb.Bounds.Width / 2, origin.Y + thumb.Bounds.Height / 2);
                        window.MouseMove(point, RawInputModifiers.None); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        Assert.Equal(Color.Parse(light ? "#0089EB" : "#A7DCFF"), Assert.IsAssignableFrom<ISolidColorBrush>(thumbBorder.Background).Color);
                        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        Assert.Equal(Color.Parse(light ? "#00549B" : "#6ABEFF"), Assert.IsAssignableFrom<ISolidColorBrush>(thumbBorder.Background).Color);
                        window.MouseUp(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                        slider.IsEnabled = false; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        Assert.Equal(Color.Parse(light ? "#BBC6CE" : "#526677"), Assert.IsAssignableFrom<ISolidColorBrush>(thumbBorder.Background).Color);
                        Assert.All(track, x => Assert.Equal(Color.Parse(light ? "#BBC6CE" : "#526677"), Assert.IsAssignableFrom<ISolidColorBrush>(x.Background).Color));
                        slider.IsEnabled = true;
                    }
                }
                finally { typeof(MainWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false); window.Close(); }
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task ResizedPanelsSurviveSelectionCollapseResizeAndRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-panel-width-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(() =>
            {
                var panelPath = Path.Combine(directory, "panels.json");
                var window = new MainWindow(new PanelLayoutStore(panelPath), new UiPreferencesStore(Path.Combine(directory, "ui.json"))) { Width = 1800, Height = 900 };
                void AddItems(MainWindow w)
                {
                    var project = (StickerForge.Core.StickerProject)typeof(MainWindow).GetField("project", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
                    project.Stickers.Add(new StickerForge.Core.StickerItem { SourcePath = Path.Combine(directory, "one.png") });
                    project.Stickers.Add(new StickerForge.Core.StickerItem { SourcePath = Path.Combine(directory, "two.png") });
                    typeof(MainWindow).GetMethod("RefreshGallery", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, null);
                }
                Grid GridOf(MainWindow w) => (Grid)typeof(MainWindow).GetField("workspaceGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
                void DragSplitter(string field, double delta)
                {
                    var splitter = (GridSplitter)typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var origin = splitter.TranslatePoint(new Point(0, 0), window)!.Value;
                    var point = new Point(origin.X + splitter.Bounds.Width / 2, origin.Y + 30);
                    window.MouseMove(point, RawInputModifiers.None); window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                    window.MouseMove(new Point(point.X + delta, point.Y), RawInputModifiers.LeftMouseButton);
                    window.MouseUp(new Point(point.X + delta, point.Y), MouseButton.Left, RawInputModifiers.LeftMouseButton);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                }
                try
                {
                    AddItems(window); window.Show(); window.UpdateLayout();
                    DragSplitter("gallerySplitter", 110); DragSplitter("propertiesSplitter", -100);
                    var grid = GridOf(window); var galleryWidth = grid.ColumnDefinitions[0].ActualWidth; var propertiesWidth = grid.ColumnDefinitions[4].ActualWidth;
                    Assert.InRange(galleryWidth, 370, 390); Assert.InRange(propertiesWidth, 390, 410);
                    var gallery = (ListBox)typeof(MainWindow).GetField("gallery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    gallery.SelectedIndex = 0; gallery.SelectedIndex = 1; window.UpdateLayout();
                    Assert.InRange(grid.ColumnDefinitions[0].ActualWidth, galleryWidth - 1, galleryWidth + 1);
                    Assert.InRange(grid.ColumnDefinitions[4].ActualWidth, propertiesWidth - 1, propertiesWidth + 1);
                    var left = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "GalleryVisibility");
                    var right = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "PropertiesVisibility");
                    void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Click(left); Click(right); Click(left); Click(right); window.UpdateLayout();
                    Assert.InRange(grid.ColumnDefinitions[0].ActualWidth, galleryWidth - 1, galleryWidth + 1);
                    Assert.InRange(grid.ColumnDefinitions[4].ActualWidth, propertiesWidth - 1, propertiesWidth + 1);
                    window.Width = 960; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    Assert.True(grid.ColumnDefinitions[0].ActualWidth < galleryWidth);
                    window.Width = 1800; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    Assert.InRange(grid.ColumnDefinitions[0].ActualWidth, galleryWidth - 1, galleryWidth + 1);
                    Assert.InRange(grid.ColumnDefinitions[4].ActualWidth, propertiesWidth - 1, propertiesWidth + 1);
                    window.Close();
                    var saved = new PanelLayoutStore(panelPath).Load();
                    Assert.InRange(saved.GalleryWidth, galleryWidth - 1, galleryWidth + 1); Assert.InRange(saved.PropertiesWidth, propertiesWidth - 1, propertiesWidth + 1);
                    var reopened = new MainWindow(new PanelLayoutStore(panelPath), new UiPreferencesStore(Path.Combine(directory, "ui.json"))) { Width = 1800, Height = 900 };
                    try { AddItems(reopened); reopened.Show(); reopened.UpdateLayout(); var restored = GridOf(reopened); Assert.InRange(restored.ColumnDefinitions[0].ActualWidth, galleryWidth - 1, galleryWidth + 1); Assert.InRange(restored.ColumnDefinitions[4].ActualWidth, propertiesWidth - 1, propertiesWidth + 1); }
                    finally { reopened.Close(); }
                }
                finally { if (window.IsVisible) window.Close(); }
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task PanelButtonsUseThemeColorsAndDistinctPointerStates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-panel-theme-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(() =>
            {
                var settings = new UiPreferencesStore(Path.Combine(directory, "ui.json"));
                var window = new MainWindow(new PanelLayoutStore(Path.Combine(directory, "panels.json")), settings) { Width = 1280, Height = 720 };
                try
                {
                    var project = (StickerForge.Core.StickerProject)typeof(MainWindow).GetField("project", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    project.Stickers.Add(new StickerForge.Core.StickerItem { SourcePath = Path.Combine(directory, "sample.png") });
                    typeof(MainWindow).GetMethod("RefreshGallery", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    window.Show(); window.UpdateLayout();
                    var button = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "GalleryVisibility");
                    foreach (var theme in new[] { "dark", "light", "system" })
                    {
                        typeof(MainWindow).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, new UiPreferences("ru", theme));
                        typeof(MainWindow).GetMethod("ApplyPreferences", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                        window.MouseMove(new Point(1, 1), RawInputModifiers.None); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        var light = theme == "light" || (theme == "system" && window.ActualThemeVariant == ThemeVariant.Light);
                        var surface = Assert.IsType<SolidColorBrush>(button.Background).Color;
                        var border = Assert.IsType<SolidColorBrush>(button.BorderBrush).Color;
                        var icon = Assert.IsType<Avalonia.Controls.Shapes.Path>(button.Content);
                        Assert.Equal(Color.Parse(light ? "#DCE7F0" : "#304B64"), surface);
                        Assert.Equal(Color.Parse(light ? "#9EB5C7" : "#65829A"), border);
                        Assert.Equal(Color.Parse(light ? "#263946" : "#F5F9FC"), Assert.IsType<SolidColorBrush>(icon.Stroke).Color);
                        Assert.Equal(new Thickness(1), button.BorderThickness);
                        var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single(x => x.Name == "PART_ContentPresenter");
                        var origin = button.TranslatePoint(new Point(0, 0), window)!.Value;
                        var point = new Point(origin.X + 16, origin.Y + 20);
                        window.MouseMove(point, RawInputModifiers.None); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        Assert.Equal(Color.Parse(light ? "#C7D9E8" : "#426782"), Assert.IsType<SolidColorBrush>(presenter.Background).Color);
                        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        Assert.Equal(Color.Parse(light ? "#ACCADF" : "#577F9D"), Assert.IsType<SolidColorBrush>(presenter.Background).Color);
                        window.MouseUp(point, MouseButton.Left, RawInputModifiers.LeftMouseButton); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    }
                }
                finally { window.Close(); }
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task PanelButtonsRemainUsableAcrossStatesAndWindowSizes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sf-panel-buttons-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(WindowLayoutTestAppBuilder));
            await session.Dispatch(() =>
            {
                foreach (var language in new[] { "ru", "en" })
                foreach (var extension in new[] { ".png", ".gif", ".mp4" })
                {
                    var preferences = new UiPreferencesStore(Path.Combine(directory, "ui.json")); preferences.Save(new UiPreferences(language, "dark"));
                    var panelStore = new PanelLayoutStore(Path.Combine(directory, "panels.json")); panelStore.Save(new PanelLayout());
                    var window = new MainWindow(panelStore, preferences) { Width = 1280, Height = 720 };
                    try
                    {
                        var project = (StickerForge.Core.StickerProject)typeof(MainWindow).GetField("project", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        project.Stickers.Add(new StickerForge.Core.StickerItem { SourcePath = Path.Combine(directory, "fixture" + extension) });
                        typeof(MainWindow).GetMethod("RefreshGallery", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                        window.Show(); window.UpdateLayout();
                        var galleryPanel = (Control)typeof(MainWindow).GetField("galleryPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        var properties = (Control)typeof(MainWindow).GetField("propertiesSide", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        var workspace = (Grid)typeof(MainWindow).GetField("workspaceGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        var canvas = (Control)typeof(MainWindow).GetField("previewCanvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        var left = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "GalleryVisibility");
                        var right = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "PropertiesVisibility");
                        void Check(bool galleryVisible, bool propertiesVisible)
                        {
                            window.UpdateLayout();
                            Assert.Equal(galleryVisible, galleryPanel.IsVisible); Assert.Equal(propertiesVisible, properties.IsVisible);
                            Assert.True(left.IsEffectivelyVisible); Assert.True(right.IsEffectivelyVisible);
                            Assert.Equal(32, left.Bounds.Width); Assert.Equal(40, left.Bounds.Height);
                            Assert.Equal(language == "en" ? (galleryVisible ? "Hide gallery" : "Show gallery") : (galleryVisible ? "Свернуть галерею" : "Показать галерею"), ToolTip.GetTip(left));
                            Assert.Equal(language == "en" ? (propertiesVisible ? "Hide properties" : "Show properties") : (propertiesVisible ? "Свернуть свойства" : "Показать свойства"), ToolTip.GetTip(right));
                            foreach (var button in new[] { left, right })
                            {
                                var point = button.TranslatePoint(new Point(0, 0), window)!.Value;
                                Assert.InRange(point.X, 0, window.ClientSize.Width - button.Bounds.Width);
                            }
                            var canvasStart = canvas.TranslatePoint(new Point(0, 0), window)!.Value.X;
                            Assert.True(left.TranslatePoint(new Point(0, 0), window)!.Value.X + left.Bounds.Width <= canvasStart);
                            Assert.True(right.TranslatePoint(new Point(0, 0), window)!.Value.X >= canvasStart + canvas.Bounds.Width);
                        }
                        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(true, true);
                        var initialCanvasWidth = workspace.ColumnDefinitions[2].ActualWidth;
                        Click(left); Check(false, true);
                        Click(left); Click(right); Check(true, false);
                        Click(left); Check(false, false);
                        Assert.True(workspace.ColumnDefinitions[2].ActualWidth > initialCanvasWidth);
                        Click(left); Click(right); Check(true, true);
                        window.Width = 700; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Check(false, true);
                        Click(left); Check(true, false);
                        Click(left); Check(false, false);
                        Click(right); Check(false, true);
                        window.Width = 1280; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Check(false, true);
                        Click(left); Check(true, true);
                        var more = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "MoreActions");
                        var menu = Assert.IsType<ContextMenu>(more.ContextMenu);
                        Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), x => ((string?)x.Header ?? "").Contains("gallery", StringComparison.OrdinalIgnoreCase) || ((string?)x.Header ?? "").Contains("галере", StringComparison.OrdinalIgnoreCase) || ((string?)x.Header ?? "").Contains("properties", StringComparison.OrdinalIgnoreCase) || ((string?)x.Header ?? "").Contains("свойств", StringComparison.OrdinalIgnoreCase));
                    }
                    finally { window.Close(); }
                }
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }
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
                        Assert.DoesNotContain(buttons, x => x.Name == "AddFilesAction");
                        var open = buttons.Single(x => x.Name == "OpenProjectAction");
                        var save = buttons.Single(x => x.Name == "SaveProjectAction");
                        var more = buttons.Single(x => x.Name == "MoreActions");
                        var settings = buttons.Single(x => x.Name == "SettingsAction");
                        Assert.Contains(buttons, x => x.Content is string text && text == (language == "en" ? "＋  Add files" : "＋  Добавить файлы"));
                        Assert.Single(buttons, x => ToolTip.GetTip(x) is string tip && tip == (language == "en" ? "Settings" : "Настройки"));
                        Assert.Equal(language == "en" ? "Open" : "Открыть", ((StackPanel)open.Content!).Children.OfType<TextBlock>().Single().Text);
                        Assert.Equal(language == "en" ? "Save" : "Сохранить", ((StackPanel)save.Content!).Children.OfType<TextBlock>().Single().Text);
                        foreach (var button in new[] { more, open, save, settings })
                        {
                            var position = button.TranslatePoint(new Point(0, 0), window)!.Value;
                            Assert.True(position.X >= 0 && position.X + button.Bounds.Width <= width, $"{width}: {button.Name} is clipped");
                        }
                        var menu = Assert.IsType<ContextMenu>(more.ContextMenu);
                        var items = menu.Items.OfType<MenuItem>().ToArray();
                        Assert.All(items, item => Assert.Empty(item.Items));
                        Assert.Contains(items, x => (string?)x.Header == (language == "en" ? "Add files…" : "Добавить файлы…"));
                        Assert.True(more.TranslatePoint(new Point(0, 0), window)!.Value.X < open.TranslatePoint(new Point(0, 0), window)!.Value.X);
                        Assert.True(open.TranslatePoint(new Point(0, 0), window)!.Value.X < save.TranslatePoint(new Point(0, 0), window)!.Value.X);
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
                        Assert.Contains(items, x => (string?)x.Header == (language == "en" ? "Добавить файлы…" : "Add files…"));
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

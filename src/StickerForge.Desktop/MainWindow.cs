using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using StickerForge.Core;
using StickerForge.Infrastructure;
using StickerForge.Media;
using Avalonia.Controls.Primitives;
using Avalonia.Platform;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls.Templates;
using SkiaSharp;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace StickerForge.Desktop;

public sealed class MainWindow : Window
{
    private readonly ProjectStore store = new();
    private readonly FfmpegService ffmpeg = FfmpegLocator.CreateDefault();
    private readonly ImageImporter importer;
    private readonly ListBox gallery = new() { SelectionMode = SelectionMode.Multiple };
    private readonly TextBox packName = new() { PlaceholderText = "Pack name", Width = 220 };
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock details = new() { Text = "Select an image", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { Text = "Ready" };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, Width = 150, Height = 8 };
    private readonly Dictionary<Guid, ImageInfo> cache = [];
    private StickerProject project = new();
    private string? projectPath;
    private bool dirty;
    private bool refreshing;
    private bool busy;
    private CancellationTokenSource? importing;
    private long previewGeneration;
    private Bitmap? previewBitmap;
    private readonly Button gifPlay = new() { Content = "Play", IsVisible = false };
    private CancellationTokenSource? gifPlayback;
    private string? gifPath;
    private int[]? gifDurations;
    private int gifFrame;
    private readonly Button videoPlay = new() { Content = "Play", IsVisible = false };
    private readonly Slider videoSeek = new() { Minimum = 0, Maximum = 1, IsVisible = false };
    private readonly TextBlock videoTime = new() { IsVisible = false };
    private CancellationTokenSource? videoPlayback;
    private CancellationTokenSource? videoSeekRequest;
    private string? videoPath;
    private VideoMetadata? videoMetadata;
    private bool updatingSeek;
    private readonly Border dropZone = new() { BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent };
    private double previewZoom = 1;
    private readonly TextBlock galleryHint = new() { Text = "Drop files or folders here, or use Import", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 8) };
    private readonly ComboBox fitMode = new() { ItemsSource = new[] { "Вписать целиком", "Заполнить с обрезкой", "Растянуть (искажает пропорции)" }, SelectedIndex = 0 };
    private readonly Slider scale = new() { Minimum = .1, Maximum = 3, Value = 1 };
    private readonly Slider offsetX = new() { Minimum = -512, Maximum = 512 };
    private readonly Slider offsetY = new() { Minimum = -512, Maximum = 512 };
    private readonly Slider rotation = new() { Minimum = -180, Maximum = 180 };
    private readonly NumericUpDown fragmentStart = new() { Minimum = 0, Maximum = 36000, Increment = .1m, FormatString = "0.0" };
    private readonly NumericUpDown fragmentDuration = new() { Minimum = .1m, Maximum = 3, Increment = .1m, Value = 3, FormatString = "0.0" };
    private bool updatingTransform;
    private PointerPressedEventArgs? dragPress;
    private Point dragStart;
    private StickerItem[]? dragItems;
    private ListBoxItem? insertionCard;
    private bool insertAfter;
    private StickerItem[]? pressedSelection;
    private double scrollDirection;
    private readonly DispatcherTimer dragScroll = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Point? previewDragStart;
    private double previewStartX, previewStartY;

    public MainWindow()
    {
        importer = new ImageImporter(new SkiaImageDecoder(), ffmpeg);
        Title = "StickerForge Studio";
        Width = 1280; Height = 760; MinWidth = 820; MinHeight = 520;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Background = new SolidColorBrush(Color.Parse("#111621"));
        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(Button("New", async () => await NewAsync(), "Ctrl+N"));
        top.Children.Add(Button("Open", async () => await OpenAsync(), "Ctrl+O"));
        top.Children.Add(Button("Save", async () => await SaveAsync(false), "Ctrl+S"));
        top.Children.Add(Button("Save as", async () => await SaveAsync(true), "Ctrl+Shift+S"));
        top.Children.Add(packName);
        top.Children.Add(Button("Import files", async () => await PickFilesAsync(), "Ctrl+I"));
        top.Children.Add(Button("Import folder", async () => await PickFolderAsync()));
        var exportButton = Button("Export stickers", ExportAsync);
        exportButton.Background = new SolidColorBrush(Color.Parse("#7658DF")); exportButton.Foreground = Brushes.White;
        top.Children.Add(exportButton);
        top.Children.Add(Button("Cancel operation", () => { importing?.Cancel(); return Task.CompletedTask; }));
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 12, 0, 0) };
        bottom.Children.Add(status); bottom.Children.Add(progress);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,4*,2.5*"), ColumnSpacing = 12 };
        var left = new DockPanel();
        var galleryTitle = new TextBlock { Text = "YOUR STICKERS", FontWeight = FontWeight.Bold, Margin = new Thickness(4, 2, 4, 12) };
        DockPanel.SetDock(galleryTitle, Dock.Top); left.Children.Add(galleryTitle);
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(Button("Remove", RemoveSelected));
        DockPanel.SetDock(actions, Dock.Top); left.Children.Add(actions);
        DockPanel.SetDock(galleryHint, Dock.Top); left.Children.Add(galleryHint);
        gallery.SelectionChanged += async (_, _) => await SelectAsync();
        gallery.AddHandler(InputElement.PointerPressedEvent, OnGalleryPointerPressed, RoutingStrategies.Tunnel, true);
        gallery.AddHandler(InputElement.PointerMovedEvent, StartGalleryDrag, RoutingStrategies.Tunnel, true);
        gallery.AddHandler(InputElement.PointerReleasedEvent, (_, _) => { dragPress = null; pressedSelection = null; }, RoutingStrategies.Tunnel, true);
        dragScroll.Tick += (_, _) =>
        {
            var scroller = gallery.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroller is not null && scrollDirection != 0) scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, scroller.Offset.Y + scrollDirection * 16));
        };
        gallery.ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel());
        ScrollViewer.SetHorizontalScrollBarVisibility(gallery, ScrollBarVisibility.Disabled);
        var menu = new ContextMenu();
        var remove = new MenuItem { Header = "Remove selected" };
        remove.Click += async (_, _) => await RemoveSelected();
        var earlier = new MenuItem { Header = "Move earlier" };
        earlier.Click += async (_, _) => await MoveSelected(-1);
        var later = new MenuItem { Header = "Move later" };
        later.Click += async (_, _) => await MoveSelected(1);
        menu.Items.Add(remove); menu.Items.Add(earlier); menu.Items.Add(later);
        gallery.ContextMenu = menu;
        dropZone.Child = gallery; dropZone.Background = new SolidColorBrush(Color.Parse("#192130")); dropZone.CornerRadius = new CornerRadius(10); dropZone.Padding = new Thickness(6);
        DragDrop.SetAllowDrop(dropZone, true);
        dropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble, true);
        dropZone.AddHandler(DragDrop.DragLeaveEvent, (_, _) => { dropZone.BorderBrush = Brushes.Transparent; ClearInsertion(); scrollDirection = 0; });
        dropZone.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, true);
        left.Children.Add(dropZone);
        Grid.SetColumn(left, 0); columns.Children.Add(left);
        var layers = new Grid { Width = 512, Height = 512, ClipToBounds = true };
        layers.Children.Add(new Checkerboard());
        layers.Children.Add(preview);
        var stickerBoundary = new Border { Width = 512, Height = 512, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = layers };
        var viewbox = new Viewbox { Stretch = Stretch.Uniform, Child = stickerBoundary };
        var canvas = new Border { Padding = new Thickness(20), CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.Parse("#192130")), Child = viewbox, ClipToBounds = true };
        canvas.PointerPressed += (_, e) =>
        {
            if (SelectedEntry() is null || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            previewDragStart = e.GetPosition(canvas); previewStartX = offsetX.Value; previewStartY = offsetY.Value; e.Pointer.Capture(canvas);
        };
        canvas.PointerMoved += (_, e) =>
        {
            if (previewDragStart is not { } start || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            var point = e.GetPosition(canvas); var size = Math.Min(Math.Max(0, canvas.Bounds.Width - 40), Math.Max(0, canvas.Bounds.Height - 40));
            if (viewbox.Stretch == Stretch.None) size = Math.Min(size, 512);
            if (size <= 0) return;
            offsetX.Value = Math.Clamp(previewStartX + (point.X - start.X) * 512 / size, offsetX.Minimum, offsetX.Maximum);
            offsetY.Value = Math.Clamp(previewStartY + (point.Y - start.Y) * 512 / size, offsetY.Minimum, offsetY.Maximum);
        };
        canvas.PointerReleased += (_, e) => { previewDragStart = null; e.Pointer.Capture(null); };
        canvas.PointerWheelChanged += (_, e) => { previewZoom = Math.Clamp(previewZoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), 0.25, 8); preview.RenderTransform = new ScaleTransform(previewZoom, previewZoom); e.Handled = true; };
        Grid.SetColumn(canvas, 1); columns.Children.Add(canvas);
        var properties = new StackPanel { Spacing = 12 };
        properties.Children.Add(new TextBlock { Text = "ВЫБРАННЫЙ СТИКЕР", FontWeight = FontWeight.Bold });
        properties.Children.Add(details);
        properties.Children.Add(new TextBlock { Text = "ПОДГОНКА", FontWeight = FontWeight.Bold });
        properties.Children.Add(fitMode);
        foreach (var (label, slider) in new[] { ("Масштаб", scale), ("Положение по горизонтали", offsetX), ("Положение по вертикали", offsetY), ("Поворот", rotation) })
        {
            properties.Children.Add(new TextBlock { Text = label }); properties.Children.Add(slider);
            slider.PropertyChanged += async (_, e) => { if (e.Property == RangeBase.ValueProperty) await TransformChangedAsync(); };
        }
        fitMode.SelectionChanged += async (_, _) => await TransformChangedAsync();
        properties.Children.Add(new TextBlock { Text = "Начало фрагмента, с" }); properties.Children.Add(fragmentStart);
        properties.Children.Add(new TextBlock { Text = "Длительность, до 3 с" }); properties.Children.Add(fragmentDuration);
        fragmentStart.ValueChanged += async (_, _) => await TransformChangedAsync();
        fragmentDuration.ValueChanged += async (_, _) => await TransformChangedAsync();
        properties.Children.Add(Button("Сбросить", async () => { if (SelectedEntry() is { } entry) { entry.Item.Transform = new(); LoadTransform(entry.Item.Transform); await TransformChangedAsync(); } }));
        properties.Children.Add(Button("Копировать на выделенные", async () => { if (SelectedEntry() is not { } entry) return; foreach (var row in gallery.SelectedItems?.OfType<ListBoxItem>() ?? []) if (row.Tag is GalleryEntry target) target.Item.Transform = CopyTransform(entry.Item.Transform); MarkDirty(); await SelectAsync(); }));
        properties.Children.Add(Button("512 × 512 / fit", () => { viewbox.Stretch = viewbox.Stretch == Stretch.None ? Stretch.Uniform : Stretch.None; return Task.CompletedTask; }));
        properties.Children.Add(Button("Fit to window", () => { previewZoom = 1; preview.RenderTransform = null; return Task.CompletedTask; }));
        gifPlay.Click += (_, _) => ToggleGif();
        properties.Children.Add(gifPlay);
        videoPlay.Click += (_, _) => { if (videoPlayback is null) StartVideo(); else StopVideo(); };
        properties.Children.Add(videoPlay);
        properties.Children.Add(videoSeek);
        properties.Children.Add(videoTime);
        videoSeek.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !updatingSeek && videoPath is not null)
            {
                var playing = videoPlayback is not null;
                StopVideo();
                if (playing) StartVideo();
                else _ = ShowSeekFrameAsync(videoPath, videoSeek.Value, ++previewGeneration);
                UpdateVideoTime(videoSeek.Value);
            }
        };
        var propertiesPanel = new Border { Background = new SolidColorBrush(Color.Parse("#192130")), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Child = new ScrollViewer { Content = properties } };
        Grid.SetColumn(propertiesPanel, 2); columns.Children.Add(propertiesPanel);
        root.Children.Add(columns); Content = root;
        packName.TextChanged += (_, _) =>
        {
            var name = packName.Text ?? "";
            if (!refreshing && !busy && project.Name != name) { project.Name = name; MarkDirty(); }
        };
        KeyDown += OnKeyDown;
        Closing += OnClosing;
        Closed += (_, _) => { StopGif(); StopVideo(); };
        RefreshGallery();
    }

    private static Button Button(string text, Func<Task> action, string? tooltip = null)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(10, 6) };
        if (tooltip is not null) ToolTip.SetTip(button, tooltip);
        button.Click += async (_, _) => await action();
        return button;
    }
    private void MarkDirty() { dirty = true; UpdateTitle(); }
    private void UpdateTitle() => Title = $"StickerForge Studio — {project.Name}{(dirty ? " *" : "")}";

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!dirty) return true;
        var dialog = new Window { Title = "Unsaved changes", Width = 390, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = "Discard unsaved changes?" });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Button("Discard", () => { dialog.Close(true); return Task.CompletedTask; }));
        buttons.Children.Add(Button("Cancel", () => { dialog.Close(false); return Task.CompletedTask; }));
        panel.Children.Add(buttons); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!dirty && !busy) return;
        e.Cancel = true;
        if (busy) { status.Text = "Cancel the current operation before closing."; return; }
        if (await ConfirmDiscardAsync()) { dirty = false; Close(); }
    }
    private async Task NewAsync()
    {
        if (busy || !await ConfirmDiscardAsync()) return;
        project = new(); projectPath = null; dirty = false; cache.Clear(); RefreshGallery();
    }
    private async Task OpenAsync()
    {
        if (busy || !await ConfirmDiscardAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open project", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("StickerForge project") { Patterns = ["*.stickerforge.json"] }] });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;
        try
        {
            project = await store.LoadAsync(path); projectPath = path; dirty = false; cache.Clear(); RefreshGallery();
            await LoadThumbnailsAsync();
            status.Text = "Project opened";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { status.Text = $"Open failed: {ex.Message}"; }
    }
    private async Task LoadThumbnailsAsync()
    {
        busy = true;
        try
        {
            foreach (var item in project.Stickers)
            {
                if (!File.Exists(item.SourcePath)) continue;
                var result = (await importer.ImportAsync([item.SourcePath]))[0];
                if (result.Info is not null) cache[item.Id] = result.Info;
            }
        }
        finally { busy = false; RefreshGallery(); }
    }
    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (busy) return false;
        var path = saveAs ? null : projectPath;
        if (path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save project", SuggestedFileName = "pack.stickerforge.json",
                FileTypeChoices = [new FilePickerFileType("StickerForge project") { Patterns = ["*.stickerforge.json"] }] });
            path = file?.TryGetLocalPath();
        }
        if (path is null) return false;
        try { await store.SaveAsync(project, path); projectPath = path; dirty = false; UpdateTitle(); status.Text = "Project saved"; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { status.Text = $"Save failed: {ex.Message}"; return false; }
    }
    private async Task PickFilesAsync()
    {
        if (busy) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import images", AllowMultiple = true });
        await ImportAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }
    private async Task PickFolderAsync()
    {
        if (busy) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Import folder", AllowMultiple = false });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;
        FolderScanResult scan;
        busy = true;
        importing = new CancellationTokenSource();
        try
        {
            var token = importing.Token;
            var report = new Progress<int>(count => status.Text = $"Scanning folder: {count} files");
            scan = await Task.Run(() => new FolderScanner().Scan(path, report, token), token);
        }
        catch (OperationCanceledException) { status.Text = "Folder scan cancelled."; return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { status.Text = $"Folder error: {ex.Message}"; return; }
        finally { importing.Dispose(); importing = null; busy = false; }
        await ImportAsync(scan.Files);
        if (scan.Errors.Count > 0)
        {
            var dialog = new Window { Title = "Folder scan errors", Width = 620, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.Content = new ScrollViewer { Content = new TextBlock { Text = string.Join(Environment.NewLine, scan.Errors), Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } };
            await dialog.ShowDialog(this);
        }
    }
    private async Task ExportAsync()
    {
        if (busy) return;
        if (project.Stickers.Count == 0) { status.Text = "Add images before exporting."; return; }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose export destination", AllowMultiple = false });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is null || busy) return;
        var sources = project.Stickers.ToArray();
        var name = project.Name;
        busy = true; importing = new CancellationTokenSource();
        try
        {
            var reportProgress = new Progress<ExportProgress>(p => { progress.Value = (double)p.Completed / p.Total; status.Text = $"Exporting {p.Completed}/{p.Total}: {p.FileName}"; });
            var result = await new StickerPackExporter().ExportAsync(sources, path, name, reportProgress, importing.Token);
            status.Text = result.Cancelled ? $"Export cancelled: {result.Exported} saved." : $"Exported {result.Exported} of {sources.Length} stickers.";
            var lines = new List<string> { status.Text, $"Folder: {result.Directory}" };
            if (result.Errors.Count > 0) { lines.Add(""); lines.Add("Some files could not be exported:"); lines.AddRange(result.Errors); }
            var dialog = new Window { Title = "Export report", Width = 620, Height = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var reportPanel = new DockPanel();
            var openFolder = Button("Open folder", () => { try { Process.Start(new ProcessStartInfo(result.Directory) { UseShellExecute = true }); } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { status.Text = $"Cannot open folder: {ex.Message}"; } return Task.CompletedTask; });
            DockPanel.SetDock(openFolder, Dock.Bottom); reportPanel.Children.Add(openFolder);
            reportPanel.Children.Add(new ScrollViewer { Content = new TextBlock { Text = string.Join(Environment.NewLine, lines), Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } }); dialog.Content = reportPanel;
            await dialog.ShowDialog(this);
        }
        catch (OperationCanceledException) { status.Text = "Export cancelled."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { status.Text = $"Export failed: {ex.Message}"; }
        finally { importing.Dispose(); importing = null; busy = false; progress.Value = 0; }
    }
    private async Task ImportAsync(IEnumerable<string> paths)
    {
        if (busy) return;
        var files = paths.ToArray(); if (files.Length == 0) return;
        busy = true; importing = new CancellationTokenSource();
        var errors = new List<string>(); var added = 0;
        try
        {
            for (var i = 0; i < files.Length; i++)
            {
                importing.Token.ThrowIfCancellationRequested();
                var results = await importer.ImportAsync([files[i]], cancellationToken: importing.Token);
                var result = results[0];
                if (result.Item is not null && result.Info is not null)
                { project.Stickers.Add(result.Item); cache[result.Item.Id] = result.Info; added++; MarkDirty(); }
                else errors.Add($"{Path.GetFileName(result.Path)}: {result.Error}");
                progress.Value = (i + 1d) / files.Length;
                status.Text = $"Importing {i + 1}/{files.Length}";
            }
        }
        catch (OperationCanceledException) { errors.Add("Import cancelled."); }
        finally { importing.Dispose(); importing = null; busy = false; progress.Value = 0; RefreshGallery(); }
        status.Text = $"Imported {added}; errors: {errors.Count}.";
        if (errors.Count > 0)
        {
            var dialog = new Window { Title = "Import report", Width = 620, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.Content = new ScrollViewer { Content = new TextBlock { Text = string.Join(Environment.NewLine, errors), Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } };
            await dialog.ShowDialog(this);
        }
    }
    private void RefreshGallery()
    {
        ClearVideo();
        StopGif();
        gifPath = null; gifDurations = null; gifPlay.IsVisible = false;
        refreshing = true;
        packName.Text = project.Name;
        previewGeneration++;
        foreach (var old in gallery.Items.OfType<ListBoxItem>())
            if (old.Tag is GalleryEntry entry) entry.Thumbnail?.Dispose();
        gallery.Items.Clear();
        foreach (var item in project.Stickers)
        {
            var name = Path.GetFileName(item.SourcePath);
            var exists = File.Exists(item.SourcePath);
            var text = exists ? name : $"{name} — missing";
            Bitmap? thumbnail = null;
            if (cache.TryGetValue(item.Id, out var info))
            {
                using var stream = new MemoryStream(info.Thumbnail);
                thumbnail = new Bitmap(stream);
            }
            var row = new StackPanel { Width = 146, Spacing = 4 };
            row.Children.Add(new Image { Source = thumbnail, Width = 140, Height = 140, Stretch = Stretch.Uniform });
            row.Children.Add(new TextBlock { Text = $"{project.Stickers.IndexOf(item) + 1:000}  {text}", TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            var card = new ListBoxItem { Content = row, Tag = new GalleryEntry(item, thumbnail), Margin = new Thickness(3) };
            ToolTip.SetTip(card, item.SourcePath);
            gallery.Items.Add(card);
        }
        refreshing = false;
        galleryHint.IsVisible = project.Stickers.Count == 0;
        details.Text = $"{project.Stickers.Count} stickers. Select an image.";
        preview.Source = null; previewBitmap?.Dispose(); previewBitmap = null;
        UpdateTitle();
    }
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (dragItems is not null)
        {
            e.DragEffects = DragDropEffects.Move;
            ClearInsertion();
            insertionCard = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (insertionCard is not null) { insertAfter = e.GetPosition(insertionCard).X >= insertionCard.Bounds.Width / 2; insertionCard.BorderBrush = Brushes.DodgerBlue; insertionCard.BorderThickness = insertAfter ? new Thickness(0, 0, 4, 0) : new Thickness(4, 0, 0, 0); }
            var scroller = gallery.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroller is not null) { var y = e.GetPosition(scroller).Y; scrollDirection = y < 35 ? -1 : y > scroller.Bounds.Height - 35 ? 1 : 0; }
            e.Handled = true; return;
        }
        var files = e.DataTransfer.TryGetFiles();
        e.DragEffects = !busy && files is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;
        dropZone.BorderBrush = e.DragEffects == DragDropEffects.Copy ? Brushes.DodgerBlue : Brushes.Transparent;
        e.Handled = true;
    }
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (dragItems is not null)
        {
            var target = insertionCard?.Tag is GalleryEntry entry ? project.Stickers.IndexOf(entry.Item) + (insertAfter ? 1 : 0) : project.Stickers.Count;
            var selected = dragItems.ToHashSet();
            target -= project.Stickers.Take(target).Count(selected.Contains);
            project.Stickers.RemoveAll(selected.Contains);
            project.Stickers.InsertRange(Math.Clamp(target, 0, project.Stickers.Count), dragItems);
            MarkDirty(); RefreshGallery();
            foreach (var card in gallery.Items.OfType<ListBoxItem>()) if (card.Tag is GalleryEntry item && selected.Contains(item.Item)) gallery.SelectedItems?.Add(card);
            e.Handled = true; return;
        }
        dropZone.BorderBrush = Brushes.Transparent;
        if (busy) return;
        var paths = e.DataTransfer.TryGetFiles()?.Select(x => x.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        e.Handled = true;
        var all = new List<string>();
        var errors = new List<string>();
        busy = true; importing = new CancellationTokenSource();
        try
        {
            var token = importing.Token;
            var report = new Progress<int>(count => status.Text = $"Scanning dropped folders: {count} files");
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(path))
                {
                    var scan = await Task.Run(() => new FolderScanner().Scan(path, report, token), token);
                    all.AddRange(scan.Files); errors.AddRange(scan.Errors);
                }
                else all.Add(path);
            }
        }
        catch (OperationCanceledException) { status.Text = "Drop cancelled."; return; }
        finally { importing.Dispose(); importing = null; busy = false; }
        await ImportAsync(all);
        if (errors.Count > 0)
        {
            var dialog = new Window { Title = "Folder scan errors", Width = 620, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.Content = new ScrollViewer { Content = new TextBlock { Text = string.Join(Environment.NewLine, errors), Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } };
            await dialog.ShowDialog(this);
        }
    }
    private void OnGalleryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(gallery).Properties.IsLeftButtonPressed) return;
        var card = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (card?.Tag is not GalleryEntry) return;
        dragPress = e; dragStart = e.GetPosition(gallery);
        pressedSelection = card.IsSelected ? project.Stickers.Where(item => gallery.SelectedItems?.OfType<ListBoxItem>().Any(row => row.Tag is GalleryEntry entry && entry.Item == item) == true).ToArray() : null;
    }
    private void ClearInsertion() { if (insertionCard is not null) insertionCard.BorderThickness = new Thickness(0); insertionCard = null; }
    private async void StartGalleryDrag(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(gallery);
        if (busy || dragPress is null || !e.GetCurrentPoint(gallery).Properties.IsLeftButtonPressed || Math.Abs(position.X - dragStart.X) + Math.Abs(position.Y - dragStart.Y) < 8) return;
        var pressed = dragPress; dragPress = null;
        dragItems = pressedSelection ?? project.Stickers.Where(item => gallery.SelectedItems?.OfType<ListBoxItem>().Any(card => card.Tag is GalleryEntry entry && entry.Item == item) == true).ToArray();
        if (dragItems.Length == 0) { dragItems = null; return; }
        try { dragScroll.Start(); using var data = new DataTransfer(); data.Add(DataTransferItem.CreateText("StickerForge gallery items")); await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move); }
        finally { dragScroll.Stop(); scrollDirection = 0; ClearInsertion(); dragItems = null; pressedSelection = null; }
    }
    private async Task SelectAsync()
    {
        previewZoom = 1; preview.RenderTransform = null;
        ClearVideo();
        StopGif();
        gifPath = null; gifDurations = null; gifPlay.IsVisible = false;
        var generation = ++previewGeneration;
        if (gallery.SelectedItem is not ListBoxItem { Tag: GalleryEntry entry }) return;
        var item = entry.Item;
        LoadTransform(item.Transform);
        details.Text = $"Loading {Path.GetFileName(item.SourcePath)}…";
        try
        {
            if (!cache.TryGetValue(item.Id, out var info))
            {
                var result = (await importer.ImportAsync([item.SourcePath]))[0];
                if (generation != previewGeneration) return;
                if (result.Info is null) { details.Text = result.Error; return; }
                info = result.Info; cache[item.Id] = info;
            }
            var firstFrame = info.FrameDurations is { Length: > 0 } frameDurations ? FrameAt(frameDurations, item.Transform.StartSeconds) : 0;
            var bytes = info.Video is { } initialVideo ? await ffmpeg.ReadTransformedFrameAsync(item.SourcePath, initialVideo, Math.Min(item.Transform.StartSeconds, Math.Max(0, initialVideo.DurationSeconds - .05)), CopyTransform(item.Transform)) : info.FrameDurations is not null
                ? await Task.Run(() => GifFrameReader.ReadFrame(item.SourcePath, firstFrame))
                : info.FfmpegStill ? await ffmpeg.ReadFrameAsync(item.SourcePath, 0) : await Task.Run(() => File.ReadAllBytes(item.SourcePath));
            if (info.Video is null) bytes = await Task.Run(() => RenderPreview(bytes, CopyTransform(item.Transform)));
            if (generation != previewGeneration) return;
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap;
            details.Text = $"{Path.GetFileName(item.SourcePath)}\n{info.Format} · {info.Width} × {info.Height}\nГотов к подготовке";
            if (info.Video is { } video)
            {
                details.Text += $"\n{video.Codec} · {video.FramesPerSecond?.ToString("0.##") ?? "unknown"} fps\n{video.DurationSeconds:0.00} s · alpha: {(video.HasAlpha == true ? "present" : "unknown")}";
                videoPath = item.SourcePath; videoMetadata = video;
                updatingSeek = true; videoSeek.Maximum = Math.Max(0, video.DurationSeconds - 0.001); videoSeek.Value = 0; updatingSeek = false;
                videoPlay.IsVisible = videoSeek.IsVisible = videoTime.IsVisible = true;
                UpdateVideoTime(0);
            }
            if (info.FrameDurations is { Length: > 1 } durations)
            {
                gifPath = item.SourcePath; gifDurations = durations; gifFrame = firstFrame;
                details.Text += $"\n{durations.Sum() / 1000d:0.00} s · {durations.Length} frames";
                gifPlay.IsVisible = true;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { if (generation == previewGeneration) details.Text = $"Preview failed: {ex.Message}"; }
    }
    private GalleryEntry? SelectedEntry() => (gallery.SelectedItem as ListBoxItem)?.Tag as GalleryEntry;
    private static TransformSettings CopyTransform(TransformSettings t) => new() { Fit = t.Fit, Scale = t.Scale, OffsetX = t.OffsetX, OffsetY = t.OffsetY, Rotation = t.Rotation, StartSeconds = t.StartSeconds, DurationSeconds = t.DurationSeconds };
    private void LoadTransform(TransformSettings t)
    {
        updatingTransform = true;
        fitMode.SelectedIndex = (int)t.Fit; scale.Value = t.Scale; offsetX.Value = t.OffsetX; offsetY.Value = t.OffsetY; rotation.Value = t.Rotation; fragmentStart.Value = (decimal)Math.Clamp(t.StartSeconds, 0, 36000); fragmentDuration.Value = (decimal)Math.Clamp(t.DurationSeconds, .1, 3);
        updatingTransform = false;
    }
    private async Task TransformChangedAsync()
    {
        if (updatingTransform || refreshing || busy || SelectedEntry() is not { } entry) return;
        var t = entry.Item.Transform; t.Fit = fitMode.SelectedIndex is >= 0 and <= 2 ? (FitMode)fitMode.SelectedIndex : FitMode.Fit;
        t.Scale = scale.Value; t.OffsetX = offsetX.Value; t.OffsetY = offsetY.Value; t.Rotation = rotation.Value;
        t.StartSeconds = (double)(fragmentStart.Value ?? 0); t.DurationSeconds = (double)(fragmentDuration.Value ?? 3);
        MarkDirty();
        if (!cache.TryGetValue(entry.Item.Id, out var info)) return;
        if (info.Video is not null) { if (videoPlayback is null) await ShowSeekFrameAsync(entry.Item.SourcePath, Math.Min(t.StartSeconds, videoSeek.Maximum), ++previewGeneration); else { StopVideo(); StartVideo(); } return; }
        if (info.FrameDurations is not null)
        {
            if (gifPlayback is null)
            {
                var frameGeneration = ++previewGeneration; var frameSettings = CopyTransform(t); var index = FrameAt(info.FrameDurations, frameSettings.StartSeconds);
                try { var frame = await Task.Run(() => RenderPreview(GifFrameReader.ReadFrame(entry.Item.SourcePath, index), frameSettings)); if (frameGeneration == previewGeneration) { gifFrame = index; SetPreview(frame); } }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { if (frameGeneration == previewGeneration) details.Text = $"Preview failed: {ex.Message}"; }
            }
            return;
        }
        var generation = ++previewGeneration; var snapshot = CopyTransform(t);
        try
        {
            var input = info.FfmpegStill ? await ffmpeg.ReadFrameAsync(entry.Item.SourcePath, 0) : await Task.Run(() => File.ReadAllBytes(entry.Item.SourcePath));
            var bytes = await Task.Run(() => RenderPreview(input, snapshot));
            if (generation != previewGeneration) return;
            using var stream = new MemoryStream(bytes); var bitmap = new Bitmap(stream);
            preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { status.Text = $"Preview failed: {ex.Message}"; }
    }
    private static byte[] RenderPreview(byte[] bytes, TransformSettings transform)
    {
        using var original = SKImage.FromEncodedData(bytes) ?? throw new InvalidDataException("Image decoding failed.");
        using var rendered = StickerPackExporter.Render(original, transform);
        using var encoded = rendered.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
    private static int FrameAt(int[] durations, double seconds)
    {
        var ms = Math.Max(0, seconds * 1000); var elapsed = 0d;
        for (var i = 0; i < durations.Length - 1; i++) { elapsed += Math.Max(durations[i], 20); if (ms < elapsed) return i; }
        return durations.Length - 1;
    }
    private void SetPreview(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); var bitmap = new Bitmap(stream);
        preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap;
    }
    private void UpdateVideoTime(double seconds) => videoTime.Text = $"{TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss\.ff")} / {TimeSpan.FromSeconds(videoMetadata?.DurationSeconds ?? 0).ToString(@"mm\:ss\.ff")}";
    private void StopVideo()
    {
        videoPlayback?.Cancel(); videoPlayback = null; videoPlay.Content = "Play";
    }
    private void ClearVideo()
    {
        videoSeekRequest?.Cancel(); videoSeekRequest = null;
        StopVideo(); videoPath = null; videoMetadata = null;
        videoPlay.IsVisible = videoSeek.IsVisible = videoTime.IsVisible = false;
        previewGeneration++;
    }
    private void StartVideo()
    {
        if (videoPath is null || videoMetadata is null) return;
        videoSeekRequest?.Cancel(); videoSeekRequest = null;
        var source = new CancellationTokenSource(); videoPlayback = source; videoPlay.Content = "Pause";
        _ = PlayVideoAsync(videoPath, videoMetadata, videoSeek.Value, source);
    }
    private async Task PlayVideoAsync(string path, VideoMetadata metadata, double start, CancellationTokenSource source)
    {
        try
        {
            while (true)
            {
                var settings = SelectedEntry()?.Item.Transform;
                var loopStart = Math.Clamp(settings?.StartSeconds ?? 0, 0, Math.Max(0, metadata.DurationSeconds - .05));
                var loopEnd = Math.Min(metadata.DurationSeconds, loopStart + (settings?.DurationSeconds ?? 3));
                if (start < loopStart || start >= loopEnd) start = loopStart;
                var clock = Stopwatch.StartNew();
                var received = false;
                var transform = CopyTransform(settings ?? new());
                await foreach (var frame in ffmpeg.StreamFramesAsync(path, metadata, start, source.Token, transform))
                {
                    if (frame.Seconds >= loopEnd) break;
                    received = true;
                    var delay = frame.Seconds - start - clock.Elapsed.TotalSeconds;
                    if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), source.Token);
                    source.Token.ThrowIfCancellationRequested();
                    var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
                    using (var locked = bitmap.Lock()) for (var y = 0; y < frame.Height; y++) Marshal.Copy(frame.Rgba, y * frame.Width * 4, IntPtr.Add(locked.Address, y * locked.RowBytes), frame.Width * 4);
                    preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap;
                    updatingSeek = true; videoSeek.Value = Math.Min(frame.Seconds, videoSeek.Maximum); updatingSeek = false;
                    UpdateVideoTime(frame.Seconds);
                }
                source.Token.ThrowIfCancellationRequested();
                if (!received) throw new InvalidDataException("Video decoder returned no frames.");
                start = loopStart;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or System.ComponentModel.Win32Exception)
        { status.Text = $"Video playback failed: {ex.Message}"; }
        finally
        {
            if (ReferenceEquals(videoPlayback, source)) { videoPlayback = null; videoPlay.Content = "Play"; }
            source.Dispose();
        }
    }
    private async Task ShowSeekFrameAsync(string path, double seconds, long generation)
    {
        videoSeekRequest?.Cancel();
        var source = new CancellationTokenSource();
        videoSeekRequest = source;
        try
        {
            var transform = CopyTransform(SelectedEntry()?.Item.Transform ?? new());
            if (videoMetadata is null) return;
            var bytes = await ffmpeg.ReadTransformedFrameAsync(path, videoMetadata, seconds, transform, source.Token);
            if (generation != previewGeneration || videoPlayback is not null) return;
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or System.ComponentModel.Win32Exception)
        { if (generation == previewGeneration) status.Text = $"Seek failed: {ex.Message}"; }
        finally { if (ReferenceEquals(videoSeekRequest, source)) videoSeekRequest = null; source.Dispose(); }
    }
    private void StopGif()
    {
        gifPlayback?.Cancel();
        gifPlayback = null;
        gifPlay.Content = "Play";
    }
    private void ToggleGif()
    {
        if (gifPlayback is not null) { StopGif(); return; }
        if (gifPath is null || gifDurations is null) return;
        var source = new CancellationTokenSource();
        gifPlayback = source;
        gifPlay.Content = "Pause";
        _ = PlayGifAsync(gifPath, gifDurations, source);
    }
    private async Task PlayGifAsync(string path, int[] durations, CancellationTokenSource source)
    {
        try
        {
            while (true)
            {
                await Task.Delay(Math.Max(durations[gifFrame], 20), source.Token);
                var transformSettings = SelectedEntry()?.Item.Transform;
                var begin = (transformSettings?.StartSeconds ?? 0) * 1000;
                var end = begin + (transformSettings?.DurationSeconds ?? 3) * 1000;
                var elapsed = 0d; var first = 0;
                while (first < durations.Length - 1 && elapsed + Math.Max(durations[first], 20) <= begin) elapsed += Math.Max(durations[first++], 20);
                var next = gifFrame + 1;
                var nextTime = durations.Take(Math.Min(next, durations.Length)).Sum(x => Math.Max(x, 20));
                if (next >= durations.Length || nextTime >= end || next < first) next = first;
                var bytes = await Task.Run(() => GifFrameReader.ReadFrame(path, next, source.Token), source.Token);
                source.Token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(bytes);
                var transform = CopyTransform(SelectedEntry()?.Item.Transform ?? new());
                var rendered = await Task.Run(() => RenderPreview(bytes, transform), source.Token);
                source.Token.ThrowIfCancellationRequested(); SetPreview(rendered); gifFrame = next;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { status.Text = $"GIF playback failed: {ex.Message}"; }
        finally
        {
            if (ReferenceEquals(gifPlayback, source)) { gifPlayback = null; gifPlay.Content = "Play"; }
            source.Dispose();
        }
    }
    private static byte[] RenderFrame(VideoFrame frame, TransformSettings transform)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(frame.Width, frame.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(frame.Rgba, 0, bitmap.GetPixels(), frame.Rgba.Length);
        using var source = SKImage.FromBitmap(bitmap); using var result = StickerPackExporter.Render(source, transform);
        using var encoded = result.Encode(SKEncodedImageFormat.Png, 100); return encoded.ToArray();
    }
    private Task RemoveSelected()
    {
        if (busy) return Task.CompletedTask;
        var selected = gallery.SelectedItems?.OfType<ListBoxItem>().Select(x => ((GalleryEntry)x.Tag!).Item).ToArray() ?? [];
        if (selected.Length == 0) return Task.CompletedTask;
        foreach (var item in selected) { project.Stickers.Remove(item); cache.Remove(item.Id); }
        MarkDirty(); RefreshGallery(); return Task.CompletedTask;
    }
    private Task MoveSelected(int delta)
    {
        if (busy || gallery.SelectedItems?.Count != 1 || gallery.SelectedItem is not ListBoxItem { Tag: GalleryEntry entry }) return Task.CompletedTask;
        var item = entry.Item;
        var index = project.Stickers.IndexOf(item); var target = index + delta;
        if (target < 0 || target >= project.Stickers.Count) return Task.CompletedTask;
        project.Stickers.RemoveAt(index); project.Stickers.Insert(target, item); MarkDirty(); RefreshGallery(); gallery.SelectedIndex = target;
        return Task.CompletedTask;
    }
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.N) await NewAsync();
            else if (e.Key == Key.O) await OpenAsync();
            else if (e.Key == Key.S) await SaveAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            else if (e.Key == Key.I) await PickFilesAsync();
            else return;
            e.Handled = true;
        }
    }
    private sealed record GalleryEntry(StickerItem Item, Bitmap? Thumbnail);
}

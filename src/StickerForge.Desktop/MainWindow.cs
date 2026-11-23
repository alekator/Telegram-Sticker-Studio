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
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,4*,2*"), ColumnSpacing = 12 };
        var left = new DockPanel();
        var galleryTitle = new TextBlock { Text = "YOUR STICKERS", FontWeight = FontWeight.Bold, Margin = new Thickness(4, 2, 4, 12) };
        DockPanel.SetDock(galleryTitle, Dock.Top); left.Children.Add(galleryTitle);
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(Button("Remove", RemoveSelected));
        actions.Children.Add(Button("↑", () => MoveSelected(-1)));
        actions.Children.Add(Button("↓", () => MoveSelected(1)));
        DockPanel.SetDock(actions, Dock.Top); left.Children.Add(actions);
        DockPanel.SetDock(galleryHint, Dock.Top); left.Children.Add(galleryHint);
        gallery.SelectionChanged += async (_, _) => await SelectAsync();
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
        dropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        dropZone.AddHandler(DragDrop.DragLeaveEvent, (_, _) => dropZone.BorderBrush = Brushes.Transparent);
        dropZone.AddHandler(DragDrop.DropEvent, OnDrop);
        left.Children.Add(dropZone);
        Grid.SetColumn(left, 0); columns.Children.Add(left);
        var layers = new Grid();
        layers.Children.Add(new Checkerboard());
        layers.Children.Add(preview);
        var canvas = new Border { Padding = new Thickness(20), CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.Parse("#192130")), Child = layers, ClipToBounds = true };
        canvas.PointerWheelChanged += (_, e) => { previewZoom = Math.Clamp(previewZoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), 0.25, 8); preview.RenderTransform = new ScaleTransform(previewZoom, previewZoom); e.Handled = true; };
        Grid.SetColumn(canvas, 1); columns.Children.Add(canvas);
        var properties = new StackPanel { Spacing = 12 };
        properties.Children.Add(new TextBlock { Text = "STICKER DETAILS", FontWeight = FontWeight.Bold });
        properties.Children.Add(details);
        properties.Children.Add(new TextBlock { Text = "EXPORT TO TELEGRAM", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 16, 0, 0) });
        properties.Children.Add(new TextBlock { Text = "1. Import and arrange your images.\n2. Choose Export stickers and select a folder.\n3. Open Telegram's @Stickers mini app and create a static sticker set. Upload the numbered WebP files and assign an emoji to each.", TextWrapping = TextWrapping.Wrap });
        properties.Children.Add(new TextBlock { Text = "Your original files stay unchanged. Export runs locally.", TextWrapping = TextWrapping.Wrap, Opacity = .75 });
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
        var sources = project.Stickers.Select(s => s.SourcePath).ToArray();
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
            dialog.Content = new ScrollViewer { Content = new TextBlock { Text = string.Join(Environment.NewLine, lines), Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } };
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
            var row = new StackPanel { Width = 116, Spacing = 4 };
            row.Children.Add(new Image { Source = thumbnail, Width = 110, Height = 110, Stretch = Stretch.Uniform });
            row.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            row.Children.Add(new TextBlock { Text = exists ? Path.GetExtension(name).TrimStart('.').ToUpperInvariant() : "MISSING", FontSize = 10 });
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
        var files = e.DataTransfer.TryGetFiles();
        e.DragEffects = !busy && files is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;
        dropZone.BorderBrush = e.DragEffects == DragDropEffects.Copy ? Brushes.DodgerBlue : Brushes.Transparent;
        e.Handled = true;
    }
    private async void OnDrop(object? sender, DragEventArgs e)
    {
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
    private async Task SelectAsync()
    {
        previewZoom = 1; preview.RenderTransform = null;
        ClearVideo();
        StopGif();
        gifPath = null; gifDurations = null; gifPlay.IsVisible = false;
        var generation = ++previewGeneration;
        if (gallery.SelectedItem is not ListBoxItem { Tag: GalleryEntry entry }) return;
        var item = entry.Item;
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
            var bytes = info.Video is not null ? info.Thumbnail : info.FrameDurations is not null
                ? await Task.Run(() => GifFrameReader.ReadFrame(item.SourcePath, 0))
                : await Task.Run(() => File.ReadAllBytes(item.SourcePath));
            if (generation != previewGeneration) return;
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap;
            details.Text = $"{Path.GetFileName(item.SourcePath)}\n{info.Format} · {info.Width} × {info.Height}\n{info.FileSize:N0} bytes";
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
                gifPath = item.SourcePath; gifDurations = durations; gifFrame = 0;
                details.Text += $"\n{durations.Sum() / 1000d:0.00} s · {durations.Length} frames";
                gifPlay.IsVisible = true;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { if (generation == previewGeneration) details.Text = $"Preview failed: {ex.Message}"; }
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
                var clock = Stopwatch.StartNew();
                var received = false;
                await foreach (var frame in ffmpeg.StreamFramesAsync(path, metadata, start, source.Token))
                {
                    received = true;
                    var delay = frame.Seconds - start - clock.Elapsed.TotalSeconds;
                    if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), source.Token);
                    source.Token.ThrowIfCancellationRequested();
                    var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
                    using (var locked = bitmap.Lock())
                        for (var y = 0; y < frame.Height; y++)
                            Marshal.Copy(frame.Rgba, y * frame.Width * 4, IntPtr.Add(locked.Address, y * locked.RowBytes), frame.Width * 4);
                    preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap;
                    updatingSeek = true; videoSeek.Value = Math.Min(frame.Seconds, videoSeek.Maximum); updatingSeek = false;
                    UpdateVideoTime(frame.Seconds);
                }
                source.Token.ThrowIfCancellationRequested();
                if (!received) throw new InvalidDataException("Video decoder returned no frames.");
                start = 0;
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
            var bytes = await ffmpeg.ReadFrameAsync(path, seconds, source.Token);
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
                var next = (gifFrame + 1) % durations.Length;
                var bytes = await Task.Run(() => GifFrameReader.ReadFrame(path, next, source.Token), source.Token);
                source.Token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(bytes);
                var bitmap = new Bitmap(stream);
                preview.Source = bitmap; previewBitmap?.Dispose(); previewBitmap = bitmap; gifFrame = next;
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

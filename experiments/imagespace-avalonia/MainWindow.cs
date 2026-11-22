using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using ImageSpace.Core;

namespace ImageSpaceAvalonia;

public sealed class MainWindow : Window
{
    private readonly Editor editor = new();
    private readonly CanvasView canvas;
    private readonly ListBox layers = new() { MinWidth = 190, Height = 330 };
    private readonly TextBlock status = new() { Text = "Brush · wheel: zoom · middle drag: pan" };
    private bool updating;
    private static readonly FilePickerFileType ProjectType = new("ImageSpace project") { Patterns = ["*.imagespace"] };
    private static readonly FilePickerFileType PngType = new("PNG image") { Patterns = ["*.png"] };

    public MainWindow()
    {
        Title = "ImageSpace + Avalonia experiment"; Width = 1100; Height = 760; MinWidth = 720; MinHeight = 500;
        canvas = new CanvasView(editor);
        canvas.Changed += UpdateLayers;
        var root = new DockPanel();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8) };
        Add(tools, "Brush", () => { editor.Eraser = false; status.Text = "Brush selected"; });
        Add(tools, "Eraser", () => { editor.Eraser = true; status.Text = "Eraser selected"; });
        Add(tools, "Undo", () => { editor.Session.Undo(); UpdateAll(); });
        Add(tools, "Redo", () => { editor.Session.Redo(); UpdateAll(); });
        Add(tools, "Fit", () => canvas.Fit());
        AddAsync(tools, "Open", OpenAsync);
        AddAsync(tools, "Save", SaveAsync);
        AddAsync(tools, "Export PNG", ExportAsync);
        DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        DockPanel.SetDock(status, Dock.Bottom); status.Margin = new Thickness(8); root.Children.Add(status);
        var sidebar = new StackPanel { Spacing = 8, Margin = new Thickness(8), Width = 190 };
        sidebar.Children.Add(new TextBlock { Text = "Layers (top first)" });
        sidebar.Children.Add(layers);
        layers.SelectionChanged += (_, _) => { if (!updating && layers.SelectedItem is LayerRow row) editor.Session.SelectLayer(row.Id); };
        Add(sidebar, "Add layer", () => { editor.Session.AddLayer($"Layer {editor.Session.Document.Layers.Count + 1}"); UpdateAll(); });
        Add(sidebar, "Toggle visibility", () => { var active = editor.Session.Document.ActiveLayer; if (active is null) return; editor.Session.Execute("Visibility", _ => active.Visible = !active.Visible); UpdateAll(); });
        Add(sidebar, "Move up", () => { editor.Session.MoveLayer(1); UpdateAll(); });
        Add(sidebar, "Move down", () => { editor.Session.MoveLayer(-1); UpdateAll(); });
        DockPanel.SetDock(sidebar, Dock.Right); root.Children.Add(sidebar); root.Children.Add(canvas); Content = root;
        Opened += (_, _) => { canvas.Fit(); UpdateLayers(); };
        Closed += (_, _) => { canvas.Dispose(); editor.Dispose(); };
        UpdateLayers();
    }
    private record LayerRow(Guid Id, string Label) { public override string ToString() => Label; }
    private void UpdateLayers()
    {
        updating = true;
        try
        {
            var rows = editor.Session.Document.Layers.AsEnumerable().Reverse().Select(l => new LayerRow(l.Id, $"{(l.Visible ? "●" : "○")} {l.Name}")).ToList();
            layers.ItemsSource = rows; layers.SelectedItem = rows.FirstOrDefault(r => r.Id == editor.Session.Document.ActiveLayerId);
        }
        finally { updating = false; }
    }
    private void UpdateAll() { canvas.Refresh(); UpdateLayers(); }
    private void Add(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label }; button.Click += (_, _) => Run(action); panel.Children.Add(button);
    }
    private void AddAsync(Panel panel, string label, Func<Task> action)
    {
        var button = new Button { Content = label }; button.Click += async (_, _) => { try { if (!editor.Session.IsInTransaction) await action(); } catch (Exception ex) { status.Text = ex.Message; } }; panel.Children.Add(button);
    }
    private void Run(Action action) { try { if (!editor.Session.IsInTransaction) action(); } catch (Exception ex) { status.Text = ex.Message; } }
    private async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open ImageSpace document", AllowMultiple = false, FileTypeFilter = [ProjectType] });
        if (files.Count == 0) return;
        await using var input = await files[0].OpenReadAsync(); using var data = new MemoryStream(); await input.CopyToAsync(data);
        editor.Open(data.ToArray()); UpdateAll(); canvas.Fit(); status.Text = "Document opened";
    }
    private async Task SaveAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save ImageSpace document", SuggestedFileName = "sticker.imagespace", FileTypeChoices = [ProjectType], DefaultExtension = "imagespace" });
        if (file is null) return;
        var bytes = editor.Save(); await using var output = await file.OpenWriteAsync(); await output.WriteAsync(bytes); editor.Session.MarkSaved(); status.Text = "Document saved";
    }
    private async Task ExportAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export PNG", SuggestedFileName = "sticker.png", FileTypeChoices = [PngType], DefaultExtension = "png" });
        if (file is null) return;
        var bytes = editor.RenderPng(); await using var output = await file.OpenWriteAsync(); await output.WriteAsync(bytes); status.Text = "PNG exported";
    }
}

using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using StickerForge.Rendering;

namespace StickerForge.Desktop;

internal readonly record struct ViewTransform(double Scale, double X, double Y)
{
    public Vector2 ToDocument(double x, double y) => new((float)((x - X) / Scale), (float)((y - Y) / Scale));
    public ViewTransform ZoomAt(double factor, double x, double y)
    {
        var scale = Math.Clamp(Scale * factor, .1, 8);
        var p = ToDocument(x, y);
        return new(scale, x - p.X * scale, y - p.Y * scale);
    }
}

internal sealed class EditorWindow : Window
{
    private readonly GraphicsSession editor;
    private readonly CanvasView canvas;
    private readonly Action<byte[], byte[]> save;
    private readonly ListBox layers = new() { Width = 190, Height = 320 };
    private readonly TextBlock status = new() { Text = "Wheel: zoom · middle drag: pan" };
    private bool updating;
    private bool modified;
    private bool closeConfirmed;
    private bool closePromptOpen;

    public EditorWindow(GraphicsSession editor, Action<byte[], byte[]> save)
    {
        this.editor = editor; this.save = save;
        Title = "Sticker editor"; Width = 1100; Height = 740; MinWidth = 720; MinHeight = 500;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        canvas = new CanvasView(editor); canvas.Changed += () => { modified = true; UpdateLayers(); };
        var root = new DockPanel { Margin = new Thickness(8) };
        var tools = new WrapPanel { Orientation = Orientation.Horizontal };
        Add(tools, "Brush", () => editor.Eraser = false);
        Add(tools, "Eraser", () => editor.Eraser = true);
        Add(tools, "Undo", () => { editor.Session.Undo(); modified = true; UpdateAll(); });
        Add(tools, "Redo", () => { editor.Session.Redo(); modified = true; UpdateAll(); });
        Add(tools, "Fit", canvas.Fit);
        Add(tools, "Apply to project", Apply);
        Add(tools, "Back to gallery", Close);
        DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var side = new StackPanel { Width = 190, Spacing = 6, Margin = new Thickness(8) };
        side.Children.Add(new TextBlock { Text = "Layers (top first)" }); side.Children.Add(layers);
        layers.SelectionChanged += (_, _) => { if (!updating && layers.SelectedItem is Row row) editor.Session.SelectLayer(row.Id); };
        Add(side, "Add layer", () => { editor.Session.AddLayer($"Layer {editor.Session.Document.Layers.Count + 1}"); modified = true; UpdateAll(); });
        Add(side, "Toggle visibility", () => { var active = editor.Session.Document.ActiveLayer; if (active is null) return; editor.Session.Execute("Visibility", _ => active.Visible = !active.Visible); modified = true; UpdateAll(); });
        Add(side, "Move up", () => { editor.Session.MoveLayer(1); modified = true; UpdateAll(); });
        Add(side, "Move down", () => { editor.Session.MoveLayer(-1); modified = true; UpdateAll(); });
        DockPanel.SetDock(side, Dock.Right); root.Children.Add(side); root.Children.Add(canvas); Content = root;
        Opened += (_, _) => { canvas.Fit(); UpdateLayers(); };
        Closing += OnClosing;
        Closed += (_, _) => { canvas.Dispose(); editor.Dispose(); };
        UpdateLayers();
    }
    private sealed record Row(Guid Id, string Name) { public override string ToString() => Name; }
    private void UpdateLayers()
    {
        updating = true;
        try { var rows = editor.Session.Document.Layers.AsEnumerable().Reverse().Select(l => new Row(l.Id, $"{(l.Visible ? "●" : "○")} {l.Name}")).ToList(); layers.ItemsSource = rows; layers.SelectedItem = rows.FirstOrDefault(x => x.Id == editor.Session.Document.ActiveLayerId); }
        finally { updating = false; }
    }
    private void UpdateAll() { canvas.Refresh(); UpdateLayers(); }
    private void Add(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(3) };
        button.Click += (_, _) => { try { if (!editor.Session.IsInTransaction) action(); } catch (Exception ex) { status.Text = $"Editor error: {ex.Message}"; } };
        panel.Children.Add(button);
    }
    private void Apply()
    {
        if (!modified) { status.Text = "No changes"; return; }
        save(editor.Save(), editor.Thumbnail()); modified = false; status.Text = "Applied to project in memory. Use Save in the gallery to write the project to disk.";
    }

    private enum CloseChoice { Continue, Apply, Discard }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closeConfirmed) return;
        if (editor.Session.IsInTransaction)
        {
            e.Cancel = true;
            try { editor.End(); modified = true; }
            catch (Exception ex) { status.Text = $"Could not finish stroke: {ex.Message}. Your editor remains open."; return; }
        }
        if (!modified) return;
        e.Cancel = true;
        if (closePromptOpen) return;
        closePromptOpen = true;
        try
        {
            var dialog = new Window { Title = "Unapplied editor changes", Width = 540, Height = 210, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
            panel.Children.Add(new TextBlock { Text = "Apply these edits to the project? To save them to disk, save the project after returning to the gallery.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
            AddChoice("Apply changes", CloseChoice.Apply);
            AddChoice("Discard changes", CloseChoice.Discard);
            AddChoice("Continue editing", CloseChoice.Continue);
            panel.Children.Add(buttons); dialog.Content = panel;
            var choice = await dialog.ShowDialog<CloseChoice>(this);
            if (choice == CloseChoice.Continue) return;
            if (choice == CloseChoice.Apply) Apply();
            closeConfirmed = true; Close();
            void AddChoice(string text, CloseChoice value)
            {
                var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 4) };
                button.Click += (_, _) => dialog.Close(value); buttons.Children.Add(button);
            }
        }
        catch (Exception ex) { status.Text = $"Could not apply edits: {ex.Message}. Your edits are still open."; }
        finally { closePromptOpen = false; }
    }
}

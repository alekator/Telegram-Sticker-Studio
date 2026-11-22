using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using StickerForge.Rendering;

namespace StickerForge.Desktop;

internal readonly record struct ViewTransform(double Scale, double X, double Y)
{
    public Vector2 ToDocument(double x, double y) => new((float)((x - X) / Scale), (float)((y - Y) / Scale));
    public ViewTransform ZoomAt(double factor, double x, double y)
    {
        var scale = Math.Clamp(Scale * factor, .1, 8); var p = ToDocument(x, y);
        return new(scale, x - p.X * scale, y - p.Y * scale);
    }
}

internal sealed class EditorWorkspace : UserControl, IDisposable
{
    private readonly WorkspaceEditSession workspace;
    private readonly CanvasView canvas;
    private readonly ListBox layers = new() { Width = 180, Height = 300 };
    private readonly TextBlock status = new() { Text = "Wheel: zoom · middle drag: pan" };
    private readonly TextBlock zoom = new() { Text = "100%" };
    private readonly ToolOptionsPanel options;
    private readonly Button brushButton;
    private readonly Button eraserButton;
    private bool updating;
    private bool disposed;
    public event Action? BackRequested;
    public event Action<bool>? SaveRequested;
    public WorkspaceEditSession State => workspace;
    public EditorWorkspace(WorkspaceEditSession workspace, string projectName)
    {
        this.workspace = workspace;
        var editor = workspace.Graphics;
        options = new ToolOptionsPanel(editor);
        brushButton = new Button { Content = "Brush (B)", Margin = new Thickness(3) };
        eraserButton = new Button { Content = "Eraser (E)", Margin = new Thickness(3) };
        brushButton.Click += (_, _) => SelectTool(false);
        eraserButton.Click += (_, _) => SelectTool(true);
        canvas = new CanvasView(editor);
        canvas.Changed += () => { workspace.Changed(); UpdateLayers(); status.Text = "Unapplied changes"; };
        canvas.ZoomChanged += value => zoom.Text = $"{value * 100:0}%";
        var root = new DockPanel { Margin = new Thickness(8) };
        var top = new WrapPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new TextBlock { Text = projectName, Margin = new Thickness(4, 8), MaxWidth = 200, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
        Add(top, "Save project", () => SaveRequested?.Invoke(false));
        Add(top, "Save project as", () => SaveRequested?.Invoke(true));
        Add(top, "Undo", () => { if (!editor.Session.CanUndo) return; editor.Session.Undo(); workspace.Changed(); UpdateAll(); });
        Add(top, "Redo", () => { if (!editor.Session.CanRedo) return; editor.Session.Redo(); workspace.Changed(); UpdateAll(); });
        Add(top, "Apply to project", () => { workspace.Apply(); status.Text = "Applied in memory. Save project to write to disk."; });
        Add(top, "Back to gallery", () => BackRequested?.Invoke());
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; bottom.Children.Add(zoom); bottom.Children.Add(status);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var side = new StackPanel { Spacing = 6, Margin = new Thickness(8) };
        side.Children.Add(options);
        side.Children.Add(new TextBlock { Text = "Layers (top first)" }); side.Children.Add(layers);
        layers.SelectionChanged += (_, _) => { if (!updating && layers.SelectedItem is Row row) editor.Session.SelectLayer(row.Id); };
        Add(side, "Add layer", () => { editor.Session.AddLayer($"Layer {editor.Session.Document.Layers.Count + 1}"); workspace.Changed(); UpdateAll(); });
        Add(side, "Toggle visibility", () => { var active = editor.Session.Document.ActiveLayer; if (active is null) return; editor.Session.Execute("Visibility", _ => active.Visible = !active.Visible); workspace.Changed(); UpdateAll(); });
        Add(side, "Move up", () => { editor.Session.MoveLayer(1); workspace.Changed(); UpdateAll(); });
        Add(side, "Move down", () => { editor.Session.MoveLayer(-1); workspace.Changed(); UpdateAll(); });
        var sideScroll = new ScrollViewer { Width = 220, Content = side, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        DockPanel.SetDock(sideScroll, Dock.Right); root.Children.Add(sideScroll);
        var tools = new StackPanel { Width = 100, Spacing = 6, Margin = new Thickness(8) };
        tools.Children.Add(brushButton); tools.Children.Add(eraserButton); Add(tools, "Fit", canvas.Fit);
        DockPanel.SetDock(tools, Dock.Left); root.Children.Add(tools);
        root.Children.Add(canvas); Content = root; UpdateLayers(); SelectTool(false);
    }
    public void FitWhenShown() => Dispatcher.UIThread.Post(() => { if (!disposed) { canvas.Fit(); canvas.Focus(); } }, DispatcherPriority.Loaded);
    public void FinishInteraction() { canvas.FinishInteraction(); workspace.FinishStroke(); }
    public void SetStatus(string message) => status.Text = message;
    private void SelectTool(bool eraser)
    {
        if (workspace.Graphics.Session.IsInTransaction) return;
        workspace.Graphics.Eraser = eraser; options.Rebuild();
        brushButton.FontWeight = eraser ? FontWeight.Normal : FontWeight.Bold;
        eraserButton.FontWeight = eraser ? FontWeight.Bold : FontWeight.Normal;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || e.Source is TextBox || workspace.Graphics.Session.IsInTransaction) return;
        if (e.Key == Key.B) { SelectTool(false); e.Handled = true; }
        else if (e.Key == Key.E) { SelectTool(true); e.Handled = true; }
    }
    private sealed record Row(Guid Id, string Name) { public override string ToString() => Name; }
    private void UpdateLayers()
    {
        updating = true;
        try { var rows = workspace.Graphics.Session.Document.Layers.AsEnumerable().Reverse().Select(l => new Row(l.Id, $"{(l.Visible ? "●" : "○")} {l.Name}")).ToList(); layers.ItemsSource = rows; layers.SelectedItem = rows.FirstOrDefault(x => x.Id == workspace.Graphics.Session.Document.ActiveLayerId); }
        finally { updating = false; }
    }
    private void UpdateAll() { canvas.Refresh(); UpdateLayers(); status.Text = "Unapplied changes"; }
    private void Add(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(3) };
        button.Click += (_, _) => { try { if (!workspace.Graphics.Session.IsInTransaction) action(); } catch (Exception ex) { status.Text = $"Editor error: {ex.Message}"; } };
        panel.Children.Add(button);
    }
    public void Dispose() { if (disposed) return; disposed = true; canvas.Dispose(); workspace.Dispose(); }
}

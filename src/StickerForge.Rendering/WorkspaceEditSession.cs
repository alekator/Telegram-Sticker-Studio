namespace StickerForge.Rendering;

public enum LeaveEditorChoice { Continue, Apply, Discard }

// Owns the open document and keeps changes made after Apply separate from the project.
public sealed class WorkspaceEditSession(GraphicsSession graphics, Action<byte[], byte[]> apply) : IDisposable
{
    private bool disposed;
    public GraphicsSession Graphics { get; } = graphics;
    public bool HasUnappliedChanges { get; private set; }
    public bool IsDisposed => disposed;

    public void Changed() { ObjectDisposedException.ThrowIf(disposed, this); HasUnappliedChanges = true; }
    public void FinishStroke()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Graphics.Session.IsInTransaction) { Graphics.End(); HasUnappliedChanges = true; }
    }
    public void Apply()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        FinishStroke();
        if (!HasUnappliedChanges) return;
        var document = Graphics.Save(); var thumbnail = Graphics.Thumbnail();
        apply(document, thumbnail);
        HasUnappliedChanges = false;
    }
    public bool PrepareLeave(LeaveEditorChoice choice)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        FinishStroke();
        if (choice == LeaveEditorChoice.Continue) return false;
        if (choice == LeaveEditorChoice.Apply) Apply();
        return true;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Graphics.Dispose();
    }
}

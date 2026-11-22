namespace StickerForge.Media;

public sealed record FolderScanResult(IReadOnlyList<string> Files, IReadOnlyList<string> Errors);

/// <summary>Finds supported media without following directory links.</summary>
public sealed class FolderScanner
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".mp4", ".mov", ".mkv", ".webm", ".avi"
    };

    public FolderScanResult Scan(string root, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var files = new List<string>();
        var errors = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                        else if (Extensions.Contains(Path.GetExtension(entry))) { files.Add(entry); progress?.Report(files.Count); }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        errors.Add($"{entry}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{folder}: {ex.Message}");
            }
        }
        files.Sort(StringComparer.Ordinal);
        return new FolderScanResult(files, errors);
    }
}

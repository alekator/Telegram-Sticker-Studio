using StickerForge.Media;

namespace StickerForge.Tests;

public sealed class FolderScannerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "stickerforge-scan-" + Guid.NewGuid().ToString("N"));

    public FolderScannerTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void FindsSupportedFilesRecursivelyAndSkipsOthers()
    {
        var child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
        var image = Path.Combine(root, "a.PNG");
        var video = Path.Combine(child, "b.mp4");
        File.WriteAllText(image, "");
        File.WriteAllText(video, "");
        File.WriteAllText(Path.Combine(child, "ignored.txt"), "");
        var result = new FolderScanner().Scan(root);
        Assert.Equal(new[] { image, video }.Order(StringComparer.Ordinal), result.Files);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ReportsMissingDirectory()
    {
        var result = new FolderScanner().Scan(Path.Combine(root, "missing"));
        Assert.Empty(result.Files);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new FolderScanner().Scan(root, cancellationToken: cancellation.Token));
    }
}

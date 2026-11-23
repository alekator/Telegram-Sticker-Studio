using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace StickerForge.Media;

// The log intentionally identifies paths by a stable digest, not by user or file names.
public static partial class ExportDiagnostics
{
    private static readonly object Sync = new();
    public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickerForge", "export.log");
    public static string PathId(string path) => $"path:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..12]}";
    public static string Sanitize(string text)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)) text = text.Replace(home, "<profile>", StringComparison.OrdinalIgnoreCase);
        text = PathPattern().Replace(text, "<path>");
        return UrlPattern().Replace(text, "<url>");
    }
    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024) File.WriteAllText(LogPath, "");
                File.AppendAllText(LogPath, $"{DateTimeOffset.UtcNow:O} {Sanitize(message)}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }
    [GeneratedRegex(@"(?:[A-Za-z]:\\|/)(?:[^\s\""<>|]+)")]
    private static partial Regex PathPattern();
    [GeneratedRegex(@"https?://[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}

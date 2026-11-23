using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace StickerForge.Media;

public sealed record FfmpegBinaryHashes(string FfmpegSha256, string FfprobeSha256);
public sealed record FfmpegLocation(string Ffmpeg, string Ffprobe, string Source);

public static class FfmpegLocator
{
    public const string StandaloneMarker = "standalone-windows.txt";
    public static readonly IReadOnlyDictionary<string, FfmpegBinaryHashes> ReleaseHashes = new Dictionary<string, FfmpegBinaryHashes>
    {
        ["windows/x64"] = new("ACE36305D537A20868A776819BE530D3CE042CF93F70ED07A9CBDBBE7B03579C", "F7D94376A390D38BAAEB151D29E37E94E9E374B0664E0436C8C3BA69A7A95AAE")
    };
    public static FfmpegService CreateDefault()
    {
        var components = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickerForge");
        return CreateForDirectory(AppContext.BaseDirectory, components, ReleaseHashes);
    }
    public static FfmpegService CreateForDirectory(string applicationDirectory, string componentDirectory, IReadOnlyDictionary<string, FfmpegBinaryHashes> approved)
    {
        var standalone = File.Exists(Path.Combine(applicationDirectory, StandaloneMarker)) ||
            (OperatingSystem.IsWindows() && File.Exists(Path.Combine(applicationDirectory, "hostfxr.dll")));
        var location = FindVerified(applicationDirectory, standalone ? applicationDirectory : componentDirectory, approved);
        if (location is not null) return new FfmpegService(location.Ffmpeg, location.Ffprobe);
        if (standalone)
            return new FfmpegService(unavailableReason: "Комплектные FFmpeg и FFprobe отсутствуют или повреждены. Распакуйте архив приложения заново.");
        var ffmpeg = FindOnPath(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        var ffprobe = FindOnPath(OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        return ffmpeg is not null && ffprobe is not null ? new FfmpegService(ffmpeg, ffprobe)
            : new FfmpegService(unavailableReason: "FFmpeg и FFprobe не найдены. Используйте полный архив приложения.");
    }
    private static string? FindOnPath(string name)
    {
        var cwd = Path.GetFullPath(Environment.CurrentDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (!Path.IsPathFullyQualified(entry)) continue;
                var directory = Path.GetFullPath(entry).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(directory, cwd, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        return null;
    }
    public static FfmpegLocation? FindVerified(string applicationDirectory, string componentDirectory,
        IReadOnlyDictionary<string, FfmpegBinaryHashes> approved)
    {
        var platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : null;
        var architecture = RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => null };
        if (platform is null || architecture is null || !approved.TryGetValue($"{platform}/{architecture}", out var hashes)) return null;
        var executable = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var probe = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        foreach (var root in new[] { applicationDirectory, componentDirectory })
        {
            var directory = Path.Combine(root, "tools", "ffmpeg", platform, architecture);
            var ffmpeg = Path.Combine(directory, executable);
            var ffprobe = Path.Combine(directory, probe);
            if (HasHash(ffmpeg, hashes.FfmpegSha256) && HasHash(ffprobe, hashes.FfprobeSha256))
                return new(ffmpeg, ffprobe, root);
        }
        return null;
    }

    private static bool HasHash(string path, string expected)
    {
        try
        {
            if (expected.Length != 64 || !File.Exists(path)) return false;
            using var stream = File.OpenRead(path);
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(expected));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { return false; }
    }
}

using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace StickerForge.Media;

public sealed record FfmpegBinaryHashes(string FfmpegSha256, string FfprobeSha256);
public sealed record FfmpegLocation(string Ffmpeg, string Ffprobe, string Source);

public static class FfmpegLocator
{
    public static FfmpegService CreateDefault()
    {
        var approved = new Dictionary<string, FfmpegBinaryHashes>();
        var components = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickerForge");
        var location = FindVerified(AppContext.BaseDirectory, components, approved);
        return location is null ? new FfmpegService() : new FfmpegService(location.Ffmpeg, location.Ffprobe);
    }
    // The release manifest is deliberately empty until binaries and licenses are approved.
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

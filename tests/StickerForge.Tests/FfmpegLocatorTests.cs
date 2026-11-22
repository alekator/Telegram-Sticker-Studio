using System.Runtime.InteropServices;
using System.Security.Cryptography;
using StickerForge.Media;

namespace StickerForge.Tests;

public sealed class FfmpegLocatorTests
{
    [Fact]
    public void PrefersVerifiedAppFilesAndRejectsChangedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "app"); var local = Path.Combine(root, "local");
        var platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var ffmpeg = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var ffprobe = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        try
        {
            var bytes = new byte[] { 1, 2, 3 };
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var hashes = new Dictionary<string, FfmpegBinaryHashes> { [$"{platform}/{arch}"] = new(hash, hash) };
            foreach (var parent in new[] { app, local })
            {
                var dir = Path.Combine(parent, "tools", "ffmpeg", platform, arch);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, ffmpeg), bytes);
                File.WriteAllBytes(Path.Combine(dir, ffprobe), bytes);
            }
            Assert.Equal(app, FfmpegLocator.FindVerified(app, local, hashes)!.Source);
            File.WriteAllText(Path.Combine(app, "tools", "ffmpeg", platform, arch, ffmpeg), "changed");
            Assert.Equal(local, FfmpegLocator.FindVerified(app, local, hashes)!.Source);
            File.WriteAllText(Path.Combine(local, "tools", "ffmpeg", platform, arch, ffprobe), "changed");
            Assert.Null(FfmpegLocator.FindVerified(app, local, hashes));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

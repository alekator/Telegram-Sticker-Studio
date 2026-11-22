using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace StickerForge.Media;

public sealed record VideoMetadata(int Width, int Height, double DurationSeconds, string Codec, double? FramesPerSecond = null, bool? HasAlpha = null, long FileSize = 0);
public sealed record FfmpegAvailability(bool Available, string Message);

/// <summary>Runs a caller-supplied FFmpeg installation without invoking a shell.</summary>
public sealed class FfmpegService
{
    private static readonly SemaphoreSlim Processes = new(2, 2);
    private readonly string ffmpeg;
    private readonly string ffprobe;

    public FfmpegService(string ffmpeg = "ffmpeg", string ffprobe = "ffprobe")
    {
        this.ffmpeg = ffmpeg;
        this.ffprobe = ffprobe;
    }

    public async Task<FfmpegAvailability> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var encoder = await RunAsync(ffmpeg, ["-version"], 1024 * 1024, cancellationToken);
            var probe = await RunAsync(ffprobe, ["-version"], 1024 * 1024, cancellationToken);
            var encText = System.Text.Encoding.UTF8.GetString(encoder);
            var probeText = System.Text.Encoding.UTF8.GetString(probe);
            if (!encText.StartsWith("ffmpeg version ", StringComparison.OrdinalIgnoreCase) ||
                !probeText.StartsWith("ffprobe version ", StringComparison.OrdinalIgnoreCase))
                return new(false, "The configured executables did not identify themselves as FFmpeg and FFprobe.");
            return new(true, encText.Split('\n')[0].Trim());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new(false, $"FFmpeg/FFprobe unavailable: {ex.Message}");
        }
    }

    public async Task<VideoMetadata> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        var output = await RunAsync(ffprobe, ["-v", "error", "-protocol_whitelist", "file,pipe", "-select_streams", "v:0", "-show_entries",
            "stream=width,height,codec_name,avg_frame_rate,pix_fmt:stream_tags=alpha_mode:format=duration", "-of", "json", path], 1024 * 1024, cancellationToken);
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
            throw new InvalidDataException("No video stream found.");
        var stream = streams[0];
        var width = stream.GetProperty("width").GetInt32();
        var height = stream.GetProperty("height").GetInt32();
        if (width <= 0 || height <= 0 || (long)width * height > 40_000_000)
            throw new InvalidDataException("Video dimensions exceed the limit.");
        var durationText = root.GetProperty("format").GetProperty("duration").GetString();
        if (!double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || !double.IsFinite(duration) || duration <= 0)
            throw new InvalidDataException("Video duration is unavailable.");
        double? fps = null;
        if (stream.TryGetProperty("avg_frame_rate", out var rate))
        {
            var parts = (rate.GetString() ?? "").Split('/');
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 && n > 0 && double.IsFinite(n / d))
                fps = n / d;
        }
        bool? alpha = null;
        if (stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("alpha_mode", out var mode) && mode.GetString() == "1")
            alpha = true;
        var pixFmt = stream.TryGetProperty("pix_fmt", out var pix) ? pix.GetString() : null;
        if (pixFmt is not null && (pixFmt.StartsWith("yuva", StringComparison.Ordinal) || pixFmt is "rgba" or "bgra" or "argb" or "abgr" or "gbrap" or "ya8" or "ya16le"))
            alpha = true;
        return new(width, height, duration, stream.GetProperty("codec_name").GetString() ?? "unknown", fps, alpha, new FileInfo(path).Length);
    }

    public Task<byte[]> ReadFrameAsync(string path, double seconds, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        return RunAsync(ffmpeg, ["-v", "error", "-protocol_whitelist", "file,pipe", "-ss", seconds.ToString("R", CultureInfo.InvariantCulture),
            "-i", Path.GetFullPath(path), "-frames:v", "1", "-f", "image2pipe", "-vcodec", "png", "pipe:1"], 32 * 1024 * 1024, cancellationToken);
    }

    private static async Task<byte[]> RunAsync(string executable, string[] arguments, int maxBytes, CancellationToken cancellationToken)
    {
        await Processes.WaitAsync(cancellationToken);
        try
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Could not start media process.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
            var stderr = DrainErrorsAsync(process.StandardError, timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[16384];
            try
            {
                int count;
                while ((count = await process.StandardOutput.BaseStream.ReadAsync(buffer, timeout.Token)) != 0)
                {
                    if (output.Length + count > maxBytes) throw new InvalidDataException("Media process output exceeds the limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                }
                await process.WaitForExitAsync(timeout.Token);
                var errors = await stderr;
                if (process.ExitCode != 0) throw new InvalidDataException($"Media process failed: {errors.Trim()}");
                return output.ToArray();
            }
            finally { if (!process.HasExited) process.Kill(true); }
        }
        finally { Processes.Release(); }
    }

    private static async Task<string> DrainErrorsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken)) != 0)
        {
            var remaining = 16384 - result.Length;
            if (remaining > 0) result.Append(buffer, 0, Math.Min(count, remaining));
        }
        return result.ToString();
    }
}

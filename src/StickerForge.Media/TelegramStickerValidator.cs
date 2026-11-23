using System.Globalization;
using System.Text.Json;
using System.Text;
using SkiaSharp;

namespace StickerForge.Media;

public sealed class TelegramStickerValidator(FfmpegService ffmpeg)
{
    public void ValidateStatic(string path)
    {
        var length = new FileInfo(path).Length;
        if (length <= 0 || length > 512 * 1024) throw new InvalidDataException("Размер WebP должен быть не больше 512 КиБ.");
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Не удалось декодировать WebP.");
        var info = codec.Info;
        if (codec.EncodedFormat != SKEncodedImageFormat.Webp || codec.FrameCount > 1 || !DimensionsValid(info.Width, info.Height))
            throw new InvalidDataException("Недопустимый формат или размеры WebP: одна сторона должна быть 512 пикселей.");
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException("WebP повреждён или не может быть декодирован.");
        ExportDiagnostics.Write($"validation static format=webp width={info.Width} height={info.Height} bytes={length} alpha={info.AlphaType} result=valid");
    }

    public async Task ValidateVideoAsync(string path, bool requireAlpha, CancellationToken token = default)
    {
        var length = new FileInfo(path).Length;
        if (length <= 0 || length > 256 * 1024) throw new InvalidDataException("Размер WebM должен быть не больше 256 КиБ.");
        using (var file = File.OpenRead(path))
        {
            var header = new byte[(int)Math.Min(4096, file.Length)]; file.ReadExactly(header);
            if (header.Length < 4 || !header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }) || header.AsSpan().IndexOf(new byte[] { 0x42, 0x82, 0x84, 0x77, 0x65, 0x62, 0x6d }) < 0)
                throw new InvalidDataException("Результат не является контейнером WebM.");
        }
        var probe = await ffmpeg.RunProbeAsync(["-v", "error", "-count_frames", "-show_frames", "-show_entries", "stream=codec_type,codec_name,width,height,avg_frame_rate,nb_read_frames:stream_tags=alpha_mode:format=duration:frame=best_effort_timestamp_time,pkt_duration_time,duration_time", "-of", "json", path], token);
        using var json = JsonDocument.Parse(probe);
        var root = json.RootElement; var streams = root.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Any(s => s.GetProperty("codec_type").GetString() == "audio")) throw new InvalidDataException("В WebM обнаружена звуковая дорожка.");
        var videos = streams.Where(s => s.GetProperty("codec_type").GetString() == "video").ToArray();
        if (videos.Length != 1) throw new InvalidDataException("В WebM должен быть один видеопоток.");
        var video = videos[0];
        var width = video.GetProperty("width").GetInt32(); var height = video.GetProperty("height").GetInt32();
        if (video.GetProperty("codec_name").GetString() != "vp9" || !DimensionsValid(width, height)) throw new InvalidDataException("Недопустимый кодек или размеры WebM.");
        var alpha = video.TryGetProperty("tags", out var tags) && tags.TryGetProperty("alpha_mode", out var mode) && mode.GetString() == "1";
        if (requireAlpha && !alpha) throw new InvalidDataException("В WebM отсутствует необходимый альфа-канал.");
        var rate = (video.GetProperty("avg_frame_rate").GetString() ?? "").Split('/');
        if (rate.Length != 2 || !double.TryParse(rate[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.TryParse(rate[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d <= 0 || !double.IsFinite(n / d) || n / d <= 0 || n / d > 30)
            throw new InvalidDataException("Частота кадров WebM недоступна или превышает 30 кадров в секунду.");
        if (!double.TryParse(root.GetProperty("format").GetProperty("duration").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || !double.IsFinite(duration) || duration <= 0 || duration > 3)
            throw new InvalidDataException("Длительность WebM недоступна или превышает 3 секунды.");
        var frames = root.GetProperty("frames").EnumerateArray().ToArray();
        if (frames.Length == 0 || !int.TryParse(video.GetProperty("nb_read_frames").GetString(), out var count) || count != frames.Length) throw new InvalidDataException("Не удалось проверить все кадры WebM.");
        double? first = null, previous = null;
        foreach (var frame in frames)
        {
            token.ThrowIfCancellationRequested();
            if (!frame.TryGetProperty("best_effort_timestamp_time", out var ts) || !double.TryParse(ts.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) throw new InvalidDataException("Не удалось определить временные метки кадров.");
            first ??= value;
            if (previous is not null && (value <= previous || value - previous < 1d / 30 - .0011)) throw new InvalidDataException("Недопустимые временные метки или частота кадров WebM.");
            if (value - first >= 3.001) throw new InvalidDataException("Кадры WebM выходят за пределы 3 секунд.");
            if (!(frame.TryGetProperty("duration_time", out var frameDuration) || frame.TryGetProperty("pkt_duration_time", out frameDuration)) || !double.TryParse(frameDuration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var frameSeconds) || !double.IsFinite(frameSeconds) || frameSeconds <= 0 || value + frameSeconds - first > 3.001)
                throw new InvalidDataException("Длительность кадров WebM недоступна или превышает 3 секунды.");
            previous = value;
        }
        await ffmpeg.RunEncoderAsync(["-v", "error", "-xerror", "-c:v", "libvpx-vp9", "-i", path, "-map", "0:v:0", "-f", "null", "-"], token);
        ExportDiagnostics.Write($"validation video container=webm codec=vp9 width={width} height={height} bytes={length} duration={duration.ToString(CultureInfo.InvariantCulture)} fps={(n / d).ToString(CultureInfo.InvariantCulture)} frames={count} audio=false alpha={alpha} requiredAlpha={requireAlpha} result=valid");
    }

    public async Task ValidateAlphaAsync(string source, string output, double start, double duration, string filter, CancellationToken token)
    {
        static string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
        const string analysis = "alphaextract,signalstats,metadata=mode=print:file=-";
        var original = await ffmpeg.RunEncoderAsync(["-v", "error", "-protocol_whitelist", "file,pipe", "-ss", N(start), "-i", source, "-t", N(duration), "-filter_complex", filter + "," + analysis, "-f", "null", "-"], token);
        var encoded = await ffmpeg.RunEncoderAsync(["-v", "error", "-xerror", "-c:v", "libvpx-vp9", "-i", output, "-vf", analysis, "-f", "null", "-"], token);
        static (double Min, double Mean)[] Parse(byte[] data)
        {
            var frames = new List<(double, double)>(); double? min = null;
            foreach (var line in Encoding.UTF8.GetString(data).Split('\n'))
            {
                if (line.StartsWith("lavfi.signalstats.YMIN=", StringComparison.Ordinal)) min = double.Parse(line[(line.IndexOf('=') + 1)..], CultureInfo.InvariantCulture);
                if (line.StartsWith("lavfi.signalstats.YAVG=", StringComparison.Ordinal) && min is not null)
                { frames.Add((min.Value, double.Parse(line[(line.IndexOf('=') + 1)..], CultureInfo.InvariantCulture))); min = null; }
            }
            return frames.ToArray();
        }
        var expected = Parse(original); var actual = Parse(encoded);
        if (expected.Length == 0 || expected.Length != actual.Length) throw new InvalidDataException("Не удалось проверить прозрачность всех кадров.");
        for (var i = 0; i < expected.Length; i++)
            if (Math.Abs(expected[i].Min - actual[i].Min) > 12 || Math.Abs(expected[i].Mean - actual[i].Mean) > 5)
                throw new InvalidDataException("Прозрачность видео не сохранилась при кодировании.");
        ExportDiagnostics.Write($"validation alpha frames={expected.Length} transparentFrames={expected.Count(f => f.Min < 255)} result=valid");
    }

    private static bool DimensionsValid(int width, int height) => width > 0 && height > 0 && width <= 512 && height <= 512 && (width == 512 || height == 512);
}

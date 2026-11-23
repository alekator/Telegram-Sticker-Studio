using System.Buffers.Binary;

namespace StickerForge.Media;

internal static class AnimatedPng
{
    public static double? Duration(string path)
    {
        using var stream = File.OpenRead(path); Span<byte> header = stackalloc byte[8];
        if (stream.Read(header) != 8 || !header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return null;
        var animated = false; double duration = 0; var frames = 0; Span<byte> frame = stackalloc byte[26];
        while (stream.Position + 12 <= stream.Length)
        {
            if (stream.Read(header) != 8) break;
            var length = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
            if (length > stream.Length - stream.Position - 4) break;
            if (header[4..].SequenceEqual("acTL"u8)) animated = true;
            if (header[4..].SequenceEqual("fcTL"u8) && length == 26)
            {
                stream.ReadExactly(frame);
                var n = BinaryPrimitives.ReadUInt16BigEndian(frame[20..22]); var d = BinaryPrimitives.ReadUInt16BigEndian(frame[22..24]);
                duration += (double)n / (d == 0 ? 100 : d); frames++;
            }
            else stream.Seek(length, SeekOrigin.Current);
            stream.Seek(4, SeekOrigin.Current);
            if (header[4..].SequenceEqual("IEND"u8)) break;
        }
        return animated && frames > 1 && duration > 0 ? duration : null;
    }
}

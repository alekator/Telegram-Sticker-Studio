using System.Globalization;
using System.Text;

namespace StickerForge.Telegram;

/// <summary>Validates Unicode Emoji 17.0 sequences, including qualified ZWJ, flags and modifiers.</summary>
public static class EmojiValidator
{
    private static readonly Lazy<HashSet<string>> Known = new(Load);
    private static HashSet<string> Load()
    {
        using var stream = typeof(EmojiValidator).Assembly.GetManifestResourceStream("StickerForge.Telegram.emoji-test-17.0.txt")
            ?? throw new InvalidOperationException("Unicode emoji data is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var result = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var comment = line.IndexOf('#'); var semicolon = line.IndexOf(';');
            if (semicolon < 0 || comment < semicolon) continue;
            var status = line[(semicolon + 1)..comment].Trim();
            if (status is not ("fully-qualified" or "minimally-qualified" or "unqualified")) continue;
            var points = line[..semicolon].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            result.Add(string.Concat(points.Select(p => char.ConvertFromUtf32(int.Parse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture)))));
        }
        return result;
    }
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 640) return false;
        var text = value.Trim(); var known = Known.Value;
        var memo = new Dictionary<(int, int), bool>();
        bool Parse(int position, int count)
        {
            while (position < text.Length && text[position] == ' ') position++;
            if (position == text.Length) return count is >= 1 and <= 20;
            if (count == 20) return false;
            if (memo.TryGetValue((position, count), out var cached)) return cached;
            for (var length = 1; length <= Math.Min(32, text.Length - position); length++)
                if (known.Contains(text.Substring(position, length)) && Parse(position + length, count + 1)) return memo[(position, count)] = true;
            return memo[(position, count)] = false;
        }
        return Parse(0, 0);
    }
}
